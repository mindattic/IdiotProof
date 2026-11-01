#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and deploys ONLY the IdiotProof Hub to C:\Apps\IdiotProof\Hub\, independent of the
    rest of the stack (Cli, Mcp, Monitor shim, Blazor). Agents never run it unless the user asks:
    the Hub may be trading real money.

.DESCRIPTION
    The Hub is a single self-sufficient process (it hosts the Monitor and serves every CLI/MCP/
    web operation over loopback HTTP), so it can be deployed and run on its own without the rest
    of the repo-root run.ps1's components.

    Publishes (Release, framework-dependent):
      IdiotProof.Hub -> C:\Apps\IdiotProof\Hub\idiotproof-hub.exe

    and writes two generated launchers next to it:
      C:\Apps\IdiotProof\Hub\run.bat    Menu: 1) Deploy & Run (re-runs this script with -Force,
                                        then starts the Hub) 2) Run (starts the already-deployed
                                        exe, deploying first if nothing is there yet). This is
                                        what the desktop shortcut points at.
      C:\Apps\IdiotProof\Hub\start.bat  The launch half with no menu, for Task Scheduler.

    A running deployed Hub is asked to stop gracefully first (via the deployed CLI's `hub stop`
    if C:\Apps\IdiotProof\Cli\idiotproof.exe exists; otherwise it is ended directly) before it is
    republished. Without -Force a publish is skipped unless the exe or launcher is missing, or a
    source file of the Hub (or a project it references) is newer than the deployed exe.

    On first run (or whenever the deployed launcher moves), a "IdiotProof Hub" shortcut is added
    to your Desktop pointing at the generated run.bat, unless -NoShortcut is passed.

.PARAMETER Force
    Republish with a clean bin/obj rebuild even if nothing looks changed.

