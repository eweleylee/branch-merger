@echo off
REM Double-click to install Branch Merger. Opens a folder picker (recommended
REM location pre-filled), then runs the installer into the folder you choose.
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Install-BranchMerger.ps1"
