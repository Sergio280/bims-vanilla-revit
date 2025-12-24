# Investigación: API de Revit 2025 para Automatización de Modelado de Encofrados

## Objetivo del Sistema
Automatizar el modelado de encofrados mediante:
1. **Extracción de curvas** de cada cara de elementos estructurales
2. **Recorte de curvas** por elementos adyacentes
3. **Creación de muros/suelos nativos** con curvas recortadas
4. **Posicionamiento preciso** pegado a la cara que encofran
5. **Manejo de geometría compleja** con masas conceptuales

---

## 1. EXTRACCIÓN DE CURVAS DE CARAS

### 1.1 Métodos Recomendados

#### Opción A: `Face.GetEdgesAsCurveLoops()` (RECOMENDADO)
```csharp
// Obtener CurveLoops de una cara planar
PlanarFace planarFace = ...; // Cara del elemento
IList<CurveLoop> curveLoops = planarFace.GetEdgesAsCurveLoops();

// curveLoops[0] = Contorno exterior
// curveLoops[1..n] = Huecos/recortes internos
```

**Ventajas:**
- Devuelve curvas cerradas organizadas en loops
- Diferencia automáticamente entre contorno exterior e interior
- Compatible directo con `Wall.Create()` y `Floor.Create()`
- Maneja automáticamente caras recortadas

**Limitaciones:**
- Solo funciona con `PlanarFace`
- Para caras curvas (cilíndricas, cónicas), se necesita teselación

#### Opción B: `Edge.AsCurve()` y `Edge.AsCurveFollowingFace()`
```csharp
// Para caras NO planares (columnas circulares, etc.)
Face face = ...; // Puede ser CylindricalFace, ConicalFace, etc.
EdgeArrayArray edgeLoops = face.EdgeLoops;

foreach (EdgeArray edgeArray in edgeLoops)
{
    List<Curve> curves = new List<Curve>();
    foreach (Edge edge in edgeArray)
    {
        Curve curve = edge.AsCurve(); // Curva en espacio 3D
        // o
        Curve curveFace = edge.AsCurveFollowingFace(face); // Curva proyectada en la cara
        curves.Add(curve);
    }
}
```

**Ventajas:**
- Funciona con cualquier tipo de cara (planar, cilíndrica, cónica, etc.)
- `AsCurveFollowingFace()` respeta la parametrización de la cara
- Más control sobre las curvas individuales

**Cuándo usar:**
- Columnas circulares/elípticas
- Vigas con secciones curvas
- Geometría compleja no planar

### 1.2 Consideraciones Importantes

**Parametrización de Edges:**
- Todos los edges se parametrizan de 0 a 1
- Útil para subdivisiones y operaciones de recorte

**Orden de Curvas:**
- `ExporterIFCUtils.SortCurveLoops()` ordena loops exteriores e interiores
- Loop exterior siempre en sentido antihorario

---

## 2. RECORTE DE CURVAS POR ELEMENTOS ADYACENTES

### 2.1 Estrategia con Operaciones Booleanas

#### A. `BooleanOperationsUtils.ExecuteBooleanOperation()`

**Firma del método:**
```csharp
public static Solid ExecuteBooleanOperation(
    Solid first,
    Solid second,
    BooleanOperationsType operationType)
```

**Operaciones disponibles:**
- `BooleanOperationsType.Union` - Unión
- `BooleanOperationsType.Difference` - Diferencia (recorte)
- `BooleanOperationsType.Intersect` - Intersección

**Implementación para Recorte de Encofrado:**

```csharp
// 1. Crear sólido de encofrado inicial
var curveLoops = planarFace.GetEdgesAsCurveLoops();
Solid solidoEncofrado = GeometryCreationUtilities.CreateExtrusionGeometry(
    curveLoops,
    planarFace.FaceNormal,
    espesorEncofrado);

// 2. Para cada elemento adyacente, descontar su geometría
foreach (var elementoAdyacente in elementosAdyacentes)
{
    Solid solidoAdyacente = ObtenerSolidoPrincipal(elementoAdyacente);

    // 3. Verificar intersección real
    Solid interseccion = BooleanOperationsUtils.ExecuteBooleanOperation(
        solidoEncofrado,
        solidoAdyacente,
        BooleanOperationsType.Intersect);

    if (interseccion != null && interseccion.Volume > TOLERANCIA_VOLUMEN)
    {
        // 4. Aplicar recorte (diferencia)
        solidoEncofrado = BooleanOperationsUtils.ExecuteBooleanOperation(
            solidoEncofrado,
            solidoAdyacente,
            BooleanOperationsType.Difference);
    }
}

// 5. Crear DirectShape con sólido recortado
var ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_Walls));
ds.SetShape(new GeometryObject[] { solidoEncofrado });
```

