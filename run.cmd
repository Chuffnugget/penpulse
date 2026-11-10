@echo off
setlocal
rem Windows PowerShell must not inherit PowerShell 7's incompatible module paths.
set "PSModulePath=%ProgramFiles%\WindowsPowerShell\Modules;%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1" %*
exit /b %errorlevel%
