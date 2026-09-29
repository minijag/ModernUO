@echo off
cd /d "%~dp0"
set "MOTION_PYTHON=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\pythonw.exe"
if exist "%MOTION_PYTHON%" (
    start "" "%MOTION_PYTHON%" "%~dp0AnimationViewer.py"
) else (
    python AnimationViewer.py
    if errorlevel 1 pause
)