**Ventajas:**
- Recorte automático preciso
- Maneja geometría compleja
- Resultado es un sólido válido

**Limitaciones:**
- Solo funciona con sólidos, NO con curvas directamente
- Puede fallar con geometría muy compleja (devuelve `null`)
- El primer argumento debe ser geometría NO de elemento si usas `ExecuteBooleanOperationModifyingOriginalSolid()`

#### B. Alternativa: Intersección de Caras `Face.Intersect(Face)`

```csharp
Face faceA = ...; // Cara del encofrado
Face faceB = ...; // Cara del elemento adyacente

FaceIntersectionFaceResult result = faceA.Intersect(faceB);

if (result == FaceIntersectionFaceResult.Intersecting)
{
    // Las caras se intersectan (NOTA: considera caras infinitas)
    // Necesitas verificar manualmente si la intersección está dentro del rango
}
```

**ADVERTENCIA IMPORTANTE:**
- `Face.Intersect(Face)` considera caras como **infinitas**
- Puede devolver `Intersecting` incluso si las caras delimitadas NO se tocan
- Solo funciona bien para configuraciones simples (cara planar vs planar/cilíndrica)
- **NO es confiable para uso general**

**Mejor Alternativa:**
```csharp
// Usar Solid.IntersectWithCurve() o verificar puntos de intersección manualmente
```

### 2.2 Estrategia con Proyección y Análisis de Sólidos

#### Método: `ReferenceIntersector` para Ray Projection

```csharp
// Proyectar rayo desde un punto en dirección específica
ReferenceIntersector refIntersector = new ReferenceIntersector(
    elementFilter,
    FindReferenceTarget.Face,
    view3D);

XYZ origin = ...; // Punto de origen
XYZ direction = planarFace.FaceNormal; // Dirección del rayo

ReferenceWithContext refContext = refIntersector.FindNearest(origin, direction);

if (refContext != null)
{
    Reference reference = refContext.GetReference();
    Element elementoAdyacente = doc.GetElement(reference.ElementId);
    XYZ intersectionPoint = reference.GlobalPoint;

    // Usar puntos de intersección para recortar curvas
}
```

**Ventajas:**
- Muy preciso para detectar elementos adyacentes
- Permite encontrar la cara exacta del contacto
- Útil para posicionamiento posterior del encofrado

**Cuándo usar:**
- Necesitas saber DÓNDE exactamente tocan los elementos
- Para verificar contacto en puntos específicos
- Para ajustar offset del encofrado

### 2.3 Filtrado de Elementos Adyacentes

**Estrategia Recomendada:**

```csharp
public static List<Element> ObtenerElementosAdyacentes(
    Document doc,
    Element elemento,
    double tolerancia = 0.1) // 10cm
{
    var bbox = elemento.get_BoundingBox(null);

    // Expandir BoundingBox para capturar elementos cercanos
    var outline = new Outline(
        new XYZ(bbox.Min.X - tolerancia, bbox.Min.Y - tolerancia, bbox.Min.Z - tolerancia),
        new XYZ(bbox.Max.X + tolerancia, bbox.Max.Y + tolerancia, bbox.Max.Z + tolerancia));

    var bbFilter = new BoundingBoxIntersectsFilter(outline);

    // Filtrar solo categorías estructurales relevantes
    var categorias = new List<BuiltInCategory>
    {
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_StructuralFoundation,
        BuiltInCategory.OST_Stairs
    };

    var elementosAdyacentes = new List<Element>();

    foreach (var categoria in categorias)
    {
        var elementos = new FilteredElementCollector(doc)
            .OfCategory(categoria)
            .WherePasses(bbFilter)
            .Where(e => e.Id != elemento.Id)
            .ToList();

        elementosAdyacentes.AddRange(elementos);
    }

    return elementosAdyacentes;
}
```

