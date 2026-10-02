@echo off
rem Removes StoneForge from Stoneshard: the game's own files put back, your mods folder kept.
"%~dp0files\dotnet\patcher\StoneForge.Patcher.exe" uninstall %*
