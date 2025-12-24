# Diseño Técnico: Sistema de Encofrado Automatizado Mejorado

## Requisitos Confirmados

### 1. Categorías de Elementos Soportados
- **OST_StructuralColumns** (Columnas estructurales)
- **OST_StructuralFraming** (Vigas)
- **OST_Walls** (Muros estructurales)
- **OST_Floors** (Losas)
- **OST_StructuralFoundation** (Cimentaciones)
- **OST_Stairs** (Escaleras)

### 2. Dirección de Extrusión
**CRÍTICO:** El encofrado debe extruirse HACIA AFUERA (sentido contrario al centro del elemento)

**Cálculo de dirección correcta:**
```csharp
XYZ DireccionExtrusion(PlanarFace cara, Solid solidoElemento)
{
    XYZ normalCara = cara.FaceNormal;
    XYZ centroElemento = ObtenerCentroide(solidoElemento);
    XYZ centroCara = ObtenerCentroide(cara);

    // Vector desde centro del elemento hacia centro de la cara
    XYZ vectorHaciaAfuera = (centroCara - centroElemento).Normalize();

    // Asegurar que la normal apunte hacia afuera
    if (normalCara.DotProduct(vectorHaciaAfuera) < 0)
    {
        normalCara = -normalCara; // Invertir si apunta hacia adentro
    }

    return normalCara;
}
```

### 3. Reglas de Encofrado por Tipo de Elemento

#### A. Columnas (OST_StructuralColumns)
```
Encofrar: Caras verticales SOLAMENTE (no superior/inferior)
Tipo: MUROS (Wall)
Criterio: Math.Abs(normal.Z) < 0.3
```

#### B. Vigas (OST_StructuralFraming)
```
Encofrar:
  - Caras laterales verticales → MUROS (Wall)
  - Cara inferior horizontal → SUELO (Floor)
  - NO encofrar cara superior

Criterios:
  - Lateral vertical: Math.Abs(normal.Z) < 0.3
  - Inferior: normal.Z < -0.7 (apunta hacia abajo)
  - Superior: normal.Z > 0.7 (omitir)
```

#### C. Muros (OST_Walls)
```
Encofrar: Caras laterales verticales SOLAMENTE (no superior/inferior)
Tipo: MUROS (Wall)
Criterio: Math.Abs(normal.Z) < 0.3
```

#### D. Losas/Suelos (OST_Floors)
```
Encofrar: Cara inferior SOLAMENTE (no superior)
Tipo: SUELO (Floor)
Criterio: normal.Z < -0.7 (apunta hacia abajo)
```

#### E. Escaleras (OST_Stairs)
```
Encofrar:
  - Caras verticales → MUROS (Wall)
  - Caras inclinadas (huellas/contrahuellas) → SUELOS (Floor)
  - Caras horizontales inferiores → SUELOS (Floor)

Criterios:
  - Vertical: Math.Abs(normal.Z) < 0.3
  - Inclinada: 0.3 <= Math.Abs(normal.Z) <= 0.7
  - Horizontal inferior: normal.Z < -0.7
```

#### F. Cimentaciones (OST_StructuralFoundation)
```
Encofrar:
  - Caras verticales → MUROS (Wall)
  - Caras horizontales inferiores → SUELOS (Floor)

Criterios:
  - Vertical: Math.Abs(normal.Z) < 0.3
  - Horizontal inferior: normal.Z < -0.7
```

### 4. Sistema de Masas Conceptuales para Geometría Curva

**Flujo para Columnas Circulares:**

```
1. Detectar cara cilíndrica (CylindricalFace)
2. Crear muro curvo siguiendo la cara
3. Crear masa conceptual temporal (FreeFormElement o DirectShape)
4. Usar InstanceVoidCutUtils.AddInstanceVoidCut() para cortar el muro
5. Resultado: Muro nativo con geometría curva exacta
```

