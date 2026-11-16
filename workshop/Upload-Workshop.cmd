@echo off
setlocal
set "DGM_PWSH="
for /f "delims=" %%P in ('where pwsh.exe 2^>nul') do if not defined DGM_PWSH set "DGM_PWSH=%%P"
if not defined DGM_PWSH if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "DGM_PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not defined DGM_PWSH set "DGM_PWSH=C:\Program Files\WindowsApps\OpenAI.CodexPrimaryRuntime.v26-1112-737-0_26.1112.737.0_x64__3k8sg7r9htsxt\dependencies\native\powershell\pwsh.exe"
if not exist "%DGM_PWSH%" (
    echo PowerShell 7 was not found. Install PowerShell 7 and run this launcher again.
    pause
    exit /b 1
)
"%DGM_PWSH%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Workshop.ps1" %*
set "DGM_RESULT=%ERRORLEVEL%"
if /I "%~1"=="-CheckOnly" exit /b %DGM_RESULT%
if not "%DGM_RESULT%"=="0" echo Upload command failed. Read the message above before retrying.
pause
exit /b %DGM_RESULT%
