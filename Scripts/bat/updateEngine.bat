@echo off
pushd "%~dp0..\.."

call git submodule update --init --recursive
