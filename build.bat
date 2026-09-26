@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
rem Embed every DLL in lib\ into the exe so it runs as a single file.
set RES=
for %%f in (lib\*.dll) do set RES=!RES! /resource:"%%f",%%~nxf
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:PCStatsBar.exe ^
  /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:lib\LibreHardwareMonitorLib.dll ^
  !RES! PCStatsBar.cs
