<#
.SYNOPSIS
  Codex documentation standard CLI for IdiotProof (CODE: IP).

.DESCRIPTION
  Subcommands:
    doctor  - validate the docs/ canon (front-matter, IDs, cross-refs, data schemas,
              story test citations, bible file-path citations, generated-digest freshness).
              Exits non-zero on any hard error.
    digest  - regenerate docs/BIBLE.digest.md from BIBLE.md section 1/3/5/9 + a status index
              + the latest amendment head.

  No build step. Authored ASCII-only so Windows PowerShell 5.1 (Win-1252 default) parses it
  regardless of file encoding. Non-ASCII tokens (status emoji, section sign) are built from
  code points at runtime.

.EXAMPLE
  pwsh tools/codex.ps1 doctor
  pwsh tools/codex.ps1 digest
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('doctor', 'digest')]
    [string]$Command = 'doctor'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DocsDir  = Join-Path $RepoRoot 'docs'
$BiblePath   = Join-Path $DocsDir 'BIBLE.md'
$DigestPath  = Join-Path $DocsDir 'BIBLE.digest.md'
$StoriesPath = Join-Path $DocsDir 'USER_STORIES.md'
$AmendPath   = Join-Path $DocsDir 'AMENDMENTS.md'

# Non-ASCII tokens built from code points (keep this file ASCII-only).
$SECT      = [string][char]0x00A7                                   # section sign
$EMO_DONE  = [string][char]0x2705                                 # check mark button
$EMO_PLAN  = [string][char]0x2B1C                                 # white large square
# Emoji above U+FFFF must be built from UTF-32 code points (surrogate pairs).
$EMO_PART  = [System.Char]::ConvertFromUtf32(0x1F7E1)             # yellow circle
$EMO_CUT   = [System.Char]::ConvertFromUtf32(0x1F5D1)             # wastebasket

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Read-Text([string]$path) { Get-Content -LiteralPath $path -Raw -Encoding UTF8 }

function Get-FrontMatter([string]$text) {
    if ($text -notmatch '(?s)^\s*---\r?\n(.*?)\r?\n---') { return $null }
    $block = $Matches[1]
    $map = @{}
    foreach ($line in ($block -split "`n")) {
        if ($line -match '^\s*([A-Za-z0-9_]+)\s*:\s*(.+?)\s*$') {
            $map[$Matches[1]] = $Matches[2].Trim()
        }
    }
    return $map
}

function Get-CanonFiles {
    $files = @()
    if (Test-Path $BiblePath)   { $files += $BiblePath }
    if (Test-Path $StoriesPath) { $files += $StoriesPath }
    if (Test-Path $AmendPath)   { $files += $AmendPath }
    $rfcDir = Join-Path $DocsDir 'rfc'
    if (Test-Path $rfcDir)  { $files += (Get-ChildItem -LiteralPath $rfcDir -Filter '*.md' -File | ForEach-Object FullName) }
    $dataDir = Join-Path $DocsDir 'data'
    if (Test-Path $dataDir) { $files += (Get-ChildItem -LiteralPath $dataDir -Filter '*.json' -File -Recurse | Where-Object { $_.FullName -notmatch '_schema' } | ForEach-Object FullName) }
    return $files
}

# ---------------------------------------------------------------------------
# DIGEST
# ---------------------------------------------------------------------------
function Get-Section([string]$bible, [string]$anchor) {
    $pattern = '(?ms)^##\s+.*?\{#' + [regex]::Escape($anchor) + '\}\s*\r?\n(.*?)(?=^##\s|\Z)'
    if ($bible -match $pattern) { return $Matches[1].Trim() }
    return ''
}

