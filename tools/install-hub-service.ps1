#Requires -Version 5.1
<#
.SYNOPSIS
    Registers the IdiotProof Hub as a Windows service that runs as the CURRENT USER (IP-T288).

.DESCRIPTION
    The Hub must run as you, not LocalSystem: LocalDB ((localdb)\MSSQLLocalDB) and the Data
    Protection key ring that decrypts your broker keys are per-user, so a LocalSystem service would
    see an empty database and could not read anyone's keys.

    What it does (elevating itself if needed):
      1. asks for your Windows password (Get-Credential, pre-filled with DOMAIN\you);
      2. grants that account "Log on as a service" (SeServiceLogonRight) via secedit, if missing;
      3. sc.exe create IdiotProof.Hub binPath= <exe> obj= <you> password= <...> start= delayed-auto;
      4. sc.exe description / failure (restart after a crash, twice a day at most);
      5. writes the service's environment: IDIOTPROOF_HUB_MONITOR=0 unless -Monitor, and
         IDIOTPROOF_HUB_LOG=%LOCALAPPDATA%\MindAttic\IdiotProof\Logs\hub-service.log.
    It does NOT start the service unless -Start. Stop any interactive Hub first (`idiotproof hub
    stop`): both listen on 127.0.0.1:5910 and the second one exits with "port in use".

    The same commands are what `idiotproof hub install-service` runs from an elevated Hub.

    Agents never run this: it changes system configuration and, with -Monitor, starts unattended
    trading at boot. Run it yourself.

.PARAMETER Exe
    The Hub exe (default C:\Apps\IdiotProof\Hub\idiotproof-hub.exe, published by run.ps1).

.PARAMETER Monitor
    Host the trading Monitor in the service (IDIOTPROOF_HUB_MONITOR=1). Off by default.

.PARAMETER StartType
    delayed-auto (default), auto or demand.

.PARAMETER Start
    Start the service after installing it.

.PARAMETER Uninstall
    Stop (gracefully) and remove the service instead.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install-hub-service.ps1
    powershell -ExecutionPolicy Bypass -File tools\install-hub-service.ps1 -Monitor -Start
    powershell -ExecutionPolicy Bypass -File tools\install-hub-service.ps1 -Uninstall
#>
param(
    [string]$Exe = 'C:\Apps\IdiotProof\Hub\idiotproof-hub.exe',
    [switch]$Monitor,
    [ValidateSet('delayed-auto', 'auto', 'demand')]
    [string]$StartType = 'delayed-auto',
    [switch]$Start,
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$serviceName = 'IdiotProof.Hub'

# ── Elevate (re-run this script as administrator with the same arguments) ─────
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-NoExit', '-File', "`"$PSCommandPath`"", '-Exe', "`"$Exe`"", '-StartType', $StartType)
    if ($Monitor) { $forward += '-Monitor' }
    if ($Start) { $forward += '-Start' }
    if ($Uninstall) { $forward += '-Uninstall' }
    Write-Host 'Elevating (the service control manager needs an administrator)...' -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList $forward
    exit 0
}

function Invoke-Checked([string]$File, [string[]]$Arguments, [string]$What, [int[]]$OkCodes = @(0)) {
    & $File @Arguments | Out-Host
    if ($OkCodes -notcontains $LASTEXITCODE) { throw "$What failed (exit $LASTEXITCODE)." }
}

# ── Uninstall ────────────────────────────────────────────────────────────────
if ($Uninstall) {
    if (-not (Get-Service $serviceName -ErrorAction SilentlyContinue)) {
        Write-Host "$serviceName is not installed." -ForegroundColor DarkGray
        exit 0
    }
    # Stop is graceful: the Hub drains running operations, stops the Monitor, releases the lease.
    Invoke-Checked 'sc.exe' @('stop', $serviceName) 'stop' @(0, 1062)
    (Get-Service $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(90))
    Invoke-Checked 'sc.exe' @('delete', $serviceName) 'delete'
    Write-Host "$serviceName removed." -ForegroundColor Green
    exit 0
}

