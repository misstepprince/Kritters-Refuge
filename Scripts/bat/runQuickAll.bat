@echo off
setlocal
pushd "%~dp0..\.." || goto pushd_failed

set "STATUS=0"
start "" "%~dp0runQuickServer.bat" %*
if errorlevel 1 set "STATUS=%ERRORLEVEL%"
start "" "%~dp0runQuickClient.bat" %*
if errorlevel 1 set "STATUS=%ERRORLEVEL%"

popd
exit /b %STATUS%

:pushd_failed
echo Failed to enter the repository root.
exit /b 1