**Implementación:**
```csharp
// PASO 1: Crear muro curvo base
Arc arco = ExtraerArcoDeCaraCilindrica(cylindricalFace);
Wall muroCurvo = Wall.Create(doc, arco, wallType.Id, nivel.Id, altura, 0, false, false);

// PASO 2: Crear masa para cortar (DirectShape categoría OST_Mass)
Solid solidoCorte = CrearSolidoDeCorteParaCaraCurva(cylindricalFace);
DirectShape masaCorte = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_Mass));
masaCorte.SetShape(new GeometryObject[] { solidoCorte });

// PASO 3: Aplicar corte al muro
InstanceVoidCutUtils.AddInstanceVoidCut(doc, muroCurvo, masaCorte);

// PASO 4: (Opcional) Ocultar o eliminar la masa de corte
```

**NOTA IMPORTANTE:**
- `InstanceVoidCutUtils` requiere que el elemento de corte sea categoría `OST_Mass`
- DirectShape puede usar categoría `OST_Mass` para este propósito
- La masa debe intersectar completamente la porción a remover

### 5. Área para Tablas de Planificación

**Objetivo:** El área reportada debe ser la de la cara encofrada (no el volumen del encofrado)

**Estrategias:**

#### Opción A: Parámetro Compartido "Área_Encofrada"
```csharp
// Crear parámetro compartido personalizado
Parameter paramArea = muro.LookupParameter("Área_Encofrada");
if (paramArea != null && !paramArea.IsReadOnly)
{
    paramArea.Set(areaCaraOriginal); // En pies cuadrados (unidad interna)
}
```

#### Opción B: Usar Parámetro de Comentarios
```csharp
// Almacenar en comentarios en formato parseable
string comentario = $"AREA:{areaCaraOriginal:F4}|ELEM:{elementoOriginal.Id.Value}";
Parameter paramComentarios = muro.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
paramComentarios.Set(comentario);
```

#### Opción C: Calcular área desde geometría del Wall/Floor
```csharp
// El área de superficie del Wall/Floor debería coincidir con el área de la cara
// Si el espesor es muy pequeño (ej: 25mm), el área de caras mayores ≈ área encofrada
double areaCalculada = CalcularAreaSuperficieExterna(muro);
```

**RECOMENDACIÓN:** Usar Opción A (parámetro compartido) para máxima precisión y facilidad en schedules.

---

## Arquitectura del Sistema Mejorado

### Clase: `EncofradoConfig` (Nueva)
```csharp
public class EncofradoConfig
{
    public WallType TipoMuro { get; set; }
    public FloorType TipoSuelo { get; set; }
    public bool UsarMasasParaGeometriaCurva { get; set; } = true;
    public bool ConvertirDirectShapeANativo { get; set; } = true;
    public bool MantenerDirectShapeTemporal { get; set; } = false;
    public double ToleranciaContacto { get; set; } = 0.05; // 5cm
    public double ToleranciaVolumen { get; set; } = 0.0001; // 0.1 litros
}
```

