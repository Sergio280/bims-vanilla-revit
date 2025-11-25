@echo off
REM ====================================
REM Deploy del Panel de Admin a Firebase Hosting
REM ====================================

echo.
echo ========================================
echo   BIMS VANILLA - Deploy a la Nube
echo ========================================
echo.

REM Verificar que Firebase CLI esté instalado
where firebase >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Firebase CLI no esta instalado.
    echo.
    echo Instalalo con: npm install -g firebase-tools
    echo.
    pause
    exit /b 1
)

echo [1/3] Verificando login en Firebase...
firebase login --reauth

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: No se pudo autenticar con Firebase.
    echo.
    pause
    exit /b 1
)

echo.
echo [2/3] Desplegando panel a Firebase Hosting...
echo.
firebase deploy --only hosting

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Fallo el despliegue.
    echo.
    pause
    exit /b 1
)

echo.
echo ========================================
echo   DESPLIEGUE EXITOSO!
echo ========================================
echo.
echo Tu panel esta disponible en:
echo https://bims-8d507.web.app
echo.
echo Puedes acceder desde cualquier dispositivo.
echo.
pause
