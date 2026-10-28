@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF

pushd "%~dp0"
if not exist "dist" mkdir "dist"

set ICONOPT=
if exist "assets\icon.ico" set ICONOPT=/win32icon:"assets\icon.ico" /resource:"assets\icon.ico",InputPolish.app.ico

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /debug- /warn:4 ^
  /win32manifest:"app.manifest" %ICONOPT% ^
  /out:"dist\InputPolish.exe" ^
  /r:"%FW%\System.Core.dll" ^
  /r:"%FW%\System.Windows.Forms.dll" ^
  /r:"%FW%\System.Drawing.dll" ^
  /r:"%FW%\System.Web.Extensions.dll" ^
  /r:"%WPF%\UIAutomationClient.dll" ^
  /r:"%WPF%\UIAutomationTypes.dll" ^
  /r:"%WPF%\WindowsBase.dll" ^
  "src\*.cs"

if errorlevel 1 (
  echo.
  echo BUILD FAILED
  popd
  exit /b 1
)
echo.
echo BUILD OK -^> dist\InputPolish.exe
popd
