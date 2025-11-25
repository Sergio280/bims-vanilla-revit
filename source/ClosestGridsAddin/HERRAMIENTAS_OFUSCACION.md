# 🛡️ Herramientas de Ofuscación para Revit Add-ins

## 📊 Comparación Completa

### **GRATUITAS**

#### 1. **Obfuscar** (Actual)
- **Precio**: GRATIS
- **Protección**: 60%
- **Pros**: Estable, integración MSBuild fácil
- **Contras**: Limitado, solo renombrado básico
- **Web**: https://github.com/obfuscar/obfuscar

#### 2. **ConfuserEx 2**
- **Precio**: GRATIS
- **Protección**: 75%
- **Pros**: Control Flow, Anti-Tamper, Anti-Debug
- **Contras**: Problemas con .NET 8.0+
- **Web**: https://github.com/mkaring/ConfuserEx

---

### **COMERCIALES**

#### 1. **.NET Reactor** ⭐ RECOMENDADA
- **Precio**:
  - Basic: $179 USD (perpetua)
  - Professional: $699 USD (perpetua)
- **Protección**: 90%
- **Características**:
  - NecroBit (virtualización IL extrema)
  - Anti-Tampering
  - String Encryption
  - Control Flow Obfuscation
  - Merge Assemblies
  - License Manager
- **Web**: https://www.eziriz.com/dotnet_reactor.htm
- **Descarga**: https://www.eziriz.com/download.htm

**Configuración para Revit**:
```xml
<Target Name="ProtectWithReactor" AfterTargets="Build" Condition="'$(Configuration)'=='Release R25'">
  <Exec Command="&quot;C:\Program Files\Eziriz\.NET Reactor\dotNET_Reactor.Console.exe&quot; -file &quot;$(TargetPath)&quot; -necrobit 1 -antitamp 1 -control_flow 1 -hide_calls 1 -resourceencryption 1" />
</Target>
```

---

#### 2. **SmartAssembly** (RedGate)
- **Precio**: $795 USD/año
- **Protección**: 85-90%
- **Características**:
  - Obfuscación robusta
  - Control Flow avanzado
  - Dependencies Embedding
  - Pruning (elimina código no usado)
  - Error Reporting integrado
- **Web**: https://www.red-gate.com/products/dotnet-development/smartassembly/

**Ventajas**:
- Muy estable
- Integración Visual Studio perfecta
- Soporte técnico excelente

---

#### 3. **Eazfuscator.NET**
- **Precio**: $399 USD/año
- **Protección**: 80%
- **Características**:
  - Automático (cero configuración)
  - String Encryption inteligente
  - Symbol Renaming Unicode
  - Resource Encryption
- **Web**: https://www.gapotchenko.com/eazfuscator.net

**Ventajas**:
- Más fácil de usar
- Soporte .NET 8.0+ excelente

---

#### 4. **Dotfuscator Professional** (PreEmptive)
- **Precio**: $1,995 USD/año
- **Protección**: 90-95%
- **Características**:
  - Control Flow muy avanzado
  - Anti-Tampering robusto
  - Runtime Intelligence (telemetría)
  - Renaming agresivo
- **Web**: https://www.preemptive.com/products/dotfuscator/

**Ventajas**:
- Usado por Microsoft
- Máxima estabilidad
- Soporte premium

---

#### 5. **ArmDot**
- **Precio**: €199 EUR/año
- **Protección**: 85%
- **Características**:
  - Anti-Debug y Anti-Dump
  - Hardware Lock
  - Soporte Revit específico
- **Web**: https://www.armdot.com/

---

#### 6. **KoiVM** (Virtualización IL)
- **Precio**: ~$500+ USD
- **Protección**: 95%+
- **Características**:
  - Virtualización completa del código IL
  - VM personalizada
  - Máxima protección
- **GitHub**: https://github.com/Yck1509/KoiVM

**Desventajas**:
- Performance penalty (20-40% más lento)
- Configuración compleja

---

## 🎯 Recomendaciones por Escenario

### **Para Proyectos Pequeños/Medianos**
**→ .NET Reactor Basic ($179)**
- Pago único
- Protección suficiente
- Fácil de usar

### **Para Proyectos Comerciales**
**→ .NET Reactor Professional ($699)**
- NecroBit extremadamente potente
- Todas las funcionalidades
- ROI excelente

### **Para Máxima Estabilidad**
**→ SmartAssembly ($795/año)**
- Muy confiable
- Soporte técnico incluido
- Integración perfecta

### **Para Máxima Protección**
**→ .NET Reactor + KoiVM (~$1,200)**
- Código virtualizado
- Prácticamente imposible de crackear
- Nivel empresarial

---

## 📋 Checklist de Implementación

### **1. Comprar Herramienta**
- [ ] Elegir herramienta según presupuesto
- [ ] Comprar licencia
- [ ] Descargar e instalar

### **2. Configurar Proyecto**
- [ ] Agregar target al .csproj
- [ ] Configurar exclusiones (Application, Commands)
- [ ] Configurar nivel de protección

### **3. Probar Build**
- [ ] Compilar en Release
- [ ] Verificar DLL ofuscado
- [ ] Probar en Revit

### **4. Verificar Funcionamiento**
- [ ] Cargar add-in en Revit
- [ ] Probar todos los comandos
- [ ] Verificar sin errores

### **5. Verificar Protección**
- [ ] Abrir DLL en ILSpy/dnSpy
- [ ] Verificar que NO se vea lógica clara
- [ ] Guardar Mapping.txt (si aplica)

