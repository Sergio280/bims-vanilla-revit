# 🔄 Sistema de Auto-Actualización - BIMS VANILLA

Sistema completo de actualizaciones automáticas para distribuir nuevas versiones a tus usuarios.

---

## 📋 Cómo Funciona

1. **Al iniciar Revit**: Verifica automáticamente si hay actualizaciones
2. **Si hay actualización**: Muestra diálogo al usuario con novedades
3. **Usuario acepta**: Descarga la nueva versión en segundo plano
4. **Al cerrar Revit**: Se ejecuta un script `.bat` que reemplaza el DLL antiguo
5. **Próximo inicio**: Usuario tiene la nueva versión

---

## ⚙️ Configuración en Firebase

### **Paso 1: Estructura en Realtime Database**

Ve a tu Firebase Console → Realtime Database y crea esta estructura:

```json
{
  "updates": {
    "latest": {
      "version": "1.0.0",
      "downloadUrl": "https://tu-servidor.com/ClosestGridsAddinVANILLA.dll",
      "releaseNotes": "• Nueva funcionalidad X\n• Corrección de bug Y\n• Mejora de rendimiento Z",
      "isMandatory": false,
      "releaseDate": "2025-11-24T00:00:00Z"
    }
  }
}
```

### **Paso 2: Reglas de Seguridad**

Actualiza las reglas para permitir lectura pública de updates:

```json
{
  "rules": {
    "updates": {
      ".read": true,
      ".write": "auth != null"
    },
    "users": {
      ".read": "auth != null",
      ".write": "auth != null"
    }
  }
}
```

---

## 🚀 Cómo Publicar una Nueva Versión

### **Opción 1: Hosting en Firebase Storage (Recomendado)**

#### 1. Sube el DLL a Firebase Storage:

```bash
# Instalar Firebase CLI si no lo tienes
npm install -g firebase-tools

# Login
firebase login

# Ve a tu carpeta del proyecto
cd "D:\repos\claude RevitExtensions-main - FIREBASE AUTHENTICATION - yo"

# Sube el DLL
firebase storage:upload "source\ClosestGridsAddin\bin\Release R25\ClosestGridsAddinVANILLA.dll" --name "releases/ClosestGridsAddinVANILLA_v1.0.1.dll"
```

#### 2. Obtén la URL pública:
- Ve a Firebase Console → Storage
- Encuentra el archivo subido
- Haz clic derecho → "Get download URL"
- Copia la URL

#### 3. Actualiza Realtime Database:

Ve a Realtime Database y actualiza:

```json
{
  "updates": {
    "latest": {
      "version": "1.0.1",
      "downloadUrl": "https://firebasestorage.googleapis.com/v0/b/bims-8d507.appspot.com/o/releases%2FClosestGridsAddinVANILLA_v1.0.1.dll?alt=media&token=...",
      "releaseNotes": "Versión 1.0.1\n\n✅ Nuevas características:\n• Comando Dividir DirectShape\n• Sistema de auto-actualización\n\n🐛 Correcciones:\n• Fix en conversión de encofrados",
      "isMandatory": false,
      "releaseDate": "2025-11-24T07:00:00Z"
    }
  }
}
```

---

### **Opción 2: Hosting en Servidor Web Propio**

Si tienes un servidor web, sube el DLL ahí y usa la URL directa:

```json
{
  "updates": {
    "latest": {
      "version": "1.0.1",
      "downloadUrl": "https://tu-dominio.com/downloads/ClosestGridsAddinVANILLA_v1.0.1.dll",
      "releaseNotes": "...",
      "isMandatory": false,
      "releaseDate": "2025-11-24T07:00:00Z"
    }
  }
}
```

---

## 📝 Formato de Versiones

Usa **Semantic Versioning**: `MAJOR.MINOR.PATCH`

- **MAJOR**: Cambios incompatibles (1.0.0 → 2.0.0)
- **MINOR**: Nuevas funcionalidades compatibles (1.0.0 → 1.1.0)
- **PATCH**: Correcciones de bugs (1.0.0 → 1.0.1)

**Importante**: La versión en Firebase debe ser MAYOR que la actual para que se detecte la actualización.

---

## 🔧 Actualizar Versión del Proyecto

### **Opción 1: Modificar .csproj (Recomendado)**

Abre `ClosestGridsAddinVANILLA.csproj` y actualiza:

```xml
<PropertyGroup>
  <Version>1.0.1</Version>
  <FileVersion>1.0.1.0</FileVersion>
  <AssemblyVersion>1.0.1.0</AssemblyVersion>
</PropertyGroup>
```

### **Opción 2: Desde Visual Studio**

1. Click derecho en el proyecto → Properties
2. Application → Assembly Information
3. Actualiza Assembly Version y File Version

---

## 🎯 Flujo Completo de Actualización

### **Para el Desarrollador:**