function Invoke-Digest {
    if (-not (Test-Path $BiblePath)) { Write-Error "BIBLE.md not found at $BiblePath"; exit 1 }
    $bible = Read-Text $BiblePath
    $fm = Get-FrontMatter $bible
    $project = if ($fm -and $fm.project) { $fm.project } else { 'IdiotProof' }
    $code    = if ($fm -and $fm.code) { $fm.code } else { 'IP' }

    $one   = Get-Section $bible ('IP-' + $SECT + '1')
    $isNot = Get-Section $bible ('IP-' + $SECT + '3')
    $laws  = Get-Section $bible ('IP-' + $SECT + '5')
    $gloss = Get-Section $bible ('IP-' + $SECT + '9')

    # Status index from USER_STORIES.md
    $done = 0; $partial = 0; $planned = 0; $cut = 0
    if (Test-Path $StoriesPath) {
        $stories = Read-Text $StoriesPath
        $done    = ([regex]::Matches($stories, [regex]::Escape($EMO_DONE))).Count
        $partial = ([regex]::Matches($stories, [regex]::Escape($EMO_PART))).Count
        $planned = ([regex]::Matches($stories, [regex]::Escape($EMO_PLAN))).Count
        $cut     = ([regex]::Matches($stories, [regex]::Escape($EMO_CUT))).Count
    }

    # Latest amendment head
    $amendHead = ''
    if (Test-Path $AmendPath) {
        $am = Read-Text $AmendPath
        $m = [regex]::Matches($am, '(?m)^##\s+(.+)$')
        if ($m.Count -gt 0) { $amendHead = $m[$m.Count - 1].Groups[1].Value.Trim() }
    }

    $today = (Get-Date).ToString('yyyy-MM-dd')
    $dash = [string][char]0x2014
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('AUTHORITATIVE ' + $dash + ' full detail in docs/BIBLE.md')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("<!-- generatedFrom: $code-${SECT}1,$code-${SECT}3,$code-${SECT}5,$code-${SECT}9 + USER_STORIES status index. Generated $today by tools/codex.ps1. Do not hand-edit. -->")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("# $project " + $dash + ' Bible Digest (generated)')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## The one sentence')
    [void]$sb.AppendLine($one)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## What it is NOT')
    [void]$sb.AppendLine($isNot)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## The Laws')
    [void]$sb.AppendLine($laws)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Glossary')
    [void]$sb.AppendLine($gloss)
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Status index (USER_STORIES.md)')
    [void]$sb.AppendLine("- done: $done")
    [void]$sb.AppendLine("- partial: $partial")
    [void]$sb.AppendLine("- planned: $planned")
    [void]$sb.AppendLine("- cut: $cut")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Latest amendment')
    [void]$sb.AppendLine($amendHead)

    Set-Content -LiteralPath $DigestPath -Value $sb.ToString() -Encoding UTF8 -NoNewline
    Write-Host "digest -> $DigestPath" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# DOCTOR
# ---------------------------------------------------------------------------
function Invoke-Doctor {
    $errs = @()
    $warns = @()
    $checks = @()

    $canonFiles = Get-CanonFiles
    $validLayers = @('bible', 'stories', 'amendments', 'rfc', 'data', 'houserules')

    # 1. Front-matter on every canon file
    foreach ($f in $canonFiles) {
        if ($f -match '\.json$') {
            try { Get-Content -LiteralPath $f -Raw | ConvertFrom-Json | Out-Null }
            catch { $errs += "Invalid JSON: $f" }
            continue
        }
        $text = Read-Text $f
        $fm = Get-FrontMatter $text
        if (-not $fm) { $errs += "Missing front-matter: $f"; continue }
        if (-not $fm.codex)   { $errs += "Missing 'codex:' in $f" }
        if (-not $fm.layer)   { $errs += "Missing 'layer:' in $f" }
        elseif ($validLayers -notcontains $fm.layer) { $errs += "Invalid layer '$($fm.layer)' in $f" }
        if (-not $fm.updated) { $errs += "Missing 'updated:' in $f" }
    }
    $checks += "front-matter: $($canonFiles.Count) canon file(s) checked"

    # 2. {#...} anchors unique; every cross-ref to a #anchor resolves
    $allText = ''
    foreach ($f in $canonFiles) { if ($f -notmatch '\.json$') { $allText += "`n" + (Read-Text $f) } }

    $anchorMatches = [regex]::Matches($allText, '\{#([^\}]+)\}')
    $anchors = @{}
    foreach ($mm in $anchorMatches) {
        $id = $mm.Groups[1].Value
        if ($anchors.ContainsKey($id)) { $errs += "Duplicate anchor: {#$id}" }
        else { $anchors[$id] = $true }
    }
    $explicitAnchorCount = $anchors.Count

    # Also treat amendment heads (## IP-A<n> ...) and story IDs (IP-US-...) as resolvable
    # anchors, since GitHub auto-slugs headings and stories are referenced by their stable ID.
    foreach ($mm in [regex]::Matches($allText, '(?m)^##\s+(IP-A\d+)\b')) { $anchors[$mm.Groups[1].Value] = $true }
    foreach ($mm in [regex]::Matches($allText, '\b(IP-US-[A-Za-z]\d+)\b'))  { $anchors[$mm.Groups[1].Value] = $true }

    $checks += "anchors: $explicitAnchorCount explicit {#id} + amendment/story IDs registered"

    $linkMatches = [regex]::Matches($allText, '\]\([^)]*#([^)]+)\)')
    $refCount = 0
    foreach ($mm in $linkMatches) {
        $target = $mm.Groups[1].Value
        if ($target -like 'HOUSE-*') { continue }   # external HouseRules anchors
        $refCount++
        if (-not $anchors.ContainsKey($target)) { $errs += "Unresolved cross-ref to #$target" }
    }
    $checks += "cross-refs: $refCount internal #anchor link(s) checked"

    # 3. data/*.json validate + unique ids (best-effort)
    $dataDir = Join-Path $DocsDir 'data'
    if (Test-Path $dataDir) {
        $dataFiles = Get-ChildItem -LiteralPath $dataDir -Filter '*.json' -File -Recurse | Where-Object { $_.FullName -notmatch '_schema' }
        $ids = @{}
        foreach ($df in $dataFiles) {
            $schemaPath = Join-Path (Join-Path $dataDir '_schema') ((($df.BaseName) -replace '\..*$', '') + '.schema.json')
            if (-not (Test-Path $schemaPath)) { $warns += "No schema for data file: $($df.Name)" }
            try {
                $json = Get-Content -LiteralPath $df.FullName -Raw | ConvertFrom-Json
                foreach ($e in @($json)) {
                    if ($e.PSObject.Properties.Name -contains 'id') {
                        if ($ids.ContainsKey($e.id)) { $errs += "Duplicate data id: $($e.id)" } else { $ids[$e.id] = $true }
                    }
                }
            } catch { $errs += "Invalid JSON data: $($df.Name)" }
        }
        $checks += "data: $($dataFiles.Count) file(s), $($ids.Count) entity id(s)"
    } else {
        $checks += "data: none (domain has no structured L5 canon)"
    }

    # 4. Every done-status story names a test token; confirm best-effort it exists on disk.
    #    Only real story lines (carrying an IP-US-<Epic><n> id) are checked, so the legend
    #    line and prose that merely contain a status glyph are skipped.
    if (Test-Path $StoriesPath) {
        # A story is a bullet that starts (after markup) with its IP-US-<id> and runs until the
        # next bullet / heading. The verifying test token is often wrapped onto a later line, so
        # accumulate each story's full text block before extracting tokens.
        $storyLines = (Read-Text $StoriesPath) -split "`n"
        $blocks = @{}        # id -> accumulated text
        $order  = @()
        $current = $null
        foreach ($raw in $storyLines) {
            $line = $raw.TrimEnd("`r")
            if ($line -match '^\s*-\s+\*\*(IP-US-[A-Za-z]\d+)\b') {
                $current = $Matches[1]
                if (-not $blocks.ContainsKey($current)) { $blocks[$current] = ''; $order += $current }
                $blocks[$current] += " " + $line
            }
            elseif ($line -match '^\s*-\s' -or $line -match '^#' ) {
                $current = $null   # a non-story bullet or a heading ends the current story
            }
            elseif ($current) {
                $blocks[$current] += " " + $line
            }
        }

        # Collect (storyId, tokens[]) for every DONE story, and the full set of cited tokens.
        $doneStories = @()
        $allTokens = @{}
        foreach ($id in $order) {
            $text = $blocks[$id]
            if ($text -notmatch [regex]::Escape($EMO_DONE)) { continue }
            $tokens = @()
            foreach ($t in [regex]::Matches($text, '`([A-Za-z_][A-Za-z0-9_]+)`')) {
                $tk = $t.Groups[1].Value
                $tokens += $tk
                $allTokens[$tk] = $true
            }
            $doneStories += [pscustomobject]@{ Id = $id; Tokens = $tokens }
        }

        # One pass over the test tree: which cited tokens actually appear in a test file?
        $foundTokens = @{}
        if ($allTokens.Count -gt 0) {
            $tokenPattern = ($allTokens.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|'
            $testFiles = Get-ChildItem -LiteralPath $RepoRoot -Recurse -Include '*.cs', '*.cy.ts', '*.ts' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules)\\' }
            $hits = $testFiles | Select-String -Pattern $tokenPattern -AllMatches -ErrorAction SilentlyContinue
            foreach ($h in $hits) { foreach ($m in $h.Matches) { $foundTokens[$m.Value] = $true } }
        }

        $doneCount = $doneStories.Count; $citedOk = 0
        foreach ($s in $doneStories) {
            $hasTest = $false
            foreach ($tk in $s.Tokens) { if ($foundTokens.ContainsKey($tk)) { $hasTest = $true; break } }
            if ($hasTest) { $citedOk++ }
            else { $errs += "done-story $($s.Id) names no test token found on disk" }
        }
        $checks += "stories: $doneCount done story(ies), $citedOk with a test found on disk"
    }

    # 5. Every code path/file cited in the bible exists on disk
    if (Test-Path $BiblePath) {
        $bible = Read-Text $BiblePath
        $pathMatches = [regex]::Matches($bible, '`([A-Za-z0-9_\.][A-Za-z0-9_\./\\\-]*\.(cs|json|razor|md|ps1|csproj|slnx))`')
        # Sanctioned external references that live outside the repo root by design.
        $externalAllow = @('MindAttic.HouseRules.md')
        $checkedPaths = @{}
        foreach ($mm in $pathMatches) {
            $rel = $mm.Groups[1].Value -replace '/', '\'
            if ($externalAllow -contains (Split-Path $rel -Leaf)) { continue }
            if ($checkedPaths.ContainsKey($rel)) { continue }
            $checkedPaths[$rel] = $true
            $full = Join-Path $RepoRoot $rel
            if (-not (Test-Path $full)) {
                $bare = Split-Path $rel -Leaf
                $found = Get-ChildItem -LiteralPath $RepoRoot -Recurse -Filter $bare -File -ErrorAction SilentlyContinue |
                         Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules)\\' } | Select-Object -First 1
                if (-not $found) { $errs += "Bible cites a path that does not exist: $rel" }
            }
        }
        $checks += "bible paths: $($checkedPaths.Count) cited file path(s) checked"
    }

    # 6. generatedFrom freshness: digest must be newer than its sources
    if (Test-Path $DigestPath) {
        $digestMtime = (Get-Item $DigestPath).LastWriteTimeUtc
        $stale = $false
        foreach ($src in @($BiblePath, $StoriesPath, $AmendPath)) {
            if ((Test-Path $src) -and ((Get-Item $src).LastWriteTimeUtc -gt $digestMtime)) {
                $stale = $true
                $warns += ("Digest older than " + (Split-Path $src -Leaf) + " - run 'codex.ps1 digest'")
            }
        }
        if (-not $stale) { $checks += "digest: up to date" }
    } else {
        $warns += "BIBLE.digest.md missing - run 'codex.ps1 digest'"
    }

    # ---- Report ----
    Write-Host ''
    Write-Host '=== Codex doctor (IP) ===' -ForegroundColor Cyan
    foreach ($c in $checks) { Write-Host "  [check] $c" -ForegroundColor Gray }
    foreach ($w in $warns)  { Write-Host "  [warn]  $w" -ForegroundColor Yellow }
    foreach ($e in $errs)   { Write-Host "  [ERROR] $e" -ForegroundColor Red }
    Write-Host ''
    if ($errs.Count -gt 0) {
        Write-Host "doctor FAILED: $($errs.Count) error(s), $($warns.Count) warning(s)." -ForegroundColor Red
        exit 1
    }
    Write-Host "doctor PASSED: 0 errors, $($warns.Count) warning(s)." -ForegroundColor Green
    exit 0
}

switch ($Command) {
    'digest' { Invoke-Digest }
    'doctor' { Invoke-Doctor }
}
