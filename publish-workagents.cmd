@echo off
setlocal

set "ROOT=%~dp0"
set "DIST=%ROOT%dist"
set "RID=win-x64"
set "CONFIG=Release"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK was not found in PATH.
    pause
    exit /b 1
)

if exist "%DIST%" (
    echo Cleaning previous dist folder...
    rmdir /s /q "%DIST%"
)

echo Publishing WorkAgents.Host...
dotnet publish "%ROOT%src\WorkAgents.Host\WorkAgents.Host.csproj" -c %CONFIG% -r %RID% --self-contained true -p:PublishSingleFile=true -o "%DIST%\WorkAgents.Host"
if errorlevel 1 goto :error

echo Publishing WorkAgents.Web...
dotnet publish "%ROOT%src\WorkAgents.Web\WorkAgents.Web.csproj" -c %CONFIG% -r %RID% --self-contained true -p:PublishSingleFile=true -o "%DIST%\WorkAgents.Web"
if errorlevel 1 goto :error

echo Publishing WorkAgents.Tray...
dotnet publish "%ROOT%src\WorkAgents.Tray\WorkAgents.Tray.csproj" -c %CONFIG% -r %RID% --self-contained true -p:PublishSingleFile=true -o "%DIST%\WorkAgents.Tray"
if errorlevel 1 goto :error

echo Assembling common definition root...
if not exist "%DIST%\definitions" mkdir "%DIST%\definitions"
if errorlevel 1 goto :error

robocopy "%ROOT%src\WorkAgents.Agents\agents" "%DIST%\definitions\agents" /E /XF *.cs
if errorlevel 8 goto :error
robocopy "%ROOT%src\WorkAgents.Agents\skills" "%DIST%\definitions\skills" /E
if errorlevel 8 goto :error

rem Authoring skills for Claude/external agents. WorkAgents runtime skills remain under definitions\skills.
if not exist "%DIST%\definitions\.agents\skills" mkdir "%DIST%\definitions\.agents\skills"
if errorlevel 1 goto :error
robocopy "%ROOT%.agents\skills" "%DIST%\definitions\.agents\skills" /E
if errorlevel 8 goto :error
robocopy "%ROOT%src\WorkAgents.Agents\teams" "%DIST%\definitions\teams" /E
if errorlevel 8 goto :error
robocopy "%ROOT%src\WorkAgents.Agents\graphs" "%DIST%\definitions\graphs" /E
if errorlevel 8 goto :error
robocopy "%ROOT%src\WorkAgents.Agents\workflows" "%DIST%\definitions\workflows" /E
if errorlevel 8 goto :error

rem Host/Web load definitions from the common sibling root in the distributable layout.
for %%D in (agents skills teams graphs workflows) do (
    if exist "%DIST%\WorkAgents.Host\%%D" rmdir /s /q "%DIST%\WorkAgents.Host\%%D"
    if exist "%DIST%\WorkAgents.Web\%%D" rmdir /s /q "%DIST%\WorkAgents.Web\%%D"
)

echo.
echo Done. Distributable files are in "%DIST%".
echo Hand over the whole "dist" folder as-is; run dist\WorkAgents.Tray\WorkAgents.Tray.exe to start Host/Web.
echo Shared definitions are in dist\definitions\ (agents, skills, teams, graphs, workflows).
echo Authoring skills are in dist\definitions\.agents\skills\ (create-agent, create-graph, create-team, graph-design).
goto :eof

:error
echo.
echo Publish failed. See the error above.
pause
exit /b 1
