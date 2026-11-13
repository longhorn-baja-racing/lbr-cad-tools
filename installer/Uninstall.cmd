@echo off
rem Removes the LBR CAD Tools SolidWorks add-in. Windows asks for administrator rights.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
