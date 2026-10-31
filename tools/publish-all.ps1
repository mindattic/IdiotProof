#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes every deployable IdiotProof project to C:\Apps\IdiotProof\ with a clean rebuild.

.DESCRIPTION
    A thin, named entry point over the repo-root run.ps1 (the one publish/launch script), so
    there is exactly one list of components to keep current. It publishes:

      Hub      C:\Apps\IdiotProof\Hub\idiotproof-hub.exe       hosts the Monitor and every operation
      Cli      C:\Apps\IdiotProof\Cli\idiotproof.exe           the `idiotproof` command line (added to the user PATH)
      Mcp      C:\Apps\IdiotProof\Mcp\idiotproof-mcp.exe       the MCP server Claude talks to
      Monitor  C:\Apps\IdiotProof\Monitor\idiotproof-monitor.exe  backward-compatible shim that runs the Hub
      Blazor   C:\Apps\IdiotProof\Blazor\IdiotProof.Blazor.exe

    and regenerates C:\Apps\IdiotProof\run.bat and start.bat. A running deployed Hub is asked to
    stop gracefully first (it drains running operations, stops the Monitor loop and releases the
    leader lease).

    NEVER run this unattended: the deployed Hub may be trading real money. Agents run it only when
    the user explicitly asks.

.PARAMETER Changed
    Republish only the components whose sources changed since their last deploy (run.ps1 without -Force).

.PARAMETER Launch
    Start the Hub and Blazor afterwards (run.ps1 -Launch).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\publish-all.ps1
    powershell -ExecutionPolicy Bypass -File tools\publish-all.ps1 -Changed -Launch
#>
param(
    [switch]$Changed,
    [switch]$Launch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$run = Join-Path (Split-Path -Parent $PSScriptRoot) 'run.ps1'
$forward = @{}
if (-not $Changed) { $forward['Force'] = $true }
if ($Launch) { $forward['Launch'] = $true }
& $run @forward
exit $LASTEXITCODE
