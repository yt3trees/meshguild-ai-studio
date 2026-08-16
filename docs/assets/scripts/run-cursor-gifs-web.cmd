@echo off
setlocal

set "GIF_RUN_ROOT=%USERPROFILE%\AppData\Local\Temp\opencode\cursor-gifs-run"
if exist "%GIF_RUN_ROOT%" rmdir /s /q "%GIF_RUN_ROOT%"
mkdir "%GIF_RUN_ROOT%\state"
mkdir "%GIF_RUN_ROOT%\secrets"
mkdir "%GIF_RUN_ROOT%\workspace"
mkdir "%GIF_RUN_ROOT%\artifacts"
mkdir "%GIF_RUN_ROOT%\team-frames"
mkdir "%GIF_RUN_ROOT%\approval-frames"
mkdir "%GIF_RUN_ROOT%\graph-frames"

set "ASPNETCORE_ENVIRONMENT=E2E"
set "DOTNET_ENVIRONMENT=E2E"
set "Profile=Local"
set "Runs__DatabasePath=%GIF_RUN_ROOT%\state\work-agents.db"
set "SecretStore__Root=%GIF_RUN_ROOT%\secrets"
set "Workspace__Root=%GIF_RUN_ROOT%\workspace"
set "Artifacts__Root=%GIF_RUN_ROOT%\artifacts"
set "Orchestration__Engine__Enabled=false"
set "Orchestration__HostBaseUrl=http://127.0.0.1:5050"
set "E2E__DeterministicAgentResponse=true"
set "OTEL_CONSOLE_DISABLED=true"

dotnet run --project "%~dp0..\..\..\src\WorkAgents.Web\WorkAgents.Web.csproj" --no-launch-profile -- --urls http://127.0.0.1:5049 > "%GIF_RUN_ROOT%\server.log" 2>&1