**Optimizaciones:**
- Usar `BoundingBoxIntersectsFilter` es MUCHO más rápido que iterar todos los elementos
- Limitar categorías reduce procesamiento
- Tolerancia ajustable según precisión requerida

---

## 3. CREACIÓN DE MUROS Y SUELOS NATIVOS CON CURVAS RECORTADAS

### 3.1 Creación de Muros

#### Método A: `Wall.Create()` con Línea Base (Simple)

```csharp
// Para muros simples sin recortes complejos
public static Wall CrearMuroSimple(
    Document doc,
    Curve curvaBase,
    WallType wallType,
    Level nivel,
    double altura)
{
    Wall muro = Wall.Create(
        doc,
        curvaBase,           // Line, Arc, o cualquier Curve
        wallType.Id,
        nivel.Id,
        altura,
        0,                   // offset desde el nivel
        false,               // flip
        false);              // structural

    return muro;
}
```

**Cuándo usar:**
- Caras planares verticales sin recortes
- Encofrado de muros rectos o con arcos simples
- Cuando NO hay geometría compleja

#### Método B: `Wall.Create()` con Perfil Vertical (Complejo)

**NOTA IMPORTANTE:** A partir de Revit 2024/2025, crear muros con perfiles personalizados tiene limitaciones. La API prefiere:
1. Crear muro simple
2. Modificar el perfil con `Wall.SetProfile()`

**Implementación Recomendada:**

```csharp
// Estrategia: Crear muro base + modificar perfil
public static Wall CrearMuroConPerfil(
    Document doc,
    CurveLoop perfilVertical,
    WallType wallType,
    Level nivel)
{
    // 1. Extraer curva base (horizontal más baja)
    Curve curvaBase = ExtraerCurvaBase(perfilVertical);

    // 2. Calcular altura desde el perfil
    double altura = CalcularAlturaDesdePerfil(perfilVertical);

    // 3. Crear muro simple
    Wall muro = Wall.Create(
        doc,
        curvaBase,
        wallType.Id,
        nivel.Id,
        altura,
        0, false, false);

    // 4. INTENTAR modificar perfil (puede fallar según complejidad)
    try
    {
        // NOTA: SetProfile() tiene limitaciones en Revit 2025
        // Para geometría MUY compleja, mejor usar DirectShape
        // o múltiples muros segmentados
    }
    catch
    {
        // Fallback: usar DirectShape
    }

    return muro;
}
```

**Limitaciones de Perfiles en Muros:**
- No todos los perfiles complejos son soportados
- Recortes internos (huecos) pueden fallar
- Para geometría compleja, DirectShape es más confiable

#### Método C: DirectShape → Conversión a Wall (RECOMENDADO para geometría compleja)

```csharp
// Flujo de dos pasos
public static Wall CrearMuroDesdeGeometriaCompleja(
    Document doc,
    PlanarFace caraConRecortes,
    WallType wallType,
    Level nivel)
{
    // PASO 1: Crear DirectShape con geometría exacta (incluyendo recortes)
    var curveLoops = caraConRecortes.GetEdgesAsCurveLoops();
    Solid solidoEncofrado = GeometryCreationUtilities.CreateExtrusionGeometry(
        curveLoops,
        caraConRecortes.FaceNormal,
        0.02); // 2cm espesor

    DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_Walls));
    ds.SetShape(new GeometryObject[] { solidoEncofrado });

    // PASO 2: Extraer información del DirectShape para crear muro
    DirectShapeData datos = ExtraerDatosDirectShape(ds);

    // PASO 3: Crear muro usando la curva más representativa
    Wall muro = Wall.Create(
        doc,
        datos.CurvaBase,
        wallType.Id,
        nivel.Id,
        datos.Altura,
        0, false, false);

    // PASO 4: Eliminar DirectShape temporal
    doc.Delete(ds.Id);

    return muro;
}
```

**Ventajas:**
- Geometría exacta en DirectShape (incluyendo recortes)
- Conversión a Wall nativo para cuantificación
- Mejor de ambos mundos

**Desventajas:**
- El Wall nativo NO tendrá los recortes visuales
- Solo para representación y mediciones

