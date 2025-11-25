# 🔐 Panel de Administración de Licencias BIMS VANILLA

Panel web profesional para gestionar las licencias de usuarios del add-in BIMS VANILLA de Revit.

---

## 🚀 Configuración Inicial

### Paso 1: Obtener credenciales de Firebase

1. **Ir a Firebase Console:**
   - https://console.firebase.google.com/
   - Seleccionar tu proyecto "BIMS"

2. **Obtener configuración:**
   - Click en el ícono de **configuración** (⚙️) → **Configuración del proyecto**
   - Scroll hacia abajo hasta **"Tus aplicaciones"**
   - Si no tienes una app web, click en **"Agregar app"** → **Web**
   - Copiar el objeto `firebaseConfig` que aparece

3. **Pegar en `firebase-config.js`:**
   ```javascript
   const firebaseConfig = {
       apiKey: "AIzaSyC...",  // Tu API key real
       authDomain: "bims-8d507.firebaseapp.com",
       databaseURL: "https://bims-8d507-default-rtdb.firebaseio.com",
       projectId: "bims-8d507",
       storageBucket: "bims-8d507.appspot.com",
       messagingSenderId: "1234567890",
       appId: "1:1234567890:web:abcdef..."
   };
   ```

### Paso 2: Crear cuenta de administrador

1. **Firebase Console → Authentication**
2. **Click en "Users" → "Add user"**
3. **Crear usuario administrador:**
   - Email: `admin@bimsvanilla.com` (o el que prefieras)
   - Password: Una contraseña segura
4. **Guardar estas credenciales** - las usarás para hacer login en el panel

---

## 🌐 Cómo Usar el Panel

### Opción A: Abrir localmente (Más simple)

1. **Navegar a la carpeta:**
   ```
   D:\repos\claude RevitExtensions-main - FIREBASE AUTHENTICATION - yo\AdminPanel\
   ```

2. **Doble click en:**
   ```
   admin-panel.html
   ```

3. **Se abrirá en tu navegador** (Chrome, Edge, Firefox, etc.)

4. **Login:**
   - Email: El que creaste en Firebase Authentication
   - Password: La contraseña

### Opción B: Hosting en Firebase (Recomendado para producción)

Si quieres acceder al panel desde cualquier lugar (no solo tu computadora):

1. **Instalar Firebase CLI:**
   ```bash
   npm install -g firebase-tools
   ```

2. **Login a Firebase:**
   ```bash
   firebase login
   ```

3. **Inicializar hosting:**
   ```bash
   cd "D:\repos\claude RevitExtensions-main - FIREBASE AUTHENTICATION - yo\AdminPanel"
   firebase init hosting
   ```
   - Select your Firebase project
   - Public directory: `.` (punto)
   - Configure as single-page app: No
   - Overwrite index.html: No

4. **Deploy:**
   ```bash
   firebase deploy --only hosting
   ```

5. **Acceder desde cualquier lugar:**
   ```
   https://bims-8d507.web.app
   ```

---

## 📊 Funcionalidades del Panel

### 🔍 Vista General

- **Estadísticas en tiempo real:**
  - Total de licencias
  - Licencias activas
  - Licencias expiradas
  - Licencias Trial

- **Búsqueda:**
  - Por email
  - Por nombre
  - Por License ID
  - Por tipo de licencia

- **Filtros rápidos:**
  - Todas
  - Activas
  - Expiradas
  - Trial

### ✏️ Editar Licencia

Al hacer click en "Editar" en cualquier licencia, puedes:

1. **Ver información:**
   - Email del usuario
   - Nombre
   - Machine ID
   - License ID

2. **Modificar:**
   - **Tipo de licencia:**
     - Trial
     - Monthly (Mensual)
     - Annual (Anual)
     - Lifetime (Vitalicia)

   - **Fecha de expiración:**
     - Selector manual de fecha y hora
     - Botones rápidos:
       - +7 días
       - +30 días (renovación mensual)
       - +90 días (renovación trimestral)
       - +365 días (renovación anual)

   - **Estado:**
     - Activa / Desactivada

3. **Acciones:**
   - **Guardar cambios:** Actualiza la licencia en Firebase
   - **Eliminar licencia:** Borra permanentemente (⚠️ no se puede deshacer)

---

## 🛠️ Casos de Uso Comunes

