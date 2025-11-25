@echo off
REM Script para compilar el instalador de BIMS VANILLA usando Inno Setup
REM Asegúrate de que Inno Setup esté instalado en C:\Program Files (x86)\Inno Setup 6\

echo ====================================
echo BIMS VANILLA - Compilar Instalador
echo ====================================
echo.

SET INNO_PATH="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
SET SCRIPT_PATH="%~dp0BimsVanilla_Installer.iss"

REM Verificar que Inno Setup esté instalado
if not exist %INNO_PATH% (
    echo ERROR: No se encontró Inno Setup en:
    echo %INNO_PATH%
    echo.
    echo Por favor instala Inno Setup desde: https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

REM Compilar el script
echo Compilando instalador...
echo.
%INNO_PATH% %SCRIPT_PATH%

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ====================================
    echo ¡INSTALADOR CREADO EXITOSAMENTE!
    echo ====================================
    echo.
    echo El instalador se encuentra en:
    echo %~dp0Output\BimsVanilla_Revit2025_Setup.exe
    echo.
    echo Presiona cualquier tecla para abrir la carpeta de salida...
    pause > nul
    explorer "%~dp0Output"
) else (
    echo.
    echo ====================================
    echo ERROR AL COMPILAR EL INSTALADOR
    echo ====================================
    echo.
    echo Revisa los errores anteriores.
    echo.
    pause
    exit /b 1
)
