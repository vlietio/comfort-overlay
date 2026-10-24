@echo off
rem Rebuilds "Comfort Overlay.exe" in the folder above, using the C# compiler built into Windows.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ ^
  /win32icon:icon.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /out:"..\Comfort Overlay.exe" ComfortOverlay.cs
if errorlevel 1 pause
