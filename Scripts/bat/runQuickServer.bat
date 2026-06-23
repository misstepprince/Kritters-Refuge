@echo off
pushd "%~dp0..\.."

call dotnet run --project Content.Server --no-build %*

pause
