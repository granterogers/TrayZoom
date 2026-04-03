@echo off
echo Building TrayZoom...
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\dist
if %ERRORLEVEL% neq 0 (
    echo.
    echo BUILD FAILED. Make sure .NET 8 SDK is installed:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
echo.
echo SUCCESS! TrayZoom.exe is in the .\dist folder.
echo.
start dist
pause