.PARAMETER Launch
    After deploying, start the Hub (same as run.bat's "Run").

.PARAMETER LauncherOnly
    Only (re)write the generated run.bat/start.bat (and the Desktop shortcut), then exit: no
    build, nothing stopped.

.PARAMETER NoShortcut
    Skip creating/updating the Desktop shortcut.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File IdiotProof.Hub\run.ps1 -Force
    powershell -ExecutionPolicy Bypass -File IdiotProof.Hub\run.ps1 -Launch
    powershell -ExecutionPolicy Bypass -File IdiotProof.Hub\run.ps1 -LauncherOnly
#>
param(
    [switch]$Force,
    [switch]$Launch,
    [switch]$LauncherOnly,
    [switch]$NoShortcut
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo        = Split-Path -Parent $PSScriptRoot
$proj        = Join-Path $PSScriptRoot 'IdiotProof.Hub.csproj'
$deployDir   = 'C:\Apps\IdiotProof\Hub'
$exeName     = 'idiotproof-hub.exe'
$exe         = Join-Path $deployDir $exeName
$launcherBat = Join-Path $deployDir 'run.bat'
$sourceExts  = @('.cs', '.csproj', '.razor', '.cshtml', '.css', '.js', '.json', '.props', '.targets', '.resx')

# cmd reads a running .bat by byte offset, and run.bat can run this script, so only rewrite a
# launcher whose text actually changed, and always with CRLF so its goto labels resolve.
function Write-BatIfChanged([string]$Path, [string]$Text) {
    $Text = ($Text -replace "`r?`n", "`r`n").TrimEnd() + "`r`n"
    if ((Test-Path -LiteralPath $Path) -and ([IO.File]::ReadAllText($Path) -ceq $Text)) { return }
    [IO.File]::WriteAllText($Path, $Text, [Text.Encoding]::ASCII)
}

function Get-ProjectDirs([string]$p0) {
    $seen  = @{}
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue([System.IO.Path]::GetFullPath($p0))
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

function Test-NeedsPublish {
    if (-not (Test-Path $exe) -or -not (Test-Path $launcherBat)) { return $true }
    $exeTime = (Get-Item $exe).LastWriteTimeUtc

    foreach ($f in 'Directory.Build.props', 'Directory.Build.targets', 'NuGet.config') {
        $p = Join-Path $repo $f
        if ((Test-Path $p) -and (Get-Item $p).LastWriteTimeUtc -gt $exeTime) { return $true }
    }
    foreach ($dir in Get-ProjectDirs $proj) {
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

# Stops only the deployed copy (never a dev instance running from the repo's bin\). Asks the
# running Hub to stop gracefully first (drain operations, stop the Monitor loop, release the SQL
# leader lease) via the deployed CLI if one exists; otherwise ends it directly.
function Stop-Deployed {
    $name  = [System.IO.Path]::GetFileNameWithoutExtension($exeName)
    $procs = @(Get-Process $name -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -ieq $exe })
    if ($procs.Count -eq 0) { return }

    $cli = 'C:\Apps\IdiotProof\Cli\idiotproof.exe'
    if (Test-Path $cli) {
        Write-Host '  Asking the running Hub to stop gracefully...' -ForegroundColor DarkYellow
        & $cli hub stop | Out-Host
        foreach ($p in $procs) { $null = $p.WaitForExit(60000) }
        $procs = @($procs | Where-Object { -not $_.HasExited })
    }
    if ($procs.Count -gt 0) {
        $procs | Stop-Process -Force
        Write-Host "  Stopped running Hub ($($procs.Count) process(es))" -ForegroundColor DarkYellow
        Start-Sleep -Milliseconds 800
    }
}

function Publish-Hub {
    if ($Force) {
        foreach ($dir in Get-ProjectDirs $proj) {
            foreach ($d in @((Join-Path $dir 'bin'), (Join-Path $dir 'obj'))) {
                if (Test-Path $d) {
                    try { Remove-Item -Recurse -Force $d -ErrorAction Stop }
                    catch { Write-Host "  (could not fully remove $d : $($_.Exception.Message))" -ForegroundColor DarkYellow }
                }
            }
        }
    }

    Stop-Deployed

    Write-Host "Publishing Hub (Release) -> $deployDir" -ForegroundColor Cyan
    dotnet publish $proj `
        --configuration Release `
        --output $deployDir `
        --no-self-contained `
        --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { return $false }
    if (-not (Test-Path $exe)) { return $false }
    # Publish skips copying unchanged files; stamp the exe so the next change check compares
    # against this deploy, not an older build.
    (Get-Item $exe).LastWriteTimeUtc = [DateTime]::UtcNow
    Write-Host "Deployed $exe" -ForegroundColor Green
    return $true
}

# The launch half shared by run.bat and start.bat: the deployed CLI's `hub start` if the CLI is
# also deployed (it waits until the Hub answers and logs its window), else the Hub exe directly.
function Get-LaunchBlock {
    return @"
echo.
echo  Starting the Hub (it hosts the Monitor)...
if exist "C:\Apps\IdiotProof\Cli\idiotproof.exe" (
    "C:\Apps\IdiotProof\Cli\idiotproof.exe" hub start
) else (
    start "IdiotProof Hub" /D "%~dp0" "%~dp0$exeName"
)
echo.
echo  Done. The Hub is in its own green window.
"@
}

function Write-Launcher {
    $bat = @"
@echo off
title IdiotProof Hub
rem Generated by $PSCommandPath - edits here are overwritten.
set "DEPLOY_PS1=$PSCommandPath"
echo.
echo   IdiotProof Hub
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
if not exist "%~dp0$exeName" goto notdeployed
goto launch
:notdeployed
echo  Nothing is deployed yet - deploying first.
goto deploy
:launch
$(Get-LaunchBlock)
"@
    Write-BatIfChanged $launcherBat $bat

    # start.bat: the same launch with no menu, for Task Scheduler.
    $start = @"
@echo off
title IdiotProof Hub
rem Generated by $PSCommandPath - edits here are overwritten.
if not exist "%~dp0$exeName" (
    echo  The Hub is not deployed yet - run "$PSCommandPath" first.
    exit /b 1
)
$(Get-LaunchBlock)
"@
    Write-BatIfChanged (Join-Path $deployDir 'start.bat') $start
}

# Idempotent: only (re)creates "IdiotProof Hub.lnk" on the Desktop if it is missing or stale
# (target/working-dir drifted), so re-running this script never duplicates shortcuts.
function Add-DesktopShortcut {
    if ($NoShortcut) { return }
    if (-not (Test-Path $launcherBat)) { return }
    $desktop = [Environment]::GetFolderPath('Desktop')
    $link    = Join-Path $desktop 'IdiotProof Hub.lnk'

    $shell = New-Object -ComObject WScript.Shell
    if (Test-Path -LiteralPath $link) {
        $existing = $shell.CreateShortcut($link)
        if ($existing.TargetPath -ieq $launcherBat -and $existing.WorkingDirectory -ieq $deployDir) { return }
    }
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath       = $launcherBat
    $shortcut.WorkingDirectory = $deployDir
    $shortcut.WindowStyle      = 1
    $shortcut.Description      = 'Deploy and run the IdiotProof Hub'
    $shortcut.IconLocation     = "$exe,0"
    $shortcut.Save()
    Write-Host "  Added Desktop shortcut: $link" -ForegroundColor Green
}

# ── Deploy ──────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $deployDir | Out-Null

if ($LauncherOnly) {
    Write-Launcher
    Add-DesktopShortcut
    Write-Host "Wrote $launcherBat and start.bat" -ForegroundColor Green
    exit 0
}

$ok = $true
$published = 0
if ($Force -or (Test-NeedsPublish)) {
    $published++
    if (-not (Publish-Hub)) {
        Write-Host 'Hub publish FAILED.' -ForegroundColor Red
        $ok = $false
    }
}
Write-Launcher
Add-DesktopShortcut

if ($published -eq 0) { Write-Host 'IdiotProof Hub is up to date.' -ForegroundColor DarkGray }

if (-not $ok) {
    Write-Host 'Deploy FAILED.' -ForegroundColor Red
    if (-not $Launch) { exit 1 }
    Write-Host 'Launching whatever is already deployed instead.' -ForegroundColor Yellow
}

if (-not $Launch) { exit 0 }

# Same as run.bat's launch half, without re-running this script.
Write-Host '  Starting the Hub (it hosts the Monitor)...' -ForegroundColor Cyan
$cliExe = 'C:\Apps\IdiotProof\Cli\idiotproof.exe'
if (Test-Path $cliExe) { & $cliExe hub start | Out-Host }
else { Start-Process $exe -WorkingDirectory $deployDir }
exit 0
