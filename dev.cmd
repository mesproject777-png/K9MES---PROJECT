@echo off
setlocal

cd /d %~dp0

echo Starting backend API...
start "MES API" cmd /k "dotnet run --project backend\K9Api\K9Api.csproj"

echo Starting Angular frontend...
start "K9 UI" cmd /k "npm start"

echo.
echo Backend:  http://localhost:5000
echo Frontend: http://localhost:4200
echo.
echo Close the opened windows to stop the servers.