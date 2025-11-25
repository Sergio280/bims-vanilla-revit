# 🔒 CORRECCIÓN DEL SISTEMA DE LICENCIAS

## 🐛 PROBLEMA IDENTIFICADO

**Síntoma reportado:**
Usuario `alejoszapatasergio@gmail.com` con 0 días de licencia seguía teniendo acceso al sistema.

**Causa raíz:**
La validación de fecha de expiración en `FirebaseLicenseService.cs` usaba el operador `>` en lugar de `>=`:

```csharp
// ❌ CÓDIGO ANTERIOR (INCORRECTO)
if (DateTime.Now > license.ExpirationDate)
{
    // Solo bloqueaba DESPUÉS del día de expiración
}
```

**Problema:**
- Si la licencia expiraba el 18/11/2025, el día 18 todavía permitía acceso
- Solo bloqueaba a partir del 19/11/2025
- Además, comparaba `DateTime` completo (con horas), causando inconsistencias

---

## ✅ SOLUCIÓN IMPLEMENTADA

### 1. **Comparación estricta de fechas**

```csharp
// ✅ CÓDIGO NUEVO (CORRECTO)
var today = DateTime.Now.Date;
var expirationDate = license.ExpirationDate.Date;

if (today >= expirationDate)
{
    // Bloquea el MISMO día de expiración
    license.IsActive = false;
    await SaveLicense(license);

    return new LicenseValidationResult
    {
        IsValid = false,
        Message = $"❌ Su licencia expiró el {license.ExpirationDate:dd/MM/yyyy}.\n\nPor favor, contacte al administrador para renovar su licencia."
    };
}
```

**Cambios clave:**
- ✅ Usa `.Date` para comparar solo fechas (ignora horas/minutos)
- ✅ Usa `>=` para bloquear el mismo día de expiración
- ✅ Desactiva automáticamente licencias expiradas (`IsActive = false`)
- ✅ Mejora el mensaje de error con emoji y saltos de línea

---

### 2. **Mensajes de advertencia mejorados**

```csharp
// Licencia válida - calcular días restantes
int daysRemaining = (expirationDate - today).Days;

string warningMessage = "";
if (daysRemaining == 1)
{
    warningMessage = "\n\n⚠️ ÚLTIMO DÍA: Su licencia expira mañana.";
}
else if (daysRemaining <= 7)
{
    warningMessage = $"\n\n⚠️ ADVERTENCIA: Su licencia expira en {daysRemaining} días.";
}

return new LicenseValidationResult
{
    IsValid = true,
    License = license,
    Message = $"✅ Licencia válida hasta {license.ExpirationDate:dd/MM/yyyy}\n({daysRemaining} día{(daysRemaining != 1 ? "s" : "")} restante{(daysRemaining != 1 ? "s" : "")}){warningMessage}"
};
```

**Mejoras:**
- ✅ Mensaje especial cuando queda 1 día (último día)
- ✅ Advertencia cuando quedan 7 días o menos
- ✅ Plurales correctos (día/días)
- ✅ Emojis para mejor visibilidad
- ✅ Formato claro y legible

---

## 📊 MATRIZ DE VALIDACIÓN

| Días Restantes | Fecha Hoy vs Expiración | Resultado | Mensaje |
|----------------|-------------------------|-----------|---------|
| 30 días | `2025-11-18` vs `2025-12-18` | ✅ Permite | "Licencia válida hasta 18/12/2025 (30 días restantes)" |
| 7 días | `2025-11-18` vs `2025-11-25` | ✅ Permite | "...⚠️ ADVERTENCIA: Su licencia expira en 7 días." |
| 1 día | `2025-11-18` vs `2025-11-19` | ✅ Permite | "...⚠️ ÚLTIMO DÍA: Su licencia expira mañana." |
| **0 días** | **`2025-11-18` vs `2025-11-18`** | **❌ BLOQUEA** | **"❌ Su licencia expiró el 18/11/2025."** |
| -1 día | `2025-11-19` vs `2025-11-18` | ❌ BLOQUEA | "❌ Su licencia expiró el 18/11/2025." |

---

