@echo off
cd /d "%~dp0"
rem Builds Kinetik.exe. It loads LibreHardwareMonitor and its dependencies from the lib\ folder next to it.
rem Now playing reads Windows' media controls, so it references the Windows Runtime metadata that ships with Windows 10 and 11.
rem "build.bat package" also makes Kinetik.zip (Kinetik.exe + lib\*.dll) for a release.
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:Kinetik.new.exe /win32icon:icon.ico /resource:icon.ico,icon.ico ^
  /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll;%SystemRoot%\System32\WinMetadata\Windows.Foundation.winmd;%SystemRoot%\System32\WinMetadata\Windows.Media.winmd;%SystemRoot%\System32\WinMetadata\Windows.Storage.winmd;System.Runtime.dll /r:lib\LibreHardwareMonitorLib.dll ^
  Kinetik.cs || exit /b 1
rem Built under a new name first, so this works while the app is running. The exe it replaces goes to ..\Old Versions
rem (when that folder is there), stamped with the date and time, to go back to if a build misbehaves.
if exist Kinetik.exe if exist "..\Old Versions\" (
  for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd-HHmmss"') do move /y Kinetik.exe "..\Old Versions\Kinetik-%%t.exe" >nul
)
if exist Kinetik.exe move /y Kinetik.exe Kinetik.old.exe >nul
move /y Kinetik.new.exe Kinetik.exe >nul || exit /b 1
if exist Kinetik.old.exe del Kinetik.old.exe 2>nul
echo Built Kinetik.exe
if /i not "%~1"=="package" exit /b 0
if exist dist rmdir /s /q dist
mkdir dist\Kinetik\lib
copy /y Kinetik.exe dist\Kinetik\ >nul
copy /y lib\*.dll dist\Kinetik\lib\ >nul
copy /y LICENSE dist\Kinetik\ >nul
if exist Kinetik.zip del Kinetik.zip
powershell -NoProfile -Command "Compress-Archive -Path dist\Kinetik\* -DestinationPath Kinetik.zip"
rmdir /s /q dist
echo Made Kinetik.zip
