@echo off
setlocal
if "%~1"=="" (
  echo Usage: run-script.cmd script.ps1 [arguments]
  exit /b 2
)
rem Windows PowerShell must not inherit PowerShell 7's incompatible module paths.
set "PSModulePath=%ProgramFiles%\WindowsPowerShell\Modules;%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File %*
exit /b %errorlevel%