# ── Install ──────────────────────────────────────────────────────────────────
if (-not (Test-Path $Exe)) { throw "$Exe does not exist - publish first (run.ps1 or tools\publish-all.ps1)." }
if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    throw "$serviceName is already installed. Remove it first: tools\install-hub-service.ps1 -Uninstall"
}

$domain = if ($env:USERDOMAIN -and $env:USERDOMAIN -ne $env:COMPUTERNAME) { $env:USERDOMAIN } else { '.' }
$account = "$domain\$env:USERNAME"
$credential = Get-Credential -UserName $account -Message "Windows password for $account (the IdiotProof Hub service runs as you)"
if (-not $credential) { throw 'No credential given.' }
$password = $credential.GetNetworkCredential().Password

# "Log on as a service" for the account, or the service fails to start with error 1069.
$sid = (New-Object Security.Principal.NTAccount($credential.UserName.Replace('.\', "$env:COMPUTERNAME\"))).Translate([Security.Principal.SecurityIdentifier]).Value
$work = Join-Path $env:TEMP "idiotproof-secedit-$PID"
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    secedit /export /cfg "$work\current.inf" /areas USER_RIGHTS | Out-Null
    $line = (Select-String -Path "$work\current.inf" -Pattern '^SeServiceLogonRight\s*=' | Select-Object -First 1)
    $current = if ($line) { ($line.Line -split '=', 2)[1].Trim() } else { '' }
    if ($current -notmatch [regex]::Escape("*$sid")) {
        $value = if ($current) { "$current,*$sid" } else { "*$sid" }
        @('[Unicode]', 'Unicode=yes', '[Version]', 'signature="$CHICAGO$"', 'Revision=1', '[Privilege Rights]', "SeServiceLogonRight = $value") |
            Set-Content -Path "$work\grant.inf" -Encoding Unicode
        secedit /configure /db "$work\grant.sdb" /cfg "$work\grant.inf" /areas USER_RIGHTS | Out-Null
        Write-Host "Granted 'Log on as a service' to $($credential.UserName)." -ForegroundColor Green
    }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Invoke-Checked 'sc.exe' @('create', $serviceName, 'binPath=', "`"$Exe`"", 'start=', $StartType, 'obj=', $credential.UserName, 'password=', $password, 'DisplayName=', 'IdiotProof Hub') 'create'
$password = $null
$description = 'IdiotProof Hub: serves every idiotproof CLI/MCP operation on 127.0.0.1' + $(if ($Monitor) { ' and hosts the trading Monitor.' } else { ' (Monitor off).' })
Invoke-Checked 'sc.exe' @('description', $serviceName, $description) 'description'
Invoke-Checked 'sc.exe' @('failure', $serviceName, 'reset=', '86400', 'actions=', 'restart/60000/restart/60000//0') 'failure actions'

# The service's environment (REG_MULTI_SZ under the service key).
$windowLog = Join-Path $env:LOCALAPPDATA 'MindAttic\IdiotProof\Logs\hub-service.log'
$environment = @("IDIOTPROOF_HUB_MONITOR=$(if ($Monitor) { '1' } else { '0' })", "IDIOTPROOF_HUB_LOG=$windowLog")
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName" -Name 'Environment' -PropertyType MultiString -Value $environment -Force | Out-Null

Write-Host ''
Write-Host "Installed $serviceName as $($credential.UserName) -> $Exe" -ForegroundColor Green
Write-Host "  Monitor: $(if ($Monitor) { 'HOSTED - the service evaluates and trades active strategies' } else { 'off (re-install with -Monitor to host it)' })"
Write-Host "  Window log: $windowLog   (idiotproof hub logs, or GET /api/logs)"

if ($Start) {
    Invoke-Checked 'sc.exe' @('start', $serviceName) 'start'
    Write-Host "Started. Check it with: idiotproof hub status" -ForegroundColor Green
}
else {
    Write-Host "Start it with: sc.exe start $serviceName   (stop any interactive Hub first: idiotproof hub stop)"
}
