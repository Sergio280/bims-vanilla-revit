# 📦 BIMS VANILLA - Instalador de Revit 2025

Este directorio contiene los archivos necesarios para crear el instalador del add-in BIMS VANILLA.

## 📋 Workflow Completo (Resumen Visual)

```
┌─────────────────────────────────────────────────────────────┐
│  1. COMPILAR (Visual Studio)                                │
│     Release R25 → ClosestGridsAddinVANILLA.dll (original)   │
└─────────────────────┬───────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────────────┐
│  2. OFUSCAR (.NET Reactor)                                  │
│     ClosestGridsAddinVANILLA.dll → Ofuscado (~90%)          │
│     ⚠️ REEMPLAZA el DLL original automáticamente            │
└─────────────────────┬───────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────────────┐
│  3. CREAR INSTALADOR (Inno Setup)                           │
│     BimsVanilla_Installer.iss → Setup.exe                   │
│     ✅ Copia: DLL ofuscado + dependencias + PNG             │
│     ❌ NO copia: RevitAPI, carpetas de ofuscación           │
└─────────────────────┬───────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────────────┐
│  4. DISTRIBUIR                                              │
│     BimsVanilla_Revit2025_Setup.exe → Clientes              │
└─────────────────────────────────────────────────────────────┘
```

## 🚀 Cómo crear el instalador

### ⚠️ IMPORTANTE: Workflow con .NET Reactor

El proceso completo es:
1. **Compilar** en Visual Studio
2. **Ofuscar** con .NET Reactor (reemplaza el DLL original)
3. **Crear instalador** con Inno Setup

### Paso 1: Compilar el add-in

1. **Abrir Visual Studio**
2. **Seleccionar configuración:** **Release R25**
3. **Build → Rebuild Solution**
4. **Verificar que no haya errores**
5. Resultado: `bin\Release R25\ClosestGridsAddinVANILLA.dll` (sin ofuscar)

### Paso 2: Ofuscar con .NET Reactor

1. **Abrir .NET Reactor**
2. **Cargar el proyecto/DLL:** `bin\Release R25\ClosestGridsAddinVANILLA.dll`
3. **Aplicar ofuscación** (nivel ~90%)
4. **IMPORTANTE:** .NET Reactor **reemplaza** el DLL original
5. Resultado: `bin\Release R25\ClosestGridsAddinVANILLA.dll` (ahora ofuscado)

> ℹ️ **Nota:** El instalador NO copia las carpetas `ClosestGridsAddinVANILLA_Secure` ni `Obfuscator_Output`

### Paso 3: Instalar Inno Setup (solo primera vez)

