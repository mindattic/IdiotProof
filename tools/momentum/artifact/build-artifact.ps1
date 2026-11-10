# IP-T499: builds the self-contained "Momentum. calls vs reality" page from the claim tracker data.
# The data comes from the Hub (`idiotproof momentum export`, IP-T514): the Hub is the only process that reads the
# database, so this script only fills the template.
# usage: powershell -File tools/momentum/artifact/build-artifact.ps1 [-Out D:\IdiotProofData\momentum\momentum-calls-vs-reality.html] [-JsonOut path]
param(
    [string]$Out = 'D:\IdiotProofData\momentum\momentum-calls-vs-reality.html',
    [string]$JsonOut
)
$ErrorActionPreference = 'Stop'

$jsonPath = if ($JsonOut) { $JsonOut } else { Join-Path $env:TEMP 'momentum-calls-export.json' }
& idiotproof momentum export --out $jsonPath | Out-Host
if ($LASTEXITCODE -ne 0) { throw "idiotproof momentum export failed ($LASTEXITCODE); is the Hub running?" }
if ($JsonOut) { "wrote $JsonOut" }

$json = [IO.File]::ReadAllText($jsonPath, [Text.Encoding]::UTF8)
$template = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot 'template.html')
$html = $template.Replace('/*__DATA__*/null', $json)
[IO.File]::WriteAllText($Out, $html, (New-Object Text.UTF8Encoding $false))
"wrote $Out ($([Math]::Round($html.Length / 1KB)) KB)"
