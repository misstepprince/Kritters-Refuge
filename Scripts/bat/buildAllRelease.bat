@echo off
pushd "%~dp0..\.."

call git submodule update --init --recursive
call dotnet build -m:1 -c Release %*

pause
