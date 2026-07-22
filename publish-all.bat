@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0tools\publish-all.ps1" %*
