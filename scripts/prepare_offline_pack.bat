@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepare_offline_pack.ps1" %*
exit /b %ERRORLEVEL%
