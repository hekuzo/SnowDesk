@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /platform:x64 /out:SnowDesk.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll SnowDesk.cs SnowPainter.cs ControlPanel.cs
if errorlevel 1 (echo Build FAILED & exit /b 1)
echo Build OK: SnowDesk.exe
