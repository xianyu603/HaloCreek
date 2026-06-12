@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0check_deps.ps1" %*
exit /b %ERRORLEVEL%