---

## 🔧 Configuración .NET Reactor (Ejemplo Completo)

### **Instalación**
1. Comprar en: https://www.eziriz.com/order.htm
2. Descargar: https://www.eziriz.com/download.htm
3. Instalar (ubicación default: `C:\Program Files\Eziriz\.NET Reactor\`)

### **Integración MSBuild**

Agregar al `ClosestGridsAddinVANILLA.csproj`:

```xml
<!-- .NET Reactor Protection -->
<PropertyGroup>
  <ReactorPath>C:\Program Files\Eziriz\.NET Reactor\dotNET_Reactor.Console.exe</ReactorPath>
</PropertyGroup>

<Target Name="ReactorProtection" AfterTargets="Build" Condition="'$(Configuration)'=='Release R25'">
  <Message Importance="High" Text="Protegiendo con .NET Reactor..." />

  <!-- Crear directorio de salida -->
  <MakeDir Directories="$(TargetDir)Protected" />

  <!-- Ejecutar .NET Reactor -->
  <Exec Command="&quot;$(ReactorPath)&quot; -file &quot;$(TargetPath)&quot; -targetfile &quot;$(TargetDir)Protected\$(TargetFileName)&quot; -necrobit 1 -antitamp 1 -control_flow 1 -hide_calls 1 -stringencryption 1 -resourceencryption 1 -suppressildasm 1 -obfuscate_public_types 0" />

  <Message Importance="High" Text="✅ DLL protegido: $(TargetDir)Protected\$(TargetFileName)" />
</Target>
```

### **Parámetros Explicados**

| Parámetro | Descripción | Valor |
|-----------|-------------|-------|
| `-necrobit` | Virtualización IL extrema | `1` = ON |
| `-antitamp` | Anti-Tampering | `1` = ON |
| `-control_flow` | Control Flow Obfuscation | `1` = ON |
| `-hide_calls` | Ofusca llamadas a métodos | `1` = ON |
| `-stringencryption` | Encripta strings | `1` = ON |
| `-resourceencryption` | Encripta recursos | `1` = ON |
| `-suppressildasm` | Anti-ILDASM | `1` = ON |
| `-obfuscate_public_types` | Ofuscar tipos públicos | `0` = OFF (para Revit) |

### **Archivo de Configuración (Alternativa)**

Crear `ReactorSettings.xml`:

```xml
<?xml version="1.0"?>
<dotNetReactor>
  <input>
    <main_file>.\ClosestGridsAddinVANILLA.dll</main_file>
  </input>

  <output>
    <directory>.\Protected</directory>
  </output>

  <protection>
    <necrobit>true</necrobit>
    <anti_tampering>true</anti_tampering>
    <control_flow>true</control_flow>
    <hide_calls>true</hide_calls>
    <string_encryption>true</string_encryption>
    <resource_encryption>true</resource_encryption>
    <suppress_ildasm>true</suppress_ildasm>
  </protection>

  <exclusions>
    <!-- NO ofuscar Application -->
    <type>ClosestGridsAddinVANILLA.Application</type>
    <type>ClosestGridsAddinVANILLA.PlaceholderAvailability</type>

    <!-- NO ofuscar Commands -->
    <pattern>*Command</pattern>
  </exclusions>
</dotNetReactor>
```

Usar en .csproj:
```xml
<Exec Command="&quot;$(ReactorPath)&quot; -project &quot;$(ProjectDir)ReactorSettings.xml&quot;" />
```

---

## ⚠️ Consideraciones Importantes

### **Performance**
- **Obfuscar/SmartAssembly/Eazfuscator**: Impacto mínimo (~1-5%)
- **.NET Reactor (sin NecroBit)**: Impacto bajo (~5-10%)
- **.NET Reactor (con NecroBit)**: Impacto medio (~15-25%)
- **KoiVM**: Impacto alto (~20-40%)

### **Compatibilidad con Revit**
- ✅ Todas las herramientas funcionan con Revit
- ⚠️ SIEMPRE excluir: `Application`, `*Command`, DTOs de JSON
- ⚠️ Probar EXHAUSTIVAMENTE antes de distribuir

### **Debugging**
- 📝 SIEMPRE guardar el Mapping.txt/Symbol Map
- 📝 Necesario para traducir stack traces ofuscados
- 📝 Un Mapping por cada versión distribuida

---

## 📞 Soporte y Recursos

### **.NET Reactor**
- Email: support@eziriz.com
- Documentación: https://www.eziriz.com/help/
- Forum: https://www.eziriz.com/forum/

### **SmartAssembly**
- Email: support@red-gate.com
- Documentación: https://documentation.red-gate.com/sa/
- Chat: Disponible en el sitio

### **Eazfuscator.NET**
- Email: support@gapotchenko.com
- Documentación: https://www.gapotchenko.com/eazfuscator.net/doc/

### **Dotfuscator**
- Email: dotfuscator@preemptive.com
- Documentación: https://www.preemptive.com/dotfuscator/pro/userguide/

---

## 🎓 Recursos Adicionales

### **Blogs/Tutoriales**
- Blog .NET Reactor: https://www.eziriz.com/blog/
- Revit API Forum: https://forums.autodesk.com/t5/revit-api-forum/bd-p/160

### **Comparaciones**
- https://www.preemptive.com/obfuscator-comparison
- https://stackshare.io/stackups/dotfuscator-vs-eazfuscator-net

---

**Última actualización**: 18/11/2025
