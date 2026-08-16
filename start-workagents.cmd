@echo off
setlocal

set "ROOT=%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK was not found in PATH.
    pause
    exit /b 1
)

echo Starting MeshGuild AI Studio Host on http://localhost:5160 ...
start "MeshGuild AI Studio Host" /D "%ROOT%" "%ComSpec%" /k "set ASPNETCORE_ENVIRONMENT=Development&& dotnet watch run --project src\WorkAgents.Host\WorkAgents.Host.csproj --no-launch-profile --urls http://localhost:5160"

timeout /t 2 /nobreak >nul

echo Starting MeshGuild AI Studio Web on http://localhost:5049 ...
start "MeshGuild AI Studio Web" /D "%ROOT%" "%ComSpec%" /k "set ASPNETCORE_ENVIRONMENT=Development&& dotnet watch run --project src\WorkAgents.Web\WorkAgents.Web.csproj --no-launch-profile --urls http://localhost:5049"

timeout /t 3 /nobreak >nul
start "" "http://localhost:5049/"

echo.
echo MeshGuild AI Studio is starting.
echo Close the Host and Web command windows to stop the application.

endlocal
