@echo off
cd /d "%~dp0"
rem Builds KinetikStats.exe (Kinetik Stats) on .NET 8: one exe, with LibreHardwareMonitor built in. Needs the .NET 8 SDK
rem to build, and the .NET 8 Desktop Runtime to run.
rem "build.bat package" also makes KinetikStats.zip for a release, and Kinetik.zip,
rem the same build named Kinetik.exe, which Kinetik 3.2 and earlier need to update themselves to Kinetik Stats.
set OUT=%LOCALAPPDATA%\Kinetik\Build\publish-KinetikStats
dotnet publish KinetikStats.csproj -c Release -nologo -v q -p:PublishSingleFile=true -o "%OUT%" || exit /b 1
copy /y "%OUT%\KinetikStats.exe" KinetikStats.new.exe >nul || exit /b 1
rem Keep previous binaries. Never delete recovery material during a build.
if not exist "..\Old Versions\" mkdir "..\Old Versions" || exit /b 1
if exist KinetikStats.exe (
  for /f %%t in ('powershell -NoProfile -Command "[Guid]::NewGuid().ToString('N')"') do move /y KinetikStats.exe "..\Old Versions\KinetikStats-%%t.exe" >nul
  if exist KinetikStats.exe exit /b 1
)
move /y KinetikStats.new.exe KinetikStats.exe >nul || exit /b 1
echo Built KinetikStats.exe
if /i not "%~1"=="package" exit /b 0
powershell -NoProfile -ExecutionPolicy Bypass -File package.ps1 -App KinetikStats
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File package-bridge.ps1
if errorlevel 1 exit /b 1
exit /b 0
