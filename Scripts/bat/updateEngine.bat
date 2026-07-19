@echo off
setlocal
pushd "%~dp0..\.." || goto pushd_failed

call git submodule update --init --recursive
set "STATUS=%ERRORLEVEL%"

popd
exit /b %STATUS%

:pushd_failed
echo Failed to enter the repository root.
exit /b 1
