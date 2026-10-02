@echo off
cd /d "%~dp0"
rem Builds Kinetik.exe. It loads LibreHardwareMonitor and its dependencies from the lib\ folder next to it.
rem "build.bat package" also makes Kinetik.zip (Kinetik.exe + lib\*.dll) for a release.
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:Kinetik.exe /win32icon:icon.ico /resource:icon.ico,icon.ico ^
  /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:lib\LibreHardwareMonitorLib.dll ^
  Kinetik.cs || exit /b 1
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
