@echo off
setlocal
pushd "%~dp0..\.." || goto pushd_failed

if not exist Scripts\logs mkdir Scripts\logs

set "LOG=Scripts\logs\Content.Tests.log"

call dotnet test Content.Tests/Content.Tests.csproj -m:1 -c DebugOpt %* -- NUnit.ConsoleOut=0 > "%LOG%" 2>&1
set "STATUS=%ERRORLEVEL%"

if %STATUS% equ 0 (
    echo Tests passed. Log written to %LOG%.
) else (
    echo Tests failed. Log written to %LOG%.
    powershell -NoProfile -Command "Get-Content '%LOG%' -Tail 80"
)

popd
pause
exit /b %STATUS%

:pushd_failed
echo Failed to enter the repository root.
pause
exit /b 1
