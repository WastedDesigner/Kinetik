@echo off
cd /d "%~dp0"
rem Builds go to C:\KinetikApps (outside OneDrive, exempt from the antivirus); earlier ones to its Old Versions.
set DEST=C:\KinetikApps\Kinetik Stats\KinetikStats
set OLD=C:\KinetikApps\Kinetik Stats\Old Versions
rem Builds KinetikStats.exe (Kinetik Stats) on .NET 8: one exe, with LibreHardwareMonitor built in. Needs the .NET 8 SDK
rem to build, and the .NET 8 Desktop Runtime to run.
rem "build.bat package" also makes KinetikStats.zip for a release, and Kinetik.zip,
rem the same build named Kinetik.exe, which Kinetik 3.2 and earlier need to update themselves to Kinetik Stats.
set OUT=%LOCALAPPDATA%\Kinetik\Build\publish-KinetikStats
dotnet publish KinetikStats.csproj -c Release -nologo -v q -p:PublishSingleFile=true -o "%OUT%" || exit /b 1
copy /y "%OUT%\KinetikStats.exe" KinetikStats.new.exe >nul || exit /b 1
rem Keep previous binaries. Never delete recovery material during a build.
if not exist "%DEST%\" mkdir "%DEST%" || exit /b 1
if not exist "%OLD%\" mkdir "%OLD%" || exit /b 1
if exist "%DEST%\KinetikStats.exe" (
  for /f %%t in ('powershell -NoProfile -Command "[Guid]::NewGuid().ToString('N')"') do move /y "%DEST%\KinetikStats.exe" "%OLD%\KinetikStats-%%t.exe" >nul
  if exist "%DEST%\KinetikStats.exe" exit /b 1
)
move /y KinetikStats.new.exe "%DEST%\KinetikStats.exe" >nul || exit /b 1
if exist lib\ xcopy /e /i /y /q lib "%DEST%\lib" >nul
set SIGNEXE=%DEST%\KinetikStats.exe
rem Sign with the personal certificate (CurrentUser\My). No certificate: the build stays unsigned.
powershell -NoProfile -Command "$c = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object Subject -eq 'CN=WastedDesigner Kinetik (personal code signing)' | Select-Object -First 1; if (-not $c) { 'Not signed: personal certificate not found'; exit 0 }; $s = Set-AuthenticodeSignature $env:SIGNEXE -Certificate $c -HashAlgorithm SHA256; 'Signed: ' + $s.Status"
echo Built %DEST%\KinetikStats.exe
if /i not "%~1"=="package" exit /b 0
rem Packaging reads the exe from this folder: copy it in for the zip, then take it out again.
copy /y "%DEST%\KinetikStats.exe" KinetikStats.exe >nul || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File package.ps1 -App KinetikStats
if errorlevel 1 (del KinetikStats.exe & exit /b 1)
powershell -NoProfile -ExecutionPolicy Bypass -File package-bridge.ps1
if errorlevel 1 (del KinetikStats.exe & exit /b 1)
del KinetikStats.exe
exit /b 0
