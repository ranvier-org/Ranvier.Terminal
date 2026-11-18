@echo off
cd /d "%~dp0"
dotnet watch --project "%~dp0examples\SystemMonitor\SystemMonitor.fsproj" run
