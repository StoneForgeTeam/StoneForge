@echo off
rem Installs StoneForge into Stoneshard: found through Steam, or give its folder - "Install StoneForge.cmd" "D:\Games\Stoneshard"
"%~dp0files\dotnet\patcher\StoneForge.Patcher.exe" install %*