### 3.2 Creación de Suelos (Losas de Encofrado)

#### Método: `Floor.Create()` con CurveLoops

```csharp
public static Floor CrearSueloConRecortes(
    Document doc,
    PlanarFace caraHorizontal,
    FloorType floorType,
    Level nivel)
{
    // Obtener CurveLoops (exterior + huecos)
    IList<CurveLoop> curveLoops = caraHorizontal.GetEdgesAsCurveLoops();

    // Crear suelo (soporta múltiples loops para huecos)
    Floor suelo = Floor.Create(
        doc,
        curveLoops,         // Lista de CurveLoops
        floorType.Id,
        nivel.Id);

    return suelo;
}
```

**Ventajas sobre Muros:**
- `Floor.Create()` soporta MEJOR múltiples CurveLoops
- Recortes internos (huecos) funcionan correctamente
- Más robusto para geometría compleja

**Recomendación:**
- Para encofrado de losas inferiores: usar `Floor`
- Para encofrado de losas como caras verticales (bordes): usar `DirectShape`

---

## 4. POSICIONAMIENTO PEGADO A LA CARA

### 4.1 Estrategia de Offset desde la Cara

```csharp
public static XYZ CalcularPosicionPegadaACara(
    PlanarFace cara,
    double offsetEncofrado = 0.0) // Normalmente 0 para contacto directo
{
    // Opción 1: Usar el centroide de la cara
    XYZ centroide = CalcularCentroideCara(cara);

    // Opción 2: Offset en dirección de la normal
    XYZ posicionPegada = centroide + (cara.FaceNormal * offsetEncofrado);

    return posicionPegada;
}

private static XYZ CalcularCentroideCara(PlanarFace cara)
{
    var curveLoops = cara.GetEdgesAsCurveLoops();
    List<XYZ> puntos = new List<XYZ>();

    // Usar solo el loop exterior
    foreach (Curve curve in curveLoops[0])
    {
        puntos.Add(curve.GetEndPoint(0));
    }

    // Promedio de puntos
    XYZ suma = puntos.Aggregate(XYZ.Zero, (acc, p) => acc + p);
    return suma / puntos.Count;
}
```

### 4.2 Alineación del Encofrado

**Para Muros Verticales:**
```csharp
// El muro se crea automáticamente perpendicular a la curva base
// La orientación se controla con el parámetro "flip" en Wall.Create()

Wall muro = Wall.Create(doc, curvaBase, wallType.Id, nivel.Id, altura,
    0,      // offset
    false,  // flip = false: normal hacia un lado
    false); // structural

// Si necesitas invertir la orientación:
Wall muroInvertido = Wall.Create(doc, curvaBase, wallType.Id, nivel.Id, altura,
    0,
    true,   // flip = true: normal hacia el otro lado
    false);
```

**Para Suelos Horizontales:**
```csharp
// Los suelos se crean en el nivel especificado
// Para ajustar elevación:
Floor suelo = Floor.Create(doc, curveLoops, floorType.Id, nivel.Id);

// Ajustar offset vertical
Parameter heightOffset = suelo.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
if (heightOffset != null && !heightOffset.IsReadOnly)
{
    heightOffset.Set(offsetVertical); // En pies (unidades internas de Revit)
}
```

### 4.3 Verificación de Contacto

```csharp
public static bool VerificarContactoEntreElementos(
    Element encofrado,
    Element elementoEstructural,
    double tolerancia = 0.005) // 5mm
{
    Solid solidoEncofrado = ObtenerSolidoPrincipal(encofrado);
    Solid solidoEstructural = ObtenerSolidoPrincipal(elementoEstructural);

    // Verificar intersección
    Solid interseccion = BooleanOperationsUtils.ExecuteBooleanOperation(
        solidoEncofrado,
        solidoEstructural,
        BooleanOperationsType.Intersect);

    // Si hay volumen de intersección, están en contacto
    bool hayContacto = interseccion != null && interseccion.Volume > tolerancia;

    return hayContacto;
}
```

---

## 5. GEOMETRÍA COMPLEJA CON MASAS CONCEPTUALES

### 5.1 Limitaciones del API para Masas

