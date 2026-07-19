@echo off
setlocal
pushd "%~dp0..\.." || goto pushd_failed

if not exist Scripts\logs mkdir Scripts\logs

set "LOG=Scripts\logs\Content.IntegrationTests.log"

call dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -m:1 -c DebugOpt %* -- NUnit.ConsoleOut=0 NUnit.MapWarningTo=Failed > "%LOG%" 2>&1
set "STATUS=%ERRORLEVEL%"

if %STATUS% equ 0 (
    echo Integration tests passed. Log written to %LOG%.
) else (
    echo Integration tests failed. Log written to %LOG%.
    powershell -NoProfile -Command "Get-Content '%LOG%' -Tail 80"
)

popd
pause
exit /b %STATUS%

:pushd_failed
echo Failed to enter the repository root.
pause
exit /b 1
