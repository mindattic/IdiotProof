using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdiotProof.Core.Research.Gappers;

public sealed record HoldoutLockResult(bool Allowed, string Reason, string? FrozenHash);

/// <summary>
/// The holdout gate (protocol §Holdout lock): <c>holdout-unlock.txt</c> must hold the full hash of
/// the commit that last touched <c>hypotheses.json</c> or <c>params.json</c>, neither file may have
/// uncommitted changes, <c>hypotheses.frozenUtc</c> must be set, and each holdout stage runs once:
/// <c>holdout-run.json</c> is written before the first holdout row and a stage already recorded
/// there refuses to run again. There is no force flag.
/// </summary>
public sealed class HoldoutLock(GapperStudyPaths paths, GapperStudyParams p, Func<string?>? frozenCommit = null, Func<bool>? hasUncommittedChanges = null)
{
    private readonly Func<string?> frozenCommit = frozenCommit ?? (() => GitFrozenCommit(paths));
    private readonly Func<bool> hasUncommittedChanges = hasUncommittedChanges ?? (() => GitHasUncommittedChanges(paths));

    public string UnlockFile => paths.UnlockFile(p);
    public string RunFile => paths.HoldoutRunFile(p);

    public HoldoutLockResult Check(string stage)
    {
        if (p.HoldoutLock.RefuseWhenFrozenUtcNull && p.Hypotheses.FrozenUtc is null)
            return new(false, "hypotheses.frozenUtc is null; the study is not frozen.", null);
        if (!File.Exists(UnlockFile))
            return new(false, $"{Path.GetFileName(UnlockFile)} does not exist; the holdout is locked.", null);
        var expected = frozenCommit();
        if (string.IsNullOrWhiteSpace(expected))
            return new(false, "git could not name the commit that froze hypotheses.json/params.json.", null);
        var provided = File.ReadAllText(UnlockFile).Trim();
        if (!string.Equals(provided, expected, StringComparison.OrdinalIgnoreCase) || provided.Length != 40)
            return new(false, $"{Path.GetFileName(UnlockFile)} holds '{provided}' but the frozen files were last committed in {expected}.", expected);
        if (p.HoldoutLock.RefuseUncommittedChanges && hasUncommittedChanges())
            return new(false, "hypotheses.json or params.json has uncommitted changes.", expected);
        if (p.HoldoutLock.RefuseSecondRun && File.Exists(RunFile))
        {
            var doc = JsonNode.Parse(File.ReadAllText(RunFile)) as JsonObject;
            if (doc?["stages"] is JsonObject stages && stages.ContainsKey(stage))
                return new(false, $"{Path.GetFileName(RunFile)} already records a '{stage}' run; the holdout opens once.", expected);
        }
        return new(true, "unlocked", expected);
    }

    /// <summary>Records the stage start in holdout-run.json before any holdout row is written.</summary>
    public void BeginStage(string stage, DateTime nowUtc, object? details = null)
    {
        var doc = File.Exists(RunFile) ? JsonNode.Parse(File.ReadAllText(RunFile)) as JsonObject ?? new JsonObject() : new JsonObject();
        doc["unlockHash"] = File.ReadAllText(UnlockFile).Trim();
        doc["head"] = GitHead(paths);
        doc["hypothesesSha256"] = p.HypothesesSha256;
        doc["paramsSha256"] = p.ParamsSha256;
        doc["runStartUtc"] ??= nowUtc.ToString("O");
        var stages = doc["stages"] as JsonObject ?? new JsonObject();
        stages[stage] = new JsonObject
        {
            ["startUtc"] = nowUtc.ToString("O"),
            ["details"] = details is null ? null : JsonSerializer.SerializeToNode(details),
        };
        doc["stages"] = stages;
        File.WriteAllText(RunFile, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void EndStage(string stage, DateTime nowUtc, object? summary = null)
    {
        if (!File.Exists(RunFile)) return;
        var doc = JsonNode.Parse(File.ReadAllText(RunFile)) as JsonObject ?? new JsonObject();
        if (doc["stages"] is JsonObject stages && stages[stage] is JsonObject s)
        {
            s["endUtc"] = nowUtc.ToString("O");
            s["summary"] = summary is null ? null : JsonSerializer.SerializeToNode(summary);
        }
        doc["runEndUtc"] = nowUtc.ToString("O");
        File.WriteAllText(RunFile, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string? GitFrozenCommit(GapperStudyPaths paths) =>
        Git(paths.StudyDir, "log", "-1", "--format=%H", "--", "hypotheses.json", "params.json");

    public static bool GitHasUncommittedChanges(GapperStudyPaths paths) =>
        !string.IsNullOrWhiteSpace(Git(paths.StudyDir, "status", "--porcelain", "--", "hypotheses.json", "params.json"));

    public static string? GitHead(GapperStudyPaths paths) => Git(paths.StudyDir, "rev-parse", "HEAD");

    /// <summary>Commits (hash, author date, subject) touching the named study files with an author date after <paramref name="sinceUtc"/>, newest first.</summary>
    public static List<(string Hash, string AuthorDate, string Subject)> GitCommitsSince(GapperStudyPaths paths, DateTime sinceUtc, IEnumerable<string> files)
    {
        var args = new List<string> { "log", "--format=%H|%aI|%s", $"--since={sinceUtc:yyyy-MM-ddTHH:mm:ssZ}", "--" };
        args.AddRange(files);
        var output = Git(paths.StudyDir, args.ToArray());
        if (string.IsNullOrWhiteSpace(output)) return [];
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|', 3))
            .Where(parts => parts.Length == 3)
            .Select(parts => (parts[0].Trim(), parts[1].Trim(), parts[2].Trim()))
            .ToList();
    }

    /// <summary>The content of a study file at a commit, or null when git cannot show it.</summary>
    public static string? GitShow(GapperStudyPaths paths, string commit, string fileName)
    {
        var relative = Git(paths.StudyDir, "ls-files", "--full-name", fileName);
        return string.IsNullOrWhiteSpace(relative) ? null : Git(paths.StudyDir, "show", $"{commit}:{relative.Trim()}");
    }

    private static string? Git(string workingDir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = workingDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(15_000);
            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception) { return null; }
    }
}
