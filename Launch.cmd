@echo off
cd /d "%~dp0"
if not exist "artifacts\native\PopRunner.exe" (
    dotnet publish src\PopRunner -c Release -o artifacts\native
    if errorlevel 1 exit /b 1
)
start "" "artifacts\native\PopRunner.exe"
