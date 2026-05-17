@echo off
echo Building DLSS Swapper+ (Release)...
echo.

"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "src\DLSS Swapper.csproj" /p:Configuration=Release /p:Platform=x64 /t:Rebuild

if %errorlevel% neq 0 (
    echo.
    echo BUILD FAILED with error code %errorlevel%
    pause
    exit /b %errorlevel%
)

set "SRC=src\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64"
set "OUTPUT=%~dp0DLSS Swapper+"

echo.
echo Cleaning output folder...
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"

echo Copying files...
xcopy /E /Y /Q /EXCLUDE:build_exclude.txt "%SRC%\*" "%OUTPUT%\"

echo.
echo BUILD SUCCEEDED
echo.
echo Output: %OUTPUT%\
echo.
pause
