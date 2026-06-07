<#
  SessionStart hook — injects docs/BIBLE.digest.md as authoritative context.
  Emits Claude Code hook JSON. Non-ASCII is escaped to \uXXXX so the output is
  safe under Windows PowerShell 5.1 / Win-1252 default encoding.
  If the digest is missing or empty, emits {}.
#>
$ErrorActionPreference = 'Stop'

# This script lives at <repo>/.claude/hooks/inject-digest.ps1
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$digestPath = Join-Path $repoRoot 'docs/BIBLE.digest.md'

if (-not (Test-Path $digestPath)) { Write-Output '{}'; return }

$digest = Get-Content -LiteralPath $digestPath -Raw -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($digest)) { Write-Output '{}'; return }

$preamble = @"
[IdiotProof Codex — AUTHORITATIVE PROJECT CONTEXT]
The following is the generated digest of docs/BIBLE.md (the single source of truth for what
IdiotProof IS, is NOT, and the laws that govern it). Treat it as authoritative. When it conflicts
with assumptions, the bible wins; the latest amendment wins over the bible. Full detail lives in
docs/BIBLE.md, docs/USER_STORIES.md, and docs/AMENDMENTS.md.

"@

$payload = $preamble + $digest

# JSON-escape and force every non-ASCII char to \uXXXX (Win-1252 safe).
$sb = New-Object System.Text.StringBuilder
foreach ($ch in $payload.ToCharArray()) {
    $code = [int][char]$ch
    switch ($ch) {
        '"'  { [void]$sb.Append('\"') }
        '\'  { [void]$sb.Append('\\') }
        "`b" { [void]$sb.Append('\b') }
        "`f" { [void]$sb.Append('\f') }
        "`n" { [void]$sb.Append('\n') }
        "`r" { [void]$sb.Append('\r') }
        "`t" { [void]$sb.Append('\t') }
        default {
            if ($code -lt 32 -or $code -gt 126) {
                [void]$sb.Append('\u')
                [void]$sb.Append($code.ToString('x4'))
            } else {
                [void]$sb.Append($ch)
            }
        }
    }
}
$escaped = $sb.ToString()

$json = '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"' + $escaped + '"}}'
Write-Output $json
