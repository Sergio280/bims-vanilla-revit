# 🚀 Desplegar Actualizaciones con GitHub Releases (GRATIS)

La forma más profesional y gratuita de distribuir actualizaciones de tu plugin.

---

## ✅ Ventajas de GitHub Releases

- ✅ **100% GRATIS** - Sin límites de descarga
- ✅ **CDN Global** - Descargas rápidas desde cualquier país
- ✅ **Profesional** - Usado por Microsoft, Google, VSCode, etc.
- ✅ **Control de versiones** - Git integrado
- ✅ **URLs permanentes** - Nunca cambian
- ✅ **Markdown** - Release notes con formato
- ✅ **Assets ilimitados** - Sube DLL, instaladores, documentación

---

## 📋 Configuración Inicial (Una sola vez)

### **Paso 1: Crear Repositorio en GitHub**

Si aún no tienes uno:

1. Ve a https://github.com/new
2. Nombre: `bims-vanilla-revit-plugin` (o el que prefieras)
3. Descripción: "BIMS VANILLA - Plugin para Revit 2025"
4. **Privado** o Público (tú decides)
5. Click "Create repository"

### **Paso 2: Subir tu código (opcional)**

```bash
cd "D:\repos\claude RevitExtensions-main - FIREBASE AUTHENTICATION - yo"

# Inicializar git (si no lo has hecho)
git init
git add .
git commit -m "Initial commit"

# Conectar con GitHub
git remote add origin https://github.com/TU_USUARIO/bims-vanilla-revit-plugin.git
git branch -M main
git push -u origin main
```

**Nota**: Si prefieres repositorio privado, los releases también pueden ser privados o públicos.

---

## 🎯 Publicar una Nueva Versión

### **Método 1: Interfaz Web de GitHub (Más Fácil)**

1. **Ve a tu repositorio** en GitHub

2. **Click en "Releases"** (lado derecho)

3. **Click "Create a new release"**

4. **Llenar el formulario:**

   - **Tag version**: `v1.0.0` (debe coincidir con la versión del plugin)
   - **Release title**: `Versión 1.0.0 - Actualización Inicial`
   - **Description** (Release notes):
   ```markdown
   ## 🎉 Versión 1.0.0

   ### ✨ Nuevas Características
   - Comando Dividir DirectShape
   - Sistema de auto-actualización
   - Mejoras en FORMWBIMS Auto-Convert

   ### 🐛 Correcciones
   - Fix: Conversión de encofrados
   - Fix: SessionCache en sistema de licencias

   ### ⚡ Rendimiento
   - Optimización de extracción de geometría

   ---

   **Instalación:**
   1. Descarga `ClosestGridsAddinVANILLA.dll`
   2. Coloca en tu carpeta de add-ins de Revit
   3. Reinicia Revit
   ```

5. **Subir archivos** (Drag & drop):
   - `ClosestGridsAddinVANILLA.dll`
   - (Opcional) `ClosestGridsAddinVANILLA.addin`
   - (Opcional) Documentación PDF

6. **Click "Publish release"**

7. **Copiar URL del DLL**:
   - Click derecho en el archivo → Copy link address
   - URL será algo como:
   ```
   https://github.com/TU_USUARIO/bims-vanilla-revit-plugin/releases/download/v1.0.0/ClosestGridsAddinVANILLA.dll
   ```

---

### **Método 2: GitHub CLI (Automático)**

**Instalar GitHub CLI:**
```bash
winget install GitHub.cli
```

**Publicar release:**
```bash
cd "D:\repos\claude RevitExtensions-main - FIREBASE AUTHENTICATION - yo"

# Login (primera vez)
gh auth login

# Crear release
gh release create v1.0.0 \
  --title "Versión 1.0.0 - Actualización Inicial" \
  --notes "## Nuevas características
- Comando Dividir DirectShape
- Sistema de auto-actualización

## Correcciones
- Fix en conversión de encofrados" \
  "source\ClosestGridsAddin\bin\Release R25\ClosestGridsAddinVANILLA.dll"

# Obtener URL del archivo
gh release view v1.0.0 --json assets --jq '.assets[0].url'
```

---

## 🔧 Actualizar Firebase con la URL de GitHub

Después de crear el release, actualiza Firebase Realtime Database:

```json
{
  "updates": {
    "latest": {
      "version": "1.0.0",
      "downloadUrl": "https://github.com/TU_USUARIO/bims-vanilla-revit-plugin/releases/download/v1.0.0/ClosestGridsAddinVANILLA.dll",
      "releaseNotes": "🎉 Versión 1.0.0\n\n✨ Nuevas características:\n• Comando Dividir DirectShape\n• Sistema de auto-actualización\n\n🐛 Correcciones:\n• Fix en conversión de encofrados",
      "isMandatory": false,
      "releaseDate": "2025-11-24T07:00:00Z"
    }
  }
}
```

---

## 📊 Ejemplo Real - Workflow Completo

### **Escenario: Publicar versión 1.0.1**

