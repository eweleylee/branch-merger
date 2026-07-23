@echo off
REM Double-click to build the release assets locally (no GitHub upload).
REM Produces Setup.exe + update files in the .\releases folder, then you
REM create the GitHub release and upload those files manually.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pack.ps1"
echo.
pause
