@echo off
rem Builds Shaqlouba.exe with the C# compiler that ships with Windows. Nothing to install.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find csc.exe. .NET Framework 4.5 or newer is required.
    exit /b 1
)

"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:Shaqlouba.exe ^
    /win32icon:assets\icon.ico /resource:assets\icon.ico,Shaqlouba.icon.ico /resource:assets\words.txt,Shaqlouba.words.txt ^
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
    src\*.cs
if errorlevel 1 exit /b 1

echo Built Shaqlouba.exe
