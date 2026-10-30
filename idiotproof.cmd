@echo off
rem idiotproof.cmd - the `idiotproof` command line from a repo checkout (IP-T62).
rem Prefers, in order:
rem   1. the published CLI   C:\Apps\IdiotProof\Cli\idiotproof.exe
rem   2. the last local build IdiotProof.Cli\bin\Debug\net10.0\idiotproof.exe  (rebuild after pulling:
rem                           dotnet build IdiotProof.Cli)
rem   3. dotnet run --project IdiotProof.Cli  (builds first; slow, but always current)
rem Arguments pass through untouched; the exit code is the CLI's (0 ok, 1 failed or Hub not running,
rem 2 usage, 3 blocked, 4 confirmation required).
setlocal
set "IDIOTPROOF_CLI_EXE=C:\Apps\IdiotProof\Cli\idiotproof.exe"
if exist "%IDIOTPROOF_CLI_EXE%" goto run
set "IDIOTPROOF_CLI_EXE=%~dp0IdiotProof.Cli\bin\Debug\net10.0\idiotproof.exe"
if exist "%IDIOTPROOF_CLI_EXE%" goto run
dotnet run --project "%~dp0IdiotProof.Cli" --no-launch-profile -v q -- %*
exit /b %ERRORLEVEL%
:run
"%IDIOTPROOF_CLI_EXE%" %*
exit /b %ERRORLEVEL%
