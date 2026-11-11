#Requires -Version 5.1
<#
.SYNOPSIS
    Runs one IdiotProof scheduled job (IP-T520): each -Run is an `idiotproof` command line, run in order
    through the CLI (so through the Hub); output and the real exit code go to a log.

.DESCRIPTION
    MindAttic.Scheduler starts jobs headless (conhost --headless), so a job's console output and exit code
    are otherwise lost: a CLI call that never reached the Hub still showed "result 0" in Task Scheduler.
    This appends a start line, every command's output and its exit code to
    %LOCALAPPDATA%\MindAttic\IdiotProof\Logs\scheduler-jobs.log, and exits with the first non-zero code.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\scheduled-job.ps1 -Job IdiotProof.MomentumCollect -Run "momentum import; momentum extract"
    -Run is one string (powershell -File passes it whole): commands separated by ';', an argument with spaces in 'single quotes'.
#>
param(
    [Parameter(Mandatory)] [string]$Job,
    [Parameter(Mandatory)] [string]$Run,
    [string]$Cli = 'C:\Apps\IdiotProof\Cli\idiotproof.exe'
)

$logDir = Join-Path $env:LOCALAPPDATA 'MindAttic\IdiotProof\Logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir 'scheduler-jobs.log'
$et = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
function Stamp { [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $et).ToString('yyyy-MM-dd HH:mm:ss') + ' ET' }
function Log([string]$line) { Add-Content -Path $log -Value $line -Encoding UTF8 }

Log "$(Stamp) [$Job] start (pid $PID)"
$failed = 0
foreach ($command in ($Run -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
    # split like a shell: 'quoted' or "quoted" words stay one argument
    $argv = @([regex]::Matches($command, "'([^']*)'|`"([^`"]*)`"|(\S+)") | ForEach-Object {
        if ($_.Groups[1].Success) { $_.Groups[1].Value } elseif ($_.Groups[2].Success) { $_.Groups[2].Value } else { $_.Groups[3].Value } })
    $started = Get-Date
    try {
        $output = & $Cli @argv 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    catch {
        $output = @("could not run $Cli : $($_.Exception.Message)")
        $code = 1
    }
    foreach ($o in ($output | Select-Object -Last 40)) { Log "    $o" }
    Log ("{0} [{1}] '{2}' exit {3} in {4:N0}s" -f (Stamp), $Job, $command, $code, ((Get-Date) - $started).TotalSeconds)
    if ($code -ne 0 -and $failed -eq 0) { $failed = $code }
}
exit $failed