1. **Ir a:** https://jrsoftware.org/isdl.php
2. **Descargar** "Inno Setup 6.x" (versión estable más reciente)
3. **Ejecutar** el instalador y seguir las instrucciones
4. **Importante:** Instalar en la ruta por defecto: `C:\Program Files (x86)\Inno Setup 6\`

### Paso 4: Compilar el instalador

**Opción A - Automática (Recomendada):**
1. Doble clic en: `CompileInstaller.bat`
2. El script compilará automáticamente el instalador
3. Al finalizar, se abrirá la carpeta `Output\`
4. Archivo generado: `BimsVanilla_Revit2025_Setup.exe`

**Opción B - Manual:**
1. Abrir Inno Setup
2. File → Open → Seleccionar `BimsVanilla_Installer.iss`
3. Build → Compile (o presionar F9)
4. El instalador se generará en `Output\`

---

## 📂 ¿Qué hace el instalador?

El instalador automáticamente:

1. **Copia el add-in a la ubicación correcta:**
   - DLLs → `C:\ProgramData\Autodesk\Revit\Addins\2025\BIMS-VANILLA\`
   - Iconos → `C:\ProgramData\Autodesk\Revit\Addins\2025\BIMS-VANILLA\Resources\`
   - Archivo .addin → `C:\ProgramData\Autodesk\Revit\Addins\2025\`

2. **Verifica requisitos:**
   - Privilegios de administrador
   - Revit no esté en ejecución (recomendación)

3. **Crea desinstalador:**
   - Panel de Control → Programas y características → "BIMS VANILLA"
   - Elimina completamente todos los archivos instalados

---

## 🔧 ¿Qué incluye el instalador?

### ✅ Archivos incluidos:
- **DLL principal (ofuscado):** `ClosestGridsAddinVANILLA.dll`
- **Dependencias externas:**
  - `FireSharp.dll` + dependencias de Firebase
  - `Newtonsoft.Json.dll`
  - `Aspose.Cells.dll`
  - `Nice3point.Revit.Extensions.dll`
  - `Nice3point.Revit.Toolkit.dll`
  - Otros DLLs del sistema necesarios
- **Recursos:** 13 iconos PNG para los botones del ribbon
- **Archivo manifest:** `ClosestGridsAddinVANILLA.addin`

### ❌ Archivos NO incluidos (intencionalmente):
- ❌ `RevitAPI.dll` - Ya está en Revit
- ❌ `RevitAPIUI.dll` - Ya está en Revit
- ❌ Carpeta `ClosestGridsAddinVANILLA_Secure` - Temporal de .NET Reactor
- ❌ Carpeta `Obfuscator_Output` - Temporal de ofuscadores

---

## 🧪 Cómo probar el instalador

1. **Ejecutar el instalador:**
   ```
   BimsVanilla_Revit2025_Setup.exe
   ```

2. **Seguir el asistente de instalación**
   - Idioma: Español o Inglés
   - Aceptar términos (si los agregas)
   - Clic en "Instalar"

3. **Verificar la instalación:**
   - Abrir Revit 2025
   - Buscar la pestaña "BIMS VANILLA" en el ribbon
   - Verificar que los 13 botones aparezcan con sus iconos
   - Probar algún comando (ej: Firebase, Ejes Cercanos)

4. **Probar la desinstalación:**
   - Panel de Control → Programas → "BIMS VANILLA"
   - Clic en "Desinstalar"
   - Verificar que Revit ya no muestre la pestaña

---

## 📝 Personalización del instalador

Puedes editar el archivo `BimsVanilla_Installer.iss` para:

- **Cambiar versión:** Modificar `#define MyAppVersion "1.0.0"`
- **Cambiar URL:** Modificar `#define MyAppURL "https://www.bimsvanilla.com"`
- **Agregar icono personalizado:** Descomentar `SetupIconFile=..\Resources\icon.ico`
- **Agregar licencia:** Agregar sección `[Messages]` con archivo de licencia
- **Cambiar idiomas:** Agregar más idiomas en la sección `[Languages]`

---

## ⚠️ Requisitos del sistema

El instalador requiere:
- ✅ Windows 10/11 (64-bit)
- ✅ Autodesk Revit 2025
- ✅ Privilegios de administrador
- ✅ .NET 8.0 Runtime (generalmente ya instalado con Revit 2025)

---

## 📞 Soporte

Si tienes problemas con el instalador:
1. Verificar que Inno Setup esté instalado correctamente
2. Verificar que el add-in compile sin errores primero
3. Revisar los logs de Inno Setup en caso de error de compilación

---

## 📜 Estructura de archivos del proyecto

```
ClosestGridsAddin\
├── bin\
│   └── Release R25\              # DLLs compilados
│       ├── *.dll
│       ├── Resources\            # Iconos PNG
│       └── *.addin
├── Installer\
│   ├── BimsVanilla_Installer.iss # Script de Inno Setup
│   ├── CompileInstaller.bat      # Script de compilación automática
│   ├── README_INSTALADOR.md      # Este archivo
│   └── Output\                   # Carpeta donde se genera el .exe
│       └── BimsVanilla_Revit2025_Setup.exe
```

---

## ✅ Checklist antes de distribuir

- [ ] **Compilar** en modo **Release R25** (Visual Studio)
- [ ] **Ofuscar** con .NET Reactor (~90% obfuscation)
- [ ] **Verificar** que el DLL ofuscado esté en `bin\Release R25\`
- [ ] **Probar** el add-in manualmente en Revit
- [ ] **Verificar** que todos los iconos se muestren
- [ ] **Verificar** que el sistema de licencias funcione
- [ ] **Compilar** el instalador con Inno Setup
- [ ] **Probar** instalación completa en una máquina limpia
- [ ] **Probar** desinstalación (no debe dejar archivos)
- [ ] **Verificar** que funciona después de instalar
- [ ] **Documentar** la versión y cambios (changelog)

---

**¡Listo para distribuir!** 🎉
