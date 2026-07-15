@echo off
setlocal EnableExtensions EnableDelayedExpansion

if defined HALOCREEK_TERMINAL_TITLE (
    title !HALOCREEK_TERMINAL_TITLE!
)

if "%~1"=="" (
    echo Usage: %~nx0 [-t] ^<session^>
    exit /b 2
)

if /I "%~1"=="-t" (
    if "%~2"=="" (
        echo Usage: %~nx0 [-t] ^<session^>
        exit /b 2
    )
    set "HALOCREEK_PSMUX_ATTACH_TARGET=%~2"
) else (
    set "HALOCREEK_PSMUX_ATTACH_TARGET=%~1"
)

set "PSMUX_SESSION="
set "PSMUX_SESSION_NAME=%HALOCREEK_PSMUX_ATTACH_TARGET%"
set "PSMUX_TARGET_SESSION=%HALOCREEK_PSMUX_ATTACH_TARGET%"
set "PSMUX_REMOTE_ATTACH=1"

psmux has-session -t "%HALOCREEK_PSMUX_ATTACH_TARGET%" >nul 2>nul
if errorlevel 1 (
    echo psmux session not found: %HALOCREEK_PSMUX_ATTACH_TARGET%
    exit /b 1
)

psmux
exit /b %ERRORLEVEL%
