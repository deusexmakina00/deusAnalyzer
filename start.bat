@echo off
cd /d %~dp0
title Cross-Platform Packet Capture Server
color 0A

echo ===================================================
echo   Cross-Platform Packet Capture Server
echo   C# .NET 8 + libpcap/npcap
echo ===================================================
echo.

echo [1/3] Restoring dependencies...
dotnet restore
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to restore dependencies
    pause
    exit /b 1
)

echo [2/3] Building application...
dotnet build --configuration Release
if %ERRORLEVEL% neq 0 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

echo [3/3] Starting servers...
echo.
echo 🌐 Web Dashboard: http://0.0.0.0:8080 (all interfaces)
echo 📡 WebSocket Server: ws://0.0.0.0:9001 (all interfaces)
echo 🌍 Network Access: Available from any device on network
echo 🔧 Local Access: http://localhost:8080
echo 📦 Packet Capture: monitoring port 16000
echo.
echo ⚠️  Running with administrator privileges...
echo 💡 Press Ctrl+C to stop all servers
echo.

dotnet run --configuration Release

if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: Server failed to start
    echo Please check the logs above for details
    echo.
    echo Common issues:
    echo - Run as Administrator
    echo - Install Npcap from https://npcap.com/
    echo - Check firewall settings
    echo.
)

pause