**IMPORTANTE:**
- **La API de Revit NO soporta la creación programática de masas in-place** (`FreeFormElement`)
- **Alternativas disponibles:**
  1. `DirectShape` (RECOMENDADO)
  2. Cargar familias de masas predefinidas
  3. Usar `MassInstanceUtils` con masas existentes

### 5.2 Estrategia Recomendada: DirectShape para Geometría Compleja

#### A. Para Columnas Circulares y Geometría Curva

```csharp
public static DirectShape CrearEncofradoColumnaCircular(
    Document doc,
    CylindricalFace caraCircular,
    double espesorEncofrado = 0.02)
{
    // OPCIÓN 1: Teselación de la cara curva
    Mesh mesh = caraCircular.Triangulate();

    List<XYZ> vertices = new List<XYZ>();
    foreach (XYZ vertex in mesh.Vertices)
    {
        // Expandir vértices hacia afuera según el espesor
        XYZ normal = ObtenerNormalEnPunto(caraCircular, vertex);
        XYZ verticeExpandido = vertex + (normal * espesorEncofrado);
        vertices.Add(verticeExpandido);
    }

    // Crear geometría desde triángulos
    TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
    builder.OpenConnectedFaceSet(false);

    // Agregar triángulos desde el mesh
    for (int i = 0; i < mesh.NumTriangles; i++)
    {
        MeshTriangle triangle = mesh.get_Triangle(i);

        XYZ v0 = mesh.Vertices[(int)triangle.get_Index(0)];
        XYZ v1 = mesh.Vertices[(int)triangle.get_Index(1)];
        XYZ v2 = mesh.Vertices[(int)triangle.get_Index(2)];

        TessellatedFace face = new TessellatedFace(
            new List<XYZ> { v0, v1, v2 },
            ElementId.InvalidElementId);

        builder.AddFace(face);
    }

    builder.CloseConnectedFaceSet();

    TessellatedShapeBuilderResult result = builder.Build();

    // Crear DirectShape
    DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
    ds.SetShape(result.GetGeometricalObjects());
    ds.Name = "Encofrado Circular";

    return ds;
}
```

**Ventajas:**
- Maneja CUALQUIER geometría curva
- Resultado visualmente preciso
- Compatible con DirectShape

**Desventajas:**
- NO es un elemento nativo de Revit (Wall/Floor)
- Más pesado computacionalmente

#### B. Alternativa: Extrusión por Segmentos

```csharp
// Para columnas circulares: dividir en segmentos planares
public static List<DirectShape> CrearEncofradoCircularSegmentado(
    Document doc,
    CylindricalFace caraCircular,
    int numeroSegmentos = 12) // 12 segmentos = 30° cada uno
{
    List<DirectShape> segmentos = new List<DirectShape>();

    // Obtener eje y radio del cilindro
    Cylinder cylinder = caraCircular.get_Geometry() as Cylinder;
    XYZ eje = cylinder.Axis;
    double radio = cylinder.Radius;

    // Dividir en segmentos angulares
    double anguloSegmento = (2 * Math.PI) / numeroSegmentos;

    for (int i = 0; i < numeroSegmentos; i++)
    {
        double anguloInicio = i * anguloSegmento;
        double anguloFin = (i + 1) * anguloSegmento;

        // Crear segmento planar para este ángulo
        DirectShape segmento = CrearSegmentoEncofrado(
            doc,
            cylinder,
            anguloInicio,
            anguloFin);

        if (segmento != null)
        {
            segmentos.Add(segmento);
        }
    }

    return segmentos;
}
```

**Ventajas:**
- Más simple que teselación completa
- Cada segmento puede convertirse a Wall si es necesario
- Mejor control sobre la discretización

**Cuándo usar:**
- Encofrado de columnas circulares con paneles planos
- Aproximación suficiente para la construcción
- Necesitas cuantificación por panel

### 5.3 Uso de MassInstanceUtils (Para Masas Existentes)

