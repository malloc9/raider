@echo off
rem Builds and opens the Raider desktop app: Accounts, their Chores, and "Run now".
setlocal
cd /d "%~dp0"

echo Building Raider...
dotnet build src\Raider.Desktop\Raider.Desktop.csproj -c Release -v quiet -nologo
if errorlevel 1 (
    echo.
    echo Build failed. See the errors above.
    pause
    exit /b 1
)

for /r "src\Raider.Desktop\bin\Release" %%F in (Raider.Desktop.exe) do (
    if exist "%%F" set "RAIDER_EXE=%%F"
)
if not defined RAIDER_EXE (
    echo Could not find Raider.Desktop.exe after the build.
    pause
    exit /b 1
)

start "" "%RAIDER_EXE%"
endlocal