```bash
# 1. Realizar cambios en el código
# 2. Actualizar versión en .csproj
<Version>1.0.2</Version>

# 3. Compilar
msbuild /p:Configuration="Release R25" /t:Build

# 4. Subir a Firebase Storage
firebase storage:upload "bin\Release R25\ClosestGridsAddinVANILLA.dll" --name "releases/v1.0.2/ClosestGridsAddinVANILLA.dll"

# 5. Obtener URL de descarga

# 6. Actualizar Firebase Realtime Database
{
  "updates": {
    "latest": {
      "version": "1.0.2",
      "downloadUrl": "[URL de Storage]",
      "releaseNotes": "...",
      "isMandatory": false
    }
  }
}
```

### **Para el Usuario:**

```
1. Abre Revit → Plugin verifica actualizaciones
2. Si hay nueva versión → Aparece diálogo
3. Usuario hace clic en "Sí"
4. Descarga en segundo plano
5. Cierra Revit → Script actualiza el DLL
6. Abre Revit nuevamente → Nueva versión activa ✅
```

---

## ⚠️ Actualizaciones Obligatorias

Para forzar una actualización (ej: bug crítico):

```json
{
  "updates": {
    "latest": {
      "version": "1.0.3",
      "downloadUrl": "...",
      "releaseNotes": "⚠️ ACTUALIZACIÓN CRÍTICA\n\nCorrige bug de seguridad importante.",
      "isMandatory": true,  ← Marca como obligatoria
      "releaseDate": "2025-11-24T10:00:00Z"
    }
  }
}
```

El usuario verá un aviso más prominente sobre la actualización obligatoria.

---

## 🔍 Debugging

### **Ver logs de actualización:**

Abre **DebugView** (Sysinternals) para ver mensajes de debug:

```
[ClosestGridsAddin] 🔔 Actualización disponible: 1.0.1
[ClosestGridsAddin] Descargando actualización...
[ClosestGridsAddin] Descarga: 50%
[ClosestGridsAddin] ✅ Actualización descargada
```

### **Ubicaciones de archivos:**

- **Caché de actualización**: `%AppData%\ClosestGridsAddin\Updates\`
- **Script de instalación**: `%AppData%\ClosestGridsAddin\Updates\apply_update.bat`
- **Backup del DLL anterior**: `[carpeta del plugin]\ClosestGridsAddinVANILLA_backup.dll`

---

## 🛡️ Seguridad

- ✅ **HTTPS obligatorio**: Solo descarga desde URLs HTTPS
- ✅ **Verificación de versión**: Compara versiones semánticas
- ✅ **Backup automático**: Guarda versión anterior antes de actualizar
- ✅ **Rollback manual**: Si algo falla, copia el backup manualmente

---

## 📊 Ejemplo Completo

### **Firebase Realtime Database:**

```json
{
  "updates": {
    "latest": {
      "version": "1.2.0",
      "downloadUrl": "https://firebasestorage.googleapis.com/v0/b/bims-8d507.appspot.com/o/releases%2Fv1.2.0%2FClosestGridsAddinVANILLA.dll?alt=media&token=abc123",
      "releaseNotes": "🎉 Versión 1.2.0\n\n✨ Nuevas características:\n• Comando Dividir DirectShape\n• Auto-actualización automática\n• Mejoras en FORMWBIMS\n\n🐛 Correcciones:\n• Fix: Conversión de encofrados\n• Fix: SessionCache en licencias\n\n⚡ Rendimiento:\n• Optimización de extracción de geometría\n• Caché mejorado",
      "isMandatory": false,
      "releaseDate": "2025-11-24T07:04:00Z"
    },
    "history": {
      "1.1.0": {
        "version": "1.1.0",
        "releaseDate": "2025-11-20T00:00:00Z",
        "releaseNotes": "Primera versión con licencias"
      },
      "1.0.0": {
        "version": "1.0.0",
        "releaseDate": "2025-11-15T00:00:00Z",
        "releaseNotes": "Versión inicial"
      }
    }
  }
}
```

---

## ✅ Checklist de Despliegue

Antes de publicar una nueva versión:

- [ ] Actualizar versión en `.csproj`
- [ ] Compilar en modo Release
- [ ] Probar localmente en Revit
- [ ] Subir DLL a Firebase Storage
- [ ] Obtener URL pública de descarga
- [ ] Actualizar Firebase Realtime Database
- [ ] Verificar reglas de seguridad
- [ ] Probar actualización en máquina de prueba
- [ ] Documentar cambios en Release Notes

---

## 🎓 Consejos

1. **Prueba primero**: Siempre prueba en una máquina de prueba antes de publicar
2. **Backup**: Firebase Storage mantiene versiones anteriores
3. **Release Notes claros**: Usuarios aprecian saber qué cambió
4. **Versionado consistente**: No saltes versiones
5. **Comunicación**: Avisa a usuarios sobre actualizaciones importantes

---

¿Necesitas ayuda? Contacta a soporte técnico.
