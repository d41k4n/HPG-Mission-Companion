@echo off
rem Builds HPGMissionCompanion.exe and the installer HPGMissionCompanionSetup.exe with the .NET Framework 4 compiler that ships with Windows (no SDK needed).
rem Usage: tools\build.cmd [output folder]    (default: bin next to the repo root)
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set OUT=%~1
if "%OUT%"=="" set OUT=%~dp0..\bin
if not exist "%OUT%" mkdir "%OUT%"
pushd "%~dp0.."
"%FW%\csc.exe" /nologo /target:winexe /win32icon:assets\app.ico /resource:docs\DEVELOPER_GUIDE.md,DEVELOPER_GUIDE.md /resource:profiles\Recommended.json,profile.Recommended.json /out:"%OUT%\HPGMissionCompanion.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll src\Core.cs src\AudioFx.cs src\Tools.cs src\Engines.cs src\Server.cs src\Install.cs src\Mixer.cs src\Names.cs src\VoicePacks.cs src\SimLink.cs src\Updates.cs src\Profiles.cs src\NotesForm.cs src\MainForm.cs src\Program.cs
set RC=%ERRORLEVEL%
rem the voice packs of the repository are copied only when the runtime folder does not have them yet (install newer ones with --install-pack or drag and drop, which archive the old version)
if %RC%==0 if exist "voicepacks\*.xml" (
  if not exist "%OUT%\voicepacks" mkdir "%OUT%\voicepacks"
  for %%F in (voicepacks\*.xml) do if not exist "%OUT%\voicepacks\%%~nxF" copy /y "%%F" "%OUT%\voicepacks\%%~nxF" >nul
)
rem SimConnect.dll comes from the MSFS SDK (not stored in the repository); copy it next to the program when the SDK is installed
if %RC%==0 call :copydll
rem the installer (setup\Setup.cs) embeds the program, SimConnect.dll when it is next to the program and the voice packs of the repository
if %RC%==0 if not defined NOSETUP call :setup
popd
exit /b %RC%

:setup
setlocal enabledelayedexpansion
set RES=/resource:"%OUT%\HPGMissionCompanion.exe",app.exe
if exist "%OUT%\SimConnect.dll" set RES=!RES! /resource:"%OUT%\SimConnect.dll",SimConnect.dll
for %%F in (voicepacks\*.xml) do set RES=!RES! /resource:"%%F",pack.%%~nxF
"%FW%\csc.exe" /nologo /target:winexe /win32icon:assets\app.ico /out:"%OUT%\HPGMissionCompanionSetup.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll !RES! setup\Setup.cs
endlocal & set RC=%ERRORLEVEL%
goto :eof

:copydll
for %%D in (A B C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
  if exist "%%D:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll" (
    echo Copying SimConnect.dll from %%D:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll
    copy /y "%%D:\MSFS 2024 SDK\SimConnect SDK\lib\SimConnect.dll" "%OUT%\SimConnect.dll" >nul
    goto :eof
  )
)
for %%D in (A B C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
  if exist "%%D:\MSFS SDK\SimConnect SDK\lib\SimConnect.dll" (
    echo Copying SimConnect.dll from %%D:\MSFS SDK\SimConnect SDK\lib\SimConnect.dll
    copy /y "%%D:\MSFS SDK\SimConnect SDK\lib\SimConnect.dll" "%OUT%\SimConnect.dll" >nul
    goto :eof
  )
)
echo WARNING: MSFS SDK not found, SimConnect.dll was not copied
goto :eof
