@echo off
setlocal

cd /d %~dp0

echo Starting backend API...
start "MES API" cmd /k "dotnet run --project backend\\K9Api"

echo Starting Angular frontend...
start "K9 UI" cmd /k ".\\node_modules\\.bin\\ng.cmd serve --port 4200"

echo.
echo Backend:  http://localhost:5000
echo Frontend: http://localhost:4200
echo.
echo Close the opened windows to stop the servers.
