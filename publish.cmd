@echo off
REM Publication Native AOT, win-x64, executable autonome.
REM
REM L'edition de liens native passe par link.exe : le script s'initialise donc dans
REM l'environnement des outils Visual Studio C++ avant d'appeler dotnet publish.
REM Prerequis : charge de travail "Developpement Desktop en C++" (MSVC + Windows SDK).
REM (Fichier volontairement sans accents : cmd.exe le lit en page de code OEM.)

setlocal

set "VSINSTALLER=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer"
set "VSWHERE=%VSINSTALLER%\vswhere.exe"

REM Les cibles ILC appellent vswhere.exe depuis le PATH pour localiser link.exe.
set "PATH=%VSINSTALLER%;%PATH%"

if not exist "%VSWHERE%" (
    echo [erreur] vswhere.exe introuvable. Installez les outils de build Visual Studio C++.
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSPATH=%%i"

if not defined VSPATH (
    echo [erreur] Outils MSVC x64 introuvables.
    exit /b 1
)

call "%VSPATH%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1

dotnet publish "%~dp0src\App.Host\App.Host.csproj" -c Release -o "%~dp0publish" %*
if errorlevel 1 exit /b 1

echo.
echo Executable : %~dp0publish\Banog.exe
endlocal
