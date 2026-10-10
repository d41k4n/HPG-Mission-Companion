@echo off
rem Builds HPGMissionCompanion.exe with the .NET Framework 4 compiler that ships with Windows (no SDK needed).
rem Usage: tools\build.cmd [output folder]    (default: bin next to the repo root)
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set OUT=%~1
if "%OUT%"=="" set OUT=%~dp0..\bin
if not exist "%OUT%" mkdir "%OUT%"
pushd "%~dp0.."
"%FW%\csc.exe" /nologo /target:winexe /win32icon:assets\app.ico /out:"%OUT%\HPGMissionCompanion.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll src\Core.cs src\AudioFx.cs src\Tools.cs src\Engines.cs src\Server.cs src\Install.cs src\SimLink.cs src\MainForm.cs src\Program.cs
set RC=%ERRORLEVEL%
rem SimConnect.dll comes from the MSFS SDK (not stored in the repository); copy it next to the program when the SDK is installed
if %RC%==0 call :copydll
popd
exit /b %RC%

:copydll
if exist "C:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll" (
  copy /y "C:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll" "%OUT%\SimConnect.dll" >nul
  goto :eof
)
if exist "C:\MSFS SDK\SimConnect SDK\lib\SimConnect.dll" (
  copy /y "C:\MSFS SDK\SimConnect SDK\lib\SimConnect.dll" "%OUT%\SimConnect.dll" >nul
  goto :eof
)
echo WARNING: MSFS SDK not found, SimConnect.dll was not copied
goto :eof