## 🧪 CÓMO PROBAR

### **Opción 1: Usar comando de prueba existente**

Ya existe un comando llamado `LicenseTestCommand` que puedes agregar al ribbon:

```csharp
var btnTestLicense = new PushButtonData(
    "TestLicenseButton",
    "Test\nLicencia",
    typeof(Application).Assembly.Location,
    typeof(LicenseTestCommand).FullName);
btnTestLicense.ToolTip = "Verificar estado de licencia";
panelHerramientas.AddItem(btnTestLicense);
```

Este comando muestra:
- ✅ MachineId actual
- ✅ Sesión local (email, userId, fecha guardada)
- ✅ Estado de licencia en Firebase
- ✅ Tipo, fecha de expiración, validaciones
- ✅ Botones para cerrar sesión o abrir login

---

### **Opción 2: Modificar fecha en Firebase manualmente**

1. Ir a Firebase Console → Realtime Database
2. Navegar a `licenses/{userId}/expirationDate`
3. Cambiar la fecha a diferentes valores:
   - **HOY:** Debería BLOQUEAR inmediatamente
   - **MAÑANA:** Debería mostrar "ÚLTIMO DÍA" y permitir
   - **Hace 1 semana:** Debería BLOQUEAR con mensaje "expiró el..."

---

### **Opción 3: Usar el comando real**

1. Compilar el proyecto
2. Abrir Revit
3. Hacer login con `alejoszapatasergio@gmail.com`
4. Ejecutar cualquier comando licenciado (FORMWBIMS, Ejes Cercanos, etc.)
5. Verificar que:
   - Si la licencia está expirada (0 días) → **DEBE BLOQUEAR**
   - Si tiene días restantes → **DEBE PERMITIR con advertencia**

---

## 🔧 ARCHIVOS MODIFICADOS

### `FirebaseLicenseService.cs`

**Ubicación:** `Services/FirebaseLicenseService.cs`

**Cambios:**
1. Líneas 109-125: Validación de expiración con `>=` y `.Date`
2. Líneas 152-170: Mensajes de advertencia mejorados

---

## 📝 NOTAS IMPORTANTES

### **Comportamiento actual:**
- ✅ El día de expiración **YA NO tiene acceso**
- ✅ Licencias expiradas se marcan automáticamente como `IsActive = false`
- ✅ Mensajes claros con emojis y advertencias progresivas

### **Caché de sesión:**
- La validación de Firebase se hace cada 5 minutos (ver `SessionCache.NeedsRevalidation()`)
- Si modificas la fecha en Firebase, puede tardar hasta 5 minutos en reflejarse
- Para forzar revalidación inmediata: **Cerrar sesión y volver a hacer login**

### **MachineId:**
- Cada licencia se vincula a un único equipo
- Si intentas usar la misma licencia en otro equipo → BLOQUEA
- Para transferir licencia: El administrador debe borrar `MachineId` en Firebase

---

## 🚀 SIGUIENTE PASO

1. **Compilar el proyecto:**
   ```bash
   Build → Rebuild Solution
   ```

2. **Probar con usuario expirado:**
   - Modificar fecha de expiración de `alejoszapatasergio@gmail.com` en Firebase
   - Cerrar sesión en Revit
   - Intentar ejecutar un comando → **DEBE BLOQUEAR**

3. **Verificar mensajes:**
   - Con 7 días → Ver advertencia
   - Con 1 día → Ver "ÚLTIMO DÍA"
   - Con 0 días → Ver bloqueo

---

## ✅ CONFIRMACIÓN DE CORRECCIÓN

**Antes:**
- ❌ Usuario con 0 días tenía acceso
- ❌ Comparación con horas causaba inconsistencias
- ❌ Mensajes genéricos sin advertencias progresivas

**Después:**
- ✅ Usuario con 0 días es BLOQUEADO
- ✅ Comparación solo de fechas (sin horas)
- ✅ Mensajes claros con emojis y advertencias
- ✅ Desactivación automática de licencias expiradas

---

**Fecha de corrección:** 18/11/2025
**Reportado por:** Usuario
**Corregido por:** Sistema automatizado
