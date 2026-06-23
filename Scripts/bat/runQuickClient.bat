@echo off
pushd "%~dp0..\.."

call dotnet run --project Content.Client --no-build %*

pause
