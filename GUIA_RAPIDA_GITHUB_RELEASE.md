# 🚀 Guía Rápida - Publicar Release en GitHub

## ✅ Instrucciones Paso a Paso (15 minutos)

### **Paso 1: Crear Cuenta/Repositorio en GitHub (5 min)**

1. **Abre tu navegador** y ve a: https://github.com

2. **Inicia sesión** (o crea cuenta si no tienes)

3. **Click en el botón "+"** (arriba derecha) → **"New repository"**

4. **Llenar formulario:**
   - Repository name: `bims-vanilla-revit`
   - Description: `BIMS VANILLA - Plugin Revit 2025`
   - Visibilidad: **Public** ✅
   - ❌ NO marcar "Initialize with README"
   - Click **"Create repository"**

---

### **Paso 2: Crear tu Primera Release (5 min)**

Después de crear el repo, verás una página con instrucciones. Ignóralas y:

1. **Click en "Releases"** (lado derecho de la página)

2. **Click en "Create a new release"**

3. **Llenar el formulario:**

   **Tag version:**
   ```
   v1.0.0
   ```

   **Release title:**
   ```
   Versión 1.0.0 - Release Inicial
   ```

   **Description (Release notes):**
   ```markdown
   ## 🎉 Versión 1.0.0 - Primera Release

   ### ✨ Características Principales
   - Sistema de licencias con Firebase
   - Comando Dividir DirectShape
   - Auto-actualización automática
   - FORMWBIMS Auto-Convert mejorado
   - Conversión inteligente a Wall/Floor

   ### 🔐 Sistema de Seguridad
   - Activaciones por hardware
   - Máximo 2 equipos por licencia (configurable)
   - Caché offline (7 días)
   - Revalidación cada 24h

   ### 📦 Instalación
   1. Descarga `ClosestGridsAddinVANILLA.dll`
   2. Coloca en: `%AppData%\Autodesk\Revit\Addins\2025\`
   3. Reinicia Revit
   4. Disfruta!
   ```

4. **Subir el DLL:**
   - Arrastra y suelta (Drag & Drop) el archivo:
   ```
   D:\repos\claude RevitExtensions-main - FIREBASE AUTHENTICATION - yo\source\ClosestGridsAddin\bin\Release R25\ClosestGridsAddinVANILLA.dll
   ```
   - O click en "Attach binaries" y selecciona el archivo

5. **Click "Publish release"** (botón verde)

---

### **Paso 3: Copiar URL de Descarga (1 min)**

Después de publicar:

1. Verás tu release publicado

2. **Click derecho** en el archivo `ClosestGridsAddinVANILLA.dll`

3. **"Copiar dirección de enlace"** (o "Copy link address")

4. La URL será algo como:
   ```
   https://github.com/TU_USUARIO/bims-vanilla-revit/releases/download/v1.0.0/ClosestGridsAddinVANILLA.dll
   ```

5. **Copia esa URL** (la necesitarás en el siguiente paso)

---

### **Paso 4: Actualizar Firebase (4 min)**

1. **Abre Firebase Console:**
   ```
   https://console.firebase.google.com/project/bims-8d507/database
   ```

2. **Navega a la raíz** y crea esta estructura (si no existe):
   - Click en "+" junto a la raíz
   - Nombre: `updates`
   - Click "+"

3. **Dentro de `updates`, crea:**
   - Nombre: `latest`
   - Click "+"

4. **Dentro de `latest`, agrega estos campos:**

   Click "+" para cada campo:

   | Nombre | Tipo | Valor |
   |--------|------|-------|
   | `version` | string | `1.0.0` |
   | `downloadUrl` | string | `[LA URL QUE COPIASTE DE GITHUB]` |
   | `releaseNotes` | string | `✨ Nueva versión 1.0.0\n\n• Sistema de licencias\n• Auto-actualización\n• Dividir DirectShape` |
   | `isMandatory` | boolean | `false` |
   | `releaseDate` | string | `2025-11-24T00:00:00Z` |