```csharp
// SI ya tienes una masa creada manualmente o cargada desde familia
public static void CrearWallDesdeMasa(Document doc, FamilyInstance masaExistente)
{
    // Obtener las caras de la masa
    Options opt = new Options();
    opt.ComputeReferences = true; // IMPORTANTE para crear wall by face

    GeometryElement geom = masaExistente.get_Geometry(opt);

    foreach (GeometryObject gObj in geom)
    {
        if (gObj is Solid solid)
        {
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace pf)
                {
                    // Crear "Wall by Face"
                    // NOTA: Esto requiere que la masa esté en modo "Mass"
                    try
                    {
                        Reference faceRef = pf.Reference;

                        // Usar MassInstanceUtils o CurtainSystemUtils
                        // para crear wall desde la cara de masa

                        // LIMITACIÓN: Funciona SOLO con masas de familia
                        // NO con DirectShape
                    }
                    catch
                    {
                        // Fallback a DirectShape
                    }
                }
            }
        }
    }
}
```

**LIMITACIÓN CRÍTICA:**
- Solo funciona con masas de **familia** (`FamilyInstance` con categoría `OST_Mass`)
- **NO funciona con DirectShape**
- Requiere crear la masa manualmente o cargar familia predefinida

**Recomendación:**
- Para automatización completa, **usar DirectShape**
- Para flujo híbrido (usuario crea masa, script crea encofrado), usar `MassInstanceUtils`

---

## 6. FLUJO DE TRABAJO RECOMENDADO

### Pseudocódigo del Sistema Completo

```csharp
public static void CrearEncofradoAutomatico(
    Document doc,
    Element elementoEstructural,
    WallType wallType,
    FloorType floorType)
{
    // PASO 1: Obtener geometría del elemento
    Solid solido = ObtenerSolidoPrincipal(elementoEstructural);

    // PASO 2: Obtener elementos adyacentes
    List<Element> elementosAdyacentes = ObtenerElementosAdyacentes(doc, elementoEstructural);

    // PASO 3: Procesar cada cara del elemento
    foreach (Face face in solido.Faces)
    {
        // PASO 3.1: Determinar si la cara necesita encofrado
        if (!DebeEncofrar(face)) continue;

        // PASO 3.2: Clasificar tipo de cara
        bool esCaraPlana = face is PlanarFace;
        bool esCaraCurva = face is CylindricalFace || face is ConicalFace;

        if (esCaraPlana)
        {
            PlanarFace pf = face as PlanarFace;

            // PASO 3.3: Crear encofrado con recortes automáticos
            DirectShape dsRecortado = CrearEncofradoConRecortes(
                doc, pf, elementosAdyacentes);

            // PASO 3.4: Determinar si es muro o suelo
            bool esVertical = Math.Abs(pf.FaceNormal.Z) < 0.3;

            if (esVertical)
            {
                // PASO 3.5: Convertir a Wall nativo
                Wall muro = ConvertirDirectShapeAMuro(
                    doc, dsRecortado, wallType);
            }
            else
            {
                // PASO 3.6: Convertir a Floor nativo
                Floor suelo = ConvertirDirectShapeASuelo(
                    doc, dsRecortado, floorType);
            }

            // PASO 3.7: Eliminar DirectShape temporal
            doc.Delete(dsRecortado.Id);
        }
        else if (esCaraCurva)
        {
            // PASO 3.8: Crear encofrado con teselación
            DirectShape dsTeselado = CrearEncofradoCurvoTeselado(
                doc, face);

            // MANTENER como DirectShape (no convertir a Wall por complejidad)
        }
    }
}

// Determinar qué caras encofrar
private static bool DebeEncofrar(Face face)
{
    // Ejemplo para columnas: encofrar solo caras verticales
    if (face is PlanarFace pf)
    {
        bool esHorizontal = Math.Abs(pf.FaceNormal.Z) > 0.7;
        return !esHorizontal; // No encofrar tapas superior/inferior
    }

    // Caras curvas: siempre encofrar (son caras laterales)
    return true;
}
```

---

## 7. MEJORES PRÁCTICAS Y RECOMENDACIONES

### 7.1 Performance

1. **Usar `BoundingBoxIntersectsFilter`** para filtrado rápido
   - 10-100x más rápido que iterar todos los elementos

2. **Cache de geometría**
   ```csharp
   Dictionary<ElementId, Solid> cacheSolidos = new Dictionary<ElementId, Solid>();

   Solid ObtenerSolidoCacheado(Element elemento)
   {
       if (!cacheSolidos.ContainsKey(elemento.Id))
       {
           cacheSolidos[elemento.Id] = ObtenerSolidoPrincipal(elemento);
       }
       return cacheSolidos[elemento.Id];
   }
   ```

