@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "ARTIFACTS=%ROOT%\artifacts"

echo Building JustDanceEditor release bundles into:
echo   %ARTIFACTS%
echo.

if not exist "%ARTIFACTS%" mkdir "%ARTIFACTS%"

for %%D in (
    "JustDanceEditor-Windows"
    "JustDanceEditor-Linux"
    "JustDanceEditor-macOS"
) do (
    if exist "%ARTIFACTS%\%%~D" (
        echo Removing stale artifact folder %%~D...
        rmdir /s /q "%ARTIFACTS%\%%~D"
        if errorlevel 1 exit /b 1
    )
)

call :PublishRuntime Windows win-x64 1
if errorlevel 1 exit /b 1

call :PublishRuntime Linux linux-x64 1
if errorlevel 1 exit /b 1

call :PublishRuntime Linux linux-arm64 1
if errorlevel 1 exit /b 1

call :PublishRuntime macOS osx-x64 1
if errorlevel 1 exit /b 1

call :PublishRuntime macOS osx-arm64 1
if errorlevel 1 exit /b 1

echo.
echo Build artifacts are ready in "%ARTIFACTS%".
exit /b 0

:PublishRuntime
set "BUNDLE=%~1"
set "RID=%~2"
set "INCLUDE_EDITOR=%~3"
set "OUT=%ARTIFACTS%\JustDanceEditor-%BUNDLE%\%RID%"

echo.
echo Publishing %BUNDLE% %RID%...
if not exist "%OUT%" mkdir "%OUT%"

dotnet publish "%ROOT%\JustDanceEditor.Cli\JustDanceEditor.Cli.csproj" --configuration Release --runtime %RID% --self-contained false -p:UseLocalKevIncProjects=false --output "%OUT%"
if errorlevel 1 exit /b 1

dotnet publish "%ROOT%\JustDanceEditor.GUI\JustDanceEditor.GUI.csproj" --configuration Release --runtime %RID% --self-contained false -p:UseLocalKevIncProjects=false --output "%OUT%"
if errorlevel 1 exit /b 1

if "%INCLUDE_EDITOR%"=="1" (
    dotnet publish "%ROOT%\JustDanceEditor.Editor\JustDanceEditor.Editor.csproj" --configuration Release --runtime %RID% --self-contained false -p:UseLocalKevIncProjects=false --output "%OUT%"
    if errorlevel 1 exit /b 1
)

exit /b 0