### Clase: `ReglasEncofrado` (Nueva)
```csharp
public static class ReglasEncofrado
{
    public static bool DebeEncofrarCara(
        Element elemento,
        PlanarFace cara,
        out TipoElementoEncofrado tipoEncofrado)
    {
        BuiltInCategory categoria = GetBuiltInCategory(elemento);
        XYZ normal = cara.FaceNormal;

        switch (categoria)
        {
            case BuiltInCategory.OST_StructuralColumns:
                return ReglaColumna(cara, normal, out tipoEncofrado);

            case BuiltInCategory.OST_StructuralFraming:
                return ReglaViga(cara, normal, out tipoEncofrado);

            case BuiltInCategory.OST_Walls:
                return ReglaMuro(cara, normal, out tipoEncofrado);

            case BuiltInCategory.OST_Floors:
                return ReglaLosa(cara, normal, out tipoEncofrado);

            case BuiltInCategory.OST_Stairs:
                return ReglaEscalera(cara, normal, out tipoEncofrado);

            case BuiltInCategory.OST_StructuralFoundation:
                return ReglaCimentacion(cara, normal, out tipoEncofrado);

            default:
                tipoEncofrado = TipoElementoEncofrado.NoDefinido;
                return false;
        }
    }

    private static bool ReglaColumna(PlanarFace cara, XYZ normal,
        out TipoElementoEncofrado tipo)
    {
        // Solo caras verticales
        bool esVertical = Math.Abs(normal.Z) < 0.3;
        tipo = esVertical ? TipoElementoEncofrado.Muro : TipoElementoEncofrado.NoDefinido;
        return esVertical;
    }

    private static bool ReglaViga(PlanarFace cara, XYZ normal,
        out TipoElementoEncofrado tipo)
    {
        double nz = normal.Z;

        // Cara inferior → Suelo
        if (nz < -0.7)
        {
            tipo = TipoElementoEncofrado.Suelo;
            return true;
        }

        // Caras laterales → Muro
        if (Math.Abs(nz) < 0.3)
        {
            tipo = TipoElementoEncofrado.Muro;
            return true;
        }

        // Cara superior → NO encofrar
        tipo = TipoElementoEncofrado.NoDefinido;
        return false;
    }

    private static bool ReglaMuro(PlanarFace cara, XYZ normal,
        out TipoElementoEncofrado tipo)
    {
        // Solo caras laterales verticales
        bool esVertical = Math.Abs(normal.Z) < 0.3;
        tipo = esVertical ? TipoElementoEncofrado.Muro : TipoElementoEncofrado.NoDefinido;
        return esVertical;
    }

    private static bool ReglaLosa(PlanarFace cara, XYZ normal,
        out TipoElementoEncofrado tipo)
    {
        // Solo cara inferior
        bool esInferior = normal.Z < -0.7;
        tipo = esInferior ? TipoElementoEncofrado.Suelo : TipoElementoEncofrado.NoDefinido;
        return esInferior;
    }

    private static bool ReglaEscalera(PlanarFace cara, XYZ normal,
        out TipoElementoEncofrado tipo)
    {
        double nz = Math.Abs(normal.Z);

        // Cara vertical → Muro
        if (nz < 0.3)
        {
            tipo = TipoElementoEncofrado.Muro;
            return true;
        }

        // Cara inclinada o horizontal inferior → Suelo
        if (normal.Z < 0.7) // No superior
        {
            tipo = TipoElementoEncofrado.Suelo;
            return true;
        }

        tipo = TipoElementoEncofrado.NoDefinido;
        return false;
    }

    private static bool ReglaCimentacion(PlanarFace cara, XYZ normal,
        out TipoElementoEncofrado tipo)
    {
        // Vertical → Muro
        if (Math.Abs(normal.Z) < 0.3)
        {
            tipo = TipoElementoEncofrado.Muro;
            return true;
        }

        // Horizontal inferior → Suelo
        if (normal.Z < -0.7)
        {
            tipo = TipoElementoEncofrado.Suelo;
            return true;
        }

        tipo = TipoElementoEncofrado.NoDefinido;
        return false;
    }
}

public enum TipoElementoEncofrado
{
    NoDefinido,
    Muro,
    Suelo
}
```

### Clase: `DireccionExtrusionHelper` (Nueva)
```csharp
public static class DireccionExtrusionHelper
{
    /// <summary>
    /// Calcula la dirección de extrusión HACIA AFUERA del elemento
    /// </summary>
    public static XYZ ObtenerDireccionHaciaAfuera(
        PlanarFace cara,
        Solid solidoElemento)
    {
        XYZ normalCara = cara.FaceNormal;

        // Calcular centroide del sólido
        XYZ centroElemento = ObtenerCentroideSolido(solidoElemento);

        // Calcular centroide de la cara
        XYZ centroCara = ObtenerCentroideCara(cara);

        // Vector desde centro del elemento hacia centro de la cara
        XYZ vectorHaciaAfuera = (centroCara - centroElemento).Normalize();

        // Verificar que la normal apunte hacia afuera
        double producto = normalCara.DotProduct(vectorHaciaAfuera);

        if (producto < 0)
        {
            // La normal apunta hacia adentro, invertirla
            normalCara = -normalCara;
        }

        return normalCara;
    }

    private static XYZ ObtenerCentroideSolido(Solid solido)
    {
        XYZ centroide = solido.ComputeCentroid();
        if (centroide == null)
        {
            // Fallback: usar centro del BoundingBox
            BoundingBoxXYZ bbox = solido.GetBoundingBox();
            centroide = (bbox.Min + bbox.Max) / 2.0;
        }
        return centroide;
    }

    private static XYZ ObtenerCentroideCara(PlanarFace cara)
    {
        var curveLoops = cara.GetEdgesAsCurveLoops();
        if (curveLoops == null || curveLoops.Count == 0)
            return XYZ.Zero;

        List<XYZ> puntos = new List<XYZ>();

        // Usar solo el primer loop (exterior)
        foreach (Curve curve in curveLoops[0])
        {
            puntos.Add(curve.GetEndPoint(0));
        }

        if (puntos.Count == 0) return XYZ.Zero;

        XYZ suma = puntos.Aggregate(XYZ.Zero, (acc, p) => acc + p);
        return suma / puntos.Count;
    }
}
```

