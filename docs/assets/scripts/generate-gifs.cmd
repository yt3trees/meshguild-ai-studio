@echo off
setlocal

where ffmpeg >nul 2>&1
if errorlevel 1 (
    echo ffmpeg was not found on PATH.
    exit /b 1
)

set "FRAME_ROOT=%USERPROFILE%\AppData\Local\Temp\opencode\cursor-gifs-run"
set "ASSET_ROOT=%~dp0.."

call :encode "%FRAME_ROOT%\team-frames" "%ASSET_ROOT%\mission-team-room.gif"
if errorlevel 1 exit /b 1
call :encode "%FRAME_ROOT%\approval-frames" "%ASSET_ROOT%\approval-flow.gif"
if errorlevel 1 exit /b 1
call :encode "%FRAME_ROOT%\graph-frames" "%ASSET_ROOT%\graph-studio.gif"
if errorlevel 1 exit /b 1

echo GIF files generated in "%ASSET_ROOT%".
exit /b 0

:encode
if not exist "%~1\frame-000.png" (
    echo No captured frames found in "%~1".
    exit /b 1
)

ffmpeg -y -framerate 12 -i "%~1\frame-%%03d.png" -filter_complex "[0:v]split[s0][s1];[s0]palettegen=stats_mode=full:max_colors=256[p];[s1][p]paletteuse=dither=sierra2_4a" -loop 0 -t 20 "%~2"
exit /b %ERRORLEVEL%
