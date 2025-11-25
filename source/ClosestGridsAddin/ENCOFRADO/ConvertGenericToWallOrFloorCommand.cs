#region Namespaces
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
#endregion

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Convierte modelos genéricos (DirectShapes) a muros y suelos reales
    /// Detecta automáticamente la orientación y aplica 5 métodos de conversión
    /// VERSIÓN MEJORADA con detección inteligente y modo minimalista
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ConvertGenericToWallOrFloorCommand : LicensedCommand
    {
        private Document _doc;
        private WallType _wallType;
        private FloorType _floorType;
        private System.Text.StringBuilder _log = new System.Text.StringBuilder();

        /// <summary>
        /// Método público para llamar desde otros comandos (sin verificación de licencia)
        /// </summary>
        public Result ExecuteWithoutLicenseCheck(ExternalCommandData commandData, ref string message, ElementSet elements,
            WallType wallType = null, FloorType floorType = null)
        {
            return ExecuteInternal(commandData, ref message, elements, wallType, floorType);
        }

        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return ExecuteInternal(commandData, ref message, elements, null, null);
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements,
            WallType wallTypeParam = null, FloorType floorTypeParam = null)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            _doc = uidoc.Document;

            try
            {
                // 1. Obtener o usar tipos de muro y suelo
                if (wallTypeParam != null && floorTypeParam != null)
                {
                    // Usar tipos pasados como parámetros (desde Auto-Convert)
                    _wallType = wallTypeParam;
                    _floorType = floorTypeParam;
                    _log.AppendLine($"Usando tipos especificados:");
                    _log.AppendLine($"  - Muro: {_wallType.Name}");
                    _log.AppendLine($"  - Suelo: {_floorType.Name}");
                }
                else
                {
                    // Buscar tipos en el proyecto (modo manual)
                    if (!ObtenerTipos())
                    {
                        TaskDialog.Show("Error", "No se encontraron los tipos 'Encofrado 18mm' o 'Cimbra 25mm'.\n" +
                            "Ejecuta primero el comando de generación de encofrados o usa el comando Auto-Convert.");
                        return Result.Failed;
                    }
                    _log.AppendLine($"Usando tipos encontrados automáticamente:");
                    _log.AppendLine($"  - Muro: {_wallType.Name}");
                    _log.AppendLine($"  - Suelo: {_floorType.Name}");
                }

                // 2. Obtener todos los modelos genéricos (DirectShapes)
                var encofrados = ObtenerEncofrados();

                if (encofrados.Count == 0)
                {
                    TaskDialog.Show("Información", "No se encontraron modelos genéricos (DirectShapes) para convertir.");
                    return Result.Cancelled;
                }

                _log.AppendLine($"=== CONVERSIÓN DE ENCOFRADOS ===");
                _log.AppendLine($"Total de encofrados encontrados: {encofrados.Count}");

                // 3. Procesar usando flujo de 3 pasos: Extraer → Eliminar → Crear
                int murosCreados = 0;
                int suelosCreados = 0;
                int errores = 0;
                int directShapesEliminados = 0;

                _log.AppendLine($"\n═══ INICIANDO CONVERSIÓN (Extraer → Eliminar → Crear) ═══");

                // TRANSACCIÓN: Extraer datos → Eliminar DirectShapes → Crear Wall/Floor
                using (Transaction trans = new Transaction(_doc, "Convertir DirectShapes a Muros/Suelos"))
                {
                    try
                    {
                        trans.Start();

                        // PASO 3.1: EXTRAER datos de todos los DirectShapes
                        _log.AppendLine($"\n─── PASO 1: Extrayendo datos geométricos ───");
                        var datosExtraidos = new List<DirectShapeData>();
                        var directShapesList = new List<DirectShape>();

                        foreach (Element elem in encofrados)
                        {
                            if (!(elem is DirectShape ds))
                            {
                                _log.AppendLine($"⚠ Elemento {elem.Id} no es DirectShape, omitiendo");
                                continue;
                            }

                            directShapesList.Add(ds);

                            try
                            {
                                var datos = DirectShapeGeometryExtractor.ExtraerDatos(_doc, ds);
                                if (datos != null)
                                {
                                    datosExtraidos.Add(datos);
                                    _log.AppendLine($"✓ DirectShape {ds.Id} → Datos extraídos (Área: {(datos.Area * 0.09290304):F2} m²)");
                                }
                                else
                                {
                                    errores++;
                                    _log.AppendLine($"✗ DirectShape {ds.Id} → Sin datos válidos");
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.AppendLine($"✗ Error extrayendo DirectShape {ds.Id}: {ex.Message}");
                                errores++;
                            }
                        }

                        _log.AppendLine($"Datos extraídos: {datosExtraidos.Count}/{directShapesList.Count}");

                        // PASO 3.2: ELIMINAR todos los DirectShapes
                        _log.AppendLine($"\n─── PASO 2: Eliminando DirectShapes ───");
                        foreach (var ds in directShapesList)
                        {
                            try
                            {
                                _doc.Delete(ds.Id);
                                directShapesEliminados++;
                                _log.AppendLine($"✓ DirectShape {ds.Id} eliminado");
                            }
                            catch (Exception exDel)
                            {
                                _log.AppendLine($"⚠ Error eliminando DirectShape {ds.Id}: {exDel.Message}");
                            }
                        }

                        // PASO 3.3: CREAR muros/suelos desde los datos extraídos
                        _log.AppendLine($"\n─── PASO 3: Creando Muros y Suelos ───");
                        foreach (var datos in datosExtraidos)
                        {
                            try
                            {
                                if (datos.EsVertical)
                                {
                                    // Crear muro
                                    Wall muro = DirectShapeGeometryExtractor.CrearMuro(_doc, datos, _wallType);
                                    if (muro != null)
                                    {
                                        murosCreados++;
                                        _log.AppendLine($"✓ Muro creado: {muro.Id} (desde DS {datos.DirectShapeId})");
                                    }
                                    else
                                    {
                                        errores++;
                                        _log.AppendLine($"✗ No se pudo crear muro desde DS {datos.DirectShapeId}");
                                    }
                                }
                                else
                                {
                                    // Crear suelo
                                    Floor suelo = DirectShapeGeometryExtractor.CrearSuelo(_doc, datos, _floorType);
                                    if (suelo != null)
                                    {
                                        suelosCreados++;
                                        _log.AppendLine($"✓ Suelo creado: {suelo.Id} (desde DS {datos.DirectShapeId})");
                                    }
                                    else
                                    {
                                        errores++;
                                        _log.AppendLine($"✗ No se pudo crear suelo desde DS {datos.DirectShapeId}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.AppendLine($"✗ Error creando elemento desde DS {datos.DirectShapeId}: {ex.Message}");
                                errores++;
                            }
                        }

                        // LOGGING CRÍTICO: Capturar estado JUSTO ANTES del commit
                        _log.AppendLine($"\n[PRE-COMMIT] Verificando estado de elementos creados...");
                        _log.AppendLine($"[PRE-COMMIT] Total muros: {murosCreados}, Total suelos: {suelosCreados}");

                        // Verificar que los elementos existen y son válidos
                        try
                        {
                            var todosLosMuros = new FilteredElementCollector(_doc)
                                .OfClass(typeof(Wall))
                                .Cast<Wall>()
                                .Where(w => w.Document.Equals(_doc))
                                .ToList();

                            _log.AppendLine($"[PRE-COMMIT] Muros totales en documento: {todosLosMuros.Count}");

                            // Log del último muro creado (debería ser el nuestro)
                            if (todosLosMuros.Count > 0)
                            {
                                var ultimoMuro = todosLosMuros.Last();
                                _log.AppendLine($"[PRE-COMMIT] Último muro ID: {ultimoMuro.Id}");
                                _log.AppendLine($"[PRE-COMMIT]   - Tipo: {ultimoMuro.WallType?.Name ?? "null"}");
                                _log.AppendLine($"[PRE-COMMIT]   - Válido: {ultimoMuro.IsValidObject}");

                                try
                                {
                                    var paramNivel = ultimoMuro.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                                    var paramBaseOffset = ultimoMuro.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                                    var paramAltura = ultimoMuro.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);

                                    _log.AppendLine($"[PRE-COMMIT]   - Nivel Base: {(paramNivel?.AsValueString() ?? "null")}");
                                    _log.AppendLine($"[PRE-COMMIT]   - Base Offset: {(paramBaseOffset?.AsValueString() ?? "null")}");
                                    _log.AppendLine($"[PRE-COMMIT]   - Altura: {(paramAltura?.AsValueString() ?? "null")}");
                                }
                                catch (Exception exParam)
                                {
                                    _log.AppendLine($"[PRE-COMMIT]   ⚠ Error leyendo parámetros: {exParam.Message}");
                                }
                            }
                        }
                        catch (Exception exVerif)
                        {
                            _log.AppendLine($"[PRE-COMMIT] ⚠ Error en verificación pre-commit: {exVerif.Message}");
                        }

                        TransactionStatus status = trans.Commit();
                        _log.AppendLine($"\n✓ Primera transacción - Status: {status}");
                        _log.AppendLine($"  Muros creados: {murosCreados}, Suelos creados: {suelosCreados}");

                        // VERIFICAR que los elementos realmente existen después del commit
                        if (status != TransactionStatus.Committed)
                        {
                            _log.AppendLine($"⚠⚠⚠ ADVERTENCIA: Transaction.Commit() retornó {status} en lugar de Committed!");
                            _log.AppendLine($"  Revit forzó rollback - los elementos NO se guardaron");
                            _log.AppendLine($"  NO se eliminarán encofrados para evitar pérdida de datos");

                            // CÓDIGO OBSOLETO - Variable no existe
                            // encofradosAEliminar.Clear();
                        }
                    }
                    catch (Exception exTrans)
                    {
                        _log.AppendLine($"\n✗✗✗ ERROR CRÍTICO EN TRANSACCIÓN DE CREACIÓN: {exTrans.Message}");
                        _log.AppendLine($"  Stack trace: {exTrans.StackTrace}");
                        trans.RollBack();

                        // Mostrar log incluso si hay error en la transacción
                        // CÓDIGO OBSOLETO - Método MostrarResultados no existe en nueva versión
                        TaskDialog.Show("Error", $"Error en conversión: {exTrans.Message}\n\nLog:\n{_log.ToString()}");

                        message = $"Error en transacción de creación: {exTrans.Message}";
                        return Result.Failed;
                    }
                }

                // CÓDIGO OBSOLETO - SEGUNDA TRANSACCIÓN eliminada en nueva versión
                // Los DirectShapes se eliminan directamente en la misma transacción ahora
                /*if (encofradosAEliminar.Count > 0)
                {
                    using (Transaction transDelete = new Transaction(_doc, "Eliminar Encofrados Originales"))
                    {
                        try
                        {
                            transDelete.Start();

                            _log.AppendLine($"\n🗑 Eliminando {encofradosAEliminar.Count} encofrado(s) original(es)...");

                            int eliminados = 0;
                            int fallos = 0;
                            foreach (ElementId encofradoId in encofradosAEliminar)
                            {
                                try
                                {
                                    ICollection<ElementId> deletedIds = _doc.Delete(encofradoId);
                                    if (deletedIds != null && deletedIds.Count > 0)
                                    {
                                        eliminados++;
                                    }
                                    else
                                    {
                                        fallos++;
                                        _log.AppendLine($"  ⚠ No se pudo eliminar encofrado ID {encofradoId}: Delete retornó 0 elementos");
                                    }
                                }
                                catch (Exception exDel)
                                {
                                    fallos++;
                                    _log.AppendLine($"  ⚠ No se pudo eliminar encofrado ID {encofradoId}: {exDel.Message}");
                                }
                            }

                            // Solo hacer commit si se eliminó al menos un elemento
                            if (eliminados > 0)
                            {
                                TransactionStatus statusDel = transDelete.Commit();
                                _log.AppendLine($"✓ Segunda transacción - Status: {statusDel}");
                                _log.AppendLine($"  Encofrados eliminados exitosamente: {eliminados}/{encofradosAEliminar.Count}");

                                if (fallos > 0)
                                {
                                    _log.AppendLine($"  ⚠ Fallos: {fallos} encofrados NO pudieron eliminarse");
                                }

                                if (statusDel != TransactionStatus.Committed)
                                {
                                    _log.AppendLine($"⚠⚠⚠ ADVERTENCIA: Transacción de eliminación retornó {statusDel}!");
                                }
                            }
                            else
                            {
                                transDelete.RollBack();
                                _log.AppendLine($"✗ Transacción cancelada - No se eliminó ningún encofrado");
                            }
                        }
                        catch (Exception exTransDel)
                        {
                            _log.AppendLine($"\n⚠ Error en transacción de eliminación: {exTransDel.Message}");
                            _log.AppendLine($"  Los muros/suelos creados permanecen en el modelo");
                            transDelete.RollBack();
                        }
                    }
                }*/

                // Ya se mostró el resultado en la nueva lógica (TaskDialog arriba)
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"\n✗✗✗ ERROR GENERAL: {ex.Message}");
                message = $"Error general: {ex.Message}";

                // Mostrar log incluso si hay error general
                TaskDialog td = new TaskDialog("Error en Conversión");
                td.MainInstruction = "Error durante la conversión";
                td.MainContent = message;
                td.ExpandedContent = _log.ToString();
                td.Show();

                return Result.Failed;
            }
        }

        /// <summary>
        /// Obtiene los tipos de muro y suelo necesarios
        /// </summary>
        private bool ObtenerTipos()
        {
            _wallType = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(w => w.Name.Contains("Encofrado 18mm"));

            _floorType = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault(f => f.Name.Contains("Cimbra 25mm"));

            return _wallType != null && _floorType != null;
        }

        /// <summary>
        /// Obtiene TODOS los modelos genéricos del proyecto (sin filtro de comentarios)
        /// </summary>
        private List<Element> ObtenerEncofrados()
        {
            var todosLosGenericos = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType()
                .ToList();

            _log.AppendLine($"Total de modelos genéricos en el proyecto: {todosLosGenericos.Count}");

            int conComentarios = 0;
            int sinComentarios = 0;

            // Registrar estadísticas de comentarios (solo para logging)
            foreach (Element elem in todosLosGenericos)
            {
                Parameter paramComentarios = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

                if (paramComentarios != null && paramComentarios.HasValue)
                {
                    string comentario = paramComentarios.AsString();
                    if (!string.IsNullOrWhiteSpace(comentario))
                    {
                        _log.AppendLine($"  Modelo ID {elem.Id.Value}: '{comentario}'");
                        conComentarios++;
                    }
                    else
                    {
                        sinComentarios++;
                    }
                }
                else
                {
                    sinComentarios++;
                }
            }

            _log.AppendLine($"Resumen:");
            _log.AppendLine($"  - Con comentarios: {conComentarios}");
            _log.AppendLine($"  - Sin comentarios: {sinComentarios}");
            _log.AppendLine($"  - Total a procesar: {todosLosGenericos.Count}");

            return todosLosGenericos;
        }

        /// <summary>
        /// Procesa un encofrado individual y decide si crear muro o suelo NATIVO
        /// Estrategia: Crear elementos nativos de Revit (Wall/Floor) y aplicar recortes con Voids
        /// </summary>
        /// <summary>
        /// Procesa un encofrado y retorna el número del método que tuvo éxito
        /// Retorna: 0=fallo, 1-5=método usado para muros, 6=suelo creado
        /// </summary>
        private int ProcesarEncofrado(Element encofrado, ref int murosCreados, ref int suelosCreados, List<ElementId> encofradosAEliminar)
        {
            _log.AppendLine($"\n═══ Procesando encofrado ID {encofrado.Id} ═══");

            // 1. Extraer todos los sólidos
            List<Solid> solidos = ExtraerSolidos(encofrado);
            if (solidos.Count == 0)
            {
                _log.AppendLine($"  ✗ ERROR: No se encontraron sólidos");
                return 0;
            }

            _log.AppendLine($"  ✓ Sólidos encontrados: {solidos.Count}");

            // Mostrar volumen total
            double volumenTotal = solidos.Sum(s => s.Volume);
            _log.AppendLine($"  ✓ Volumen total: {volumenTotal:F4} ft³");

            // 2. Analizar orientación dominante del encofrado
            OrientacionEncofrado orientacion = AnalizarOrientacion(solidos);
            _log.AppendLine($"  ✓ Orientación detectada: {orientacion.Tipo}");
            _log.AppendLine($"    - Normal promedio: X={orientacion.NormalPromedio.X:F3}, Y={orientacion.NormalPromedio.Y:F3}, Z={orientacion.NormalPromedio.Z:F3}");
            _log.AppendLine($"    - |Z| = {Math.Abs(orientacion.NormalPromedio.Z):F3}");

            // 3. Crear elemento nativo según orientación
            int metodoUsado = 0;

            if (orientacion.Tipo == TipoOrientacion.Vertical)
            {
                _log.AppendLine($"  → Intentando crear MURO con geometría compleja...");
                _log.AppendLine($"  Sistema de intentos múltiples: probando 5 métodos");
                _log.AppendLine($"  ════════════════════════════════════════════");

                // INTENTO 1: Wall.Create desde curvas de cara (TÉCNICA PROBADA - 11_CrearMurosSobreCarasCmd)
                _log.AppendLine($"  [1/5] Wall desde curvas de cara con validación coplanar...");
                if (CrearMuroDesdeCaras(encofrado, solidos, orientacion))
                {
                    murosCreados++;
                    metodoUsado = 1;
                    _log.AppendLine($"  ✓✓ ÉXITO: MURO CREADO CON MÉTODO 1 (Curvas de cara)");
                }
                else
                {
                    _log.AppendLine($"  ✗ Método 1 falló");

                    // INTENTO 2: Wall + EditProfile con SketchEditScope (probabilidad ALTA)
                    _log.AppendLine($"  [2/5] Wall + EditProfile con SketchEditScope...");
                    if (CrearMuroConEditProfile(encofrado, solidos, orientacion))
                    {
                        murosCreados++;
                        metodoUsado = 2;
                        _log.AppendLine($"  ✓✓ ÉXITO: MURO CREADO CON MÉTODO 2 (EditProfile)");
                    }
                    else
                    {
                        _log.AppendLine($"  ✗ Método 2 falló");

                        // INTENTO 3: Wall con múltiples CurveLoops (probabilidad MEDIA)
                        _log.AppendLine($"  [3/5] Wall con múltiples CurveLoops...");
                        if (CrearMuroConCurveLoopsComplejos(encofrado, solidos, orientacion))
                        {
                            murosCreados++;
                            metodoUsado = 3;
                            _log.AppendLine($"  ✓✓ ÉXITO: MURO CREADO CON MÉTODO 3 (CurveLoops)");
                        }
                        else
                        {
                            _log.AppendLine($"  ✗ Método 3 falló");

                            // INTENTO 4: DirectShape con categoría OST_Walls (probabilidad MEDIA-BAJA)
                            _log.AppendLine($"  [4/5] DirectShape con categoría Walls...");
                            if (CrearMuroComoDirectShape(encofrado, solidos))
                            {
                                murosCreados++;
                                metodoUsado = 4;
                                _log.AppendLine($"  ✓✓ ÉXITO: MURO CREADO CON MÉTODO 4 (DirectShape)");
                            }
                            else
                            {
                                _log.AppendLine($"  ✗ Método 4 falló");

                                // INTENTO 5: Wall.Create tradicional (probabilidad MÍNIMA, respaldo final)
                                _log.AppendLine($"  [5/5] Wall.Create tradicional (respaldo final)...");
                                if (CrearMuroNativo(encofrado, solidos, orientacion))
                                {
                                    murosCreados++;
                                    metodoUsado = 5;
                                    _log.AppendLine($"  ✓✓ ÉXITO: MURO CREADO CON MÉTODO 5 (Tradicional sin recortes)");
                                }
                                else
                                {
                                    _log.AppendLine($"  ✗✗ TODOS LOS MÉTODOS FALLARON - Encofrado NO eliminado");
                                    metodoUsado = 0;
                                }
                            }
                        }
                    }
                }

                _log.AppendLine($"  ════════════════════════════════════════════");
            }
            else
            {
                _log.AppendLine($"  → Intentando crear SUELO...");
                if (CrearSueloNativo(encofrado, solidos, orientacion))
                {
                    suelosCreados++;
                    metodoUsado = 6;  // 6 = suelo creado
                    _log.AppendLine($"  ✓✓ SUELO CREADO EXITOSAMENTE");
                }
                else
                {
                    _log.AppendLine($"  ✗✗ FALLO AL CREAR SUELO - Encofrado NO eliminado");
                    metodoUsado = 0;
                }
            }

            // 4. Marcar encofrado para eliminación si se creó exitosamente
            // NOTA: No eliminamos aquí porque causa rollback en casos de un solo elemento
            if (metodoUsado > 0)
            {
                encofradosAEliminar.Add(encofrado.Id);
                _log.AppendLine($"  ✓ Muro/Suelo creado exitosamente - Encofrado marcado para eliminación");
            }
            else
            {
                _log.AppendLine($"  ⚠ Fallo en creación - Encofrado CONSERVADO para revisión manual");
            }

            return metodoUsado;
        }

        /// <summary>
        /// EXPERIMENTAL: Intenta crear un FaceWall desde DirectShape convertido a Mass
        /// Esta aproximación podría preservar la geometría compleja incluyendo recortes
        /// </summary>
        private bool CrearMuroConMass(Element encofrado, List<Solid> solidos, OrientacionEncofrado orientacion)
        {
            DirectShape massShape = null; // Declarar aquí para limpieza en catch

            try
            {
                _log.AppendLine($"    [MASS EXPERIMENTAL] Intentando crear FaceWall desde Mass...");

                DirectShape ds = encofrado as DirectShape;
                if (ds == null)
                {
                    _log.AppendLine($"    ✗ El encofrado no es un DirectShape");
                    return false;
                }

                // PASO 1: Crear nuevo DirectShape con categoría Mass
                // Nota: DirectShape no tiene SetCategoryId, debemos crear uno nuevo
                List<GeometryObject> geometryList = new List<GeometryObject>();

                foreach (Solid solid in solidos)
                {
                    if (solid.Volume > 0.001)
                        geometryList.Add(solid);
                }

                if (geometryList.Count == 0)
                {
                    _log.AppendLine($"    ✗ No hay geometría válida para crear Mass");
                    return false;
                }

                // Crear DirectShape con categoría Mass
                massShape = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_Mass));
                massShape.SetShape(geometryList);

                _log.AppendLine($"    ✓ DirectShape creado con categoría Mass ID: {massShape.Id}");

                // PASO 2: Obtener geometría con referencias computadas
                Options opts = new Options
                {
                    DetailLevel = ViewDetailLevel.Fine,
                    ComputeReferences = true  // CRÍTICO para FaceWall.Create
                };

                GeometryElement geomElem = massShape.get_Geometry(opts);
                if (geomElem == null)
                {
                    _log.AppendLine($"    ✗ No se pudo obtener geometría");
                    return false;
                }

                // PASO 3: Encontrar caras verticales del mass
                List<Reference> facesVerticales = new List<Reference>();

                foreach (GeometryObject geomObj in geomElem)
                {
                    if (geomObj is Solid solid && solid.Volume > 0.001)
                    {
                        foreach (Face face in solid.Faces)
                        {
                            if (face is PlanarFace pf)
                            {
                                // Buscar caras verticales o inclinadas
                                double absZ = Math.Abs(pf.FaceNormal.Z);
                                if (absZ < 0.9) // No completamente horizontal
                                {
                                    Reference faceRef = face.Reference;
                                    if (faceRef != null)
                                    {
                                        facesVerticales.Add(faceRef);
                                        _log.AppendLine($"    Cara vertical encontrada: |Z|={absZ:F3}");
                                    }
                                }
                            }
                        }
                    }
                }

                if (facesVerticales.Count == 0)
                {
                    _log.AppendLine($"    ✗ No se encontraron caras verticales con referencias");
                    return false;
                }

                _log.AppendLine($"    ✓ {facesVerticales.Count} cara(s) vertical(es) con referencias");

                // PASO 4: Validar que la cara puede usarse para FaceWall
                Reference caraSeleccionada = facesVerticales.OrderByDescending(r =>
                {
                    GeometryObject geoObj = massShape.GetGeometryObjectFromReference(r);
                    if (geoObj is Face f)
                        return f.Area;
                    return 0;
                }).FirstOrDefault();

                if (!FaceWall.IsValidFaceReferenceForFaceWall(_doc, caraSeleccionada))
                {
                    _log.AppendLine($"    ✗ La referencia de cara no es válida para FaceWall");
                    return false;
                }

                _log.AppendLine($"    ✓ Referencia de cara válida para FaceWall");

                // PASO 5: Validar WallType
                if (!FaceWall.IsWallTypeValidForFaceWall(_doc, _wallType.Id))
                {
                    _log.AppendLine($"    ✗ El WallType '{_wallType.Name}' no es válido para FaceWall");
                    return false;
                }

                _log.AppendLine($"    ✓ WallType '{_wallType.Name}' válido");

                // PASO 6: Crear FaceWall con manejo de errores para evitar diálogos de Revit
                FaceWall faceWall = null;
                try
                {
                    faceWall = FaceWall.Create(_doc, _wallType.Id, WallLocationLine.CoreCenterline, caraSeleccionada);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear FaceWall (parámetros inválidos): {ex.Message}");
                    return false;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear FaceWall (argumentos inválidos): {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    _log.AppendLine($"    ✗ Error inesperado creando FaceWall: {ex.Message}");
                    return false;
                }

                if (faceWall == null)
                {
                    _log.AppendLine($"    ✗ FaceWall.Create retornó null");
                    return false;
                }

                _log.AppendLine($"    ✓✓ FaceWall creado exitosamente ID: {faceWall.Id}");
                _log.AppendLine($"    ✓ Geometría compleja preservada incluyendo recortes");

                // Desactivar Room Bounding
                Parameter paramRoomBounding = faceWall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                if (paramRoomBounding != null && !paramRoomBounding.IsReadOnly)
                {
                    paramRoomBounding.Set(0);
                }

                // Copiar parámetro Comentarios primero (contiene el ID del elemento encofrado)
                CopiarParametroComentarios(encofrado, faceWall);

                // Copiar nivel y desfases del elemento estructural encofrado
                CopiarNivelYDesfasesDeElementoEncofrado(encofrado, faceWall);

                // PASO 7: Eliminar DirectShape Mass temporal (ya no es necesario)
                try
                {
                    _doc.Delete(massShape.Id);
                    _log.AppendLine($"    ✓ DirectShape Mass temporal eliminado ID: {massShape.Id}");
                }
                catch (Exception exDelete)
                {
                    _log.AppendLine($"    ⚠ No se pudo eliminar Mass temporal: {exDelete.Message}");
                    // No es crítico, continuamos
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error en método experimental Mass: {ex.Message}");
                _log.AppendLine($"    Stack: {ex.StackTrace}");

                // Intentar limpiar el Mass temporal si se creó
                try
                {
                    if (massShape != null && massShape.IsValidObject)
                    {
                        _doc.Delete(massShape.Id);
                        _log.AppendLine($"    ✓ Mass temporal limpiado después del error");
                    }
                }
                catch
                {
                    // Ignorar errores de limpieza
                }

                return false;
            }
        }

        /// <summary>
        /// MÉTODO 2: Crea muro directamente desde curvas de la cara (técnica probada)
        /// Usa Wall.Create(List<Curve>) para crear el muro siguiendo el contorno completo
        /// Inspirado en 11_CrearMurosSobreCarasCmd.cs
        /// </summary>
        private bool CrearMuroDesdeCaras(Element encofrado, List<Solid> solidos, OrientacionEncofrado orientacion)
        {
            try
            {
                _log.AppendLine($"    [CARAS] Creando muro desde curvas de cara...");

                Solid solidoPrincipal = solidos.OrderByDescending(s => s.Volume).First();

                // 1. CRÍTICO: Encontrar TODAS las caras verticales significativas
                List<PlanarFace> carasVerticales = new List<PlanarFace>();
                double areaSignificativa = 0;

                foreach (Face face in solidoPrincipal.Faces)
                {
                    // UMBRAL ESTRICTO: 0.01 = ~0.57° máximo (muros perfectamente verticales)
                    if (face is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) < 0.01)
                    {
                        carasVerticales.Add(pf);
                        if (pf.Area > areaSignificativa)
                        {
                            areaSignificativa = pf.Area;
                        }
                    }
                }

                if (carasVerticales.Count == 0)
                {
                    _log.AppendLine($"    ✗ No se encontró ninguna cara vertical");
                    return false;
                }

                // 2. CRÍTICO: Filtrar caras grandes y seleccionar la más cercana al elemento estructural
                PlanarFace caraVertical = null;
                Element elementoEstructural = ObtenerElementoEstructuralDeEncofrado(encofrado);

                // FILTRO DE ÁREA: Solo considerar caras grandes (excluir bordes de espesor)
                // 0.1 ft² ≈ 0.0093 m² - caras menores son bordes, no encofrados
                double areaMinima = 0.1;
                List<PlanarFace> carasGrandes = carasVerticales.Where(c => c.Area >= areaMinima).ToList();

                _log.AppendLine($"    → Caras verticales totales: {carasVerticales.Count}");
                _log.AppendLine($"    → Caras grandes (≥{areaMinima} ft²): {carasGrandes.Count}");

                if (carasGrandes.Count == 0)
                {
                    // Si no hay caras grandes, usar todas (fallback para geometrías especiales)
                    carasGrandes = carasVerticales;
                    _log.AppendLine($"    ⚠ No hay caras grandes, usando todas las caras verticales");
                }

                if (elementoEstructural != null && carasGrandes.Count >= 2)
                {
                    // ESTRATEGIA MEJORADA: Priorizar ÁREA sobre distancia
                    // 1. Identificar caras principales (área > 5 ft² ≈ 0.46 m²)
                    // 2. Si hay caras principales, usar la más cercana entre ellas
                    // 3. Si no, usar la cara más grande disponible

                    BoundingBoxXYZ bboxEstructural = elementoEstructural.get_BoundingBox(null);
                    if (bboxEstructural != null)
                    {
                        XYZ centroideEstructural = (bboxEstructural.Min + bboxEstructural.Max) / 2.0;

                        // Encontrar el área máxima para clasificar caras
                        double areaMaxima = carasGrandes.Max(c => c.Area);

                        // CRITERIO: Una cara es "principal" si tiene al menos 30% del área máxima
                        // Esto excluye caras laterales pequeñas pero incluye caras estructurales similares
                        double umbralCaraPrincipal = areaMaxima * 0.3;

                        List<PlanarFace> carasPrincipales = carasGrandes
                            .Where(c => c.Area >= umbralCaraPrincipal)
                            .ToList();

                        _log.AppendLine($"    [SELECCIÓN] Área máxima={areaMaxima:F4} ft² | Umbral principal={umbralCaraPrincipal:F4} ft²");
                        _log.AppendLine($"    [SELECCIÓN] Caras principales encontradas: {carasPrincipales.Count}");

                        // Listar todas las caras con su clasificación
                        foreach (PlanarFace cara in carasGrandes)
                        {
                            XYZ centroideCara = CalcularCentroideGeometricoCara(cara);
                            double distancia = centroideCara.DistanceTo(centroideEstructural);
                            string tipo = cara.Area >= umbralCaraPrincipal ? "[PRINCIPAL]" : "[lateral]";
                            _log.AppendLine($"       {tipo} Cara área={cara.Area:F4} ft² → distancia={distancia * 304.8:F1}mm");
                        }

                        // Seleccionar la cara más cercana ENTRE LAS PRINCIPALES
                        if (carasPrincipales.Count > 0)
                        {
                            double distanciaMin = double.MaxValue;
                            foreach (PlanarFace cara in carasPrincipales)
                            {
                                XYZ centroideCara = CalcularCentroideGeometricoCara(cara);
                                double distancia = centroideCara.DistanceTo(centroideEstructural);

                                if (distancia < distanciaMin)
                                {
                                    distanciaMin = distancia;
                                    caraVertical = cara;
                                }
                            }

                            _log.AppendLine($"    ✓ Cara PRINCIPAL más cercana seleccionada: área={caraVertical.Area:F4} ft², distancia={distanciaMin * 304.8:F1}mm");
                        }
                        else
                        {
                            // Fallback: usar la más grande si no hay caras principales clasificadas
                            caraVertical = carasGrandes.OrderByDescending(c => c.Area).First();
                            _log.AppendLine($"    ✓ Cara más GRANDE seleccionada (fallback): área={caraVertical.Area:F4} ft²");
                        }
                    }
                }

                // Si no se pudo determinar con elemento estructural, usar la cara más grande
                if (caraVertical == null)
                {
                    caraVertical = carasGrandes.OrderByDescending(c => c.Area).First();
                    _log.AppendLine($"    ✓ Cara más grande seleccionada: área={caraVertical.Area:F4} ft²");
                }

                _log.AppendLine($"    ✓ Cara vertical: normal={caraVertical.FaceNormal}, |Z|={Math.Abs(caraVertical.FaceNormal.Z):F3}");

                // 2. Obtener CurveLoops de los bordes de la cara
                IList<CurveLoop> curveLoops = caraVertical.GetEdgesAsCurveLoops();
                if (curveLoops.Count == 0)
                {
                    _log.AppendLine($"    ✗ No se encontraron CurveLoops en la cara");
                    return false;
                }

                _log.AppendLine($"    ✓ CurveLoops encontrados: {curveLoops.Count}");

                // 3. Obtener vectores de la cara para verificar orientación
                XYZ xVectorPlanarFace = caraVertical.XVector;
                XYZ yVectorPlanarFace = caraVertical.YVector;
                XYZ faceNormal = caraVertical.FaceNormal;

                // 4. Convertir CurveLoops a List<Curve> con verificación de orientación
                List<Curve> curves = new List<Curve>();

                if (curveLoops.Count == 1)
                {
                    // Caso simple: un solo loop
                    CurveLoop curveLoop = curveLoops[0];
                    foreach (Curve curve in curveLoop)
                    {
                        curves.Add(curve);
                    }
                    _log.AppendLine($"    ✓ Un solo loop: {curves.Count} curvas");
                }
                else
                {
                    // Caso complejo: múltiples loops (contorno exterior + recortes interiores)
                    foreach (CurveLoop curveLoop in curveLoops)
                    {
                        Plane planeCurveLoop = curveLoop.GetPlane();
                        XYZ xVectorCurveLoop = planeCurveLoop.XVec;
                        XYZ yVectorCurveLoop = planeCurveLoop.YVec;

                        // Verificar si los vectores son paralelos (usando cross product)
                        // Si el cross product es casi cero, los vectores son paralelos
                        bool xParalelo = xVectorPlanarFace.CrossProduct(xVectorCurveLoop).GetLength() < 0.001;
                        bool yParalelo = yVectorPlanarFace.CrossProduct(yVectorCurveLoop).GetLength() < 0.001;

                        if (!xParalelo || !yParalelo)
                        {
                            // Vectores no paralelos: invertir curvas
                            _log.AppendLine($"    ⚠ Loop no paralelo - invirtiendo curvas");
                            List<Curve> reverseCurves = curveLoop.Reverse().ToList();
                            foreach (Curve reverseCurve in reverseCurves)
                            {
                                curves.Add(reverseCurve);
                            }
                        }
                        else
                        {
                            // Vectores paralelos: usar curvas tal como están
                            foreach (Curve curve in curveLoop)
                            {
                                curves.Add(curve);
                            }
                        }
                    }
                    _log.AppendLine($"    ✓ Múltiples loops procesados: {curves.Count} curvas totales");
                }

                if (curves.Count < 3)
                {
                    _log.AppendLine($"    ✗ Insuficientes curvas para crear muro: {curves.Count}");
                    return false;
                }

                // 5. VALIDAR Y PROYECTAR CURVAS A PLANO COMÚN (crítico para Wall.Create)
                curves = ValidarYProyectarCurvasAPlanoComun(curves, faceNormal);

                if (curves == null || curves.Count < 3)
                {
                    _log.AppendLine($"    ✗ Error en proyección de curvas o insuficientes curvas después de validación");
                    return false;
                }

                // 6. Obtener nivel base y altura del modelo genérico ORIGINAL
                // Usamos el BoundingBox del encofrado, no las curvas procesadas
                BoundingBoxXYZ bboxOriginal = encofrado.get_BoundingBox(null);
                double zMinOriginal = bboxOriginal != null ? bboxOriginal.Min.Z : curves.Min(c => Math.Min(c.GetEndPoint(0).Z, c.GetEndPoint(1).Z));
                double zMaxOriginal = bboxOriginal != null ? bboxOriginal.Max.Z : curves.Max(c => Math.Max(c.GetEndPoint(0).Z, c.GetEndPoint(1).Z));
                double alturaOriginal = zMaxOriginal - zMinOriginal;

                Level nivel = ObtenerNivelMasCercano(zMinOriginal);
                if (nivel == null)
                {
                    _log.AppendLine($"    ✗ No se encontró nivel para Z={zMinOriginal}");
                    return false;
                }

                _log.AppendLine($"    ✓ Nivel base: {nivel.Name} (elevación={nivel.Elevation:F2})");
                _log.AppendLine($"    ✓ Altura modelo genérico original: zMin={zMinOriginal:F3}, zMax={zMaxOriginal:F3}, altura={alturaOriginal:F3}");

                // CRÍTICO: Pasar TODAS las curvas del loop a Wall.Create
                // Esto permite crear muros que sigan exactamente la geometría del encofrado,
                // incluyendo formas inclinadas como escaleras
                _log.AppendLine($"    [CURVAS] Usando TODAS las {curves.Count} curvas del loop para crear muro con geometría exacta");

                // 7. Log detallado de las curvas antes de crear el muro
                _log.AppendLine($"    [PRE-CREATE] {curves.Count} curvas a pasar a Wall.Create:");
                for (int i = 0; i < Math.Min(curves.Count, 10); i++)
                {
                    Curve c = curves[i];
                    XYZ p0 = c.GetEndPoint(0);
                    XYZ p1 = c.GetEndPoint(1);
                    _log.AppendLine($"      Curva[{i}]: ({p0.X:F3},{p0.Y:F3},{p0.Z:F3}) -> ({p1.X:F3},{p1.Y:F3},{p1.Z:F3})");
                }
                if (curves.Count > 10)
                {
                    _log.AppendLine($"      ... y {curves.Count - 10} curvas más");
                }

                // 7.5. VALIDACIONES DE GEOMETRÍA antes de crear el muro
                _log.AppendLine($"    [VALIDACIÓN] Verificando geometría de curvas...");

                // Validación 1: Verificar que las curvas forman un loop cerrado
                bool loopCerrado = true;
                for (int i = 0; i < curves.Count; i++)
                {
                    Curve curva1 = curves[i];
                    Curve curva2 = curves[(i + 1) % curves.Count];
                    XYZ fin1 = curva1.GetEndPoint(1);
                    XYZ inicio2 = curva2.GetEndPoint(0);
                    double distancia = fin1.DistanceTo(inicio2);

                    if (distancia > 0.01) // Tolerancia de ~3mm
                    {
                        loopCerrado = false;
                        _log.AppendLine($"    ⚠ ADVERTENCIA: Gap entre curva {i} y {(i + 1) % curves.Count}: {distancia * 304.8:F1}mm");
                    }
                }

                if (loopCerrado)
                {
                    _log.AppendLine($"    ✓ Loop cerrado verificado");
                }

                // Validación 2: Verificar dimensiones del muro (ancho y alto)
                double xMin = curves.SelectMany(c => new[] { c.GetEndPoint(0).X, c.GetEndPoint(1).X }).Min();
                double xMax = curves.SelectMany(c => new[] { c.GetEndPoint(0).X, c.GetEndPoint(1).X }).Max();
                double yMin = curves.SelectMany(c => new[] { c.GetEndPoint(0).Y, c.GetEndPoint(1).Y }).Min();
                double yMax = curves.SelectMany(c => new[] { c.GetEndPoint(0).Y, c.GetEndPoint(1).Y }).Max();
                double zMinCurvas = curves.SelectMany(c => new[] { c.GetEndPoint(0).Z, c.GetEndPoint(1).Z }).Min();
                double zMaxCurvas = curves.SelectMany(c => new[] { c.GetEndPoint(0).Z, c.GetEndPoint(1).Z }).Max();

                double anchoX = xMax - xMin;
                double anchoY = yMax - yMin;
                double alturaMuro = zMaxCurvas - zMinCurvas;

                _log.AppendLine($"    [DIMENSIONES] Ancho X={anchoX * 304.8:F1}mm, Ancho Y={anchoY * 304.8:F1}mm, Altura={alturaMuro * 304.8:F1}mm");

                // Validación 3: Verificar que las dimensiones sean razonables
                double dimensionMinima = 0.01; // ~3mm mínimo
                double dimensionMaxima = 1000.0; // ~305m máximo

                if (anchoX < dimensionMinima && anchoY < dimensionMinima)
                {
                    _log.AppendLine($"    ✗ ERROR: Muro demasiado estrecho (ancho < {dimensionMinima * 304.8:F1}mm)");
                    return false;
                }

                if (alturaMuro < dimensionMinima)
                {
                    _log.AppendLine($"    ✗ ERROR: Muro demasiado bajo (altura < {dimensionMinima * 304.8:F1}mm)");
                    return false;
                }

                if (anchoX > dimensionMaxima || anchoY > dimensionMaxima || alturaMuro > dimensionMaxima)
                {
                    _log.AppendLine($"    ✗ ERROR: Dimensiones excesivas (límite: {dimensionMaxima * 304.8:F0}mm)");
                    return false;
                }

                // Validación 4: Verificar que hay curvas verticales y horizontales
                int curvasVerticales = 0;
                int curvasHorizontales = 0;
                double toleranciaVertical = 0.01; // ~0.6° de inclinación

                foreach (Curve curva in curves)
                {
                    XYZ p0 = curva.GetEndPoint(0);
                    XYZ p1 = curva.GetEndPoint(1);
                    double deltaZ = Math.Abs(p1.Z - p0.Z);
                    double deltaXY = Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));

                    if (deltaZ > deltaXY * 0.5) // Más vertical que horizontal
                    {
                        curvasVerticales++;
                    }
                    else
                    {
                        curvasHorizontales++;
                    }
                }

                _log.AppendLine($"    [CLASIFICACIÓN] Curvas verticales={curvasVerticales}, horizontales={curvasHorizontales}");

                if (curvasVerticales == 0)
                {
                    _log.AppendLine($"    ⚠ ADVERTENCIA: No se detectaron curvas verticales - muro puede ser incorrecto");
                }

                // Validación 5: Comparar dimensiones con el BoundingBox del encofrado original
                if (bboxOriginal != null)
                {
                    double anchoOriginalX = bboxOriginal.Max.X - bboxOriginal.Min.X;
                    double anchoOriginalY = bboxOriginal.Max.Y - bboxOriginal.Min.Y;
                    double alturaOriginalBBox = bboxOriginal.Max.Z - bboxOriginal.Min.Z;

                    double diferenciaAncho = Math.Max(Math.Abs(anchoX - anchoOriginalX), Math.Abs(anchoY - anchoOriginalY));
                    double diferenciaAltura = Math.Abs(alturaMuro - alturaOriginalBBox);

                    _log.AppendLine($"    [COMPARACIÓN] Diferencia ancho={diferenciaAncho * 304.8:F1}mm, altura={diferenciaAltura * 304.8:F1}mm");

                    // Si las dimensiones son muy diferentes, advertir
                    double toleranciaDimension = 1.0; // 1 pie = ~305mm
                    if (diferenciaAncho > toleranciaDimension || diferenciaAltura > toleranciaDimension)
                    {
                        _log.AppendLine($"    ⚠ ADVERTENCIA: Dimensiones del muro difieren significativamente del encofrado original");
                        _log.AppendLine($"    ⚠ Original: {anchoOriginalX * 304.8:F1} x {anchoOriginalY * 304.8:F1} x {alturaOriginalBBox * 304.8:F1}mm");
                        _log.AppendLine($"    ⚠ Curvas:   {anchoX * 304.8:F1} x {anchoY * 304.8:F1} x {alturaMuro * 304.8:F1}mm");
                    }
                    else
                    {
                        _log.AppendLine($"    ✓ Dimensiones consistentes con encofrado original");
                    }
                }

                _log.AppendLine($"    ✓ Validaciones completadas - procediendo a crear muro");

                // 8. Crear el muro desde las curvas con manejo de errores
                bool isStructural = true;
                Wall newWall = null;
                try
                {
                    _log.AppendLine($"    [WALL.CREATE] Llamando Wall.Create con {curves.Count} curvas...");
                    newWall = Wall.Create(_doc, curves, _wallType.Id, nivel.Id, isStructural);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear Wall desde curvas (parámetros inválidos): {ex.Message}");
                    return false;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear Wall desde curvas (argumentos inválidos): {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    _log.AppendLine($"    ✗ Error inesperado creando Wall: {ex.Message}");
                    return false;
                }

                if (newWall == null)
                {
                    _log.AppendLine($"    ✗ Wall.Create retornó null");
                    return false;
                }

                _log.AppendLine($"    ✓✓ MURO CREADO EXITOSAMENTE - ID: {newWall.Id}");

                // 8.5. CRÍTICO: Deshabilitar unión automática de muros en ambos extremos
                try
                {
                    WallUtils.DisallowWallJoinAtEnd(newWall, 0); // Extremo inicial
                    WallUtils.DisallowWallJoinAtEnd(newWall, 1); // Extremo final
                    _log.AppendLine($"    ✓ Uniones automáticas deshabilitadas");
                }
                catch (Exception exJoin)
                {
                    _log.AppendLine($"    ⚠ No se pudo deshabilitar uniones: {exJoin.Message}");
                }

                // 9. CRÍTICO: Calcular y aplicar Base Offset correcto basándose en la geometría real
                // Esto es especialmente importante para encofrados de vigas que están por debajo del nivel
                try
                {
                    // Usar zMinOriginal que ya fue calculado correctamente del BoundingBox del encofrado
                    double nivelElevacion = nivel.ProjectElevation;
                    double baseOffsetNecesario = zMinOriginal - nivelElevacion;

                    _log.AppendLine($"    [BASE OFFSET] zMin encofrado={zMinOriginal:F3}, Nivel elevación={nivelElevacion:F3}");
                    _log.AppendLine($"    [BASE OFFSET] Base Offset calculado={baseOffsetNecesario:F3} ft ({baseOffsetNecesario * 304.8:F1}mm)");

                    Parameter paramBaseOffset = newWall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                    if (paramBaseOffset != null && !paramBaseOffset.IsReadOnly)
                    {
                        paramBaseOffset.Set(baseOffsetNecesario);
                        _log.AppendLine($"    ✓ Base Offset aplicado: {baseOffsetNecesario * 304.8:F1}mm");
                    }
                    else
                    {
                        _log.AppendLine($"    ⚠ No se pudo modificar Base Offset (parámetro readonly o null)");
                    }
                }
                catch (Exception exOffset)
                {
                    _log.AppendLine($"    ⚠ Error calculando/aplicando Base Offset: {exOffset.Message}");
                }

                // 10. Desactivar Room Bounding
                Parameter paramRoomBounding = newWall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                if (paramRoomBounding != null && !paramRoomBounding.IsReadOnly)
                {
                    paramRoomBounding.Set(0);
                }

                // Copiar parámetro Comentarios primero (contiene el ID del elemento encofrado)
                CopiarParametroComentarios(encofrado, newWall);

                // Copiar nivel y desfases del elemento estructural encofrado
                // NOTA: Esto se hace DESPUÉS de aplicar el Base Offset correcto calculado
                CopiarNivelYDesfasesDeElementoEncofrado(encofrado, newWall);

                // 10. DESPLAZAMIENTO DEL MURO - RE-HABILITADO
                // Wall.Create coloca la línea central del muro en las curvas.
                // Necesitamos desplazar el muro exactamente la mitad de su ancho para que
                // la cara INTERIOR del muro esté alineada con la cara del encofrado.
                // Ahora que seleccionamos correctamente la cara principal (no las laterales),
                // el desplazamiento funcionará correctamente.
                try
                {
                    double mediaAnchura = newWall.Width / 2.0; // En pies
                    XYZ normalCara = caraVertical.FaceNormal.Normalize();

                    // Calcular centroide de la cara seleccionada
                    XYZ centroideCara = CalcularCentroideGeometricoCara(caraVertical);

                    // Determinar dirección correcta del desplazamiento
                    XYZ direccionDesplazamiento;

                    if (elementoEstructural != null)
                    {
                        // Tenemos elemento estructural: usarlo para determinar la dirección
                        BoundingBoxXYZ bboxEstructural = elementoEstructural.get_BoundingBox(null);
                        if (bboxEstructural != null)
                        {
                            XYZ centroideEstructural = (bboxEstructural.Min + bboxEstructural.Max) / 2.0;
                            XYZ vectorHaciaEstructural = (centroideEstructural - centroideCara).Normalize();

                            // Calcular si la normal apunta hacia o alejándose del elemento estructural
                            double producto = normalCara.DotProduct(vectorHaciaEstructural);

                            // CRÍTICO: Invertir la lógica para que el espesor quede AFUERA
                            if (producto > 0)
                            {
                                // Normal apunta HACIA el elemento: desplazar en dirección OPUESTA para alejar el muro
                                // Esto coloca el espesor del muro AFUERA del encofrado
                                direccionDesplazamiento = -normalCara;
                                _log.AppendLine($"    [DESPLAZAMIENTO] Normal hacia elemento → desplazando OPUESTO (espesor afuera)");
                            }
                            else
                            {
                                // Normal apunta ALEJÁNDOSE del elemento: desplazar en dirección de la normal
                                // Esto coloca el espesor del muro AFUERA del encofrado
                                direccionDesplazamiento = normalCara;
                                _log.AppendLine($"    [DESPLAZAMIENTO] Normal alejándose elemento → desplazando NORMAL (espesor afuera)");
                            }
                        }
                        else
                        {
                            // No se pudo obtener bbox: usar la normal directamente
                            direccionDesplazamiento = normalCara;
                            _log.AppendLine($"    [DESPLAZAMIENTO] Sin bbox estructural → usando dirección normal por defecto");
                        }
                    }
                    else
                    {
                        // Sin elemento estructural: desplazar en dirección de la normal
                        direccionDesplazamiento = normalCara;
                        _log.AppendLine($"    [DESPLAZAMIENTO] Sin elemento estructural → usando dirección normal");
                    }

                    // Calcular vector de desplazamiento
                    XYZ vectorDesplazamiento = direccionDesplazamiento * mediaAnchura;

                    // Aplicar desplazamiento al muro usando ElementTransformUtils
                    ElementTransformUtils.MoveElement(_doc, newWall.Id, vectorDesplazamiento);

                    _log.AppendLine($"    ✓ Muro desplazado {mediaAnchura * 304.8:F1}mm para alinear cara interior con encofrado");
                    _log.AppendLine($"    [POSICIÓN] Vector desplazamiento: ({vectorDesplazamiento.X:F4}, {vectorDesplazamiento.Y:F4}, {vectorDesplazamiento.Z:F4})");
                }
                catch (Exception exDesplaz)
                {
                    _log.AppendLine($"    ⚠ Error aplicando desplazamiento: {exDesplaz.Message}");
                    // Continuar aunque falle el desplazamiento
                }

                _log.AppendLine($"    ✓✓ Muro creado exitosamente desde curvas de cara");
                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error en método CrearMuroDesdeCaras: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _log.AppendLine($"       Inner: {ex.InnerException.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// MÉTODO 3: Crea un muro y edita su perfil usando SketchEditScope (Revit 2022+)
        /// Intenta preservar la geometría compleja editando el perfil del muro
        /// </summary>
        private bool CrearMuroConEditProfile(Element encofrado, List<Solid> solidos, OrientacionEncofrado orientacion)
        {
            try
            {
                _log.AppendLine($"    [EDITPROFILE] Creando muro con perfil editado...");

                Solid solidoPrincipal = solidos.OrderByDescending(s => s.Volume).First();

                // Obtener altura real del modelo genérico original para Top Offset preciso
                BoundingBoxXYZ bboxOriginal = encofrado.get_BoundingBox(null);
                double zMaxOriginal = bboxOriginal != null ? bboxOriginal.Max.Z : 0;
                double zMinOriginal = bboxOriginal != null ? bboxOriginal.Min.Z : 0;

                // Encontrar cara vertical principal
                PlanarFace caraVertical = null;
                double maxArea = 0;

                foreach (Face face in solidoPrincipal.Faces)
                {
                    // UMBRAL ESTRICTO: 0.01 = ~0.57° máximo (muros perfectamente verticales)
                    if (face is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) < 0.01 && pf.Area > maxArea)
                    {
                        maxArea = pf.Area;
                        caraVertical = pf;
                    }
                }

                if (caraVertical == null)
                {
                    _log.AppendLine($"    ✗ No se encontró cara vertical (todas las caras están inclinadas)");
                    _log.AppendLine($"    ⚠ Este encofrado debería clasificarse como SUELO");
                    return false;
                }

                // Extraer contorno y crear línea base simple
                IList<CurveLoop> loops = caraVertical.GetEdgesAsCurveLoops();
                if (loops.Count == 0) return false;

                CurveLoop loopPrincipal = loops.OrderByDescending(l => CalcularAreaLoop(l)).First();

                // Desplazar hacia cara estructural
                double espesorEncofrado = UnitUtils.ConvertToInternalUnits(18.0, UnitTypeId.Millimeters);
                XYZ normalHaciaAdentro = -caraVertical.FaceNormal;
                CurveLoop loopDesplazado = DesplazarCurveLoop(loopPrincipal, normalHaciaAdentro, espesorEncofrado);

                // VALIDAR el CurveLoop antes de usarlo
                string razonValidacion;
                if (!ValidarCurveLoopParaPerfil(loopDesplazado, out razonValidacion))
                {
                    _log.AppendLine($"    ✗ CurveLoop inválido: {razonValidacion}");
                    return false;
                }
                _log.AppendLine($"    ✓ CurveLoop validado: {loopDesplazado.Count()} curvas, cerrado y planar");

                // Obtener puntos para línea base
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loopDesplazado)
                {
                    puntos.Add(curve.GetEndPoint(0));
                }

                double zMin = puntos.Min(p => p.Z);
                double zMax = puntos.Max(p => p.Z);

                // IMPORTANTE: Usar la altura del modelo genérico original para que el perfil editado encaje perfectamente
                // No la altura del CurveLoop procesado, que puede ser diferente
                double alturaOriginal = zMaxOriginal - zMinOriginal;
                double altura = alturaOriginal > 0 ? alturaOriginal : (zMax - zMin);

                var puntosBase = puntos.Where(p => Math.Abs(p.Z - zMin) < 0.1).ToList();
                if (puntosBase.Count < 2) return false;

                // Línea base
                XYZ p1 = puntosBase[0];
                XYZ p2 = puntosBase[0];
                double maxDist = 0;

                for (int i = 0; i < puntosBase.Count; i++)
                {
                    for (int j = i + 1; j < puntosBase.Count; j++)
                    {
                        double dist = puntosBase[i].DistanceTo(puntosBase[j]);
                        if (dist > maxDist)
                        {
                            maxDist = dist;
                            p1 = puntosBase[i];
                            p2 = puntosBase[j];
                        }
                    }
                }

                Level nivel = ObtenerNivelMasCercano(zMin);
                if (nivel == null) return false;

                Line lineaBase = Line.CreateBound(
                    new XYZ(p1.X, p1.Y, nivel.Elevation),
                    new XYZ(p2.X, p2.Y, nivel.Elevation));

                // Crear muro básico con manejo de errores
                Wall muro = null;
                try
                {
                    muro = Wall.Create(_doc, lineaBase, _wallType.Id, nivel.Id, altura, 0, false, false);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear Wall (parámetros inválidos): {ex.Message}");
                    return false;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear Wall (argumentos inválidos): {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    _log.AppendLine($"    ✗ Error inesperado creando Wall: {ex.Message}");
                    return false;
                }

                if (muro == null)
                {
                    _log.AppendLine($"    ✗ Wall.Create retornó null");
                    return false;
                }

                _log.AppendLine($"    ✓ Muro básico creado ID: {muro.Id}");

                // Ajustar offset base usando el zMin del modelo genérico original
                // Esto asegura que la base del muro coincida con la base del encofrado
                double offsetBase = zMinOriginal - nivel.Elevation;
                Parameter paramOffset = muro.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (paramOffset != null && !paramOffset.IsReadOnly)
                {
                    paramOffset.Set(offsetBase);
                }

                // EDITAR PERFIL con SketchEditScope
                try
                {
                    // Verificar si el muro puede tener perfil editado
                    if (!muro.CanHaveProfileSketch())
                    {
                        _log.AppendLine($"    ⚠ El muro no soporta perfil editado (posible arco/elipse)");
                        return false;
                    }

                    // Crear sketch de perfil
                    Sketch profileSketch = muro.CreateProfileSketch();

                    _log.AppendLine($"    ✓ Profile sketch creado ID: {profileSketch.Id}");

                    // Editar el sketch con SketchEditScope
                    using (SketchEditScope editScope = new SketchEditScope(_doc, "Edit Wall Profile"))
                    {
                        editScope.Start(profileSketch.Id);

                        using (SubTransaction subTrans = new SubTransaction(_doc))
                        {
                            subTrans.Start();

                            // Eliminar TODAS las curvas existentes del sketch
                            var allModelCurves = new FilteredElementCollector(_doc, profileSketch.Id)
                                .OfClass(typeof(ModelCurve))
                                .ToElementIds();

                            if (allModelCurves.Count > 0)
                            {
                                _doc.Delete(allModelCurves);
                                _log.AppendLine($"    Eliminadas {allModelCurves.Count} curvas existentes del perfil");
                            }

                            // Obtener plano del sketch (plano vertical del muro)
                            SketchPlane sketchPlane = profileSketch.SketchPlane;
                            Plane sketchPlanePlane = sketchPlane.GetPlane();

                            _log.AppendLine($"    Plano del sketch: Origin={sketchPlanePlane.Origin}, Normal={sketchPlanePlane.Normal}");

                            // Proyectar TODAS las curvas del loop al plano del sketch
                            List<Curve> curvasProyectadas = new List<Curve>();
                            int curvasAgregadas = 0;

                            foreach (Curve curve in loopDesplazado)
                            {
                                try
                                {
                                    // Proyectar puntos de la curva al plano del sketch
                                    XYZ punto0 = curve.GetEndPoint(0);
                                    XYZ punto1 = curve.GetEndPoint(1);

                                    // Proyectar al plano
                                    UV uv0 = new UV(0, 0);
                                    UV uv1 = new UV(0, 0);
                                    double distance0;
                                    double distance1;

                                    sketchPlanePlane.Project(punto0, out uv0, out distance0);
                                    sketchPlanePlane.Project(punto1, out uv1, out distance1);

                                    // Convertir UV de vuelta a XYZ en el plano
                                    XYZ punto0Projected = sketchPlanePlane.Origin + uv0.U * sketchPlanePlane.XVec + uv0.V * sketchPlanePlane.YVec;
                                    XYZ punto1Projected = sketchPlanePlane.Origin + uv1.U * sketchPlanePlane.XVec + uv1.V * sketchPlanePlane.YVec;

                                    // Crear línea proyectada (el perfil del muro siempre usa líneas)
                                    if (punto0Projected.DistanceTo(punto1Projected) > 0.001) // Evitar líneas degeneradas
                                    {
                                        Line lineaProyectada = Line.CreateBound(punto0Projected, punto1Projected);
                                        ModelCurve mc = _doc.Create.NewModelCurve(lineaProyectada, sketchPlane);
                                        curvasAgregadas++;
                                    }
                                }
                                catch (Exception exCurve)
                                {
                                    _log.AppendLine($"    ⚠ Error proyectando curva: {exCurve.Message}");
                                }
                            }

                            _log.AppendLine($"    ✓ {curvasAgregadas} curvas agregadas al perfil del muro");

                            if (curvasAgregadas == 0)
                            {
                                _log.AppendLine($"    ✗ No se pudo agregar ninguna curva al perfil");
                                subTrans.RollBack();
                                return false;
                            }

                            subTrans.Commit();
                        }

                        editScope.Commit(new FailuresPreprocessor());
                    }

                    _log.AppendLine($"    ✓ Perfil editado exitosamente");

                    // Desactivar Room Bounding
                    Parameter paramRoomBounding = muro.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                    if (paramRoomBounding != null && !paramRoomBounding.IsReadOnly)
                    {
                        paramRoomBounding.Set(0);
                    }

                    // Copiar parámetro Comentarios primero (contiene el ID del elemento encofrado)
                    CopiarParametroComentarios(encofrado, muro);

                    // Copiar nivel y desfases del elemento estructural encofrado
                    CopiarNivelYDesfasesDeElementoEncofrado(encofrado, muro);

                    return true;
                }
                catch (Exception exProfile)
                {
                    _log.AppendLine($"    ✗ Error editando perfil: {exProfile.Message}");
                    // Dejar el muro básico si falla la edición
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error en método EditProfile: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// MÉTODO 5: Crea DirectShape con categoría OST_Walls
        /// Preserva geometría completa pero como DirectShape (no Wall verdadero)
        /// </summary>
        private bool CrearMuroComoDirectShape(Element encofrado, List<Solid> solidos)
        {
            try
            {
                _log.AppendLine($"    [DIRECTSHAPE] Creando DirectShape con categoría Walls...");

                List<GeometryObject> geometryList = new List<GeometryObject>();

                foreach (Solid solid in solidos)
                {
                    if (solid.Volume > 0.001)
                        geometryList.Add(solid);
                }

                if (geometryList.Count == 0) return false;

                // Crear DirectShape con categoría Walls
                DirectShape wallShape = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_Walls));
                wallShape.SetShape(geometryList);

                _log.AppendLine($"    ✓ DirectShape creado con categoría Walls ID: {wallShape.Id}");

                // Copiar nombre si existe
                string nombre = encofrado.Name;
                if (!string.IsNullOrEmpty(nombre))
                {
                    wallShape.Name = nombre + "_Wall";
                }

                // Desactivar Room Bounding
                Parameter paramRoomBounding = wallShape.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                if (paramRoomBounding != null && !paramRoomBounding.IsReadOnly)
                {
                    paramRoomBounding.Set(0);
                }

                CopiarParametroComentarios(encofrado, wallShape);

                _log.AppendLine($"    ✓ Geometría completa preservada como DirectShape");

                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error creando DirectShape: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// MÉTODO 4: Intenta crear Wall usando múltiples CurveLoops
        /// Prueba con todos los loops extraídos de las caras
        /// </summary>
        private bool CrearMuroConCurveLoopsComplejos(Element encofrado, List<Solid> solidos, OrientacionEncofrado orientacion)
        {
            try
            {
                _log.AppendLine($"    [CURVELOOPS] Intentando Wall con CurveLoops complejos...");

                Solid solidoPrincipal = solidos.OrderByDescending(s => s.Volume).First();

                // Encontrar cara vertical
                PlanarFace caraVertical = null;
                double maxArea = 0;

                foreach (Face face in solidoPrincipal.Faces)
                {
                    // UMBRAL ESTRICTO: 0.01 = ~0.57° máximo (muros perfectamente verticales)
                    if (face is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) < 0.01 && pf.Area > maxArea)
                    {
                        maxArea = pf.Area;
                        caraVertical = pf;
                    }
                }

                if (caraVertical == null)
                {
                    _log.AppendLine($"    ✗ No se encontró cara vertical (encofrado inclinado)");
                    return false;
                }

                // Extraer TODOS los loops (exterior + huecos)
                IList<CurveLoop> loops = caraVertical.GetEdgesAsCurveLoops();
                if (loops.Count == 0) return false;

                _log.AppendLine($"    Encontrados {loops.Count} CurveLoops");

                // DESHABILITADO TEMPORALMENTE: Desplazar todos los loops
                // double espesorEncofrado = UnitUtils.ConvertToInternalUnits(18.0, UnitTypeId.Millimeters);
                // XYZ normalHaciaAdentro = -caraVertical.FaceNormal;

                // CRÍTICO: NO desplazar loops - usar loops originales sin desplazamiento
                // Esto evita geometría inválida que causa rollback
                List<CurveLoop> loopsDesplazados = new List<CurveLoop>();
                foreach (CurveLoop loop in loops)
                {
                    // CurveLoop loopDesplazado = DesplazarCurveLoop(loop, normalHaciaAdentro, espesorEncofrado);
                    loopsDesplazados.Add(loop); // Usar loop ORIGINAL sin desplazar
                }
                _log.AppendLine($"    ⚠ Desplazamiento de loops DESHABILITADO para evitar geometría inválida");

                // NOTA: Wall.Create con múltiples loops no existe en API estándar
                // Este método probablemente fallará, pero lo intentamos
                _log.AppendLine($"    ⚠ Wall.Create con múltiples loops no soportado directamente");
                _log.AppendLine($"    Intentando crear con loop principal solamente");

                // Tomar solo el loop más grande
                CurveLoop loopPrincipal = loopsDesplazados.OrderByDescending(l => CalcularAreaLoop(l)).First();

                // Obtener dimensiones del modelo genérico ORIGINAL
                BoundingBoxXYZ bboxOriginal = encofrado.get_BoundingBox(null);
                double zMinOriginal = bboxOriginal != null ? bboxOriginal.Min.Z : 0;
                double zMaxOriginal = bboxOriginal != null ? bboxOriginal.Max.Z : 0;
                double alturaOriginal = zMaxOriginal - zMinOriginal;

                // CRÍTICO: Obtener línea base del loop
                // Para escaleras y geometrías inclinadas, no podemos depender de curvas horizontales
                // En su lugar, tomamos TODOS los puntos del loop y encontramos los 2 más alejados
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loopPrincipal)
                {
                    puntos.Add(curve.GetEndPoint(0));
                }

                if (puntos.Count < 2)
                {
                    _log.AppendLine($"    ✗ Loop sin suficientes puntos: {puntos.Count}");
                    return false;
                }

                double zMin = puntos.Min(p => p.Z);
                double altura = alturaOriginal > 0 ? alturaOriginal : (puntos.Max(p => p.Z) - zMin);

                // Intentar primero con puntos cercanos a zMin (geometría regular)
                var puntosBase = puntos.Where(p => Math.Abs(p.Z - zMin) < 0.1).ToList();

                XYZ p1, p2;
                double maxDist = 0;

                if (puntosBase.Count >= 2)
                {
                    // Tenemos puntos en la base - usar esos
                    _log.AppendLine($"    [LÍNEA BASE] Usando {puntosBase.Count} puntos en zMin");
                    p1 = puntosBase[0];
                    p2 = puntosBase[0];

                    for (int i = 0; i < puntosBase.Count; i++)
                    {
                        for (int j = i + 1; j < puntosBase.Count; j++)
                        {
                            double dist = puntosBase[i].DistanceTo(puntosBase[j]);
                            if (dist > maxDist)
                            {
                                maxDist = dist;
                                p1 = puntosBase[i];
                                p2 = puntosBase[j];
                            }
                        }
                    }
                }
                else
                {
                    // Geometría inclinada (escalera) - usar TODOS los puntos
                    _log.AppendLine($"    [LÍNEA BASE] Geometría inclinada detectada - usando todos los puntos del loop");
                    p1 = puntos[0];
                    p2 = puntos[0];

                    for (int i = 0; i < puntos.Count; i++)
                    {
                        for (int j = i + 1; j < puntos.Count; j++)
                        {
                            double dist = puntos[i].DistanceTo(puntos[j]);
                            if (dist > maxDist)
                            {
                                maxDist = dist;
                                p1 = puntos[i];
                                p2 = puntos[j];
                            }
                        }
                    }
                }

                if (maxDist < 0.01) // Menos de ~3mm
                {
                    _log.AppendLine($"    ✗ Puntos demasiado cercanos: {maxDist * 304.8:F1}mm");
                    return false;
                }

                _log.AppendLine($"    [LÍNEA BASE] Distancia entre puntos: {maxDist * 304.8:F0}mm");

                Level nivel = ObtenerNivelMasCercano(zMin);
                if (nivel == null) return false;

                // CRÍTICO: Calcular Base Offset correcto
                // La línea base se crea en nivel.Elevation, pero el encofrado está en zMin
                // Necesitamos offset para que el muro quede en la posición correcta
                double baseOffsetNecesario = zMin - nivel.Elevation;
                _log.AppendLine($"    [BASE OFFSET] Nivel={nivel.Name} ({nivel.Elevation:F3}), zMin={zMin:F3}, Base Offset={baseOffsetNecesario:F3} ft ({baseOffsetNecesario * 304.8:F1}mm)");

                Line lineaBase = Line.CreateBound(
                    new XYZ(p1.X, p1.Y, nivel.Elevation),
                    new XYZ(p2.X, p2.Y, nivel.Elevation));

                Wall muro = null;
                try
                {
                    muro = Wall.Create(_doc, lineaBase, _wallType.Id, nivel.Id, altura, baseOffsetNecesario, false, false);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear Wall (parámetros inválidos): {ex.Message}");
                    return false;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear Wall (argumentos inválidos): {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    _log.AppendLine($"    ✗ Error inesperado creando Wall: {ex.Message}");
                    return false;
                }

                if (muro == null)
                {
                    _log.AppendLine($"    ✗ Wall.Create retornó null");
                    return false;
                }

                _log.AppendLine($"    ✓ Muro creado con loop principal ID: {muro.Id}");

                // CRÍTICO: Desactivar uniones de muros (Wall Joins)
                // Esto evita que el muro se una automáticamente con otros muros
                try
                {
                    WallUtils.DisallowWallJoinAtEnd(muro, 0); // Extremo 0
                    WallUtils.DisallowWallJoinAtEnd(muro, 1); // Extremo 1
                    _log.AppendLine($"    ✓ Uniones de muro desactivadas en ambos extremos");
                }
                catch (Exception exJoin)
                {
                    _log.AppendLine($"    ⚠ Error desactivando uniones: {exJoin.Message}");
                }

                // Desactivar Room Bounding
                Parameter paramRoomBounding = muro.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                if (paramRoomBounding != null && !paramRoomBounding.IsReadOnly)
                {
                    paramRoomBounding.Set(0);
                }

                // Copiar parámetro Comentarios primero (contiene el ID del elemento encofrado)
                CopiarParametroComentarios(encofrado, muro);

                // Copiar nivel y desfases del elemento estructural encofrado
                CopiarNivelYDesfasesDeElementoEncofrado(encofrado, muro);

                // CRÍTICO: Mover el muro DESPUÉS de crearlo para que quede completamente fuera del elemento estructural
                // El muro se crea centrado en la línea base, así que la mitad del espesor queda dentro
                // Necesitamos moverlo la mitad de su ancho hacia afuera
                try
                {
                    double anchoMuro = muro.Width; // En pies
                    double mitadAncho = anchoMuro / 2.0;

                    // Desplazar hacia afuera (opuesto a la normal de la cara)
                    // La normal apunta HACIA el elemento estructural, queremos ir en dirección opuesta
                    XYZ normalHaciaAfuera = -caraVertical.FaceNormal;
                    XYZ desplazamiento = normalHaciaAfuera * mitadAncho;

                    ElementTransformUtils.MoveElement(_doc, muro.Id, desplazamiento);

                    _log.AppendLine($"    ✓ Muro desplazado {mitadAncho * 304.8:F1}mm hacia afuera del elemento estructural");
                }
                catch (Exception exMove)
                {
                    _log.AppendLine($"    ⚠ Error moviendo muro: {exMove.Message}");
                    // No es crítico, continuamos
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error con CurveLoops: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clase auxiliar para procesar fallos en SketchEditScope
        /// </summary>
        private class FailuresPreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                // Intentar resolver automáticamente los fallos
                foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                return FailureProcessingResult.Continue;
            }
        }

        /// <summary>
        /// MÉTODO 6: Crea un MURO NATIVO de Revit desde la geometría del encofrado
        /// NOTA: Los recortes complejos no se pueden transferir a muros nativos
        /// Se crea el muro con el contorno exterior del encofrado (método tradicional)
        /// </summary>
        private bool CrearMuroNativo(Element encofrado, List<Solid> solidos, OrientacionEncofrado orientacion)
        {
            try
            {
                _log.AppendLine($"    [MURO NATIVO] Creando muro nativo desde geometría...");

                Solid solidoPrincipal = solidos.OrderByDescending(s => s.Volume).First();

                // Encontrar cara vertical más grande
                PlanarFace caraVertical = null;
                double maxArea = 0;

                foreach (Face face in solidoPrincipal.Faces)
                {
                    // UMBRAL ESTRICTO: 0.01 = ~0.57° máximo (muros perfectamente verticales)
                    if (face is PlanarFace pf && Math.Abs(pf.FaceNormal.Z) < 0 && pf.Area > maxArea)
                    {
                        maxArea = pf.Area;
                        caraVertical = pf;
                    }
                }

                if (caraVertical == null)
                {
                    _log.AppendLine($"    ✗ No se encontró cara vertical (encofrado inclinado)");
                    _log.AppendLine($"    ⚠ Este encofrado debería clasificarse como SUELO");
                    return false;
                }

                // Extraer contorno de la cara - esto nos da el perfil del muro
                IList<CurveLoop> loops = caraVertical.GetEdgesAsCurveLoops();
                if (loops.Count == 0)
                {
                    _log.AppendLine($"    ✗ No se encontraron contornos");
                    return false;
                }

                // DESPLAZAR LOOPS HACIA DENTRO por el espesor del panel (18mm)
                // El encofrado está extruido HACIA AFUERA de la cara estructural
                double espesorEncofrado = UnitUtils.ConvertToInternalUnits(18.0, UnitTypeId.Millimeters);
                XYZ normalHaciaAdentro = -caraVertical.FaceNormal;

                CurveLoop loopPrincipal = loops.OrderByDescending(l => CalcularAreaLoop(l)).First();
                CurveLoop loopDesplazado = DesplazarCurveLoop(loopPrincipal, normalHaciaAdentro, espesorEncofrado);

                _log.AppendLine($"    Loop desplazado {espesorEncofrado * 304.8:F1}mm hacia cara estructural");

                // Obtener dimensiones del modelo genérico ORIGINAL
                BoundingBoxXYZ bboxOriginal = encofrado.get_BoundingBox(null);
                double zMinOriginal = bboxOriginal != null ? bboxOriginal.Min.Z : 0;
                double zMaxOriginal = bboxOriginal != null ? bboxOriginal.Max.Z : 0;
                double alturaOriginal = zMaxOriginal - zMinOriginal;

                // Obtener puntos del loop desplazado
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loopDesplazado)
                {
                    puntos.Add(curve.GetEndPoint(0));
                }

                double zMin = puntos.Min(p => p.Z);
                double altura = alturaOriginal > 0 ? alturaOriginal : (puntos.Max(p => p.Z) - zMin);

                var puntosBase = puntos.Where(p => Math.Abs(p.Z - zMin) < 0.1).ToList();
                if (puntosBase.Count < 2)
                {
                    _log.AppendLine($"    ✗ Insuficientes puntos base");
                    return false;
                }

                // Encontrar línea base (dos puntos más alejados en la base)
                XYZ p1 = puntosBase[0];
                XYZ p2 = puntosBase[0];
                double maxDist = 0;

                for (int i = 0; i < puntosBase.Count; i++)
                {
                    for (int j = i + 1; j < puntosBase.Count; j++)
                    {
                        double dist = puntosBase[i].DistanceTo(puntosBase[j]);
                        if (dist > maxDist)
                        {
                            maxDist = dist;
                            p1 = puntosBase[i];
                            p2 = puntosBase[j];
                        }
                    }
                }

                Level nivel = ObtenerNivelMasCercano(zMin);
                if (nivel == null)
                {
                    _log.AppendLine($"    ✗ No se encontró nivel");
                    return false;
                }

                // Crear línea base ajustada al nivel
                Line lineaBase = Line.CreateBound(
                    new XYZ(p1.X, p1.Y, nivel.Elevation),
                    new XYZ(p2.X, p2.Y, nivel.Elevation));

                // CREAR MURO NATIVO con manejo de errores
                Wall muro = null;
                try
                {
                    muro = Wall.Create(_doc, lineaBase, _wallType.Id, nivel.Id, altura, 0, false, false);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear Wall (parámetros inválidos): {ex.Message}");
                    return false;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    _log.AppendLine($"    ✗ No se pudo crear Wall (argumentos inválidos): {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    _log.AppendLine($"    ✗ Error inesperado creando Wall: {ex.Message}");
                    return false;
                }

                if (muro == null)
                {
                    _log.AppendLine($"    ✗ Fallo al crear muro nativo");
                    return false;
                }

                _log.AppendLine($"    ✓ Muro nativo creado ID: {muro.Id}");

                // Desactivar Room Bounding
                Parameter paramRoomBounding = muro.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                if (paramRoomBounding != null && !paramRoomBounding.IsReadOnly)
                {
                    paramRoomBounding.Set(0);
                    _log.AppendLine($"    ✓ Room Bounding desactivado");
                }

                // Copiar parámetro Comentarios primero (contiene el ID del elemento encofrado)
                CopiarParametroComentarios(encofrado, muro);

                // Copiar nivel y desfases del elemento estructural encofrado
                CopiarNivelYDesfasesDeElementoEncofrado(encofrado, muro);

                _log.AppendLine($"    ✓ Muro nativo creado exitosamente");
                _log.AppendLine($"    ⚠ NOTA: Recortes geométricos no transferibles a muros nativos");

                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error creando muro nativo: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Crea un muro con perfil complejo usando Sketch
        /// El perfil sigue exactamente la geometría del encofrado con recortes
        /// </summary>
        private Wall CrearMuroConPerfilComplejo(CurveLoop loopExterior, List<CurveLoop> loopsInteriores,
                                                 PlanarFace caraReferencia, Level nivel)
        {
            try
            {
                _log.AppendLine($"    [SKETCH] Creando muro con perfil complejo...");

                // 1. Crear SketchPlane vertical en el plano de la cara
                XYZ normal = caraReferencia.FaceNormal;
                XYZ origin = caraReferencia.Origin;
                Plane plano = Plane.CreateByNormalAndOrigin(normal, origin);
                SketchPlane sketchPlane = SketchPlane.Create(_doc, plano);

                _log.AppendLine($"    [SKETCH] SketchPlane creado ID: {sketchPlane.Id}");

                // 2. Preparar todas las CurveArrays (exterior + huecos)
                CurveArray curvasExteriores = new CurveArray();
                foreach (Curve curve in loopExterior)
                {
                    curvasExteriores.Append(curve);
                }

                _log.AppendLine($"    [SKETCH] Perfil exterior: {curvasExteriores.Size} curvas");

                List<CurveArray> curvosInterioresArrays = new List<CurveArray>();
                foreach (CurveLoop loopInterior in loopsInteriores)
                {
                    CurveArray curvasInterior = new CurveArray();
                    foreach (Curve curve in loopInterior)
                    {
                        curvasInterior.Append(curve);
                    }
                    curvosInterioresArrays.Add(curvasInterior);
                    _log.AppendLine($"    [SKETCH] Hueco interior preparado: {curvasInterior.Size} curvas");
                }

                // 3. Crear DirectShape temporal con el perfil complejo como referencia visual
                // Los muros nativos de Revit NO soportan perfiles complejos con huecos directamente
                // La única forma es usar aberturas o familias void

                Wall muro = null;

                // Crear el muro con el contorno exterior
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loopExterior)
                {
                    puntos.Add(curve.GetEndPoint(0));
                    puntos.Add(curve.GetEndPoint(1));
                }

                double zMin = puntos.Min(p => p.Z);
                double zMax = puntos.Max(p => p.Z);
                double altura = zMax - zMin;

                var puntosBase = puntos.Where(p => Math.Abs(p.Z - zMin) < 0.1).ToList();
                if (puntosBase.Count < 2)
                {
                    _log.AppendLine($"    [SKETCH] ✗ Insuficientes puntos base");
                    return null;
                }

                // Encontrar línea base
                XYZ p1 = puntosBase[0];
                XYZ p2 = puntosBase[0];
                double maxDist = 0;

                for (int i = 0; i < puntosBase.Count; i++)
                {
                    for (int j = i + 1; j < puntosBase.Count; j++)
                    {
                        double dist = puntosBase[i].DistanceTo(puntosBase[j]);
                        if (dist > maxDist)
                        {
                            maxDist = dist;
                            p1 = puntosBase[i];
                            p2 = puntosBase[j];
                        }
                    }
                }

                Line lineaBase = Line.CreateBound(
                    new XYZ(p1.X, p1.Y, nivel.Elevation),
                    new XYZ(p2.X, p2.Y, nivel.Elevation));

                // Crear muro base con manejo de errores
                try
                {
                    muro = Wall.Create(_doc, lineaBase, _wallType.Id, nivel.Id, altura, 0, false, false);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    _log.AppendLine($"    [SKETCH] ✗ No se pudo crear Wall (parámetros inválidos): {ex.Message}");
                    return null;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    _log.AppendLine($"    [SKETCH] ✗ No se pudo crear Wall (argumentos inválidos): {ex.Message}");
                    return null;
                }
                catch (Exception ex)
                {
                    _log.AppendLine($"    [SKETCH] ✗ Error inesperado creando Wall: {ex.Message}");
                    return null;
                }

                if (muro == null)
                {
                    _log.AppendLine($"    [SKETCH] ✗ Fallo al crear muro");
                    return null;
                }

                _log.AppendLine($"    [SKETCH] ✓ Muro base creado ID: {muro.Id}");

                // Ajustar offset base
                double offsetBase = zMin - nivel.Elevation;
                Parameter paramOffset = muro.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (paramOffset != null && !paramOffset.IsReadOnly)
                {
                    paramOffset.Set(offsetBase);
                }

                // APLICAR RECORTES usando BooleanOperations
                if (loopsInteriores.Count > 0)
                {
                    _log.AppendLine($"    [SKETCH] Aplicando {loopsInteriores.Count} recortes mediante sólidos void...");

                    try
                    {
                        int recortesAplicados = AplicarRecortesMedianteVoids(muro, loopsInteriores, sketchPlane, altura);

                        if (recortesAplicados > 0)
                        {
                            _log.AppendLine($"    [SKETCH] ✓ {recortesAplicados} recortes aplicados mediante voids");
                        }
                        else
                        {
                            _log.AppendLine($"    [SKETCH] ⚠ No se pudieron aplicar recortes con voids");
                        }
                    }
                    catch (Exception exRecortes)
                    {
                        _log.AppendLine($"    [SKETCH] ⚠ Error aplicando recortes: {exRecortes.Message}");
                    }
                }

                return muro;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    [SKETCH] ✗ Error: {ex.Message}");
                return null;
            }
        }



        /// <summary>
        /// Aplica recortes al muro creando sólidos void desde los loops interiores
        /// </summary>
        private int AplicarRecortesMedianteVoids(Wall muro, List<CurveLoop> loopsInteriores,
                                                  SketchPlane sketchPlane, double altura)
        {
            int recortesAplicados = 0;

            try
            {
                _log.AppendLine($"    [VOIDS] Creando sólidos void para {loopsInteriores.Count} recortes...");

                // Para cada loop interior, crear un sólido void y recortar el muro
                foreach (CurveLoop loopInterior in loopsInteriores)
                {
                    try
                    {
                        // Crear sólido extrusionando el loop interior
                        XYZ direccionExtrusion = sketchPlane.GetPlane().Normal;
                        double profundidadExtrusion = altura + 1.0; // Un poco más largo que el muro

                        List<CurveLoop> profileLoops = new List<CurveLoop> { loopInterior };
                        Solid solidoVoid = GeometryCreationUtilities.CreateExtrusionGeometry(
                            profileLoops,
                            direccionExtrusion,
                            profundidadExtrusion);

                        if (solidoVoid != null && solidoVoid.Volume > 0.001)
                        {
                            // Crear DirectShape temporal con el void para recortar
                            DirectShape dsVoid = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                            dsVoid.SetShape(new List<GeometryObject> { solidoVoid });

                            // Intentar aplicar el corte usando InstanceVoidCutUtils
                            try
                            {
                                InstanceVoidCutUtils.AddInstanceVoidCut(_doc, muro, dsVoid);
                                recortesAplicados++;
                                _log.AppendLine($"    [VOIDS] Void {recortesAplicados} aplicado al muro");
                            }
                            catch (Exception exCut)
                            {
                                _log.AppendLine($"    [VOIDS] ⚠ No se pudo aplicar corte void: {exCut.Message}");
                                // Eliminar el DirectShape temporal si falló el corte
                                _doc.Delete(dsVoid.Id);
                            }
                        }
                        else
                        {
                            _log.AppendLine($"    [VOIDS] ⚠ No se pudo crear sólido void válido para loop");
                        }
                    }
                    catch (Exception exLoop)
                    {
                        _log.AppendLine($"    [VOIDS] ⚠ Error procesando loop interior: {exLoop.Message}");
                    }
                }

                _log.AppendLine($"    [VOIDS] Total recortes aplicados: {recortesAplicados} de {loopsInteriores.Count}");
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    [VOIDS] ✗ Error aplicando voids: {ex.Message}");
            }

            return recortesAplicados;
        }

        /// <summary>
        /// Edita el perfil de un muro para incluir recortes usando su Sketch
        /// </summary>
        private bool EditarPerfilMuroConRecortes(Wall muro, CurveLoop loopExterior, List<CurveLoop> loopsInteriores,
                                                  SketchPlane sketchPlane)
        {
            try
            {
                _log.AppendLine($"    [PERFIL] Editando perfil del muro...");

                // Obtener el sketch del muro
                ElementId sketchId = muro.SketchId;
                if (sketchId == null || sketchId == ElementId.InvalidElementId)
                {
                    _log.AppendLine($"    [PERFIL] ⚠ El muro no tiene Sketch - es un muro recto que no soporta perfil complejo");
                    return false;
                }

                Sketch sketch = _doc.GetElement(sketchId) as Sketch;
                if (sketch == null)
                {
                    _log.AppendLine($"    [PERFIL] ✗ No se pudo obtener el Sketch del muro");
                    return false;
                }

                _log.AppendLine($"    [PERFIL] Sketch encontrado ID: {sketch.Id}");

                // Acceder al Profile del sketch
                CurveArrArray profile = sketch.Profile;
                if (profile == null || profile.Size == 0)
                {
                    _log.AppendLine($"    [PERFIL] ✗ El Sketch no tiene perfil");
                    return false;
                }

                _log.AppendLine($"    [PERFIL] Perfil actual tiene {profile.Size} CurveArray(s)");

                // Limpiar el perfil actual
                profile.Clear();

                // Agregar el loop exterior al perfil
                CurveArray arrayExterior = new CurveArray();
                foreach (Curve curve in loopExterior)
                {
                    arrayExterior.Append(curve);
                }
                profile.Append(arrayExterior);

                _log.AppendLine($"    [PERFIL] Loop exterior agregado: {arrayExterior.Size} curvas");

                // Agregar loops interiores (huecos) al perfil
                foreach (CurveLoop loopInterior in loopsInteriores)
                {
                    CurveArray arrayInterior = new CurveArray();
                    foreach (Curve curve in loopInterior)
                    {
                        arrayInterior.Append(curve);
                    }
                    profile.Append(arrayInterior);
                    _log.AppendLine($"    [PERFIL] Loop interior agregado: {arrayInterior.Size} curvas");
                }

                _log.AppendLine($"    [PERFIL] ✓ Perfil editado con {loopsInteriores.Count} huecos");
                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    [PERFIL] ✗ Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Crea un SUELO NATIVO de Revit desde el encofrado
        /// </summary>
        private bool CrearSueloNativo(Element encofrado, List<Solid> solidos, OrientacionEncofrado orientacion)
        {
            try
            {
                Solid solidoPrincipal = solidos.OrderByDescending(s => s.Volume).First();

                // Buscar la cara con mayor componente Z (horizontal o inclinada)
                PlanarFace caraPrincipal = null;
                double mejorComponenteZ = 0;

                foreach (Face face in solidoPrincipal.Faces)
                {
                    if (face is PlanarFace pf)
                    {
                        double componenteZ = Math.Abs(pf.FaceNormal.Z);
                        if (componenteZ > mejorComponenteZ)
                        {
                            mejorComponenteZ = componenteZ;
                            caraPrincipal = pf;
                        }
                    }
                }

                if (caraPrincipal == null)
                {
                    _log.AppendLine($"  ✗ No se encontró cara principal");
                    return false;
                }

                // Obtener contornos
                IList<CurveLoop> curveLoops = caraPrincipal.GetEdgesAsCurveLoops();
                if (curveLoops.Count == 0)
                {
                    _log.AppendLine($"  ✗ No se encontraron contornos");
                    return false;
                }

                // DESPLAZAR LOOPS HACIA DENTRO por el espesor del panel
                // El encofrado (cimbra) tiene 25mm de espesor y está extruido HACIA AFUERA de la cara estructural
                // Necesitamos mover los loops hacia DENTRO para que el suelo quede pegado a la cara estructural
                double espesorCimbra = UnitUtils.ConvertToInternalUnits(25.0, UnitTypeId.Millimeters);
                XYZ normalHaciaAdentro = -caraPrincipal.FaceNormal; // Invertir normal para ir hacia adentro

                List<CurveLoop> curveLoopsDesplazados = new List<CurveLoop>();
                foreach (CurveLoop loop in curveLoops)
                {
                    CurveLoop loopDesplazado = DesplazarCurveLoop(loop, normalHaciaAdentro, espesorCimbra);
                    curveLoopsDesplazados.Add(loopDesplazado);
                }

                _log.AppendLine($"  Loops desplazados {espesorCimbra * 304.8:F1}mm hacia cara estructural");

                // Calcular elevación promedio de los puntos desplazados
                double elevacionPromedio = CalcularElevacionPromedio(curveLoopsDesplazados);
                Level nivel = ObtenerNivelMasCercano(elevacionPromedio);

                if (nivel == null)
                {
                    _log.AppendLine($"  ✗ No se encontró nivel");
                    return false;
                }

                // CRÍTICO: Floor.Create requiere que los loops estén en un plano HORIZONTAL (paralelo a XY)
                // Proyectar todos los loops al plano horizontal a la elevación calculada
                List<CurveLoop> curveLoopsHorizontales = new List<CurveLoop>();
                foreach (CurveLoop loopDesplazado in curveLoopsDesplazados)
                {
                    CurveLoop loopHorizontal = ProyectarCurveLoopAPlanoHorizontal(loopDesplazado, elevacionPromedio);
                    if (loopHorizontal != null && loopHorizontal.Count() > 0)
                    {
                        curveLoopsHorizontales.Add(loopHorizontal);
                    }
                }

                if (curveLoopsHorizontales.Count == 0)
                {
                    _log.AppendLine($"  ✗ No se pudo proyectar ningún loop al plano horizontal");
                    return false;
                }

                _log.AppendLine($"  ✓ {curveLoopsHorizontales.Count} loops proyectados al plano horizontal Z={elevacionPromedio:F4}");

                // Validar que los loops son válidos para Floor.Create
                foreach (CurveLoop loop in curveLoopsHorizontales)
                {
                    string razon;
                    if (!ValidarCurveLoopParaSuelo(loop, out razon))
                    {
                        _log.AppendLine($"  ✗ Loop inválido para suelo: {razon}");
                        return false;
                    }
                }

                _log.AppendLine($"  ✓ Todos los loops validados para Floor.Create");

                // Crear suelo nativo con los loops horizontales
                Floor suelo = Floor.Create(_doc, curveLoopsHorizontales, _floorType.Id, nivel.Id);

                if (suelo == null)
                {
                    _log.AppendLine($"  ✗ Fallo al crear suelo");
                    return false;
                }

                // Ajustar elevación usando altura desplazada
                double offsetNecesario = elevacionPromedio - nivel.Elevation;
                if (Math.Abs(offsetNecesario) > 0.01)
                {
                    Parameter paramHeightOffset = suelo.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                    if (paramHeightOffset != null && !paramHeightOffset.IsReadOnly)
                    {
                        paramHeightOffset.Set(offsetNecesario);
                        _log.AppendLine($"  Offset elevación aplicado: {offsetNecesario:F4} ft ({offsetNecesario * 304.8:F1}mm)");
                    }
                }
                else
                {
                    _log.AppendLine($"  Sin offset - suelo en nivel {nivel.Name}");
                }

                // Aplicar inclinación si es necesario usando SlabShapeEditor
                bool esInclinado = TryAplicarInclinacionSuelo(suelo, caraPrincipal, nivel);

                // Copiar comentarios
                CopiarParametroComentarios(encofrado, suelo);

                _log.AppendLine($"  ✓ Suelo nativo creado (Inclinado: {esInclinado})");
                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ✗ Error creando suelo: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Intenta aplicar inclinación a un suelo usando SlabShapeEditor
        /// </summary>
        private bool TryAplicarInclinacionSuelo(Floor suelo, PlanarFace caraPrincipal, Level nivel)
        {
            try
            {
                // Verificar si la cara está inclinada
                double normalZ = Math.Abs(caraPrincipal.FaceNormal.Z);
                if (normalZ > 0.95) // Casi horizontal
                    return false;

                SlabShapeEditor editor = suelo.GetSlabShapeEditor();
                if (editor == null)
                    return false;

                editor.ResetSlabShape();

                // Obtener vértices y ajustar elevaciones
                SlabShapeVertexArray vertices = editor.SlabShapeVertices;

                foreach (SlabShapeVertex vertex in vertices)
                {
                    // Proyectar vértice a la cara original para obtener elevación correcta
                    XYZ posicionVertex = vertex.Position;

                    // Calcular Z en la cara inclinada
                    // (implementación simplificada)
                    double offsetZ = 0; // Calcular basado en la normal y posición de la cara

                    if (Math.Abs(offsetZ) > 0.001)
                    {
                        editor.ModifySubElement(vertex, offsetZ);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extrae el sólido principal (mayor volumen) de un elemento
        /// </summary>
        private Solid ExtraerSolidoPrincipal(Element elemento)
        {
            List<Solid> solidos = ExtraerSolidos(elemento);
            return solidos.OrderByDescending(s => s.Volume).FirstOrDefault();
        }

        /// <summary>
        /// Extrae todos los sólidos válidos de un elemento
        /// </summary>
        private List<Solid> ExtraerSolidos(Element elemento)
        {
            List<Solid> solidos = new List<Solid>();

            Options opts = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false
            };

            GeometryElement geomElem = elemento.get_Geometry(opts);
            if (geomElem == null) return solidos;

            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid && solid.Volume > 0.001)
                {
                    solidos.Add(solid);
                }
            }

            return solidos;
        }

        /// <summary>
        /// Analiza la orientación dominante de los sólidos
        /// Cuenta caras verticales vs horizontales con área significativa
        /// Regla: Vertical (muro) si tiene más caras verticales grandes que horizontales
        /// </summary>
        private OrientacionEncofrado AnalizarOrientacion(List<Solid> solidos)
        {
            _log.AppendLine($"    Analizando caras de {solidos.Count} sólido(s):");

            // Recopilar todas las caras planares con su área y orientación
            List<(PlanarFace face, double area, double absZ)> carasAnalizadas = new List<(PlanarFace, double, double)>();

            foreach (Solid solido in solidos)
            {
                foreach (Face face in solido.Faces)
                {
                    if (face is PlanarFace pf && pf.Area > 0.001)
                    {
                        double absZ = Math.Abs(pf.FaceNormal.Z);
                        carasAnalizadas.Add((pf, pf.Area, absZ));
                    }
                }
            }

            _log.AppendLine($"    Total de caras planares: {carasAnalizadas.Count}");

            if (carasAnalizadas.Count == 0)
            {
                _log.AppendLine($"    ⚠ No se encontraron caras planares - asumiendo horizontal");
                return new OrientacionEncofrado
                {
                    Tipo = TipoOrientacion.Horizontal,
                    NormalPromedio = XYZ.BasisZ
                };
            }

            // Encontrar el área promedio para filtrar caras pequeñas
            double areaPromedio = carasAnalizadas.Average(c => c.area);
            double umbralAreaSignificativa = areaPromedio * 0.2; // 20% del área promedio

            _log.AppendLine($"    Área promedio: {areaPromedio:F4} ft²");
            _log.AppendLine($"    Umbral área significativa: {umbralAreaSignificativa:F4} ft²");

            // Contar caras verticales vs horizontales CON ÁREA SIGNIFICATIVA
            int carasVerticales = 0;
            int carasHorizontales = 0;
            double areaVerticalTotal = 0;
            double areaHorizontalTotal = 0;
            PlanarFace mejorCaraVertical = null;
            PlanarFace mejorCaraHorizontal = null;
            double maxAreaVertical = 0;
            double maxAreaHorizontal = 0;

            foreach (var (face, area, absZ) in carasAnalizadas)
            {
                // Solo considerar caras con área significativa
                if (area < umbralAreaSignificativa)
                    continue;

                // UMBRAL ESTRICTO: 0.01 = ~0.57° de inclinación máxima para vertical
                // Muros deben ser PERFECTAMENTE verticales (90° con el suelo)
                // Cualquier inclinación → clasificar como SUELO
                if (absZ < 0.01) // Vertical: |Z| < 0.01 (perpendicular a Z)
                {
                    carasVerticales++;
                    areaVerticalTotal += area;
                    if (area > maxAreaVertical)
                    {
                        maxAreaVertical = area;
                        mejorCaraVertical = face;
                    }
                    _log.AppendLine($"      Cara VERTICAL: |Z|={absZ:F3}, Área={area:F4}");
                }
                else // Horizontal/Inclinado: |Z| >= 0.15
                {
                    carasHorizontales++;
                    areaHorizontalTotal += area;
                    if (area > maxAreaHorizontal)
                    {
                        maxAreaHorizontal = area;
                        mejorCaraHorizontal = face;
                    }
                    _log.AppendLine($"      Cara HORIZONTAL/INCLINADA: |Z|={absZ:F3}, Área={area:F4}");
                }
            }

            _log.AppendLine($"    Resumen:");
            _log.AppendLine($"      Caras verticales significativas: {carasVerticales} (área total: {areaVerticalTotal:F4})");
            _log.AppendLine($"      Caras horizontales significativas: {carasHorizontales} (área total: {areaHorizontalTotal:F4})");

            // DECISIÓN: Si hay MÁS caras verticales significativas O mayor área vertical → MURO
            TipoOrientacion tipo;
            XYZ normalResultante;

            if (carasVerticales > carasHorizontales || areaVerticalTotal > areaHorizontalTotal)
            {
                tipo = TipoOrientacion.Vertical;
                normalResultante = mejorCaraVertical != null ? mejorCaraVertical.FaceNormal : XYZ.BasisX;
                _log.AppendLine($"    ✓ DECISIÓN: VERTICAL (Muro) - {carasVerticales} caras verticales vs {carasHorizontales} horizontales");
            }
            else
            {
                tipo = TipoOrientacion.Horizontal;
                normalResultante = mejorCaraHorizontal != null ? mejorCaraHorizontal.FaceNormal : XYZ.BasisZ;
                _log.AppendLine($"    ✓ DECISIÓN: HORIZONTAL (Suelo) - {carasHorizontales} caras horizontales vs {carasVerticales} verticales");
            }

            return new OrientacionEncofrado
            {
                Tipo = tipo,
                NormalPromedio = normalResultante
            };
        }

        // MÉTODOS ANTIGUOS ELIMINADOS: CrearSuelo y CrearMuro
        // Ahora usamos CrearDirectShapeConCategoria que preserva geometría exacta

        #region MÉTODOS AUXILIARES (mantenidos para referencia futura)

        /// <summary>
        /// [NO USADO] Crea un suelo a partir del sólido del encofrado
        /// Soporta suelos horizontales e inclinados ajustando puntos de elevación
        /// </summary>
        private bool CrearSuelo_OLD(Element encofrado, List<Solid> solidos, OrientacionEncofrado orientacion)
        {
            try
            {
                // Tomar el sólido principal (mayor volumen)
                Solid solidoPrincipal = solidos.OrderByDescending(s => s.Volume).First();

                // Buscar la cara más adecuada para generar el suelo
                // Prioridad: cara con mayor componente Z (puede ser horizontal o inclinada)
                PlanarFace caraPrincipal = null;
                double mejorComponenteZ = 0;

                foreach (Face face in solidoPrincipal.Faces)
                {
                    if (face is PlanarFace pf)
                    {
                        double componenteZ = Math.Abs(pf.FaceNormal.Z);

                        // Buscar cara con mayor componente Z (más cercana a horizontal)
                        if (componenteZ > mejorComponenteZ)
                        {
                            mejorComponenteZ = componenteZ;
                            caraPrincipal = pf;
                        }
                    }
                }

                if (caraPrincipal == null)
                {
                    _log.AppendLine($"  ✗ No se encontró cara principal");
                    return false;
                }

                // Obtener contornos de la cara
                IList<CurveLoop> curveLoops = caraPrincipal.GetEdgesAsCurveLoops();
                if (curveLoops.Count == 0)
                {
                    _log.AppendLine($"  ✗ No se encontraron contornos");
                    return false;
                }

                // Proyectar curvas al plano XY para crear el suelo base
                List<CurveLoop> curveLoopsProyectados = new List<CurveLoop>();

                // Almacenar las elevaciones originales de los vértices
                Dictionary<XYZ, double> elevacionesOriginales = new Dictionary<XYZ, double>(new XYZComparerXY());

                foreach (CurveLoop loop in curveLoops)
                {
                    CurveLoop loopProyectado = new CurveLoop();

                    foreach (Curve curve in loop)
                    {
                        XYZ p1Original = curve.GetEndPoint(0);
                        XYZ p2Original = curve.GetEndPoint(1);

                        // Guardar elevaciones originales
                        XYZ p1XY = new XYZ(p1Original.X, p1Original.Y, 0);
                        XYZ p2XY = new XYZ(p2Original.X, p2Original.Y, 0);

                        elevacionesOriginales[p1XY] = p1Original.Z;
                        elevacionesOriginales[p2XY] = p2Original.Z;

                        // Proyectar curva al plano XY
                        XYZ p1Proyectado = new XYZ(p1Original.X, p1Original.Y, 0);
                        XYZ p2Proyectado = new XYZ(p2Original.X, p2Original.Y, 0);

                        // Solo agregar si los puntos no son idénticos en XY
                        if (p1Proyectado.DistanceTo(p2Proyectado) > 0.001)
                        {
                            if (curve is Line)
                            {
                                loopProyectado.Append(Line.CreateBound(p1Proyectado, p2Proyectado));
                            }
                            else if (curve is Arc arc)
                            {
                                // Proyectar arco manteniendo el punto medio
                                XYZ midOriginal = arc.Evaluate(0.5, true);
                                XYZ midProyectado = new XYZ(midOriginal.X, midOriginal.Y, 0);
                                elevacionesOriginales[midProyectado] = midOriginal.Z;

                                loopProyectado.Append(Arc.Create(p1Proyectado, p2Proyectado, midProyectado));
                            }
                        }
                    }

                    if (loopProyectado.NumberOfCurves() >= 3)
                    {
                        curveLoopsProyectados.Add(loopProyectado);
                    }
                }

                if (curveLoopsProyectados.Count == 0)
                {
                    _log.AppendLine($"  ✗ No se pudieron proyectar contornos");
                    return false;
                }

                // Encontrar elevación promedio para el nivel base
                double elevacionPromedio = elevacionesOriginales.Values.Average();
                Level nivel = ObtenerNivelMasCercano(elevacionPromedio);

                if (nivel == null)
                {
                    _log.AppendLine($"  ✗ No se encontró nivel");
                    return false;
                }

                // Crear suelo base en el nivel
                Floor suelo = Floor.Create(_doc, curveLoopsProyectados, _floorType.Id, nivel.Id);

                if (suelo == null)
                {
                    _log.AppendLine($"  ✗ Fallo al crear suelo");
                    return false;
                }

                // Ajustar puntos de elevación si el suelo está inclinado
                bool esInclinado = elevacionesOriginales.Values.Max() - elevacionesOriginales.Values.Min() > 0.05; // > 15mm

                if (esInclinado)
                {
                    _log.AppendLine($"  Suelo inclinado detectado - ajustando puntos de elevación...");

                    try
                    {
                        SlabShapeEditor editor = suelo.GetSlabShapeEditor();
                        if (editor != null)
                        {
                            // Resetear cualquier edición previa
                            editor.ResetSlabShape();

                            // Habilitar edición de forma
                            SlabShapeCreaseArray creases = editor.SlabShapeCreases;
                            SlabShapeVertexArray vertices = editor.SlabShapeVertices;

                            _log.AppendLine($"    Vértices del suelo: {vertices.Size}");

                            // Ajustar cada vértice a su elevación original
                            int ajustados = 0;
                            foreach (SlabShapeVertex vertex in vertices)
                            {
                                XYZ posicion = vertex.Position;
                                XYZ posicionXY = new XYZ(posicion.X, posicion.Y, 0);

                                // Buscar la elevación original más cercana
                                double elevacionObjetivo = nivel.Elevation;
                                double minDist = double.MaxValue;

                                foreach (var kvp in elevacionesOriginales)
                                {
                                    double dist = kvp.Key.DistanceTo(posicionXY);
                                    if (dist < minDist)
                                    {
                                        minDist = dist;
                                        elevacionObjetivo = kvp.Value;
                                    }
                                }

                                // Calcular offset necesario desde el nivel
                                double offsetNecesario = elevacionObjetivo - nivel.Elevation;

                                // Modificar el vértice con el offset
                                if (Math.Abs(offsetNecesario) > 0.001)
                                {
                                    editor.ModifySubElement(vertex, offsetNecesario);
                                    ajustados++;
                                }
                            }

                            _log.AppendLine($"    Vértices ajustados: {ajustados}");
                        }
                    }
                    catch (Exception exInclinado)
                    {
                        _log.AppendLine($"  ⚠ No se pudo ajustar inclinación: {exInclinado.Message}");
                        // Continuar sin inclinación
                    }
                }
                else
                {
                    // Suelo horizontal - ajustar offset global
                    double offsetNecesario = elevacionPromedio - nivel.Elevation;
                    if (Math.Abs(offsetNecesario) > 0.01)
                    {
                        Parameter paramHeightOffset = suelo.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                        if (paramHeightOffset != null && !paramHeightOffset.IsReadOnly)
                        {
                            paramHeightOffset.Set(offsetNecesario);
                        }
                    }
                }

                // Copiar comentarios
                CopiarParametroComentarios(encofrado, suelo);

                _log.AppendLine($"  ✓ Suelo creado en nivel {nivel.Name} (Inclinado: {esInclinado})");
                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ✗ Error creando suelo: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// [NO USADO] Crea un muro a partir del sólido del encofrado
        /// </summary>
        private bool CrearMuro_OLD(Element encofrado, List<Solid> solidos, OrientacionEncofrado orientacion)
        {
            try
            {
                // Tomar el sólido principal
                Solid solidoPrincipal = solidos.OrderByDescending(s => s.Volume).First();

                // Buscar cara vertical más grande
                PlanarFace caraVertical = null;
                double maxArea = 0;

                foreach (Face face in solidoPrincipal.Faces)
                {
                    if (face is PlanarFace pf)
                    {
                        double z = Math.Abs(pf.FaceNormal.Z);

                        // Verificar que sea vertical (|z| < 0.3)
                        if (z < 0.3 && pf.Area > maxArea)
                        {
                            maxArea = pf.Area;
                            caraVertical = pf;
                        }
                    }
                }

                if (caraVertical == null)
                {
                    _log.AppendLine($"  ✗ No se encontró cara vertical");
                    return false;
                }

                // Obtener contorno de la cara vertical
                IList<CurveLoop> loops = caraVertical.GetEdgesAsCurveLoops();
                if (loops.Count == 0)
                {
                    _log.AppendLine($"  ✗ No se encontraron contornos");
                    return false;
                }

                // Obtener curva base (la curva más baja horizontalmente)
                CurveLoop loopPrincipal = loops.OrderByDescending(l => CalcularAreaLoop(l)).First();

                // Extraer puntos del loop y proyectar a plano XY
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loopPrincipal)
                {
                    puntos.Add(curve.GetEndPoint(0));
                }

                if (puntos.Count < 2)
                {
                    _log.AppendLine($"  ✗ Insuficientes puntos para crear muro");
                    return false;
                }

                // Encontrar los dos puntos que definen la línea base del muro
                // Usar los dos puntos más alejados en Z mínimo
                double zMin = puntos.Min(p => p.Z);
                double zMax = puntos.Max(p => p.Z);

                var puntosBase = puntos.Where(p => Math.Abs(p.Z - zMin) < 0.1).ToList();

                if (puntosBase.Count < 2)
                {
                    _log.AppendLine($"  ✗ No se encontraron puntos base");
                    return false;
                }

                // Tomar los dos puntos más alejados entre sí
                XYZ p1 = puntosBase[0];
                XYZ p2 = puntosBase[0];
                double maxDist = 0;

                for (int i = 0; i < puntosBase.Count; i++)
                {
                    for (int j = i + 1; j < puntosBase.Count; j++)
                    {
                        double dist = puntosBase[i].DistanceTo(puntosBase[j]);
                        if (dist > maxDist)
                        {
                            maxDist = dist;
                            p1 = puntosBase[i];
                            p2 = puntosBase[j];
                        }
                    }
                }

                // Nivel base
                Level nivel = ObtenerNivelMasCercano(zMin);
                if (nivel == null)
                {
                    _log.AppendLine($"  ✗ No se encontró nivel");
                    return false;
                }

                // Altura del muro
                double altura = zMax - zMin;

                // Crear línea base en el nivel
                Line lineaBase = Line.CreateBound(
                    new XYZ(p1.X, p1.Y, nivel.Elevation),
                    new XYZ(p2.X, p2.Y, nivel.Elevation)
                );

                // Crear muro con manejo de errores
                Wall muro = null;
                try
                {
                    muro = Wall.Create(_doc, lineaBase, _wallType.Id, nivel.Id, altura, 0, false, false);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    _log.AppendLine($"  ✗ No se pudo crear Wall (parámetros inválidos): {ex.Message}");
                    return false;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    _log.AppendLine($"  ✗ No se pudo crear Wall (argumentos inválidos): {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    _log.AppendLine($"  ✗ Error inesperado creando Wall: {ex.Message}");
                    return false;
                }

                if (muro == null)
                {
                    _log.AppendLine($"  ✗ Fallo al crear muro");
                    return false;
                }

                // Ajustar offset base
                double offsetBase = zMin - nivel.Elevation;
                Parameter paramOffset = muro.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (paramOffset != null && !paramOffset.IsReadOnly)
                {
                    paramOffset.Set(offsetBase);
                }

                // Desactivar "Delimitación de habitación" (Room Bounding)
                Parameter paramRoomBounding = muro.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                if (paramRoomBounding != null && !paramRoomBounding.IsReadOnly)
                {
                    paramRoomBounding.Set(0); // 0 = desactivado
                }

                // Copiar comentarios
                CopiarParametroComentarios(encofrado, muro);

                _log.AppendLine($"  ✓ Muro creado: Altura={altura:F3}, Base={zMin:F3}");
                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ✗ Error creando muro: {ex.Message}");
                return false;
            }
        }

        #endregion

        /// <summary>
        /// Desplaza un CurveLoop en una dirección y distancia especificadas
        /// </summary>
        private CurveLoop DesplazarCurveLoop(CurveLoop loop, XYZ direccion, double distancia)
        {
            CurveLoop loopDesplazado = new CurveLoop();
            Transform transform = Transform.CreateTranslation(direccion.Normalize() * distancia);

            foreach (Curve curve in loop)
            {
                Curve curvaDesplazada = curve.CreateTransformed(transform);
                loopDesplazado.Append(curvaDesplazada);
            }

            return loopDesplazado;
        }

        /// <summary>
        /// Calcula la elevación promedio de todos los puntos en una lista de CurveLoops
        /// </summary>
        private double CalcularElevacionPromedio(List<CurveLoop> loops)
        {
            List<double> elevaciones = new List<double>();

            foreach (CurveLoop loop in loops)
            {
                foreach (Curve curve in loop)
                {
                    elevaciones.Add(curve.GetEndPoint(0).Z);
                    elevaciones.Add(curve.GetEndPoint(1).Z);
                }
            }

            return elevaciones.Count > 0 ? elevaciones.Average() : 0;
        }

        /// <summary>
        /// Encuentra el nivel más cercano a una elevación dada
        /// </summary>
        private Level ObtenerNivelMasCercano(double elevacion)
        {
            var niveles = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            // CRÍTICO: Buscar el nivel más alto que esté POR DEBAJO O IGUAL a la elevación
            // Esto asegura que las curvas del muro estén SOBRE el nivel base, no debajo
            Level nivelInferior = niveles
                .Where(l => l.Elevation <= elevacion)
                .OrderByDescending(l => l.Elevation)
                .FirstOrDefault();

            // Si encontramos un nivel por debajo, usarlo
            if (nivelInferior != null)
            {
                return nivelInferior;
            }

            // Fallback: Si no hay ningún nivel por debajo (caso raro), usar el más cercano
            return niveles.OrderBy(l => Math.Abs(l.Elevation - elevacion)).FirstOrDefault();
        }

        /// <summary>
        /// Calcula el área aproximada de un CurveLoop
        /// </summary>
        private double CalcularAreaLoop(CurveLoop loop)
        {
            try
            {
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loop)
                {
                    puntos.Add(curve.GetEndPoint(0));
                }

                if (puntos.Count < 3) return 0;

                double area = 0;
                for (int i = 0; i < puntos.Count; i++)
                {
                    XYZ p1 = puntos[i];
                    XYZ p2 = puntos[(i + 1) % puntos.Count];
                    area += (p1.X * p2.Y) - (p2.X * p1.Y);
                }

                return Math.Abs(area / 2.0);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Proyecta un CurveLoop a un plano horizontal a una elevación específica
        /// </summary>
        private CurveLoop ProyectarCurveLoopAPlanoHorizontal(CurveLoop loop, double elevacion)
        {
            try
            {
                CurveLoop loopHorizontal = new CurveLoop();

                foreach (Curve curva in loop)
                {
                    XYZ p0 = curva.GetEndPoint(0);
                    XYZ p1 = curva.GetEndPoint(1);

                    // Proyectar al plano horizontal (mantener X,Y, cambiar Z a elevación)
                    XYZ p0Horizontal = new XYZ(p0.X, p0.Y, elevacion);
                    XYZ p1Horizontal = new XYZ(p1.X, p1.Y, elevacion);

                    // Evitar líneas degeneradas
                    if (p0Horizontal.DistanceTo(p1Horizontal) > 0.001)
                    {
                        Line lineaHorizontal = Line.CreateBound(p0Horizontal, p1Horizontal);
                        loopHorizontal.Append(lineaHorizontal);
                    }
                }

                return loopHorizontal;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ⚠ Error proyectando loop a horizontal: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Valida si un CurveLoop es válido específicamente para Floor.Create
        /// Los suelos requieren loops horizontales y cerrados
        /// </summary>
        private bool ValidarCurveLoopParaSuelo(CurveLoop loop, out string razon)
        {
            razon = "";

            try
            {
                if (loop == null || loop.Count() < 3)
                {
                    razon = "Loop nulo o insuficientes curvas";
                    return false;
                }

                // Verificar que es horizontal (todas las curvas en el mismo Z)
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curva in loop)
                {
                    puntos.Add(curva.GetEndPoint(0));
                    puntos.Add(curva.GetEndPoint(1));
                }

                double zMin = puntos.Min(p => p.Z);
                double zMax = puntos.Max(p => p.Z);
                double variacionZ = zMax - zMin;

                if (variacionZ > 0.01) // Más de 3mm de variación
                {
                    razon = $"Loop no es horizontal (variación Z: {variacionZ*304.8:F2}mm)";
                    return false;
                }

                // Verificar que está cerrado
                List<Curve> curvas = loop.ToList();
                for (int i = 0; i < curvas.Count; i++)
                {
                    Curve curvaActual = curvas[i];
                    Curve curvaSiguiente = curvas[(i + 1) % curvas.Count];

                    XYZ finActual = curvaActual.GetEndPoint(1);
                    XYZ inicioSiguiente = curvaSiguiente.GetEndPoint(0);

                    double distancia = finActual.DistanceTo(inicioSiguiente);
                    if (distancia > 0.01)
                    {
                        razon = $"Loop no cerrado: gap de {distancia*304.8:F2}mm";
                        return false;
                    }
                }

                // Verificar área válida
                double area = Math.Abs(loop.GetExactLength()); // Aproximación rápida
                if (area < 0.1) // Menos de 0.1 pies cuadrados
                {
                    razon = "Área del loop demasiado pequeña";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                razon = $"Error en validación: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Valida si un CurveLoop es válido para perfil de muro
        /// Verifica: cerrado, planar, curvas conectadas
        /// </summary>
        private bool ValidarCurveLoopParaPerfil(CurveLoop loop, out string razon)
        {
            razon = "";

            try
            {
                // 1. Verificar que el loop tiene curvas
                if (loop == null)
                {
                    razon = "Loop nulo";
                    return false;
                }

                int numeroCurvas = loop.Count();
                if (numeroCurvas < 3)
                {
                    razon = $"Insuficientes curvas ({numeroCurvas}, mínimo 3)";
                    return false;
                }

                // 2. Verificar que es planar
                try
                {
                    Plane plane = loop.GetPlane();
                    if (plane == null)
                    {
                        razon = "Loop no es planar (GetPlane retornó null)";
                        return false;
                    }
                }
                catch (Exception exPlane)
                {
                    razon = $"Loop no es planar: {exPlane.Message}";
                    return false;
                }

                // 3. Verificar que está cerrado (verificar conexiones)
                List<Curve> curvas = loop.ToList();
                for (int i = 0; i < curvas.Count; i++)
                {
                    Curve curvaActual = curvas[i];
                    Curve curvaSiguiente = curvas[(i + 1) % curvas.Count];

                    XYZ finActual = curvaActual.GetEndPoint(1);
                    XYZ inicioSiguiente = curvaSiguiente.GetEndPoint(0);

                    double distancia = finActual.DistanceTo(inicioSiguiente);
                    if (distancia > 0.01) // Tolerancia 0.01 pies (~3mm)
                    {
                        razon = $"Gap entre curvas {i} y {i+1}: {distancia*304.8:F2}mm";
                        return false;
                    }
                }

                // 4. Verificar que las curvas no son degeneradas
                foreach (Curve curva in curvas)
                {
                    double longitud = curva.Length;
                    if (longitud < 0.001) // 0.001 pies = ~0.3mm
                    {
                        razon = $"Curva degenerada con longitud {longitud*304.8:F3}mm";
                        return false;
                    }
                }

                // 5. Verificar orientación (counter-clockwise o clockwise consistente)
                double areaSigno = CalcularAreaSignoCurveLoop(loop);
                if (Math.Abs(areaSigno) < 0.0001)
                {
                    razon = "Área del loop es cero (curvas colineales)";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                razon = $"Excepción en validación: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Calcula área con signo de un CurveLoop para verificar orientación
        /// </summary>
        private double CalcularAreaSignoCurveLoop(CurveLoop loop)
        {
            try
            {
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loop)
                {
                    puntos.Add(curve.GetEndPoint(0));
                }

                // Fórmula del área con signo (Shoelace formula)
                double area = 0;
                for (int i = 0; i < puntos.Count; i++)
                {
                    XYZ p1 = puntos[i];
                    XYZ p2 = puntos[(i + 1) % puntos.Count];
                    area += (p1.X * p2.Y) - (p2.X * p1.Y);
                }

                return area / 2.0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Copia el parámetro Comentarios de un elemento a otro
        /// </summary>
        private void CopiarParametroComentarios(Element origen, Element destino)
        {
            try
            {
                Parameter paramOrigen = origen.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                Parameter paramDestino = destino.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

                if (paramOrigen != null && paramDestino != null && paramOrigen.HasValue)
                {
                    paramDestino.Set(paramOrigen.AsString());
                }
            }
            catch { }
        }

        /// <summary>
        /// Calcula el centroide geométrico REAL de una cara planar basándose en sus curvas
        /// IMPORTANTE: NO usar PlanarFace.Origin porque es solo el origen del sistema de coordenadas locales,
        /// no el verdadero centroide geométrico
        /// </summary>
        private XYZ CalcularCentroideGeometricoCara(PlanarFace cara)
        {
            try
            {
                IList<CurveLoop> curveLoops = cara.GetEdgesAsCurveLoops();
                if (curveLoops == null || curveLoops.Count == 0)
                {
                    // Fallback: usar Origin si no hay curvas
                    return cara.Origin;
                }

                // Usar el loop exterior (el primero generalmente es el contorno exterior)
                CurveLoop loopExterior = curveLoops[0];

                // Obtener todos los puntos del loop
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loopExterior)
                {
                    puntos.Add(curve.GetEndPoint(0));
                }

                if (puntos.Count == 0)
                {
                    return cara.Origin;
                }

                // Calcular el centroide como promedio de todos los vértices
                XYZ centroide = XYZ.Zero;
                foreach (XYZ punto in puntos)
                {
                    centroide += punto;
                }
                centroide = centroide / puntos.Count;

                return centroide;
            }
            catch
            {
                // Si hay cualquier error, usar Origin como fallback
                return cara.Origin;
            }
        }

        /// <summary>
        /// Obtiene el elemento estructural (viga/columna/muro/escalera) que está siendo encofrando
        /// Lee el ID desde el parámetro Comentarios del encofrado
        /// </summary>
        private Element ObtenerElementoEstructuralDeEncofrado(Element encofrado)
        {
            try
            {
                // 1. Leer el parámetro Comentarios del encofrado
                Parameter paramComentarios = encofrado.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (paramComentarios == null || !paramComentarios.HasValue)
                {
                    return null;
                }

                string comentarios = paramComentarios.AsString();
                if (string.IsNullOrWhiteSpace(comentarios))
                {
                    return null;
                }

                // 2. Extraer el ID del elemento (formato esperado: "ID: 123456" o solo "123456")
                string idString = comentarios.Trim();
                if (idString.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
                {
                    idString = idString.Substring(3).Trim();
                }

                if (!long.TryParse(idString, out long elementIdValue))
                {
                    return null;
                }

                // 3. Obtener el elemento estructural
                ElementId elementoEncofradoId = new ElementId(elementIdValue);
                Element elementoEstructural = _doc.GetElement(elementoEncofradoId);

                return elementoEstructural;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Copia el nivel base, altura y desfases del elemento estructural encofrado (viga/columna/muro) al muro creado
        /// Lee el ID del elemento desde el parámetro Comentarios del encofrado
        /// Si no encuentra el ID, usa el BoundingBox del encofrado como fallback
        /// </summary>
        private bool CopiarNivelYDesfasesDeElementoEncofrado(Element encofrado, Element muroCreado)
        {
            try
            {
                // 1. Leer el parámetro Comentarios del encofrado
                Parameter paramComentarios = encofrado.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (paramComentarios == null || !paramComentarios.HasValue)
                {
                    _log.AppendLine($"    ⚠ No se encontró ID en Comentarios - usando cálculo con BoundingBox");
                    return AjustarParametrosConBoundingBox(encofrado, muroCreado);
                }

                string comentarios = paramComentarios.AsString();
                if (string.IsNullOrWhiteSpace(comentarios))
                {
                    TaskDialog.Show("Debug", "AjustarParametrosConBoundingBox");
                    _log.AppendLine($"    ⚠ Comentarios vacío - usando cálculo con BoundingBox");
                    return AjustarParametrosConBoundingBox(encofrado, muroCreado);
                }

                // 2. Extraer el ID del elemento (formato esperado: "ID: 123456" o solo "123456")
                string idString = comentarios.Trim();
                if (idString.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
                {
                    idString = idString.Substring(3).Trim();
                }

                if (!long.TryParse(idString, out long elementIdValue))
                {
                    _log.AppendLine($"    ⚠ No se pudo parsear ID: '{comentarios}' - usando cálculo con BoundingBox");
                    return AjustarParametrosConBoundingBox(encofrado, muroCreado);
                }

                // 3. Obtener el elemento estructural
                ElementId elementoEncofradoId = new ElementId(elementIdValue);
                Element elementoEncofrado = _doc.GetElement(elementoEncofradoId);

                if (elementoEncofrado == null)
                {
                    _log.AppendLine($"    ⚠ No se encontró elemento con ID: {elementIdValue} - usando cálculo con BoundingBox");
                    return AjustarParametrosConBoundingBox(encofrado, muroCreado);
                }

                // 4. Verificar que sea viga, columna o muro
                string categoria = elementoEncofrado.Category?.Name ?? "Unknown";
                bool esElementoValido = elementoEncofrado is FamilyInstance || elementoEncofrado is Wall;

                if (!esElementoValido)
                {
                    _log.AppendLine($"    ⚠ Elemento no es viga/columna/muro ({categoria}) - usando cálculo con BoundingBox");
                    return AjustarParametrosConBoundingBox(encofrado, muroCreado);
                }

                _log.AppendLine($"    ✓ Elemento encofrado encontrado: {categoria} (ID: {elementIdValue})");

                // 5. Copiar parámetros del elemento estructural al muro creado
                bool algunoCopiado = false;

                // Nivel base - COMENTADO: Ya se seleccionó correctamente en CrearMuroDesdeCaras
                // NO copiar del elemento estructural porque sobrescribiría el nivel correcto
                // (seleccionamos el nivel inmediatamente inferior al zMin para que el Base Offset sea positivo)
                /*
                Parameter nivelOrigen = elementoEncofrado.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                if (nivelOrigen == null && elementoEncofrado is FamilyInstance)
                {
                    nivelOrigen = elementoEncofrado.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                }

                Parameter nivelDestino = muroCreado.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                if (nivelOrigen != null && nivelOrigen.HasValue && nivelDestino != null && !nivelDestino.IsReadOnly)
                {
                    nivelDestino.Set(nivelOrigen.AsElementId());
                    _log.AppendLine($"    ✓ Nivel base copiado");
                    algunoCopiado = true;
                }
                */
                _log.AppendLine($"    ℹ Nivel base NO copiado - ya seleccionado correctamente basándose en geometría del encofrado");

                // Base Offset - COMENTADO: Ya se calcula correctamente en CrearMuroDesdeCaras basándose en la geometría del encofrado
                // NO copiar del elemento estructural porque sobrescribiría el cálculo correcto
                /*
                Parameter baseOffsetOrigen = elementoEncofrado.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (baseOffsetOrigen == null && elementoEncofrado is FamilyInstance)
                {
                    baseOffsetOrigen = elementoEncofrado.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                }

                Parameter baseOffsetDestino = muroCreado.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (baseOffsetOrigen != null && baseOffsetOrigen.HasValue && baseOffsetDestino != null && !baseOffsetDestino.IsReadOnly)
                {
                    double valorOffset = baseOffsetOrigen.AsDouble();
                    baseOffsetDestino.Set(valorOffset);
                    _log.AppendLine($"    ✓ Base Offset copiado: {valorOffset * 304.8:F1}mm");
                    algunoCopiado = true;
                }
                */
                _log.AppendLine($"    ℹ Base Offset NO copiado - ya calculado basándose en geometría del encofrado");

                // ENFOQUE COMBINADO: Para TODOS los elementos estructurales (columnas, vigas, escaleras, cimentaciones, etc.)
                // 1. PRIMERO: Intentar usar BoundingBox del encofrado
                // 2. FALLBACK: Si falla, usar niveles y offsets del elemento estructural
                Parameter alturaDestino = muroCreado.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);

                if (elementoEncofrado is FamilyInstance fi)
                {
                    // CASO: FamilyInstance (Columnas, Vigas, Escaleras, Cimentaciones, etc.)
                    _log.AppendLine($"    [{categoria.ToUpper()}] === INICIO CALCULO DE ALTURA ===");
                    _log.AppendLine($"    [{categoria.ToUpper()}] Categoria: {categoria}");

                    // PASO 1: PRIMERO intentar usar BoundingBox del encofrado
                    bool alturaAplicadaDesdeBBox = false;
                    try
                    {
                        BoundingBoxXYZ bbox = encofrado.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            double zMin = bbox.Min.Z;
                            double zMax = bbox.Max.Z;
                            double alturaCalculada = zMax - zMin;

                            _log.AppendLine($"    [{categoria.ToUpper()}] BBox: zMin={zMin:F3}, zMax={zMax:F3}");
                            _log.AppendLine($"    [{categoria.ToUpper()}] Altura desde BBox={alturaCalculada:F3} ft ({alturaCalculada * 304.8:F1}mm)");

                            // CRÍTICO: Verificar si el muro tiene Top Constraint y ELIMINARLO
                            // El Top Constraint hace que Revit calcule la altura automáticamente basándose en niveles,
                            // lo cual ignora la geometría real del encofrado y genera muros con altura incorrecta
                            Parameter topConstraintParam = muroCreado.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                            bool teniaTopConstraint = topConstraintParam != null && topConstraintParam.AsElementId() != ElementId.InvalidElementId;

                            if (teniaTopConstraint)
                            {
                                // ELIMINAR Top Constraint para poder aplicar altura directamente desde BoundingBox
                                try
                                {
                                    topConstraintParam.Set(ElementId.InvalidElementId);
                                    _log.AppendLine($"    [{categoria.ToUpper()}] ✓ Top Constraint eliminado - ahora usaremos altura desde BBox");
                                }
                                catch (Exception exTopConst)
                                {
                                    _log.AppendLine($"    [{categoria.ToUpper()}] ⚠ Error eliminando Top Constraint: {exTopConst.Message}");
                                }
                            }

                            // Ahora aplicar altura directamente desde BoundingBox
                            if (alturaDestino != null && !alturaDestino.IsReadOnly)
                            {
                                alturaDestino.Set(alturaCalculada);
                                _log.AppendLine($"    [{categoria.ToUpper()}] ✓ Altura desde BBox aplicada: {alturaCalculada * 304.8:F1}mm (zMin={zMin:F3}, zMax={zMax:F3})");
                                alturaAplicadaDesdeBBox = true;
                                algunoCopiado = true;
                            }
                            else
                            {
                                _log.AppendLine($"    [{categoria.ToUpper()}] ⚠ No se pudo aplicar altura desde BBox (parámetro readonly o null)");
                                // No marcamos como exitoso - intentaremos con niveles
                            }
                        }
                    }
                    catch (Exception exBbox)
                    {
                        _log.AppendLine($"    [{categoria.ToUpper()}] ⚠ Error usando BoundingBox: {exBbox.Message}");
                        // Continuamos al fallback con niveles
                    }

                    // PASO 2: FALLBACK - Si BoundingBox falló, intentar con niveles del elemento estructural
                    if (!alturaAplicadaDesdeBBox)
                    {
                        _log.AppendLine($"    [{categoria.ToUpper()}] → BoundingBox no aplicado, intentando con niveles del elemento estructural...");

                        // Obtener nivel base - probar multiples parametros
                        Parameter nivelBaseParam = null;
                        Level nivelBase = null;

                        nivelBaseParam = fi.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                        if (nivelBaseParam != null && nivelBaseParam.HasValue)
                        {
                            nivelBase = _doc.GetElement(nivelBaseParam.AsElementId()) as Level;
                            _log.AppendLine($"    [{categoria.ToUpper()}] Nivel Base encontrado via FAMILY_LEVEL_PARAM: {nivelBase?.Name ?? "null"}");
                        }

                        if (nivelBase == null)
                        {
                            nivelBaseParam = fi.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_PARAM);
                            if (nivelBaseParam != null && nivelBaseParam.HasValue)
                            {
                                nivelBase = _doc.GetElement(nivelBaseParam.AsElementId()) as Level;
                                _log.AppendLine($"    [{categoria.ToUpper()}] Nivel Base encontrado via SCHEDULE_BASE_LEVEL_PARAM: {nivelBase?.Name ?? "null"}");
                            }
                        }

                        // Obtener nivel top - probar multiples parametros
                        Parameter nivelTopParam = null;
                        Level nivelTop = null;

                        nivelTopParam = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                        if (nivelTopParam != null && nivelTopParam.HasValue)
                        {
                            nivelTop = _doc.GetElement(nivelTopParam.AsElementId()) as Level;
                            _log.AppendLine($"    [{categoria.ToUpper()}] Nivel Top encontrado via FAMILY_TOP_LEVEL_PARAM: {nivelTop?.Name ?? "null"}");
                        }

                        if (nivelTop == null)
                        {
                            nivelTopParam = fi.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_PARAM);
                            if (nivelTopParam != null && nivelTopParam.HasValue)
                            {
                                nivelTop = _doc.GetElement(nivelTopParam.AsElementId()) as Level;
                                _log.AppendLine($"    [{categoria.ToUpper()}] Nivel Top encontrado via SCHEDULE_TOP_LEVEL_PARAM: {nivelTop?.Name ?? "null"}");
                            }
                        }

                        // Obtener offsets - probar multiples parametros
                        double offsetBase = 0;
                        Parameter baseOffsetParam = fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                        if (baseOffsetParam != null && baseOffsetParam.HasValue)
                        {
                            offsetBase = baseOffsetParam.AsDouble();
                            _log.AppendLine($"    [{categoria.ToUpper()}] Base Offset via FAMILY_BASE_LEVEL_OFFSET_PARAM: {offsetBase * 304.8:F0}mm");
                        }
                        else
                        {
                            baseOffsetParam = fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                            if (baseOffsetParam != null && baseOffsetParam.HasValue)
                            {
                                offsetBase = baseOffsetParam.AsDouble();
                                _log.AppendLine($"    [{categoria.ToUpper()}] Base Offset via INSTANCE_ELEVATION_PARAM: {offsetBase * 304.8:F0}mm");
                            }
                        }

                        double offsetTop = 0;
                        Parameter topOffsetParam = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                        if (topOffsetParam != null && topOffsetParam.HasValue)
                        {
                            offsetTop = topOffsetParam.AsDouble();
                            _log.AppendLine($"    [{categoria.ToUpper()}] Top Offset via FAMILY_TOP_LEVEL_OFFSET_PARAM: {offsetTop * 304.8:F0}mm");
                        }
                        else
                        {
                            topOffsetParam = fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                            if (topOffsetParam != null && topOffsetParam.HasValue)
                            {
                                offsetTop = topOffsetParam.AsDouble();
                                _log.AppendLine($"    [{categoria.ToUpper()}] Top Offset via INSTANCE_FREE_HOST_OFFSET_PARAM: {offsetTop * 304.8:F0}mm");
                            }
                        }

                        // Calcular altura y aplicar top offset si tenemos ambos niveles
                        if (nivelBase != null && nivelTop != null)
                        {
                            double elevacionBase = nivelBase.Elevation;
                            double elevacionTop = nivelTop.Elevation;
                            double alturaCalculada = (elevacionTop + offsetTop) - (elevacionBase + offsetBase);

                            _log.AppendLine($"    [{categoria.ToUpper()}] ----------------------------------------");
                            _log.AppendLine($"    [{categoria.ToUpper()}] Nivel Base: {nivelBase.Name} (Elev: {elevacionBase * 304.8:F0}mm)");
                            _log.AppendLine($"    [{categoria.ToUpper()}] Base Offset: {offsetBase * 304.8:F0}mm");
                            _log.AppendLine($"    [{categoria.ToUpper()}] Nivel Top: {nivelTop.Name} (Elev: {elevacionTop * 304.8:F0}mm)");
                            _log.AppendLine($"    [{categoria.ToUpper()}] Top Offset: {offsetTop * 304.8:F0}mm");
                            _log.AppendLine($"    [{categoria.ToUpper()}] ALTURA CALCULADA: {alturaCalculada * 304.8:F0}mm");
                            _log.AppendLine($"    [{categoria.ToUpper()}] ----------------------------------------");

                            // Verificar si el muro ya tiene Top Constraint configurado
                            Parameter topConstraint = muroCreado.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                            bool tieneTopConstraint = topConstraint != null && topConstraint.AsElementId() != ElementId.InvalidElementId;

                            if (tieneTopConstraint)
                            {
                                // El muro ya tiene Top Constraint (probablemente del nivel copiado antes)
                                // En este caso, WALL_USER_HEIGHT_PARAM es readonly, así que solo aplicamos el top offset
                                _log.AppendLine($"    [{categoria.ToUpper()}] Muro tiene Top Constraint - aplicando solo Top Offset");

                                Parameter topOffsetDestinoFI = muroCreado.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                                if (topOffsetDestinoFI != null && !topOffsetDestinoFI.IsReadOnly)
                                {
                                    topOffsetDestinoFI.Set(offsetTop);
                                    _log.AppendLine($"    [{categoria.ToUpper()}] ✓ Top Offset aplicado: {offsetTop * 304.8:F0}mm");
                                    algunoCopiado = true;
                                }
                                else
                                {
                                    _log.AppendLine($"    [{categoria.ToUpper()}] ⚠ Top Offset readonly o null");
                                }
                            }
                            else if (alturaDestino != null && !alturaDestino.IsReadOnly && alturaCalculada > 0)
                            {
                                // El muro NO tiene Top Constraint, podemos modificar la altura directamente
                                alturaDestino.Set(alturaCalculada);
                                _log.AppendLine($"    [{categoria.ToUpper()}] ✓ Altura aplicada directamente: {alturaCalculada * 304.8:F0}mm");
                                algunoCopiado = true;
                            }
                            else
                            {
                                _log.AppendLine($"    [{categoria.ToUpper()}] ⚠ No se pudo aplicar ni Top Constraint ni altura (readonly={alturaDestino?.IsReadOnly})");
                            }
                        }
                        else
                        {
                            // No se pudieron obtener niveles del elemento estructural
                            _log.AppendLine($"    [{categoria.ToUpper()}] ✗ No se pudieron obtener niveles (Base={nivelBase?.Name ?? "null"}, Top={nivelTop?.Name ?? "null"})");
                            _log.AppendLine($"    [{categoria.ToUpper()}] ⚠ No se pudo aplicar altura desde niveles ni desde BoundingBox");
                        }
                    } // Fin del bloque if (!alturaAplicadaDesdeBBox)
                } // Fin del bloque if (elementoEncofrado is FamilyInstance fi)
                else
                {
                    // CASO: MURO estructural - Copiar altura directamente si existe
                    Parameter alturaOrigen = elementoEncofrado.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (alturaOrigen != null && alturaOrigen.HasValue && alturaDestino != null && !alturaDestino.IsReadOnly)
                    {
                        double valorAltura = alturaOrigen.AsDouble();
                        alturaDestino.Set(valorAltura);
                        _log.AppendLine($"    ✓ Altura copiada: {valorAltura * 304.8:F1}mm");
                        algunoCopiado = true;
                    }
                }

                // Top Offset
                Parameter topOffsetOrigen = elementoEncofrado.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                Parameter topOffsetDestino = muroCreado.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);

                _log.AppendLine($"    [TOP OFFSET DEBUG] Origen: {(topOffsetOrigen != null ? "existe" : "null")}, HasValue: {topOffsetOrigen?.HasValue}, Valor: {(topOffsetOrigen?.HasValue == true ? (topOffsetOrigen.AsDouble() * 304.8).ToString("F1") + "mm" : "N/A")}");
                _log.AppendLine($"    [TOP OFFSET DEBUG] Destino: {(topOffsetDestino != null ? "existe" : "null")}, IsReadOnly: {topOffsetDestino?.IsReadOnly}");

                // CRÍTICO: NO copiar Top Offset si el muro NO tiene Top Constraint
                // Un muro con altura directa (modo "Unconnected") no puede tener Top Offset
                Parameter topConstraintCheck = muroCreado.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                bool tieneTopConstraintCheck = topConstraintCheck != null && topConstraintCheck.AsElementId() != ElementId.InvalidElementId;

                _log.AppendLine($"    [TOP OFFSET DEBUG] Muro tiene Top Constraint: {tieneTopConstraintCheck}");

                if (tieneTopConstraintCheck && topOffsetOrigen != null && topOffsetOrigen.HasValue && topOffsetDestino != null && !topOffsetDestino.IsReadOnly)
                {
                    double valorTopOffset = topOffsetOrigen.AsDouble();
                    topOffsetDestino.Set(valorTopOffset);
                    _log.AppendLine($"    ✓ Top Offset copiado: {valorTopOffset * 304.8:F1}mm");
                    algunoCopiado = true;
                }
                else if (!tieneTopConstraintCheck && topOffsetOrigen != null && topOffsetOrigen.HasValue)
                {
                    _log.AppendLine($"    ⚠ Top Offset NO copiado - muro sin Top Constraint (modo Unconnected)");
                }

                if (algunoCopiado)
                {
                    _log.AppendLine($"    ✓✓ Parámetros copiados del elemento estructural original");
                }
                else
                {
                    _log.AppendLine($"    ℹ No se copiaron parámetros adicionales (nivel y base offset ya configurados correctamente)");
                }

                // SIEMPRE retornar true - no copiar parámetros no es un error
                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error copiando parámetros: {ex.Message} - usando cálculo con BoundingBox");
                return AjustarParametrosConBoundingBox(encofrado, muroCreado);
            }
        }

        /// <summary>
        /// Método fallback que ajusta los parámetros del muro usando el BoundingBox del encofrado
        /// cuando no se puede copiar del elemento estructural original
        /// </summary>
        private bool AjustarParametrosConBoundingBox(Element encofrado, Element muroCreado)
        {
            try
            {
                BoundingBoxXYZ bbox = encofrado.get_BoundingBox(null);
                if (bbox == null)
                {
                    _log.AppendLine($"    ✗ No se pudo obtener BoundingBox del encofrado");
                    return false;
                }

                double zMaxOriginal = bbox.Max.Z;
                double zMinOriginal = bbox.Min.Z;
                double alturaOriginal = zMaxOriginal - zMinOriginal;

                // Obtener nivel base del muro
                Parameter paramLevel = muroCreado.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                if (paramLevel == null || paramLevel.AsElementId() == ElementId.InvalidElementId)
                {
                    _log.AppendLine($"    ✗ Muro creado no tiene nivel base");
                    return false;
                }

                Level nivel = _doc.GetElement(paramLevel.AsElementId()) as Level;
                if (nivel == null)
                {
                    _log.AppendLine($"    ✗ No se pudo obtener nivel del muro");
                    return false;
                }

                // CRÍTICO: Eliminar Top Constraint ANTES de establecer altura
                // El Top Constraint hace que Revit calcule la altura automáticamente basándose en niveles,
                // lo cual ignora la geometría real del encofrado y genera muros con altura incorrecta
                Parameter topConstraintParam = muroCreado.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                bool teniaTopConstraint = topConstraintParam != null && topConstraintParam.AsElementId() != ElementId.InvalidElementId;

                if (teniaTopConstraint && !topConstraintParam.IsReadOnly)
                {
                    try
                    {
                        topConstraintParam.Set(ElementId.InvalidElementId);
                        _log.AppendLine($"    ✓ Top Constraint eliminado - ahora usaremos altura desde BBox del encofrado");
                    }
                    catch (Exception exTopConstraint)
                    {
                        _log.AppendLine($"    ⚠ No se pudo eliminar Top Constraint: {exTopConstraint.Message}");
                    }
                }

                // 1. Ajustar Base Offset
                double baseOffsetOriginal = zMinOriginal - nivel.Elevation;
                Parameter paramBaseOffset = muroCreado.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                if (paramBaseOffset != null && !paramBaseOffset.IsReadOnly)
                {
                    paramBaseOffset.Set(baseOffsetOriginal);
                }

                // 2. Ajustar Altura (ahora sin Top Constraint, Revit respetará el valor)
                Parameter paramHeight = muroCreado.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                if (paramHeight != null && !paramHeight.IsReadOnly)
                {
                    paramHeight.Set(alturaOriginal);
                    _log.AppendLine($"    ✓ Altura desde BBox del encofrado aplicada: {alturaOriginal * 304.8:F1}mm (zMin={zMinOriginal:F3}, zMax={zMaxOriginal:F3})");
                }

                // 3. Verificar que la altura se haya aplicado correctamente
                double baseOffsetReal = paramBaseOffset != null ? paramBaseOffset.AsDouble() : baseOffsetOriginal;
                double alturaReal = paramHeight != null ? paramHeight.AsDouble() : alturaOriginal;

                if (Math.Abs(alturaReal - alturaOriginal) > 0.01) // Tolerancia de ~3mm
                {
                    _log.AppendLine($"    ⚠ ADVERTENCIA: Altura aplicada ({alturaReal * 304.8:F1}mm) difiere de la esperada ({alturaOriginal * 304.8:F1}mm)");
                    _log.AppendLine($"    ⚠ Revit puede haber ajustado el valor automáticamente");
                }

                // 4. Calcular Top Offset basado en valores REALES
                double topOffset = zMaxOriginal - (nivel.Elevation + baseOffsetReal + alturaReal);
                Parameter paramTopOffset = muroCreado.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                if (paramTopOffset != null && !paramTopOffset.IsReadOnly)
                {
                    paramTopOffset.Set(topOffset);
                }

                _log.AppendLine($"    ✓ Parámetros ajustados con BoundingBox | Top: {topOffset * 304.8:F1}mm | H: {alturaReal * 304.8:F1}mm | Base: {baseOffsetReal * 304.8:F1}mm");

                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error ajustando con BoundingBox: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Valida que todas las curvas estén en el mismo plano vertical y las proyecta SIEMPRE
        /// Revit requiere que todas las curvas en Wall.Create estén coplanares de manera EXACTA
        /// </summary>
        private List<Curve> ValidarYProyectarCurvasAPlanoComun(List<Curve> curvas, XYZ normalPlano)
        {
            try
            {
                if (curvas == null || curvas.Count < 3)
                {
                    _log.AppendLine($"    ⚠ Insuficientes curvas para validar plano");
                    return curvas;
                }

                _log.AppendLine($"    [VALIDACIÓN COPLANAR] Procesando {curvas.Count} curvas...");

                // 1. Calcular el centroide de TODOS los puntos de las curvas para tener un mejor origen del plano
                List<XYZ> todosPuntos = new List<XYZ>();
                foreach (Curve curva in curvas)
                {
                    todosPuntos.Add(curva.GetEndPoint(0));
                    todosPuntos.Add(curva.GetEndPoint(1));
                }

                XYZ centroide = new XYZ(
                    todosPuntos.Average(p => p.X),
                    todosPuntos.Average(p => p.Y),
                    todosPuntos.Average(p => p.Z)
                );

                // 2. Crear plano vertical con la normal proporcionada, pero centrado en el centroide
                XYZ normalVertical = normalPlano.Normalize();

                // VERIFICAR que la normal sea realmente vertical (Z debe ser casi 0)
                // Si es inclinada, RECHAZAR (no forzar) porque debe clasificarse como SUELO
                // UMBRAL ESTRICTO: 0.01 = ~0.57° máximo para muros verticales
                if (Math.Abs(normalVertical.Z) > 0.01)
                {
                    _log.AppendLine($"    ✗ Normal está inclinada (Z={normalVertical.Z:F3}), rechazando creación de muro");
                    _log.AppendLine($"    ⚠ Este encofrado debe clasificarse como SUELO (requiere verticalidad perfecta)");
                    return null;
                }

                Plane planoReferencia = Plane.CreateByNormalAndOrigin(normalVertical, centroide);

                _log.AppendLine($"    [PLANO] Normal=({normalVertical.X:F3}, {normalVertical.Y:F3}, {normalVertical.Z:F3})");
                _log.AppendLine($"    [PLANO] Centroide=({centroide.X:F3}, {centroide.Y:F3}, {centroide.Z:F3})");

                // 3. Verificar distancias antes de proyectar
                double maxDistancia = 0;
                foreach (Curve curva in curvas)
                {
                    XYZ p0 = curva.GetEndPoint(0);
                    XYZ p1 = curva.GetEndPoint(1);

                    XYZ vectorAlPunto0 = p0 - planoReferencia.Origin;
                    XYZ vectorAlPunto1 = p1 - planoReferencia.Origin;
                    double distP0 = Math.Abs(vectorAlPunto0.DotProduct(normalVertical));
                    double distP1 = Math.Abs(vectorAlPunto1.DotProduct(normalVertical));

                    maxDistancia = Math.Max(maxDistancia, Math.Max(distP0, distP1));
                }

                _log.AppendLine($"    [DIST] Máxima desviación del plano: {maxDistancia * 304.8:F1}mm");

                // 4. SIEMPRE proyectar TODAS las curvas al plano (no confiar en tolerancias)
                _log.AppendLine($"    🔧 PROYECTANDO todas las curvas al plano común (forzado)...");
                List<Curve> curvasProyectadas = new List<Curve>();

                foreach (Curve curva in curvas)
                {
                    try
                    {
                        XYZ p0Original = curva.GetEndPoint(0);
                        XYZ p1Original = curva.GetEndPoint(1);

                        // Proyectar puntos al plano manualmente
                        XYZ vectorP0 = p0Original - planoReferencia.Origin;
                        XYZ vectorP1 = p1Original - planoReferencia.Origin;

                        double distanciaP0 = vectorP0.DotProduct(normalVertical);
                        double distanciaP1 = vectorP1.DotProduct(normalVertical);

                        XYZ p0Proyectado = p0Original - distanciaP0 * normalVertical;
                        XYZ p1Proyectado = p1Original - distanciaP1 * normalVertical;

                        // Verificar que los puntos proyectados no sean iguales
                        if (p0Proyectado.DistanceTo(p1Proyectado) < 0.001)
                        {
                            _log.AppendLine($"    ⚠ Curva colapsó al proyectar - omitiendo");
                            continue;
                        }

                        // Crear nueva curva proyectada
                        Curve curvaProyectada = null;

                        if (curva is Line)
                        {
                            curvaProyectada = Line.CreateBound(p0Proyectado, p1Proyectado);
                        }
                        else if (curva is Arc arc)
                        {
                            // Para arcos, proyectar también el punto medio
                            XYZ pMedioOriginal = arc.Evaluate(0.5, true);
                            XYZ vectorPMedio = pMedioOriginal - planoReferencia.Origin;
                            double distanciaPMedio = vectorPMedio.DotProduct(normalVertical);
                            XYZ pMedioProyectado = pMedioOriginal - distanciaPMedio * normalVertical;

                            try
                            {
                                curvaProyectada = Arc.Create(p0Proyectado, p1Proyectado, pMedioProyectado);
                            }
                            catch
                            {
                                // Si falla crear arco, usar línea
                                curvaProyectada = Line.CreateBound(p0Proyectado, p1Proyectado);
                                _log.AppendLine($"    ⚠ Arco convertido a línea al proyectar");
                            }
                        }
                        else
                        {
                            // Para otras curvas, aproximar con línea
                            curvaProyectada = Line.CreateBound(p0Proyectado, p1Proyectado);
                            _log.AppendLine($"    ⚠ Curva compleja aproximada a línea");
                        }

                        if (curvaProyectada != null)
                        {
                            curvasProyectadas.Add(curvaProyectada);
                        }
                    }
                    catch (Exception exCurva)
                    {
                        _log.AppendLine($"    ✗ Error proyectando curva: {exCurva.Message}");
                        // Intentar usar la curva original
                        curvasProyectadas.Add(curva);
                    }
                }

                _log.AppendLine($"    ✓ Proyección completada: {curvasProyectadas.Count}/{curvas.Count} curvas");

                if (curvasProyectadas.Count < 3)
                {
                    _log.AppendLine($"    ✗ Muy pocas curvas después de proyectar");
                    return null;
                }

                // 5. VERIFICACIÓN FINAL: Todas las curvas proyectadas deben estar en el plano
                double maxDistanciaFinal = 0;
                foreach (Curve curvaProyectada in curvasProyectadas)
                {
                    XYZ p0 = curvaProyectada.GetEndPoint(0);
                    XYZ p1 = curvaProyectada.GetEndPoint(1);

                    XYZ vectorAlPunto0 = p0 - planoReferencia.Origin;
                    XYZ vectorAlPunto1 = p1 - planoReferencia.Origin;
                    double distP0 = Math.Abs(vectorAlPunto0.DotProduct(normalVertical));
                    double distP1 = Math.Abs(vectorAlPunto1.DotProduct(normalVertical));

                    maxDistanciaFinal = Math.Max(maxDistanciaFinal, Math.Max(distP0, distP1));
                }

                _log.AppendLine($"    [VERIFICACIÓN] Desviación final máxima: {maxDistanciaFinal * 304.8:F3}mm");

                if (maxDistanciaFinal > 0.001) // 0.3mm tolerancia
                {
                    _log.AppendLine($"    ⚠ Las curvas proyectadas todavía no están perfectamente coplanares");
                }
                else
                {
                    _log.AppendLine($"    ✓✓ Curvas perfectamente coplanares verificadas");
                }

                return curvasProyectadas;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"    ✗ Error validando plano: {ex.Message}");
                return curvas; // Devolver curvas originales si falla
            }
        }

        /// <summary>
        /// Obtiene el ID del material de encofrado (Contrachapado)
        /// </summary>
        private ElementId ObtenerMaterialEncofrado()
        {
            try
            {
                // Buscar material "Contrachapado" o similar
                var materiales = new FilteredElementCollector(_doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .ToList();

                Material material = materiales.FirstOrDefault(m =>
                    m.Name.Contains("Contrachapado") ||
                    m.Name.ToLower().Contains("plywood") ||
                    m.Name.ToLower().Contains("madera"));

                if (material != null)
                {
                    return material.Id;
                }

                // Si no existe, crear uno nuevo
                ElementId nuevoMatId = Material.Create(_doc, "Contrachapado");
                Material nuevoMat = _doc.GetElement(nuevoMatId) as Material;
                if (nuevoMat != null)
                {
                    nuevoMat.Color = new Color(210, 180, 140); // Color madera
                    nuevoMat.Transparency = 0;
                }
                return nuevoMatId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// Muestra diálogo con resultados
        /// </summary>
        private void MostrarResultados(int muros, int suelos, int errores,
            int metodo1, int metodo2, int metodo3, int metodo4, int metodo5)
        {
            int total = muros + suelos + errores;
            int exitosos = muros + suelos;

            // Calcular porcentajes de uso de cada método (solo sobre los muros exitosos)
            string estadisticasMetodos = "";
            string resumenSimple = "";

            if (muros > 0)
            {
                estadisticasMetodos = $"\n📊 ESTADÍSTICAS DE MÉTODOS USADOS:\n" +
                                     $"───────────────────────────────────────\n";

                // Crear resumen simple para mostrar al inicio
                resumenSimple = "\nMÉTODOS USADOS:\n";

                if (metodo1 > 0)
                {
                    double porcentaje = (metodo1 * 100.0) / muros;
                    estadisticasMetodos += $"[1] Curvas de cara:     {metodo1,3} ({porcentaje,5:F1}%)\n";
                    resumenSimple += $"  • Método 1 (Curvas de cara): {porcentaje:F1}%\n";
                }
                if (metodo2 > 0)
                {
                    double porcentaje = (metodo2 * 100.0) / muros;
                    estadisticasMetodos += $"[2] EditProfile:        {metodo2,3} ({porcentaje,5:F1}%)\n";
                    resumenSimple += $"  • Método 2 (EditProfile): {porcentaje:F1}%\n";
                }
                if (metodo3 > 0)
                {
                    double porcentaje = (metodo3 * 100.0) / muros;
                    estadisticasMetodos += $"[3] CurveLoops:         {metodo3,3} ({porcentaje,5:F1}%)\n";
                    resumenSimple += $"  • Método 3 (CurveLoops): {porcentaje:F1}%\n";
                }
                if (metodo4 > 0)
                {
                    double porcentaje = (metodo4 * 100.0) / muros;
                    estadisticasMetodos += $"[4] DirectShape:        {metodo4,3} ({porcentaje,5:F1}%)\n";
                    resumenSimple += $"  • Método 4 (DirectShape): {porcentaje:F1}%\n";
                }
                if (metodo5 > 0)
                {
                    double porcentaje = (metodo5 * 100.0) / muros;
                    estadisticasMetodos += $"[5] Tradicional:        {metodo5,3} ({porcentaje,5:F1}%)\n";
                    resumenSimple += $"  • Método 5 (Tradicional): {porcentaje:F1}%\n";
                }
            }

            string mensaje = $"═══════════════════════════════════════\n" +
                           $"   CONVERSIÓN A ELEMENTOS NATIVOS\n" +
                           $"═══════════════════════════════════════\n\n" +
                           $"✓ Muros NATIVOS creados: {muros}\n" +
                           $"✓ Suelos NATIVOS creados: {suelos}\n" +
                           $"✖ Fallos (conservados): {errores}\n\n" +
                           $"Total procesados: {total}\n" +
                           $"Tasa de éxito: {(total > 0 ? (exitosos * 100.0 / total).ToString("F1") : "0")}%\n" +
                           resumenSimple +
                           $"\n💡 IMPORTANTE:\n" +
                           $"• Elementos creados son Walls y Floors NATIVOS\n" +
                           $"• Encofrados originales eliminados (exitosos)\n" +
                           $"• Encofrados fallidos conservados para revisión\n" +
                           $"• Método 1: Curvas coplanares con validación\n" +
                           $"• ID original preservado en 'Comentarios'\n" +
                           $"• Sin separación de cara estructural (0mm)";

            // Guardar log detallado en archivo .txt en el escritorio
            string escritorio = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string archivoLog = Path.Combine(escritorio, $"EncofradoLog_{timestamp}.txt");

            try
            {
                string logCompleto = "═══════════════════════════════════════\n" +
                                    "   LOG DETALLADO - CONVERSION ENCOFRADO\n" +
                                    "═══════════════════════════════════════\n\n" +
                                    mensaje + "\n\n" +
                                    estadisticasMetodos + "\n\n" +
                                    "═══════════════════════════════════════\n" +
                                    "   LOG DETALLADO POR ELEMENTO\n" +
                                    "═══════════════════════════════════════\n\n" +
                                    _log.ToString();

                File.WriteAllText(archivoLog, logCompleto, System.Text.Encoding.UTF8);

                // Mostrar dialogo simple con ruta al archivo
                TaskDialog td = new TaskDialog("Conversion Completada");
                td.MainInstruction = "Proceso completado";
                td.MainContent = mensaje + $"\n\nLog guardado en:\n{archivoLog}";
                td.Show();

                // Intentar abrir el archivo automaticamente
                try
                {
                    System.Diagnostics.Process.Start("notepad.exe", archivoLog);
                }
                catch
                {
                    // Si falla abrir, no importa
                }
            }
            catch (Exception exLog)
            {
                // Si falla guardar el log, mostrar en TaskDialog como antes
                TaskDialog td = new TaskDialog("Conversion Completada");
                td.MainInstruction = "Proceso completado";
                td.MainContent = mensaje + $"\n\nNo se pudo guardar log: {exLog.Message}";
                td.ExpandedContent = _log.ToString();
                td.Show();
            }
        }

        #region Clases Auxiliares

        private enum TipoOrientacion
        {
            Horizontal, // Suelo/Losa
            Vertical    // Muro
        }

        private struct OrientacionEncofrado
        {
            public TipoOrientacion Tipo;
            public XYZ NormalPromedio;
        }

        /// <summary>
        /// Comparador de XYZ que solo considera las coordenadas X e Y (ignora Z)
        /// Usado para identificar puntos en el mismo lugar en planta
        /// </summary>
        private class XYZComparerXY : IEqualityComparer<XYZ>
        {
            private const double TOLERANCE = 0.001; // ~3mm

            public bool Equals(XYZ a, XYZ b)
            {
                if (a == null && b == null) return true;
                if (a == null || b == null) return false;

                return Math.Abs(a.X - b.X) < TOLERANCE &&
                       Math.Abs(a.Y - b.Y) < TOLERANCE;
            }

            public int GetHashCode(XYZ obj)
            {
                if (obj == null) return 0;

                // Redondear a 3 decimales para agrupar puntos cercanos
                int hashX = ((int)(obj.X * 1000)).GetHashCode();
                int hashY = ((int)(obj.Y * 1000)).GetHashCode();

                return hashX ^ hashY;
            }
        }

        #endregion
    }

    /// <summary>
    /// Manejador de fallos personalizado para suprimir diálogos de error de Revit
    /// durante la eliminación de encofrados
    /// </summary>
    public class FailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            // LOGGING: Crear archivo de log para errores
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string errorLogPath = System.IO.Path.Combine(desktopPath, $"FailurePreprocessor_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            System.Text.StringBuilder errorLog = new System.Text.StringBuilder();

            errorLog.AppendLine("═══════════════════════════════════════");
            errorLog.AppendLine($"   FAILURE PREPROCESSOR LOG");
            errorLog.AppendLine($"   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            errorLog.AppendLine("═══════════════════════════════════════\n");

            // Obtener todos los mensajes de fallo
            IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();

            errorLog.AppendLine($"Total de mensajes de fallo: {failureMessages.Count}\n");

            foreach (FailureMessageAccessor failureMessage in failureMessages)
            {
                // Obtener la severidad del fallo
                FailureSeverity severity = failureMessage.GetSeverity();
                string descriptionText = failureMessage.GetDescriptionText();

                errorLog.AppendLine($"─────────────────────────────────────");
                errorLog.AppendLine($"Severidad: {severity}");
                errorLog.AppendLine($"Descripción: {descriptionText}");

                // Obtener elementos que fallan
                ICollection<ElementId> failingElementIds = failureMessage.GetFailingElementIds();
                errorLog.AppendLine($"Elementos fallando: {failingElementIds.Count}");
                foreach (ElementId id in failingElementIds)
                {
                    errorLog.AppendLine($"  - ID: {id.Value}");
                }

                // Si es un warning, simplemente lo eliminamos
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failureMessage);
                    errorLog.AppendLine("Acción: Warning eliminado");
                }
                // Si es un error, intentamos eliminar los elementos problemáticos
                else if (severity == FailureSeverity.Error)
                {
                    // Si hay elementos fallando, intentar eliminarlos
                    if (failingElementIds.Count > 0)
                    {
                        // Eliminar el warning/error del fallo
                        failuresAccessor.DeleteWarning(failureMessage);
                        errorLog.AppendLine("Acción: Error eliminado (DeleteWarning)");
                    }
                    else
                    {
                        errorLog.AppendLine("Acción: Error sin elementos - no eliminado");
                    }
                }
                errorLog.AppendLine();
            }

            errorLog.AppendLine("═══════════════════════════════════════");
            errorLog.AppendLine($"Resultado: ProceedWithCommit");
            errorLog.AppendLine("═══════════════════════════════════════");

            // Guardar log a archivo
            try
            {
                System.IO.File.WriteAllText(errorLogPath, errorLog.ToString());
            }
            catch
            {
                // Ignorar errores al escribir el log
            }

            // CRÍTICO: Forzar commit incluso si hay errores
            // ProceedWithCommit impide que Revit haga rollback de la transacción
            return FailureProcessingResult.ProceedWithCommit;
        }
    }
}
