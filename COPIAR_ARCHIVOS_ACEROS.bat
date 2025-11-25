@echo off
echo ============================================
echo SCRIPT DE INTEGRACION - ACEROS DE REFUERZO
echo ============================================
echo.

set "ORIGEN=D:\repos\ACEROSDEREFFUERZONET8 CLAUDE\ACEROSDEREFFUERZONET-SOLUCION-FIREBASE\ACEROSDEREFFUERZONET8"
set "DESTINO=D:\repos\RevitExtensions-main\source\ClosestGridsAddin"

echo Origen: %ORIGEN%
echo Destino: %DESTINO%
echo.

:: Verificar que existen los directorios
if not exist "%ORIGEN%" (
    echo ERROR: No se encuentra el directorio origen
    echo Por favor, verifique la ruta del proyecto ACEROSDEREFFUERZONET8
    pause
    exit /b 1
)

if not exist "%DESTINO%" (
    echo ERROR: No se encuentra el directorio destino
    echo Por favor, verifique la ruta del proyecto ClosestGridsAddin
    pause
    exit /b 1
)

echo.
echo Seleccione el tipo de integracion:
echo.
echo 1. Copiar SOLO comandos de aceros (sin autenticacion)
echo 2. Copiar comandos + sistema de autenticacion local
echo 3. Ver lista de archivos a copiar (sin copiar)
echo 4. Salir
echo.

set /p OPCION="Ingrese opcion (1-4): "

if "%OPCION%"=="1" goto COPIAR_COMANDOS
if "%OPCION%"=="2" goto COPIAR_CON_AUTH
if "%OPCION%"=="3" goto LISTAR_ARCHIVOS
if "%OPCION%"=="4" goto FIN

echo Opcion invalida
pause
exit /b 1

:COPIAR_COMANDOS
echo.
echo ============================================
echo COPIANDO ARCHIVOS DE COMANDOS...
echo ============================================
echo.

echo Copiando archivos .cs principales...
copy "%ORIGEN%\ACEROCOLUMNAS.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROVIGAS.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROMUROS.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROLOSASYCIMIENTOS.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROESTRIBOSCOLUMNAS.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\DIVISORDECOLUMNAS.cs" "%DESTINO%\" /Y

echo.
echo Copiando archivos XAML...
copy "%ORIGEN%\ACEROCOLUMNASXAML.xaml" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROCOLUMNASXAML.xaml.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROVIGASXAML.xaml" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROVIGASXAML.xaml.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROMUROSXAML.xaml" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROMUROSXAML.xaml.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROLOSASYCIMIENTOSXAML.xaml" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROLOSASYCIMIENTOSXAML.xaml.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROESTCOLXAML.xaml" "%DESTINO%\" /Y
copy "%ORIGEN%\ACEROESTCOLXAML.xaml.cs" "%DESTINO%\" /Y

echo.
echo ============================================
echo ARCHIVOS COPIADOS EXITOSAMENTE
echo ============================================
echo.
echo PASOS SIGUIENTES:
echo.
echo 1. Abra el proyecto en Visual Studio
echo 2. Busque y reemplace en TODOS los archivos copiados:
echo    - Buscar: "namespace ACEROSDEREFFUERZONET8"
echo    - Reemplazar: "namespace ClosestGridsAddin"
echo.
echo 3. Busque y comente las referencias a Firebase:
echo    - LicenseManager
echo    - FirebaseManager
echo    - FirebaseService
echo.
echo 4. Modifique Application.cs para usar las clases reales
echo    en lugar de PlaceholderCommand
echo.
echo 5. Compile y pruebe
echo.
pause
goto FIN

:COPIAR_CON_AUTH
echo.
echo ============================================
echo COPIANDO CON SISTEMA DE AUTENTICACION...
echo ============================================
echo.

:: Copiar comandos primero
call :COPIAR_COMANDOS_INTERNO

echo.
echo Copiando archivos de autenticacion...
copy "%ORIGEN%\AuthenticationModels.cs" "%DESTINO%\" /Y
copy "%ORIGEN%\LoginWindow.xaml" "%DESTINO%\" /Y
copy "%ORIGEN%\LoginWindow.xaml.cs" "%DESTINO%\" /Y

echo.
echo ============================================
echo ARCHIVOS COPIADOS EXITOSAMENTE
echo ============================================
echo.
echo PASOS SIGUIENTES:
echo.
echo 1. Siga los pasos 1-5 de la opcion anterior
echo.
echo 2. Cree el archivo LicenseManagerLocal.cs usando la
echo    plantilla del README
echo.
echo 3. Agregue el boton de Login en Application.cs
echo.
echo 4. Cree LoginCommand.cs usando la plantilla del README
echo.
pause
goto FIN

:COPIAR_COMANDOS_INTERNO
copy "%ORIGEN%\ACEROCOLUMNAS.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROVIGAS.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROMUROS.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROLOSASYCIMIENTOS.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROESTRIBOSCOLUMNAS.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\DIVISORDECOLUMNAS.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROCOLUMNASXAML.xaml" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROCOLUMNASXAML.xaml.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROVIGASXAML.xaml" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROVIGASXAML.xaml.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROMUROSXAML.xaml" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROMUROSXAML.xaml.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROLOSASYCIMIENTOSXAML.xaml" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROLOSASYCIMIENTOSXAML.xaml.cs" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROESTCOLXAML.xaml" "%DESTINO%\" /Y >nul 2>&1
copy "%ORIGEN%\ACEROESTCOLXAML.xaml.cs" "%DESTINO%\" /Y >nul 2>&1
exit /b 0

:LISTAR_ARCHIVOS
echo.
echo ============================================
echo ARCHIVOS QUE SE COPIARIAN
echo ============================================
echo.
echo COMANDOS PRINCIPALES (.cs):
echo   - ACEROCOLUMNAS.cs
echo   - ACEROVIGAS.cs
echo   - ACEROMUROS.cs
echo   - ACEROLOSASYCIMIENTOS.cs
echo   - ACEROESTRIBOSCOLUMNAS.cs
echo   - DIVISORDECOLUMNAS.cs
echo.
echo INTERFACES XAML:
echo   - ACEROCOLUMNASXAML.xaml y .xaml.cs
echo   - ACEROVIGASXAML.xaml y .xaml.cs
echo   - ACEROMUROSXAML.xaml y .xaml.cs
echo   - ACEROLOSASYCIMIENTOSXAML.xaml y .xaml.cs
echo   - ACEROESTCOLXAML.xaml y .xaml.cs
echo.
echo AUTENTICACION (solo con opcion 2):
echo   - AuthenticationModels.cs
echo   - LoginWindow.xaml y .xaml.cs
echo.
echo ARCHIVOS QUE SE OMITEN (Firebase):
echo   - FirebaseIntegration.cs
echo   - FirebaseManager.cs
echo   - FirebaseModels.cs
echo   - FirebaseService.cs
echo   - FirebaseServiceSimple.cs
echo   - AuthenticationService.cs (usa Firebase)
echo   - LicenseManager.cs (usa Firebase)
echo.
pause
goto FIN

:FIN
echo.
echo Presione cualquier tecla para salir...
pause >nul
