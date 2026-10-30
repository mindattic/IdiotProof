# SessionStart: keep the IdiotProof MCP server's Release build current so `.mcp.json`
# (dotnet run --no-build -c Release) never serves a stale tool schema. Builds only when a source
# file under IdiotProof.Mcp / IdiotProof.Operations / IdiotProof.Core is newer than the built dll.
# Never fails the session: a locked dll (server already running) just keeps the current build.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dll = Join-Path $root 'IdiotProof.Mcp\bin\Release\net10.0\idiotproof-mcp.dll'
$built = if (Test-Path $dll) { (Get-Item $dll).LastWriteTimeUtc } else { [datetime]::MinValue }

$sources = 'IdiotProof.Mcp', 'IdiotProof.Operations', 'IdiotProof.Core' | ForEach-Object { Join-Path $root $_ }
$newest = Get-ChildItem -Path $sources -Recurse -File -Include *.cs, *.csproj -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1

if ($newest -and $newest.LastWriteTimeUtc -gt $built) {
    & dotnet build (Join-Path $root 'IdiotProof.Mcp\IdiotProof.Mcp.csproj') -c Release -v q -nologo *> $null
    if ($LASTEXITCODE -ne 0) { Write-Output '[idiotproof-mcp] Release build failed or is locked; MCP tools may be stale.' }
}
exit 0
