@echo off
setlocal

cd /d %~dp0

set BACKEND_PORT=5100
set FRONTEND_PORT=4400

echo Stopping old K9 dev servers on ports %BACKEND_PORT% and %FRONTEND_PORT%...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ports = @(%BACKEND_PORT%, %FRONTEND_PORT%); $processIds = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $ports -contains $_.LocalPort } | Select-Object -ExpandProperty OwningProcess -Unique; foreach ($processId in $processIds) { if ($processId) { try { $process = Get-Process -Id $processId -ErrorAction Stop; Write-Host ('Stopping PID {0} ({1})' -f $processId, $process.ProcessName); Stop-Process -Id $processId -Force } catch {} } }"
timeout /t 2 /nobreak >nul

echo Starting backend API...
start "K9 API" cmd /k "dotnet run --project backend\K9Api\K9Api.csproj"

echo Starting Angular frontend...
start "K9 UI" cmd /k "npm start"

echo.
echo Backend:  http://localhost:%BACKEND_PORT%
echo Frontend: http://localhost:%FRONTEND_PORT%
echo.
echo Close the opened windows to stop the servers.