### Clase: `GeometriaCurvaHelper` (Nueva)
```csharp
public static class GeometriaCurvaHelper
{
    /// <summary>
    /// Crea encofrado para cara cilíndrica usando masa conceptual para cortar
    /// </summary>
    public static Wall CrearEncofradoColumnaCircular(
        Document doc,
        CylindricalFace caraCilindrica,
        WallType wallType,
        Level nivel,
        double altura,
        List<Element> elementosAdyacentes)
    {
        // PASO 1: Extraer arco de la cara cilíndrica
        Arc arcoBase = ExtraerArcoDeCaraCilindrica(caraCilindrica, nivel.Elevation);

        if (arcoBase == null) return null;

        // PASO 2: Crear muro curvo base
        Wall muroCurvo = Wall.Create(
            doc,
            arcoBase,
            wallType.Id,
            nivel.Id,
            altura,
            0,
            false,
            false);

        if (muroCurvo == null) return null;

        // PASO 3: Crear sólidos de corte para elementos adyacentes
        List<DirectShape> masasDeCorte = new List<DirectShape>();

        foreach (var elemAdyacente in elementosAdyacentes)
        {
            Solid solidoAdyacente = EncofradoBaseHelper.ObtenerSolidoPrincipal(elemAdyacente);
            if (solidoAdyacente == null) continue;

            // Verificar intersección con el muro
            Solid solidoMuro = EncofradoBaseHelper.ObtenerSolidoPrincipal(muroCurvo);

            try
            {
                Solid interseccion = BooleanOperationsUtils.ExecuteBooleanOperation(
                    solidoMuro,
                    solidoAdyacente,
                    BooleanOperationsType.Intersect);

                if (interseccion != null && interseccion.Volume > 0.0001)
                {
                    // Crear masa de corte
                    DirectShape masaCorte = DirectShape.CreateElement(
                        doc,
                        new ElementId(BuiltInCategory.OST_Mass));

                    masaCorte.SetShape(new GeometryObject[] { solidoAdyacente });
                    masaCorte.Name = $"Corte_{elemAdyacente.Id}";

                    masasDeCorte.Add(masaCorte);

                    // Aplicar corte al muro
                    InstanceVoidCutUtils.AddInstanceVoidCut(doc, muroCurvo, masaCorte);
                }
            }
            catch
            {
                // Si falla el corte, continuar con siguiente elemento
            }
        }

        // PASO 4: (Opcional) Ocultar masas de corte
        foreach (var masa in masasDeCorte)
        {
            // Opción 1: Cambiar a workset oculto
            // Opción 2: Eliminar si no se necesitan para regeneración futura
            // Opción 3: Mantener pero en categoría que no se visualiza
        }

        return muroCurvo;
    }

    private static Arc ExtraerArcoDeCaraCilindrica(
        CylindricalFace caraCilindrica,
        double elevacion)
    {
        try
        {
            // Obtener los parámetros del cilindro
            var cylinder = caraCilindrica.GetSurface() as CylindricalSurface;
            if (cylinder == null) return null;

            XYZ origen = cylinder.Origin;
            XYZ eje = cylinder.Axis;
            double radio = cylinder.Radius;

            // Proyectar origen al nivel
            XYZ origenProyectado = new XYZ(origen.X, origen.Y, elevacion);

            // Crear arco de 360 grados (círculo completo)
            // O extraer arco parcial según los límites de la cara

            BoundingBoxUV bbox = caraCilindrica.GetBoundingBox();
            double uMin = bbox.Min.U;
            double uMax = bbox.Max.U;

            // Crear puntos del arco
            XYZ punto1 = origenProyectado + radio * XYZ.BasisX;
            XYZ puntoMedio = origenProyectado + radio * XYZ.BasisY;
            XYZ punto2 = origenProyectado - radio * XYZ.BasisX;

            // Crear arco (semicírculo como ejemplo)
            // Para círculo completo, usar Wall.Create con curva circular
            Arc arco = Arc.Create(punto1, punto2, puntoMedio);

            return arco;
        }
        catch
        {
            return null;
        }
    }
}
```

