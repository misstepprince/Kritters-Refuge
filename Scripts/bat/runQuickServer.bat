@echo off
setlocal
pushd "%~dp0..\.." || goto pushd_failed

call dotnet run --project Content.Server --no-build %*
set "STATUS=%ERRORLEVEL%"

popd
pause
exit /b %STATUS%

:pushd_failed
echo Failed to enter the repository root.
pause
exit /b 1