### Renovar una licencia expirada

1. Buscar al usuario por email
2. Click en "Editar"
3. Click en botón "+30 días" (o el período que necesites)
4. Click en "Guardar Cambios"
5. ✅ El usuario puede volver a usar el add-in inmediatamente

### Convertir Trial a licencia completa

1. Buscar la licencia Trial
2. Click en "Editar"
3. Cambiar tipo a "Annual" o "Monthly"
4. Agregar +365 días (si es anual)
5. Click en "Guardar Cambios"

### Desactivar una licencia (sin eliminarla)

1. Buscar la licencia
2. Click en "Editar"
3. Desmarcar checkbox "Licencia Activa"
4. Click en "Guardar Cambios"
5. El usuario no podrá usar el add-in aunque no haya expirado

### Renovación masiva (próximamente)

Para renovar múltiples licencias a la vez, puedes usar scripts de Firebase Admin SDK.

---

## 📱 Responsive Design

El panel está optimizado para:
- 💻 **Desktop:** Experiencia completa
- 📱 **Tablet:** Interfaz adaptada
- 📱 **Mobile:** Funcionalidad básica (ver y editar)

---

## 🔒 Seguridad

### Buenas prácticas:

1. **Nunca compartas las credenciales de administrador**
2. **Cambia la contraseña regularmente**
3. **Solo otorga acceso al panel a personas de confianza**
4. **Revisa el log de cambios en Firebase Console**

### Configurar reglas de seguridad en Firebase:

Firebase Console → Realtime Database → Rules

```json
{
  "rules": {
    "licenses": {
      ".read": "auth != null",
      ".write": "auth != null"
    }
  }
}
```

Esto asegura que **solo usuarios autenticados** puedan leer/escribir licencias.

---

## 🐛 Solución de Problemas

### Error: "Firebase is not defined"

**Solución:** Verifica que tienes conexión a internet. Los scripts de Firebase se cargan desde CDN.

### Error: "Invalid credentials"

**Solución:**
1. Verifica el email y password en Firebase Authentication
2. Asegúrate de haber creado el usuario administrador

### Error: "Permission denied"

**Solución:**
1. Verifica las reglas de seguridad en Realtime Database
2. Asegúrate de estar autenticado

### Las licencias no se cargan

**Solución:**
1. Abre la consola del navegador (F12)
2. Revisa errores en la pestaña "Console"
3. Verifica `firebase-config.js` con las credenciales correctas

---

## 📊 Estructura de Datos

El panel trabaja con esta estructura en Firebase:

```json
{
  "licenses": {
    "9jOuGmSkeTTLnsYr23KO5drnkL32": {
      "CreatedAt": "2025-10-20T12:16:18.378Z",
      "Email": "usuario@ejemplo.com",
      "ExpirationDate": "2025-12-19T12:16:18.378Z",
      "IsActive": true,
      "LicenseId": "1f837484-1882-49ab-956b-007ed446bbce",
      "LicenseKey": "DAA8B1F33A2EAE...",
      "LicenseType": "Trial",
      "MachineId": "a14427...",
      "MaxDevices": 1,
      "UserId": "9jOuGmSkeTTLnsYr23KO5drnkL32",
      "ValidationCount": 97
    }
  }
}
```

---

## 🎨 Personalización

### Cambiar colores:

Editar `admin-panel.css`:

```css
:root {
    --primary-color: #2563eb;  /* Azul principal */
    --success-color: #22c55e;   /* Verde */
    --danger-color: #ef4444;    /* Rojo */
}
```

### Cambiar logo:

Editar `admin-panel.html` línea 11:
```html
<h1>🔐 TU LOGO AQUI</h1>
```

---

## 📞 Soporte

Si tienes problemas:
1. Revisa la consola del navegador (F12)
2. Verifica las credenciales de Firebase
3. Asegúrate de tener permisos de administrador

---

## 🚀 Próximas Funcionalidades

- [ ] Exportar licencias a Excel
- [ ] Gráficos de uso
- [ ] Notificaciones de licencias próximas a expirar
- [ ] Renovación masiva
- [ ] Historial de cambios
- [ ] Envío de emails automáticos

---

**¡Listo para usar!** 🎉

Abre `admin-panel.html` en tu navegador y comienza a gestionar tus licencias.