5. **Estructura final debe verse así:**
   ```
   updates/
     latest/
       version: "1.0.0"
       downloadUrl: "https://github.com/..."
       releaseNotes: "✨ Nueva versión..."
       isMandatory: false
       releaseDate: "2025-11-24T00:00:00Z"
   ```

6. **Verificar reglas de seguridad:**
   - Click en "Reglas" (arriba)
   - Debe incluir:
   ```json
   {
     "rules": {
       "updates": {
         ".read": true,
         ".write": "auth != null"
       }
     }
   }
   ```
   - Si no está, agrégalo y click "Publicar"

---

## ✅ ¡Listo!

Ahora tu sistema de auto-actualización está configurado.

### **Para Probar:**

1. Abre Revit 2025
2. Si tu DLL tiene versión 0.9.0 (por ejemplo)
3. Firebase tiene versión 1.0.0
4. Al abrir Revit, debería aparecer un diálogo:
   ```
   🎉 Nueva versión disponible

   Versión actual: 0.9.0
   Nueva versión: 1.0.0

   ¿Desea descargar e instalar?
   ```

---

## 📝 Para Futuras Actualizaciones

Cuando tengas una nueva versión:

### **Paso A: Compilar nueva versión**

1. Actualiza versión en `.csproj`:
   ```xml
   <Version>1.0.1</Version>
   ```

2. Compila:
   ```bash
   msbuild /p:Configuration="Release R25" /t:Build
   ```

### **Paso B: Publicar en GitHub**

1. Ve a tu repo: `https://github.com/TU_USUARIO/bims-vanilla-revit/releases`
2. Click "Draft a new release"
3. Tag: `v1.0.1`
4. Title: `Versión 1.0.1`
5. Notes: Describe los cambios
6. Sube el nuevo DLL
7. Publish

### **Paso C: Actualizar Firebase**

1. Ve a Firebase → `/updates/latest`
2. Cambia `version` a `1.0.1`
3. Cambia `downloadUrl` a la nueva URL
4. Actualiza `releaseNotes`

---

## 🎯 Resumen Visual

```
┌─────────────────────────────────────┐
│   1. GitHub: Crear Repositorio      │
│      → bims-vanilla-revit           │
└──────────────┬──────────────────────┘
               ↓
┌─────────────────────────────────────┐
│   2. GitHub: Crear Release          │
│      → v1.0.0                       │
│      → Subir DLL                    │
└──────────────┬──────────────────────┘
               ↓
┌─────────────────────────────────────┐
│   3. GitHub: Copiar URL del DLL     │
│      → Click derecho en archivo     │
└──────────────┬──────────────────────┘
               ↓
┌─────────────────────────────────────┐
│   4. Firebase: Actualizar /updates  │
│      → version: "1.0.0"             │
│      → downloadUrl: "[URL]"         │
└──────────────┬──────────────────────┘
               ↓
┌─────────────────────────────────────┐
│   5. Probar en Revit                │
│      → Abrir Revit                  │
│      → Ver notificación             │
└─────────────────────────────────────┘
```

---

## 💡 Tips

- ✅ Usa versionado semántico: `MAJOR.MINOR.PATCH`
- ✅ Escribe release notes claros
- ✅ Prueba la descarga antes de avisar a usuarios
- ✅ Mantén backup de versiones anteriores
- ✅ GitHub guarda historial completo de releases

---

## ❓ Preguntas Frecuentes

**P: ¿Cuánto cuesta GitHub Releases?**
R: Es 100% gratis e ilimitado

**P: ¿Los usuarios necesitan cuenta de GitHub?**
R: No, la descarga es pública

**P: ¿Puedo borrar un release si me equivoco?**
R: Sí, puedes editar o eliminar releases

**P: ¿Cómo sé cuántas veces se descargó?**
R: GitHub muestra estadísticas en cada release

---

¿Necesitas ayuda? Revisa `INSTRUCCIONES_AUTO_UPDATE.md` para más detalles.
