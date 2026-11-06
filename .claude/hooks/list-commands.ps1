# SessionStart (IP-T440): list this repo's local Claude Code slash commands so they're visible
# every time a session opens here, instead of only when someone thinks to look in .claude/commands.
#
# For each .claude/commands/*.md file, prints "/name - <first paragraph, trimmed>". Never fails the
# session: any problem is one line of context, exit 0.
$ErrorActionPreference = 'Stop'
try {
    $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $dir = Join-Path $root '.claude\commands'
    if (-not (Test-Path $dir)) { exit 0 }

    $files = Get-ChildItem -Path $dir -Filter '*.md' | Sort-Object Name
    if ($files.Count -eq 0) { exit 0 }

    $lines = foreach ($f in $files) {
        $text = Get-Content -Path $f.FullName -Raw -Encoding UTF8
        # First paragraph = lines up to the first blank line.
        $paragraph = ($text -split "`r?`n`r?`n")[0] -replace '\s+', ' '
        $paragraph = $paragraph.Trim()
        # Strip markdown emphasis so it reads cleanly on one line.
        $paragraph = $paragraph -replace '\*\*', ''
        # Prefer the first sentence; otherwise truncate at the last word boundary.
        if ($paragraph -match '^(.{1,140}?[.!?])\s') {
            $desc = $matches[1]
        }
        elseif ($paragraph.Length -gt 100) {
            $cut = $paragraph.Substring(0, 97)
            $lastSpace = $cut.LastIndexOf(' ')
            if ($lastSpace -gt 60) { $cut = $cut.Substring(0, $lastSpace) }
            $desc = $cut + '...'
        }
        else {
            $desc = $paragraph
        }
        "  /$($f.BaseName) - $desc"
    }

    Write-Output "[idiotproof] $($files.Count) local slash commands (.claude/commands):"
    Write-Output ($lines -join "`n")
}
catch {
    Write-Output "[idiotproof] Slash-command listing skipped: $($_.Exception.Message)"
}
exit 0