3. **Operaciones booleanas costosas**
   - Verificar `Volume > TOLERANCIA` antes de operación booleana
   - Usar tolerancias razonables (0.0001 m³ ~ 1 litro)

### 7.2 Robustez

1. **Siempre usar try-catch en operaciones geométricas**
   ```csharp
   try
   {
       Solid resultado = BooleanOperationsUtils.ExecuteBooleanOperation(...);
   }
   catch
   {
       // Fallback: usar geometría sin recortes
   }
   ```

2. **Verificar resultados nulos**
   - Operaciones booleanas pueden devolver `null`
   - Validar `Volume > 0` después de operaciones

3. **Tolerancias configurables**
   ```csharp
   const double TOLERANCIA_CONTACTO = 0.05;      // 5cm
   const double TOLERANCIA_VOLUMEN = 0.0001;     // 0.1 litros
   const double TOLERANCIA_INTERSECCION = 0.1;   // 10cm
   ```

### 7.3 Conversión a Elementos Nativos

**Estrategia de Dos Fases:**

1. **Fase 1: DirectShape para geometría exacta**
   - Crear encofrado con todos los recortes
   - Operaciones booleanas complejas
   - Resultado: geometría precisa

2. **Fase 2: Conversión a Wall/Floor**
   - Extraer curva base del DirectShape
   - Crear Wall/Floor simplificado
   - Copiar parámetros importantes
   - Eliminar DirectShape temporal

**Ventajas:**
- DirectShape: visualización exacta
- Wall/Floor: cuantificación nativa, integración BIM

**Desventajas:**
- Wall/Floor pierde recortes visuales
- Puede requerir mantener ambos (con workset separado)

### 7.4 Categorías Apropiadas

```csharp
// Para DirectShapes de encofrado
var categoriaEncofrado = new ElementId(BuiltInCategory.OST_GenericModel);
// o mejor:
var categoriaEncofrado = new ElementId(BuiltInCategory.OST_TemporaryStructure);

DirectShape ds = DirectShape.CreateElement(doc, categoriaEncofrado);
```

**Opciones de categoría:**
- `OST_GenericModel` - Modelo genérico (más común)
- `OST_TemporaryStructure` - Construcciones temporales (semánticamente correcto)
- `OST_Walls` - Si quieres que aparezcan como muros en schedules

---

## 8. LIMITACIONES CONOCIDAS DE LA API

### 8.1 Operaciones Booleanas

- **Falla con geometría degenerada** (caras de área cero, edges de longitud cero)
- **Puede devolver `null` inesperadamente** incluso con sólidos válidos
- **No funciona con curvas directamente** (solo sólidos)

**Solución:** Siempre tener fallback a geometría sin recortes

### 8.2 Face.Intersect(Face)

- **Considera caras como infinitas**
- **No es confiable para uso general**
- **Solo funciona bien para configuraciones simples**

**Solución:** Usar operaciones booleanas de sólidos en su lugar

### 8.3 Muros con Perfiles Complejos

- **API limitada para `Wall.SetProfile()`**
- **No todos los perfiles son soportados**
- **Recortes internos pueden fallar**

**Solución:** Usar DirectShape para geometría compleja + Wall simplificado para cuantificación

### 8.4 Masas Conceptuales

- **NO se pueden crear programáticamente** (`FreeFormElement`)
- **Solo se pueden usar masas existentes** (de familias o creadas manualmente)

**Solución:** Usar DirectShape como alternativa completa

---

## 9. CÓDIGO DE EJEMPLO COMPLETO

Ver archivos existentes en tu proyecto:
- `EncofradoBaseHelper.cs` - Métodos compartidos
- `DirectShapeGeometryExtractor.cs` - Extracción y conversión
- `EncofradoColumnaCommand.cs` - Ejemplo de comando

**Mejoras sugeridas basadas en esta investigación:**

1. **Optimizar detección de adyacencias**
   - Implementar cache de sólidos
   - Usar `BoundingBoxIntersectsFilter` (ya lo tienes ✓)

