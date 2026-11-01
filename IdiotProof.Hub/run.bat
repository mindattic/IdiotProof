@echo off
rem Dev convenience wrapper: double-click to deploy (if changed) and launch the Hub on its own,
rem independent of the rest of the stack (Cli, Mcp, Monitor shim, Blazor). See run.ps1 for the
rem actual logic, which also writes C:\Apps\IdiotProof\Hub\run.bat - the generated launcher that
rem the Desktop shortcut points at - and keeps that shortcut up to date.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" -Launch
pause
