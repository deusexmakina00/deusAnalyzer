#!/bin/bash

# Cross-Platform Packet Capture Server Start Script
# For Linux/macOS

set -e

echo "==================================================="
echo "  Cross-Platform Packet Capture Server"
echo "  C# .NET 8 + libpcap"
echo "==================================================="
echo

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "⚠️  This script requires root privileges for packet capture"
    echo "Please run with sudo:"
    echo "sudo ./start.sh"
    exit 1
fi

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    echo "❌ .NET 8 SDK is not installed"
    echo "Please install .NET 8 SDK first:"
    echo "https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi

# Check libpcap
echo "[INFO] Checking libpcap availability..."
if [ "$(uname)" == "Darwin" ]; then
    # macOS
    if [ ! -f "/usr/lib/libpcap.dylib" ] && [ ! -f "/usr/local/lib/libpcap.dylib" ]; then
        echo "⚠️  libpcap not found, trying to continue..."
    fi
elif [ "$(expr substr $(uname -s) 1 5)" == "Linux" ]; then
    # Linux
    if ! ldconfig -p | grep -q libpcap; then
        echo "❌ libpcap not found"
        echo "Please install libpcap-dev:"
        echo "  Ubuntu/Debian: sudo apt-get install libpcap-dev"
        echo "  CentOS/RHEL:   sudo yum install libpcap-devel"
        echo "  Fedora:        sudo dnf install libpcap-devel"
        exit 1
    fi
fi

echo "[1/3] Restoring dependencies..."
dotnet restore

echo "[2/3] Building application..."
dotnet build --configuration Release

echo "[3/3] Starting servers..."
echo
echo "🌐 Web Dashboard: http://0.0.0.0:8080 (all interfaces)"
echo "📡 WebSocket Server: ws://0.0.0.0:9001 (all interfaces)"
echo "🌍 Network Access: Available from any device on network"
echo "🔧 Local Access: http://localhost:8080"
echo "📦 Packet Capture: monitoring port 16000 (multi-interface)"
echo
echo "💡 Press Ctrl+C to stop all servers"
echo

# Run the application
dotnet run --configuration Release

echo
echo "Server stopped."