2. **Mejorar robustez de operaciones booleanas**
   - Más try-catch específicos
   - Fallbacks claros

3. **Implementar teselación para geometría curva**
   - `TessellatedShapeBuilder` para columnas circulares
   - Segmentación angular como alternativa

4. **Separar visualización de cuantificación**
   - DirectShape para geometría exacta
   - Wall/Floor para schedules y mediciones

---

## 10. FUENTES Y REFERENCIAS

### Documentación Oficial Revit API 2025

- [Solid and face creation](https://help.autodesk.com/view/RVT/2025/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Geometry_GeometryObject_Class_Solids_Faces_and_Edges_Solid_and_face_creation_html)
- [Curves](https://help.autodesk.com/view/RVT/2025/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Geometry_GeometryObject_Class_Curves_html)
- [Finding geometry by ray projection](https://help.autodesk.com/view/RVT/2025/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Geometry_Finding_geometry_by_ray_projection_html)
- [Solid analysis](https://help.autodesk.com/view/RVT/2025/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Geometry_GeometryObject_Class_Solids_Faces_and_Edges_Solid_analysis_html)
- [DirectShape](https://help.autodesk.com/view/RVT/2025/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_DirectShape_html)

### Revit API Docs

- [BooleanOperationsUtils Class](https://www.revitapidocs.com/2025.3/a7be98f3-9e8a-ee51-f46c-2479cb72c598.htm)
- [Wall Class](https://www.revitapidocs.com/2025/b5891733-c602-12df-beab-da414b58d608.htm)
- [DirectShape Methods](https://www.revitapidocs.com/2025/fd331ea8-38a7-67c2-a7c8-40d10418c3f3.htm)

### The Building Coder (Jeremy Tammik)

- [Creating Face Wall and Mass Floor](https://thebuildingcoder.typepad.com/blog/2017/12/creating-face-wall-and-mass-floor.html)
- [Boolean Operations and InstanceVoidCutUtils](https://thebuildingcoder.typepad.com/blog/2011/06/boolean-operations-and-instancevoidcututils.html)
- [Face Intersect Face is Unbounded](https://thebuildingcoder.typepad.com/blog/2019/09/face-intersect-face-is-unbounded.html)
- [DirectShape Topics](https://thebuildingcoder.typepad.com/blog/2018/01/directshape-topics-and-happy-new-year.html)
- [Getting the Wall Elevation Profile](https://thebuildingcoder.typepad.com/blog/2015/01/getting-the-wall-elevation-profile.html)

### Autodesk Forums

- [Trim/Extend Element Method](https://forums.autodesk.com/t5/revit-api-forum/trim-extend-element-method/td-p/9671024)
- [Intersection fails](https://forums.autodesk.com/t5/revit-api-forum/intersection-fails/td-p/10294420)
- [Convert element faces to individual DirectShape faces](https://forums.autodesk.com/t5/revit-api-forum/convert-element-faces-to-individual-directshape-faces/td-p/5681573)

---

## CONCLUSIONES Y PRÓXIMOS PASOS

### Estrategia Recomendada Final

1. **Para caras planares simples:**
   - Usar `Wall.Create()` o `Floor.Create()` directamente

2. **Para caras planares con recortes:**
   - DirectShape con operaciones booleanas → Conversión a Wall/Floor

3. **Para geometría curva (columnas circulares):**
   - DirectShape con teselación (TessellatedShapeBuilder)
   - Mantener como DirectShape sin conversión

4. **Para detección de adyacencias:**
   - `BoundingBoxIntersectsFilter` + operaciones booleanas de sólidos

5. **Para posicionamiento:**
   - Calcular offset desde centroide de cara
   - Usar `flip` parameter en Wall.Create() para orientación

### Preguntas Pendientes para el Usuario

Antes de implementar mejoras, necesito saber:

1. **Espesor del encofrado**: ¿Constante o variable según elemento?
2. **Criterio de geometría compleja**: ¿Cuándo usar DirectShape vs Wall nativo?
3. **Salida esperada**: ¿DirectShape, Wall/Floor, o ambos?
4. **Recortes por adyacencias**: ¿Siempre o solo cuando intersectan más de X%?
5. **Cuantificación**: ¿Necesitas área neta (con recortes) o bruta?
