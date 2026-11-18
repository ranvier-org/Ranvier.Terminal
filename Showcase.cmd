@echo off
rem Launch from Explorer or a user-owned terminal so the development session owns its lifetime.
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\launch-showcase.ps1" %*
if errorlevel 1 pause
