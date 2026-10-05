@echo off
setlocal
pushd "%~dp0"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
dotnet run --project "Shotlight.Core.Checks" -c Release
if errorlevel 1 goto failure
dotnet publish "Shotlight\Shotlight.csproj" -c Release -r win-x64 --self-contained true -o "artifacts\win-x64"
if errorlevel 1 goto failure
copy /y "README.txt" "artifacts\win-x64\README.txt" >nul
copy /y "Run-Checks.cmd" "artifacts\win-x64\Run-Checks.cmd" >nul
echo.
echo Built artifacts\win-x64\Shotlight.exe
popd
pause
exit /b 0
:failure
echo Build or checks failed. Review the output above.
popd
pause
exit /b 1
