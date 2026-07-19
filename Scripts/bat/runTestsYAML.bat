@echo off
setlocal
pushd "%~dp0..\.." || goto pushd_failed

if not exist Scripts\logs mkdir Scripts\logs

set "LOG=Scripts\logs\Content.YAMLLinter.log"

call dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj -m:1 -c DebugOpt %* > "%LOG%" 2>&1
set "STATUS=%ERRORLEVEL%"
if not %STATUS% equ 0 goto fail

call dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c DebugOpt --no-build -- NUnit.ConsoleOut=0 >> "%LOG%" 2>&1
set "STATUS=%ERRORLEVEL%"
if %STATUS% equ 0 (
    echo YAML tests passed. Log written to %LOG%.
) else (
    goto fail
)
goto end

:fail
echo YAML tests failed. Log written to %LOG%.
if exist "%LOG%" powershell -NoProfile -Command "Get-Content '%LOG%' -Tail 80"

:end
popd
pause
exit /b %STATUS%

:pushd_failed
echo Failed to enter the repository root.
pause
exit /b 1
