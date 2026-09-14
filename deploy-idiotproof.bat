@echo off
rem Desktop entry point: republish IdiotProof from source to C:\Apps\IdiotProof\, then start it.
rem
rem Same convention as the Prose repo's deploy-*.bat files: one icon per app, each republishing
rem before it launches, so you can never be looking at a stale build.
rem
rem Lives in the repo rather than in C:\Apps\IdiotProof\ (where Start.bat already does this job),
rem because that folder is a build output that can be deleted and regenerated at any time - an icon
rem pointing into it breaks the first time someone clears it. The repo is the thing that survives.
rem
rem publish-all.ps1 -Launch publishes Monitor and Blazor, writes the in-folder launch scripts, and
rem then runs C:\Apps\IdiotProof\launch.bat, which starts both and opens the browser.
title Deploy IdiotProof
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish-all.ps1" -Launch
if errorlevel 1 (
    echo.
    echo   DEPLOY FAILED -- see the output above. Nothing was launched.
    pause
    exit /b 1
)