---

## Flujo de Ejecución Mejorado

### Método Principal: `CrearEncofradoInteligente()`

```
1. Usuario selecciona elemento(s)
2. Usuario selecciona WallType y FloorType
3. Sistema detecta categoría del elemento
4. Para cada cara del elemento:

   4.1. Verificar si debe encofrarse según ReglasEncofrado
   4.2. Determinar tipo de encofrado (Muro o Suelo)
   4.3. Calcular dirección de extrusión HACIA AFUERA
   4.4. Obtener elementos adyacentes

   4.5. Si es cara PLANAR:
        a. Crear DirectShape con recortes por adyacentes
        b. Convertir a Wall o Floor nativo
        c. Almacenar área de cara original en parámetro
        d. Eliminar DirectShape temporal

   4.6. Si es cara CURVA (cilíndrica):
        a. Crear Wall curvo base
        b. Crear masas de corte para elementos adyacentes
        c. Aplicar cortes con InstanceVoidCutUtils
        d. Almacenar área de cara original en parámetro
        e. (Opcional) Ocultar masas de corte

5. Mostrar resumen al usuario:
   - Número de caras encofradas
   - Tipo de elementos creados (muros/suelos)
   - Área total encofrada
   - Recortes aplicados
```

---

## Parámetros Compartidos Necesarios

### Definición de Parámetros

```xml
<!-- Archivo: EncofradoSharedParameters.txt -->

# This is a Revit shared parameter file.
*META	VERSION	MINVERSION
META	2	1
*GROUP	ID	NAME
GROUP	1	Encofrado
*PARAM	GUID	NAME	DATATYPE	DATACATEGORY	GROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE
PARAM	a1b2c3d4-e5f6-7890-abcd-ef1234567890	Área_Encofrada	AREA	-1	1	1	Área de la cara encofrada	1
PARAM	b2c3d4e5-f6g7-8901-bcde-f12345678901	ID_Elemento_Original	TEXT	-1	1	1	ID del elemento estructural que se está encofrando	1
PARAM	c3d4e5f6-g7h8-9012-cdef-123456789012	Tipo_Encofrado	TEXT	-1	1	1	Tipo de encofrado (Columna/Viga/Muro/Losa/Escalera)	1
```

### Código para Crear/Asignar Parámetros

```csharp
public static void AsignarParametrosEncofrado(
    Element encofrado,
    double areaCaraOriginal,
    Element elementoOriginal)
{
    // 1. Área encofrada
    Parameter paramArea = encofrado.LookupParameter("Área_Encofrada");
    if (paramArea != null && !paramArea.IsReadOnly)
    {
        paramArea.Set(areaCaraOriginal); // Unidades internas (pies cuadrados)
    }

    // 2. ID elemento original
    Parameter paramID = encofrado.LookupParameter("ID_Elemento_Original");
    if (paramID != null && !paramID.IsReadOnly)
    {
        paramID.Set(elementoOriginal.Id.ToString());
    }

    // 3. Tipo de encofrado
    Parameter paramTipo = encofrado.LookupParameter("Tipo_Encofrado");
    if (paramTipo != null && !paramTipo.IsReadOnly)
    {
        string tipoElemento = ObtenerNombreTipoElemento(elementoOriginal);
        paramTipo.Set(tipoElemento);
    }
}

private static string ObtenerNombreTipoElemento(Element elemento)
{
    BuiltInCategory categoria = GetBuiltInCategory(elemento);

    switch (categoria)
    {
        case BuiltInCategory.OST_StructuralColumns:
            return "Columna";
        case BuiltInCategory.OST_StructuralFraming:
            return "Viga";
        case BuiltInCategory.OST_Walls:
            return "Muro";
        case BuiltInCategory.OST_Floors:
            return "Losa";
        case BuiltInCategory.OST_Stairs:
            return "Escalera";
        case BuiltInCategory.OST_StructuralFoundation:
            return "Cimentación";
        default:
            return "Desconocido";
    }
}
```

