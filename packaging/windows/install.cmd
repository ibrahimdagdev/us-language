@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "EXITCODE=%ERRORLEVEL%"

echo.
if not "%EXITCODE%"=="0" (
  echo Kurulum basarisiz. Exit code: %EXITCODE%
) else (
  echo Kurulum tamamlandi.
  echo Yeni bir terminal veya VS Code acip "us --version" calistirin.
)

echo.
pause
exit /b %EXITCODE%