```bash
# 1. Actualizar versión en .csproj
# <Version>1.0.1</Version>

# 2. Compilar
cd "D:\repos\claude RevitExtensions-main - FIREBASE AUTHENTICATION - yo\source\ClosestGridsAddin"
msbuild /p:Configuration="Release R25" /t:Build

# 3. Commit cambios (opcional)
git add .
git commit -m "Release v1.0.1: Bug fixes"
git push

# 4. Crear release en GitHub
gh release create v1.0.1 \
  --title "Versión 1.0.1 - Correcciones" \
  --notes "## 🐛 Correcciones
- Fix crítico en conversión de DirectShapes
- Mejora de rendimiento en FORMWBIMS

## 📝 Notas
Esta es una actualización recomendada para todos los usuarios." \
  "bin\Release R25\ClosestGridsAddinVANILLA.dll"

# 5. Obtener URL
gh release view v1.0.1 --json assets --jq '.assets[0].url'
# Output: https://github.com/.../v1.0.1/ClosestGridsAddinVANILLA.dll

# 6. Actualizar Firebase manualmente con esa URL
```

**Resultado:**
- ✅ Release publicado en GitHub
- ✅ DLL disponible para descarga
- ✅ Changelog público
- ✅ Usuarios reciben actualización automáticamente

---

## 🔒 Releases Privados (Solo para Licencias Premium)

Si quieres que solo usuarios con licencia descarguen:

### **Opción A: Repositorio Privado**
- Los releases siguen siendo accesibles con autenticación
- Necesitas un token de GitHub en el código

### **Opción B: Pre-releases**
- Marca como "Pre-release" para beta testers
- Solo visible para colaboradores del repo

### **Opción C: Combinado con Firebase**
```csharp
// Agregar autenticación en UpdateChecker.cs
private async Task<bool> DownloadUpdateAsync(string downloadUrl, string licenseKey)
{
    using (HttpClient client = new HttpClient())
    {
        // Agregar header de autenticación
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {licenseKey}");

        // Tu servidor valida la licencia antes de redirigir a GitHub
        var response = await client.GetAsync($"https://tu-api.com/download?key={licenseKey}");
        // ...
    }
}
```

---

## 📈 Estadísticas de Descargas

GitHub te da estadísticas automáticas:

1. Ve a tu repositorio
2. Click en "Releases"
3. Cada release muestra:
   - 📥 Número de descargas por archivo
   - 📅 Fecha de publicación
   - 👥 Quién lo publicó

---

## 🎓 Mejores Prácticas

### **Versionado Semántico**
```
v1.0.0 → Primera versión estable
v1.0.1 → Bug fix
v1.1.0 → Nueva característica
v2.0.0 → Cambio incompatible
```

### **Tags consistentes**
```
✅ v1.0.0
✅ v1.0.1
❌ 1.0.0
❌ version-1.0.0
```

### **Release Notes claras**
```markdown
## ✨ Nuevas Características
- [Feature A]: Descripción breve

## 🐛 Correcciones
- [Bug B]: Lo que se corrigió

## ⚠️ Cambios Importantes
- [Breaking Change]: Qué cambió

## 📦 Instalación
1. Descargar DLL
2. Copiar a: %AppData%\Autodesk\Revit\Addins\2025
3. Reiniciar Revit
```

---

## 🆚 Comparación de Opciones

| Característica | GitHub Releases | Firebase Storage | Google Drive |
|----------------|-----------------|------------------|--------------|
| **Costo** | ✅ Gratis ilimitado | ✅ 5GB gratis | ✅ 15GB gratis |
| **CDN** | ✅ Sí (global) | ✅ Sí | ❌ No |
| **Velocidad** | ⚡ Muy rápida | ⚡ Muy rápida | 🐢 Media |
| **Versionado** | ✅ Integrado | ❌ Manual | ❌ Manual |
| **Profesional** | ✅ Sí | ✅ Sí | ❌ No |
| **URLs estables** | ✅ Sí | ✅ Sí | ⚠️ Pueden cambiar |
| **Estadísticas** | ✅ Sí | ⚠️ Limitadas | ❌ No |

**Ganador: GitHub Releases** 🏆

---

## 📝 Checklist de Publicación

Antes de publicar:

- [ ] Versión actualizada en `.csproj`
- [ ] Compilado en Release
- [ ] Probado en Revit
- [ ] Release notes escritas
- [ ] Tag creado (ej: v1.0.0)
- [ ] DLL subido a release
- [ ] URL copiada
- [ ] Firebase actualizado
- [ ] Probado descarga

---

## 💡 Pro Tips

1. **Automatización**: Usa GitHub Actions para compilar y publicar automáticamente
2. **Draft Releases**: Crea como borrador, prueba, luego publica
3. **Pre-releases**: Marca como pre-release para betas
4. **Changelogs**: Usa herramientas como `github-changelog-generator`
5. **Firma digital**: Firma tu DLL con certificado de código para mayor confianza

---

¿Listo para publicar tu primera release? 🚀

**Comando rápido:**
```bash
gh release create v1.0.0 --generate-notes "bin\Release R25\ClosestGridsAddinVANILLA.dll"
```
