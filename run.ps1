#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and deploys IdiotProof (the Hub, its CLI and MCP server, the Monitor shim and the
    Blazor web server) to C:\Apps\IdiotProof\, and optionally starts the Hub and Blazor. The one
    script for publishing and launching IdiotProof. Agents never run it unless the user asks:
    the app may be trading real money.

.DESCRIPTION
    Publishes (Release, framework-dependent):
      IdiotProof.Hub     -> C:\Apps\IdiotProof\Hub\idiotproof-hub.exe      (hosts the Monitor + every operation)
      IdiotProof.Cli     -> C:\Apps\IdiotProof\Cli\idiotproof.exe          (the `idiotproof` command line)
      IdiotProof.Mcp     -> C:\Apps\IdiotProof\Mcp\idiotproof-mcp.exe      (MCP server for Claude)
      IdiotProof.Monitor -> C:\Apps\IdiotProof\Monitor\idiotproof-monitor.exe (shim: runs the Hub, IP-T81)
      IdiotProof.Blazor  -> C:\Apps\IdiotProof\Blazor\IdiotProof.Blazor.exe
    puts C:\Apps\IdiotProof\Cli on the user PATH (once; idempotent) so `idiotproof` resolves
    anywhere, and writes three generated files:
      Blazor\run.bat  runs the Blazor exe with ASPNETCORE_URLS preset to
                      http://localhost:5100, so it never collides with the dev
                      server (65025/65026).
      run.bat         (C:\Apps\IdiotProof\run.bat) the desktop shortcut points
                      at it; a stale deploy.bat from before the rename is deleted.
                      Opens a menu first:
                      1) Deploy & Run runs this script with -Force (a clean
                      republish), then starts the Monitor and Blazor and opens the
                      browser; 2) Run starts the already-deployed copy without
                      calling this script (deploying first if nothing is there yet).
                      Written on every run of this script, so an existing install
                      picks up launcher changes without a republish.
      start.bat       (C:\Apps\IdiotProof\start.bat) the launch half of run.bat with no
                      menu: starts the Hub (which hosts the Monitor) and Blazor.

    A running deployed Hub is asked to stop gracefully (`idiotproof hub stop`: drain the
    running operations, stop the Monitor loop, release the leader lease) before it is
    republished; only if it does not exit is it ended.

    Without -Force a component is republished only when its deployed exe or
    the launcher run.bat is missing, or a source file of the component (its project plus
    every project it references, transitively) is newer than its deployed exe, so
    an unchanged run is a fast no-op. A running deployed copy of a component is
    stopped only when that component is about to be republished. The script itself
    never prompts: agents and /deploy run it non-interactively; the menu lives only
    in the generated run.bat.

.PARAMETER Force
    Republish every component with a clean bin/obj rebuild even if nothing looks changed.

