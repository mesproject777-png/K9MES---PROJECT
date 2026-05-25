@echo off
setlocal

cd /d %~dp0

echo The old database initializer was removed after the backend conversion to .NET 8.
echo Start the .NET API with: dotnet run --project backend\K9Api
