@echo off
setlocal

pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_latest_offline_to_123yun.ps1" %*
exit /b %ERRORLEVEL%
