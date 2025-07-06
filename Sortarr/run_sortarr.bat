@echo off
REM Batch script to run Sortarr in automated mode from the project directory
SET SORTARR_PATH="%~dp0Sortarr.exe"
IF NOT EXIST %SORTARR_PATH% (
    ECHO Sortarr.exe not found in %~dp0
    EXIT /B 1
)
START "" %SORTARR_PATH% --auto
ECHO Sortarr launched in automated mode
EXIT /B 0