@echo off
rem "run.bat" runs the key map tests.
rem "run.bat e2e" also drives real apps. It takes over the keyboard and mouse for about 3 minutes, so
rem don't type while it runs. It needs Word, Excel and Chrome, and Shaqlouba must not be running.
setlocal
cd /d "%~dp0.."

set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "OUT=%TEMP%\shaqlouba_tests"
if not exist "%OUT%" mkdir "%OUT%"

"%FW%\csc.exe" /nologo /codepage:65001 /target:exe /out:"%OUT%\keymap_tests.exe" ^
    /resource:assets\words.txt,Shaqlouba.words.txt src\KeyMap.cs src\Words.cs tests\KeyMapTests.cs
if errorlevel 1 exit /b 1
"%OUT%\keymap_tests.exe"
if errorlevel 1 exit /b 1

if /i not "%~1"=="e2e" exit /b 0

call "%~dp0..\build.bat"
if errorlevel 1 exit /b 1

"%FW%\csc.exe" /nologo /codepage:65001 /target:exe /out:"%OUT%\e2e.exe" /lib:"%FW%\WPF" ^
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.CSharp.dll ^
    /reference:System.Core.dll /reference:UIAutomationClient.dll /reference:UIAutomationTypes.dll /reference:WindowsBase.dll ^
    src\Native.cs src\ClipboardEx.cs tests\E2E.cs
if errorlevel 1 exit /b 1
"%OUT%\e2e.exe" "%CD%\Shaqlouba.exe" "%OUT%"