.PARAMETER Launch
    After deploying, start the Hub (hosting the Monitor) and Blazor and open the browser (same as run.bat's Run).

.PARAMETER LauncherOnly
    Only (re)write C:\Apps\IdiotProof\run.bat and start.bat, then exit: no build, nothing stopped.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File run.ps1 -Force
    powershell -ExecutionPolicy Bypass -File run.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File run.ps1 -LauncherOnly
#>
param(
    [switch]$Force,
    [switch]$Launch,
    [switch]$LauncherOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo       = $PSScriptRoot
$deployDir  = 'C:\Apps\IdiotProof'
$blazorUrl  = 'http://localhost:5100'
$launcherBat = Join-Path $deployDir 'run.bat'
$sourceExts = @('.cs', '.csproj', '.razor', '.cshtml', '.css', '.js', '.json', '.props', '.targets', '.resx')

$components = @(
    [pscustomobject]@{
        Name    = 'Hub'
        Proj    = Join-Path $repo 'IdiotProof.Hub\IdiotProof.Hub.csproj'
        OutDir  = Join-Path $deployDir 'Hub'
        ExeName = 'idiotproof-hub.exe'
        Extra   = @()
    },
    [pscustomobject]@{
        Name    = 'Cli'
        Proj    = Join-Path $repo 'IdiotProof.Cli\IdiotProof.Cli.csproj'
        OutDir  = Join-Path $deployDir 'Cli'
        ExeName = 'idiotproof.exe'
        Extra   = @()
    },
    [pscustomobject]@{
        Name    = 'Mcp'
        Proj    = Join-Path $repo 'IdiotProof.Mcp\IdiotProof.Mcp.csproj'
        OutDir  = Join-Path $deployDir 'Mcp'
        ExeName = 'idiotproof-mcp.exe'
        Extra   = @()
    },
    [pscustomobject]@{
        Name    = 'Monitor'
        Proj    = Join-Path $repo 'IdiotProof.Monitor\IdiotProof.Monitor.csproj'
        OutDir  = Join-Path $deployDir 'Monitor'
        ExeName = 'idiotproof-monitor.exe'
        Extra   = @()
    },
    [pscustomobject]@{
        Name    = 'Blazor'
        Proj    = Join-Path $repo 'IdiotProof.Blazor\IdiotProof.Blazor.csproj'
        OutDir  = Join-Path $deployDir 'Blazor'
        ExeName = 'IdiotProof.Blazor.exe'
        Extra   = @('run.bat')
    }
)

# The project's directory plus the directories of every project it references, transitively.
# cmd reads a running .bat by byte offset, and run.bat runs this script, so only rewrite a launcher
# whose text actually changed, and always with CRLF so its goto labels resolve.
function Write-BatIfChanged([string]$Path, [string]$Text) {
    $Text = ($Text -replace "`r?`n", "`r`n").TrimEnd() + "`r`n"
    if ((Test-Path -LiteralPath $Path) -and ([IO.File]::ReadAllText($Path) -ceq $Text)) { return }
    [IO.File]::WriteAllText($Path, $Text, [Text.Encoding]::ASCII)
}

function Get-ProjectDirs([string]$proj) {
    $seen  = @{}
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue([System.IO.Path]::GetFullPath($proj))
    while ($queue.Count -gt 0) {
        $p = $queue.Dequeue()
        if ($seen.ContainsKey($p) -or -not (Test-Path $p)) { continue }
        $seen[$p] = $true
        $dir = Split-Path $p
        foreach ($m in Select-String -LiteralPath $p -Pattern '<ProjectReference\s+Include="([^"]+)"' -AllMatches) {
            foreach ($g in $m.Matches) {
                $queue.Enqueue([System.IO.Path]::GetFullPath((Join-Path $dir $g.Groups[1].Value)))
            }
        }
    }
    return @($seen.Keys | ForEach-Object { Split-Path $_ })
}

function Test-NeedsPublish($c) {
    $exe = Join-Path $c.OutDir $c.ExeName
    if (-not (Test-Path $exe) -or -not (Test-Path $launcherBat)) { return $true }
    foreach ($f in $c.Extra) { if (-not (Test-Path (Join-Path $c.OutDir $f))) { return $true } }
    $exeTime = (Get-Item $exe).LastWriteTimeUtc

    foreach ($f in 'Directory.Build.props', 'Directory.Build.targets', 'NuGet.config') {
        $p = Join-Path $repo $f
        if ((Test-Path $p) -and (Get-Item $p).LastWriteTimeUtc -gt $exeTime) { return $true }
    }
    foreach ($dir in Get-ProjectDirs $c.Proj) {
        $newer = Get-ChildItem -Path $dir -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -notmatch '\\(bin|obj)\\' -and
                $sourceExts -contains $_.Extension.ToLowerInvariant() -and
                $_.LastWriteTimeUtc -gt $exeTime
            } |
            Select-Object -First 1
        if ($newer) { return $true }
    }
    return $false
}

# Stops only the deployed copy (never a dev instance running from the repo's bin\).
function Stop-Deployed($c) {
    $exe   = Join-Path $c.OutDir $c.ExeName
    $name  = [System.IO.Path]::GetFileNameWithoutExtension($c.ExeName)
    $procs = @(Get-Process $name -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -ieq $exe })
    # The Hub hosts the Monitor: ask it to stop gracefully first (drain operations, finish the
    # tick, release the SQL leader lease) via the deployed CLI's `hub stop`, then wait for it.
    if ($procs.Count -gt 0 -and $c.Name -eq 'Hub') {
        $cli = Join-Path $deployDir 'Cli\idiotproof.exe'
        if (Test-Path $cli) {
            Write-Host '  Asking the running Hub to stop gracefully...' -ForegroundColor DarkYellow
            & $cli hub stop | Out-Host
            foreach ($p in $procs) { $null = $p.WaitForExit(60000) }
            $procs = @($procs | Where-Object { -not $_.HasExited })
        }
    }
    if ($procs.Count -gt 0) {
        $procs | Stop-Process -Force
        Write-Host "  Stopped running $($c.Name) ($($procs.Count) process(es))" -ForegroundColor DarkYellow
        Start-Sleep -Milliseconds 800
    }
}

