@echo off
REM ====================================================
REM   BIMS VANILLA - Publicar Release en GitHub
REM ====================================================
echo.
echo ========================================
echo   BIMS VANILLA - Publicar Release
echo ========================================
echo.

REM Verificar si GitHub CLI está instalado
where gh >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [1/6] GitHub CLI no encontrado. Instalando...
    echo.
    echo Aceptando terminos de winget...
    winget install GitHub.cli --accept-source-agreements --accept-package-agreements

    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo ERROR: No se pudo instalar GitHub CLI.
        echo Por favor, instalalo manualmente desde: https://cli.github.com/
        pause
        exit /b 1
    )

    echo.
    echo GitHub CLI instalado. Por favor, cierra y vuelve a abrir esta ventana.
    echo Luego ejecuta este script nuevamente.
    pause
    exit /b 0
) else (
    echo [1/6] GitHub CLI encontrado: OK
)

echo.
echo [2/6] Verificando autenticacion en GitHub...
gh auth status >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo No estas autenticado. Iniciando login...
    echo.
    echo INSTRUCCIONES:
    echo 1. Se abrira tu navegador
    echo 2. Autoriza la aplicacion
    echo 3. Vuelve a esta ventana
    echo.
    pause
    gh auth login

    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo ERROR: No se pudo autenticar.
        pause
        exit /b 1
    )
) else (
    echo Autenticacion: OK
)

echo.
echo [3/6] Obteniendo informacion del usuario...
for /f "delims=" %%i in ('gh api user -q .login') do set GH_USER=%%i
echo Usuario de GitHub: %GH_USER%

echo.
echo [4/6] Verificando si el repositorio existe...
gh repo view %GH_USER%/bims-vanilla-revit >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Repositorio no existe. Creando...
    echo.
    gh repo create bims-vanilla-revit --public --description "BIMS VANILLA - Plugin para Revit 2025"

    if %ERRORLEVEL% NEQ 0 (
        echo.
        echo ERROR: No se pudo crear el repositorio.
        pause
        exit /b 1
    )

    echo Repositorio creado exitosamente!
) else (
    echo Repositorio existe: OK
)

echo.
echo [5/6] Creando release...
echo.

REM Solicitar version
set /p VERSION="Ingresa la version (ej: 1.0.0): "

REM Validar que se ingreso una version
if "%VERSION%"=="" (
    echo ERROR: Debe ingresar una version.
    pause
    exit /b 1
)

REM Solicitar notas de version
echo.
echo Ingresa las notas de version (Release Notes):
echo Ejemplo: Nueva funcionalidad X, Correccion de bug Y
set /p RELEASE_NOTES="Release Notes: "

if "%RELEASE_NOTES%"=="" (
    set RELEASE_NOTES=Version %VERSION%
)

REM Ruta del DLL
set DLL_PATH=source\ClosestGridsAddin\bin\Release R25\ClosestGridsAddinVANILLA.dll

REM Verificar que el DLL existe
if not exist "%DLL_PATH%" (
    echo.
    echo ERROR: No se encontro el DLL en:
    echo %DLL_PATH%
    echo.
    echo Por favor, compila el proyecto primero.
    pause
    exit /b 1
)

echo.
echo Creando release v%VERSION%...
echo DLL: %DLL_PATH%
echo.

gh release create "v%VERSION%" ^
    --repo %GH_USER%/bims-vanilla-revit ^
    --title "Version %VERSION%" ^
    --notes "%RELEASE_NOTES%" ^
    "%DLL_PATH%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: No se pudo crear el release.
    pause
    exit /b 1
)

echo.
echo ========================================
echo   Release Creado Exitosamente!
echo ========================================

echo.
echo [6/6] Obteniendo URL de descarga...
echo.

REM Obtener URL del asset
for /f "delims=" %%i in ('gh release view "v%VERSION%" --repo %GH_USER%/bims-vanilla-revit --json assets -q ".assets[0].url"') do set DOWNLOAD_URL=%%i

echo URL de descarga:
echo %DOWNLOAD_URL%
echo.

echo ========================================
echo   SIGUIENTE PASO: Actualizar Firebase
echo ========================================
echo.
echo 1. Ve a Firebase Console:
echo    https://console.firebase.google.com/project/bims-8d507/database
echo.
echo 2. Navega a: /updates/latest
echo.
echo 3. Actualiza con estos valores:
echo.
echo    {
echo      "version": "%VERSION%",
echo      "downloadUrl": "%DOWNLOAD_URL%",
echo      "releaseNotes": "%RELEASE_NOTES%",
echo      "isMandatory": false,
echo      "releaseDate": "%date% %time%"
echo    }
echo.
echo 4. Click "Actualizar"
echo.
echo ========================================

REM Copiar URL al portapapeles
echo %DOWNLOAD_URL% | clip
echo.
echo La URL ha sido copiada al portapapeles!
echo.

pause