---

## Próximos Pasos de Implementación

### Fase 1: Fundamentos (PRIORIDAD ALTA)
- [x] Documentar diseño técnico
- [ ] Crear clase `ReglasEncofrado`
- [ ] Crear clase `DireccionExtrusionHelper`
- [ ] Modificar `EncofradoBaseHelper.CrearEncofradoInteligente()` para usar dirección correcta

### Fase 2: Geometría Curva (PRIORIDAD ALTA)
- [ ] Crear clase `GeometriaCurvaHelper`
- [ ] Implementar `CrearEncofradoColumnaCircular()`
- [ ] Probar sistema de corte con masas conceptuales

### Fase 3: Conversión a Nativos (PRIORIDAD MEDIA)
- [ ] Mejorar `DirectShapeToWallFloorConverter`
- [ ] Implementar parámetros compartidos
- [ ] Validar área reportada en schedules

### Fase 4: Integración (PRIORIDAD MEDIA)
- [ ] Actualizar todos los comandos existentes
- [ ] Crear interfaz de usuario para selección de tipos
- [ ] Testing con modelos reales

### Fase 5: Optimización (PRIORIDAD BAJA)
- [ ] Cache de geometría
- [ ] Procesamiento por lotes
- [ ] Mejoras de performance

---

## Notas Técnicas Adicionales

### Limitación: InstanceVoidCutUtils

**Requisitos:**
- El elemento a cortar debe ser de categoría que soporte cortes (Walls, Floors, Roofs, Ceilings)
- El elemento de corte debe ser categoría `OST_Mass` o familia con sólido void
- Ambos elementos deben intersectarse geométricamente

**Alternativa si falla:**
- Mantener como DirectShape sin conversión a Wall nativo
- Usar teselación para geometría exacta

### Cálculo de Área Real

Para validar que el área reportada sea correcta:

```csharp
double CalcularAreaRealEncofrado(Element encofrado)
{
    Solid solido = ObtenerSolidoPrincipal(encofrado);
    if (solido == null) return 0;

    double areaTotal = 0;

    // Sumar área de caras externas (las que encofran)
    foreach (Face face in solido.Faces)
    {
        if (face is PlanarFace pf)
        {
            // Determinar si es cara externa (normal apunta hacia afuera)
            // Sumar solo las caras mayores (frente y dorso del encofrado delgado)
            if (face.Area > 0.01) // Filtrar caras pequeñas (bordes)
            {
                areaTotal += face.Area;
            }
        }
    }

    // Si el encofrado es delgado, dividir por 2 (frente + dorso)
    return areaTotal / 2.0;
}
```

---

## Validación y Testing

### Casos de Prueba

1. **Columna rectangular simple**
   - 4 caras verticales → 4 muros
   - Verificar dirección de extrusión hacia afuera

2. **Columna circular**
   - Crear muro curvo
   - Verificar corte por masas conceptuales

3. **Viga con losa adyacente**
   - 2 caras laterales → muros
   - 1 cara inferior → suelo
   - Verificar recorte donde intersecta la losa

4. **Escalera**
   - Caras verticales → muros
   - Peldaños inclinados → suelos
   - Descansos horizontales → suelos

5. **Muro con aberturas**
   - Caras laterales → muros
   - Verificar que aberturas se reflejen en encofrado

---

## Mejoras Futuras (Post-MVP)

1. **Puntales y soportes**: Generar elementos de apoyo para encofrados
2. **Paneles modulares**: Dividir encofrados en paneles de tamaño estándar
3. **Cálculo de materiales**: Madera, tornillos, separadores
4. **Secuencia constructiva**: Ordenar encofrados por fases de construcción
5. **Exportación a fabricación**: BOM, planos de taller
6. **Integración con Navisworks**: Simulación 4D del proceso de encofrado
