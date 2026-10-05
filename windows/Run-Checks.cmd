@echo off
setlocal
pushd "%~dp0"
if not exist "Shotlight.exe" (
  echo Run this script beside the published Shotlight.exe.
  popd
  pause
  exit /b 1
)
start "" /wait "%~dp0Shotlight.exe" --run-checks --report "%~dp0Shotlight-checks.txt"
set "SHOTLIGHT_CHECK_RESULT=%ERRORLEVEL%"
if exist "Shotlight-checks.txt" type "Shotlight-checks.txt"
echo.
echo Check exit code: %SHOTLIGHT_CHECK_RESULT%
popd
pause
exit /b %SHOTLIGHT_CHECK_RESULT%
