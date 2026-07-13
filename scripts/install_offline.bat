@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_offline.ps1" %*
exit /b %ERRORLEVEL%