function Publish($c) {
    if ($Force) {
        foreach ($dir in Get-ProjectDirs $c.Proj) {
            foreach ($d in @((Join-Path $dir 'bin'), (Join-Path $dir 'obj'))) {
                if (Test-Path $d) {
                    try { Remove-Item -Recurse -Force $d -ErrorAction Stop }
                    catch { Write-Host "  (could not fully remove $d : $($_.Exception.Message))" -ForegroundColor DarkYellow }
                }
            }
        }
    }

    Stop-Deployed $c

    $exe = Join-Path $c.OutDir $c.ExeName
    Write-Host "Publishing $($c.Name) (Release) -> $($c.OutDir)" -ForegroundColor Cyan
    dotnet publish $c.Proj `
        --configuration Release `
        --output $c.OutDir `
        --no-self-contained `
        --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { return $false }
    if (-not (Test-Path $exe)) { return $false }
    # Publish skips copying unchanged files; stamp the exe so the next
    # change check compares against this deploy, not an older build.
    (Get-Item $exe).LastWriteTimeUtc = [DateTime]::UtcNow

    if ($c.Name -eq 'Blazor') {
        $runBat = @"
@echo off
rem Generated by $repo\run.ps1 - edits here are overwritten.
cd /d "%~dp0"
set ASPNETCORE_URLS=$blazorUrl
set ASPNETCORE_ENVIRONMENT=Development
"%~dp0IdiotProof.Blazor.exe"
"@
        Write-BatIfChanged (Join-Path $c.OutDir 'run.bat') $runBat
    }
    Write-Host "Deployed $exe" -ForegroundColor Green
    return $true
}

function Write-Launcher {
    $bat = @"
@echo off
title IdiotProof
rem Generated by $repo\run.ps1 - edits here are overwritten.
set "DEPLOY_PS1=$repo\run.ps1"
echo.
echo   IdiotProof
echo     1^) Deploy ^& Run
echo     2^) Run
echo.
choice /c 12 /n /m "  Choose 1 or 2: "
if errorlevel 2 goto run
:deploy
if exist "%DEPLOY_PS1%" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%DEPLOY_PS1%" -Force
    if errorlevel 1 echo Redeploy failed - launching whatever is already in this folder instead.
) else (
    echo Source repo not found at "%DEPLOY_PS1%" - launching existing build without redeploying.
)
goto launch
:run
if not exist "%~dp0Monitor\idiotproof-monitor.exe" goto notdeployed
if not exist "%~dp0Blazor\run.bat" goto notdeployed
goto launch
:notdeployed
echo  Nothing is deployed yet - deploying first.
goto deploy
:launch
echo.
echo  Starting Monitor...
start "IdiotProof Monitor" powershell -NoExit -Command "& '%~dp0Monitor\idiotproof-monitor.exe'"
echo  Starting Blazor ($blazorUrl)...
start "IdiotProof Blazor" "%~dp0Blazor\run.bat"
echo  Waiting for Blazor to start...
timeout /t 4 /nobreak >nul
start "" "$blazorUrl"
echo.
echo  Done. Monitor is in its own window; Blazor is at $blazorUrl
"@
    Write-BatIfChanged $launcherBat $bat
    # run.bat replaces the old generated deploy.bat.
    $oldBat = Join-Path $deployDir 'deploy.bat'
    if (Test-Path $oldBat) { Remove-Item $oldBat -Force -ErrorAction SilentlyContinue }
}

# ── Deploy ──────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $deployDir | Out-Null

if ($LauncherOnly) {
    Write-Launcher
    Write-Host "Wrote $launcherBat" -ForegroundColor Green
    exit 0
}

$ok = $true
$published = 0
foreach ($c in $components) {
    if ($Force -or (Test-NeedsPublish $c)) {
        $published++
        if (-not (Publish $c)) {
            Write-Host "$($c.Name) publish FAILED." -ForegroundColor Red
            $ok = $false
        }
    }
}
Write-Launcher
if ($published -eq 0) { Write-Host 'IdiotProof is up to date.' -ForegroundColor DarkGray }

if (-not $ok) {
    Write-Host 'Deploy FAILED.' -ForegroundColor Red
    if (-not $Launch) { exit 1 }
    Write-Host 'Launching whatever is already deployed instead.' -ForegroundColor Yellow
}

if (-not $Launch) { exit 0 }

# Same as run.bat's launch half, without re-running this script.
Write-Host '  Starting Monitor...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList '-NoExit', '-Command', "& '$deployDir\Monitor\idiotproof-monitor.exe'"
Write-Host "  Starting Blazor ($blazorUrl)..." -ForegroundColor Cyan
Start-Process (Join-Path $deployDir 'Blazor\run.bat')
Start-Sleep -Seconds 4
Start-Process $blazorUrl
exit 0
