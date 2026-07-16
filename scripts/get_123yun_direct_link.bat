@echo off
setlocal

pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0get_123yun_direct_link.ps1" %*
exit /b %ERRORLEVEL%
