@echo off
setlocal
pushd "%~dp0..\.." || goto pushd_failed

call git submodule update --init --recursive
set "STATUS=%ERRORLEVEL%"
if not %STATUS% equ 0 goto finished
call dotnet build -m:1 -c Debug %*
set "STATUS=%ERRORLEVEL%"

:finished
popd
pause
exit /b %STATUS%

:pushd_failed
echo Failed to enter the repository root.
pause
exit /b 1
