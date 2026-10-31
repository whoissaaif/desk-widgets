@echo off
rem Builds bin\DeskWidgets.exe with the C# compiler that ships with Windows (.NET Framework 4).
rem The Windows media session (Now playing) comes from the WinRT metadata in System32\WinMetadata,
rem which every Windows 10/11 PC has.
cd /d "%~dp0"
rem No character that makes code read differently from how it compiles (tools\check-hidden.ps1).
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-hidden.ps1
if errorlevel 1 exit /b 1
if not exist bin mkdir bin
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set WINMD=%WINDIR%\System32\WinMetadata
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /utf8output /codepage:65001 /out:bin\DeskWidgets.exe ^
  /win32icon:assets\app.ico /resource:assets\app.ico,DeskWidgets.app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Security.dll /r:System.Web.Extensions.dll ^
  /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll" ^
  /r:"%WINMD%\Windows.Foundation.winmd" /r:"%WINMD%\Windows.Media.winmd" /r:"%WINMD%\Windows.Storage.winmd" ^
  /recurse:src\*.cs
if errorlevel 1 exit /b 1
echo Built bin\DeskWidgets.exe - run it once and "Desk Widgets" appears in the Start menu.
