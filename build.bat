@echo off
rem Builds bin\DeskWidgets.exe with the C# compiler that ships with Windows (.NET Framework 4).
cd /d "%~dp0"
if not exist bin mkdir bin
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /utf8output /codepage:65001 /out:bin\DeskWidgets.exe ^
  /win32icon:assets\app.ico /resource:assets\app.ico,DeskWidgets.app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll ^
  /recurse:src\*.cs
if errorlevel 1 exit /b 1
echo Built bin\DeskWidgets.exe - run it once and "Desk Widgets" appears in the Start menu.
