@echo off
cd /d "%~dp0"
rem Builds KinetikStats.exe (Kinetik Stats). It loads LibreHardwareMonitor and its dependencies from the lib\ folder next to it.
rem Now playing reads Windows' media controls, so it references the Windows Runtime metadata that ships with Windows 10 and 11.
rem "build.bat package" also makes KinetikStats.zip (KinetikStats.exe + lib\*.dll) for a release, and Kinetik.zip,
rem the same build named Kinetik.exe, which Kinetik 3.2 and earlier need to update themselves to Kinetik Stats.
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:KinetikStats.new.exe /win32icon:icon.ico /resource:icon.ico,icon.ico ^
  /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll;%SystemRoot%\System32\WinMetadata\Windows.Foundation.winmd;%SystemRoot%\System32\WinMetadata\Windows.Media.winmd;%SystemRoot%\System32\WinMetadata\Windows.Storage.winmd;System.Runtime.dll /r:lib\LibreHardwareMonitorLib.dll ^
  KinetikStats.cs SuiteSupport.cs || exit /b 1
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
