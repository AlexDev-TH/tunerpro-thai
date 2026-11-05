@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4 is required.
  exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ %EXTRA_FLAGS% /win32manifest:src\app.manifest /resource:lang\th.txt,th.txt /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:dist\TunerPro-Thai-Setup.exe src\Setup.cs
if errorlevel 1 exit /b 1
echo Built dist\TunerPro-Thai-Setup.exe
