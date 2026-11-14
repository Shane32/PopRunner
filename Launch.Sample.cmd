@echo off
cd /d "%~dp0"
if exist "artifacts\demo\PopRunner.Sample.exe" (
    start "" "artifacts\demo\PopRunner.Sample.exe"
) else (
    dotnet run --project src\PopRunner.Sample -c Release
)
