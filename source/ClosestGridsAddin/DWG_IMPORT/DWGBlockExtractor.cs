using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;
using ClosestGridsAddinVANILLA.Commands;

namespace ClosestGridsAddinVANILLA.DWG_IMPORT
{
    /// <summary>
    /// Extractor de bloques DWG importados de SolidWorks
    /// Procesa bloques anidados y crea DirectShape por cada instancia,
    /// identificando piezas idénticas mediante firma geométrica
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DWGBlockExtractorCommand : LicensedCommand
    {
        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Solicitar selección del DWG importado
                Reference reference = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new ImportInstanceSelectionFilter(),
                    "Seleccione el archivo DWG importado de SolidWorks");

                Element element = doc.GetElement(reference);
                ImportInstance importInstance = element as ImportInstance;

                if (importInstance == null)
                {
                    TaskDialog.Show("Error", "El elemento seleccionado no es un DWG importado");
                    return Result.Failed;
                }

                // CONFIGURACIÓN: Preguntar al usuario cómo crear los DirectShapes
                TaskDialog configDialog = new TaskDialog("Configuración de Extracción");
                configDialog.MainInstruction = "¿Cómo desea crear los DirectShapes?";
                configDialog.MainContent = "OPCIÓN 1 (LENTO): Un DirectShape por cada sólido - Máxima división\n" +
                                          "  • Ventaja: Cada pieza completamente independiente\n" +
                                          "  • Desventaja: MUY LENTO para miles de piezas\n\n" +
                                          "OPCIÓN 2 (RÁPIDO): Agrupar sólidos en lotes\n" +
                                          "  • Ventaja: Hasta 100x más rápido\n" +
                                          "  • Desventaja: Varios sólidos por DirectShape\n\n" +
                                          "OPCIÓN 3 (ULTRA RÁPIDO): Todo en un solo DirectShape\n" +
                                          "  • Ventaja: Instantáneo\n" +
                                          "  • Desventaja: Todos los sólidos juntos\n\n" +
                                          "PRESIONE ESC EN CUALQUIER MOMENTO PARA CANCELAR";

                configDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Un DirectShape por sólido (LENTO - puede tardar horas)");
                configDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Agrupar en lotes de 50 sólidos (RECOMENDADO - rápido y manejable)");
                configDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Todo en un solo DirectShape (ULTRA RÁPIDO - segundos)");
                configDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
                configDialog.DefaultButton = TaskDialogResult.CommandLink2;

                TaskDialogResult configResult = configDialog.Show();

                if (configResult == TaskDialogResult.Cancel)
                {
                    return Result.Cancelled;
                }

                // Determinar tamaño de lote según la opción seleccionada
                int solidosPorDirectShape = 1;  // Por defecto: uno por uno

                if (configResult == TaskDialogResult.CommandLink2)
                {
                    solidosPorDirectShape = 50;  // Lotes de 50
                }
                else if (configResult == TaskDialogResult.CommandLink3)
                {
                    solidosPorDirectShape = int.MaxValue;  // Todos juntos
                }

                // NUEVA CONFIGURACIÓN: Selección de categoría
                TaskDialog categoryDialog = new TaskDialog("Seleccionar Categoría");
                categoryDialog.MainInstruction = "¿En qué categoría desea crear los DirectShapes?";
                categoryDialog.MainContent = "Seleccione la categoría que mejor represente los elementos importados:";

                categoryDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Modelo genérico (recomendado para geometría general)");
                categoryDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Equipos especiales (para maquinaria y equipos)");
                categoryDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Mobiliario (para muebles y accesorios)");
                categoryDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    "Equipos mecánicos (para sistemas HVAC)");
                categoryDialog.CommonButtons = TaskDialogCommonButtons.Cancel;
                categoryDialog.DefaultButton = TaskDialogResult.CommandLink1;

                TaskDialogResult categoryResult = categoryDialog.Show();

                if (categoryResult == TaskDialogResult.Cancel)
                {
                    return Result.Cancelled;
                }

                // Determinar categoría según la opción seleccionada
                BuiltInCategory selectedCategory = BuiltInCategory.OST_GenericModel; // Por defecto

                if (categoryResult == TaskDialogResult.CommandLink1)
                {
                    selectedCategory = BuiltInCategory.OST_GenericModel;
                }
                else if (categoryResult == TaskDialogResult.CommandLink2)
                {
                    selectedCategory = BuiltInCategory.OST_SpecialityEquipment;
                }
                else if (categoryResult == TaskDialogResult.CommandLink3)
                {
                    selectedCategory = BuiltInCategory.OST_Furniture;
                }
                else if (categoryResult == TaskDialogResult.CommandLink4)
                {
                    selectedCategory = BuiltInCategory.OST_MechanicalEquipment;
                }

                // Crear extractor y procesar con la categoría seleccionada
                var extractor = new DWGBlockExtractor(doc);
                var resultado = extractor.ExtractAndCreateDirectShapes(importInstance, solidosPorDirectShape, selectedCategory);

                // Mostrar resumen
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("═══════════════════════════════════════");
                sb.AppendLine("   EXTRACCIÓN DE BLOQUES DWG COMPLETADA");
                sb.AppendLine("═══════════════════════════════════════\n");
                sb.AppendLine($"✓ Bloques procesados: {resultado.TotalBloques}");
                sb.AppendLine($"✓ DirectShapes creados: {resultado.DirectShapesCreados}");
                sb.AppendLine($"✓ Grupos únicos identificados: {resultado.GruposUnicos}");
                sb.AppendLine($"✓ Piezas idénticas detectadas: {resultado.TotalBloques - resultado.GruposUnicos}");
                sb.AppendLine($"\n⏱ Tiempo procesamiento: {resultado.TiempoProcesamiento:F2}s");
                sb.AppendLine("\n💡 Los DirectShape creados tienen:");
                sb.AppendLine("  • Parámetro 'Comentarios' con ID de grupo");
                sb.AppendLine("  • Parámetro 'ID_Pieza' con firma única");
                sb.AppendLine("  • Parámetro 'Volumen_Original' en m³");
                sb.AppendLine("  • Nombre del bloque original (chino)");

                TaskDialog td = new TaskDialog("Extracción Completada");
                td.MainInstruction = "Proceso finalizado exitosamente";
                td.MainContent = sb.ToString();
                td.Show();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}\n{ex.StackTrace}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Filtro de selección para ImportInstance
    /// </summary>
    public class ImportInstanceSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is ImportInstance;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    /// <summary>
    /// Clase principal para extraer bloques de DWG
    /// </summary>
    public class DWGBlockExtractor
    {
        private Document _doc;
        private StringBuilder _log;
        private Dictionary<string, List<BlockInfo>> _bloquesPorGrupo;
        private int _contadorGrupos = 0;
        private int _bloquesRechazados = 0; // Contador de bloques rechazados por transformaciones inválidas
        private int _instanciasGeometriaProcesadas = 0; // Contador de GeometryInstances procesadas
        private int _nivelMaximoRecursion = 0; // Nivel máximo de recursión alcanzado
        private BuiltInCategory _categoryForDirectShapes = BuiltInCategory.OST_GenericModel; // Categoría para crear DirectShapes

        // Contadores de geometría rechazada
        private int _solidsRechazadosVolumen = 0; // Solids con volumen <= 0 Y sin caras (ya no se usan)
        private int _solidsSinVolumenCapturados = 0; // Solids con volumen = 0 pero con caras válidas (superficies)
        private int _solidsVaciosForzados = 0; // 🆕 Solids vacíos que se forzaron a crear con BoundingBox
        private int _meshesRechazadosSinTriangulos = 0; // Meshes sin triángulos
        private int _curvesEncontradas = 0; // Curves encontradas (no procesadas actualmente)
        private int _otrosGeometriaEncontrados = 0; // Otros tipos de geometría
        private List<string> _diagnosticoSolidsRechazados = new List<string>(); // Para diagnosticar primeros 10 rechazados
        private List<string> _diagnosticoSolidsValidosQueFallan = new List<string>(); // 🆕 Solids con geometría que fallan DirectShape

        // 🔬 DIAGNÓSTICO PROFUNDO: Contadores por nivel de recursión
        private Dictionary<int, int> _geometryInstancesPorNivel = new Dictionary<int, int>();
        private Dictionary<int, int> _symbolGeometryNullPorNivel = new Dictionary<int, int>();
        private Dictionary<int, int> _instanceGeometryNullPorNivel = new Dictionary<int, int>();
        private Dictionary<int, int> _solidosEncontradosPorNivel = new Dictionary<int, int>();

        public DWGBlockExtractor(Document doc)
        {
            _doc = doc;
            _log = new StringBuilder();
            _bloquesPorGrupo = new Dictionary<string, List<BlockInfo>>();
        }

        /// <summary>
        /// Extrae todos los bloques del DWG y crea DirectShapes (SIMPLIFICADO - SIN AGRUPACIÓN)
        /// </summary>
        /// <param name="importInstance">Instancia DWG importada</param>
        /// <param name="solidosPorDirectShape">Número de sólidos por DirectShape (1=uno por uno, 50=lotes, int.MaxValue=todos juntos)</param>
        /// <param name="category">Categoría de Revit para crear los DirectShapes</param>
        public ResultadoExtraccion ExtractAndCreateDirectShapes(ImportInstance importInstance, int solidosPorDirectShape = 1, BuiltInCategory category = BuiltInCategory.OST_GenericModel)
        {
            // Asignar la categoría seleccionada al campo de la clase
            _categoryForDirectShapes = category;

            DateTime inicio = DateTime.Now;

            string modoCreacion = solidosPorDirectShape == 1 ? "Un DirectShape por sólido" :
                                 solidosPorDirectShape == int.MaxValue ? "Todo en un solo DirectShape" :
                                 $"Lotes de {solidosPorDirectShape} sólidos por DirectShape";

            _log.AppendLine("═══════════════════════════════════════");
            _log.AppendLine("   INICIO EXTRACCIÓN BLOQUES DWG");
            _log.AppendLine($"   MODO: {modoCreacion}");
            _log.AppendLine("═══════════════════════════════════════\n");

            // PASO 1: Extraer TODOS los objetos geométricos individuales recursivamente (Solids + Meshes)
            _log.AppendLine("PASO 1: Extrayendo TODOS los objetos geométricos del DWG (Solids + Meshes/Superficies)...");
            _log.AppendLine("  (Esto puede tomar varios minutos si hay miles de piezas)\n");

            List<SolidoIndividual> todosSolidos = new List<SolidoIndividual>();
            GeometryElement geoElement = importInstance.get_Geometry(new Options());

            if (geoElement != null)
            {
                Transform transformInicial = Transform.Identity;
                ExtraerSolidosIndividuales(geoElement, transformInicial, todosSolidos, 0, null, importInstance);
            }

            _log.AppendLine($"\n  ✓ Total objetos geométricos encontrados: {todosSolidos.Count} (Solids + Meshes + Superficies)");
            _log.AppendLine($"  ✓ GeometryInstances procesadas (bloques): {_instanciasGeometriaProcesadas}");
            _log.AppendLine($"  ✓ Nivel máximo recursión alcanzado: {_nivelMaximoRecursion}");
            _log.AppendLine($"  ⚠ Transformaciones rechazadas: {_bloquesRechazados}");

            // Reportar Solids sin volumen capturados (NUEVO)
            if (_solidsSinVolumenCapturados > 0)
            {
                _log.AppendLine($"\n  📋 SUPERFICIES SIN VOLUMEN CAPTURADAS: {_solidsSinVolumenCapturados}");
                _log.AppendLine($"     (Podrían ser chapas, fajas transportadoras, paneles, etc.)");
            }

            // 🆕 Reportar Solids vacíos forzados
            if (_solidsVaciosForzados > 0)
            {
                _log.AppendLine($"\n  ⚡ SOLIDS VACÍOS FORZADOS: {_solidsVaciosForzados}");
                _log.AppendLine($"     → Estos Solids tenían Vol=0, Faces=0, Edges=0");
                _log.AppendLine($"     → Se intentó extraer EDGES → TessellatedShapeBuilder → Model Lines");
                _log.AppendLine($"     → Aparecerán en Revit como 'DWG_Edges_*' o 'DWG_Tessellated_*' o ModelCurves");
            }

            // 🆕 Reportar Curves procesadas
            if (_curvesEncontradas > 0)
            {
                _log.AppendLine($"\n  📐 CURVES PROCESADAS: {_curvesEncontradas}");
                _log.AppendLine($"     → Líneas, polylines, arcos del DWG");
                _log.AppendLine($"     → Aparecerán en Revit como 'DWG_Curve_*' o ModelCurves");
            }

            // Reportar geometría rechazada/no procesada
            if (_solidsRechazadosVolumen > 0 || _meshesRechazadosSinTriangulos > 0 ||
                _otrosGeometriaEncontrados > 0)
            {
                _log.AppendLine($"\n  ANÁLISIS DE GEOMETRÍA NO PROCESADA:");
                if (_solidsRechazadosVolumen > 0)
                    _log.AppendLine($"    • Solids sin volumen Y sin caras (forzados a crear): {_solidsRechazadosVolumen}");
                if (_meshesRechazadosSinTriangulos > 0)
                    _log.AppendLine($"    • Meshes sin triángulos: {_meshesRechazadosSinTriangulos}");
                if (_otrosGeometriaEncontrados > 0)
                    _log.AppendLine($"    • Otros tipos de geometría: {_otrosGeometriaEncontrados}");

                int totalNoProcessado = _meshesRechazadosSinTriangulos + _otrosGeometriaEncontrados;
                _log.AppendLine($"    → TOTAL REALMENTE NO PROCESADO: {totalNoProcessado}");

                // DIAGNÓSTICO DETALLADO de Solids rechazados
                if (_diagnosticoSolidsRechazados.Count > 0)
                {
                    _log.AppendLine($"\n  🔬 DIAGNÓSTICO DETALLADO (Primeros {_diagnosticoSolidsRechazados.Count} Solids rechazados):");
                    foreach (string info in _diagnosticoSolidsRechazados)
                    {
                        _log.AppendLine($"    {info}");
                    }
                }
            }

            // 🆕 DIAGNÓSTICO CRÍTICO: Solids VÁLIDOS que FALLAN al crear DirectShape
            if (_diagnosticoSolidsValidosQueFallan.Count > 0)
            {
                _log.AppendLine($"\n  🔴 DIAGNÓSTICO CRÍTICO: Solids VÁLIDOS que FALLAN DirectShape ({_diagnosticoSolidsValidosQueFallan.Count} encontrados):");
                _log.AppendLine($"  ════════════════════════════════════════════════════════════════════");
                _log.AppendLine($"  ESTOS son los elementos que tienen geometría REAL pero Revit rechaza:");
                foreach (string diagnostico in _diagnosticoSolidsValidosQueFallan)
                {
                    _log.AppendLine(diagnostico);
                }
                _log.AppendLine($"  ════════════════════════════════════════════════════════════════════");
                _log.AppendLine($"  💡 POSIBLES CAUSAS:");
                _log.AppendLine($"     1. Geometría non-manifold (bordes no cerrados)");
                _log.AppendLine($"     2. Caras auto-intersectadas o degeneradas");
                _log.AppendLine($"     3. Tolerancias muy pequeñas en el modelo CAD");
                _log.AppendLine($"     4. Transforms con determinante negativo (reflexiones)");
                _log.AppendLine($"  💡 SOLUCIÓN RECOMENDADA:");
                _log.AppendLine($"     → Revisar estos objetos en el DWG original y simplificar/reparar geometría");
                _log.AppendLine($"     → O exportar el DWG con mayor tesselación desde SolidWorks");
            }

            // 🔬 DIAGNÓSTICO POR NIVEL DE RECURSIÓN
            _log.AppendLine($"\n  🔍 ANÁLISIS DE PROFUNDIDAD DE BLOQUES (Por Nivel de Recursión):");
            _log.AppendLine($"  ╔════════════════════════════════════════════════════════════════════╗");
            _log.AppendLine($"  ║ Nivel │ GeometryInstances │ SymbolGeo NULL │ InstanceGeo NULL │ Solids ║");
            _log.AppendLine($"  ╠═══════╪═══════════════════╪════════════════╪══════════════════╪════════╣");

            for (int nivel = 0; nivel <= _nivelMaximoRecursion; nivel++)
            {
                int geoInstances = _geometryInstancesPorNivel.ContainsKey(nivel) ? _geometryInstancesPorNivel[nivel] : 0;
                int symbolNull = _symbolGeometryNullPorNivel.ContainsKey(nivel) ? _symbolGeometryNullPorNivel[nivel] : 0;
                int instanceNull = _instanceGeometryNullPorNivel.ContainsKey(nivel) ? _instanceGeometryNullPorNivel[nivel] : 0;
                int solids = _solidosEncontradosPorNivel.ContainsKey(nivel) ? _solidosEncontradosPorNivel[nivel] : 0;

                string advertencia = "";
                if (symbolNull > 0 && instanceNull > 0)
                    advertencia = " ⚠ BLOQUES PERDIDOS";
                else if (symbolNull > 0)
                    advertencia = " (fallback usado)";

                _log.AppendLine($"  ║   {nivel,2}  │      {geoInstances,6}       │      {symbolNull,6}      │      {instanceNull,6}      │  {solids,6} ║{advertencia}");
            }

            _log.AppendLine($"  ╚════════════════════════════════════════════════════════════════════╝");

            // Resumen de bloques perdidos
            int bloquesTotalesPerdidos = 0;
            foreach (var kvp in _instanceGeometryNullPorNivel)
            {
                bloquesTotalesPerdidos += kvp.Value;
            }

            if (bloquesTotalesPerdidos > 0)
            {
                _log.AppendLine($"\n  ❌ ADVERTENCIA CRÍTICA: {bloquesTotalesPerdidos} bloques NO procesables encontrados!");
                _log.AppendLine($"     → Estos bloques tienen GetSymbolGeometry() = NULL Y GetInstanceGeometry() = NULL");
                _log.AppendLine($"     → Estos son los elementos FALTANTES que ves en Revit");
                _log.AppendLine($"     → Causa probable: Bloques con geometría externa no embedida en el DWG");
            }

            if (todosSolidos.Count == 0)
            {
                _log.AppendLine("  ⚠ No se encontraron objetos geométricos con geometría válida");
                return new ResultadoExtraccion
                {
                    TotalBloques = 0,
                    DirectShapesCreados = 0,
                    GruposUnicos = 0,
                    TiempoProcesamiento = 0,
                    RutaLog = ""
                };
            }

            _log.AppendLine();

            // PASO 2: Procesar TODOS los objetos individuales (SIN agrupamiento)
            _log.AppendLine("PASO 2: Preparando para crear DirectShapes individuales...");
            _log.AppendLine($"  ✓ Total objetos a procesar: {todosSolidos.Count}");
            _log.AppendLine($"  ✓ Modo: Un DirectShape por objeto individual\n");

            // PASO 3: Crear UN DirectShape por cada objeto individual
            _log.AppendLine("PASO 3: Creando DirectShapes individuales...");

            int directShapesCreados = 0;
            int solidosProcessados = 0;

            using (Transaction trans = new Transaction(_doc, "Crear DirectShapes desde DWG"))
            {
                trans.Start();

                // Iterar sobre CADA objeto individual (sin agrupamiento)
                for (int i = 0; i < todosSolidos.Count; i++)
                {
                    SolidoIndividual solidoIndividual = todosSolidos[i];

                    bool creado = false;

                    // 🆕 MANEJO ESPECIAL para CURVES (líneas/polylines/arcos)
                    if (solidoIndividual.EsCurve)
                    {
                        // Intentar crear ModelCurve o DirectShape desde Curve
                        creado = CrearDirectShapeParaCurve(solidoIndividual, directShapesCreados);

                        if (creado)
                        {
                            directShapesCreados++;
                            solidosProcessados++;

                            if (directShapesCreados % 500 == 0)
                            {
                                _log.AppendLine($"  → {directShapesCreados} DirectShapes creados ({solidosProcessados}/{todosSolidos.Count} objetos) [incluye {_curvesEncontradas} curves]");
                            }
                        }
                    }
                    // 🆕 MANEJO ESPECIAL para Solids vacíos forzados
                    else if (solidoIndividual.EsSolidVacio)
                    {
                        // Intentar crear geometría visual para Solids vacíos
                        creado = CrearDirectShapeParaSolidVacio(solidoIndividual, directShapesCreados);

                        if (creado)
                        {
                            directShapesCreados++;
                            solidosProcessados++;

                            if (directShapesCreados % 500 == 0)
                            {
                                _log.AppendLine($"  → {directShapesCreados} DirectShapes creados ({solidosProcessados}/{todosSolidos.Count} objetos) [incluye {_solidsVaciosForzados} vacíos]");
                            }
                        }
                    }
                    else
                    {
                        // Procesamiento NORMAL para Solids/Meshes válidos
                        try
                        {
                            // Crear DirectShape directamente para este objeto individual
                            DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));

                            // Aplicar transformación si es necesario
                            GeometryObject geoTransformado = solidoIndividual.Geometria;
                            if (!solidoIndividual.Transform.IsIdentity && geoTransformado is Solid solid)
                            {
                                geoTransformado = SolidUtils.CreateTransformed(solid, solidoIndividual.Transform);
                            }
                            else if (!solidoIndividual.Transform.IsIdentity && geoTransformado is Mesh mesh)
                            {
                                geoTransformado = mesh.get_Transformed(solidoIndividual.Transform);
                            }

                            // Asignar geometría
                            ds.SetShape(new List<GeometryObject> { geoTransformado });

                            // Asignar nombre simple
                            ds.Name = $"DWG_Element_{directShapesCreados:D6}";

                            directShapesCreados++;
                            solidosProcessados++;

                            // Mostrar progreso cada 100 DirectShapes
                            if (directShapesCreados % 100 == 0)
                            {
                                _log.AppendLine($"  → {directShapesCreados} DirectShapes creados ({solidosProcessados}/{todosSolidos.Count} objetos)");
                            }
                        }
                        catch (Exception exDS)
                        {
                            _log.AppendLine($"  ✗ Error creando DirectShape individual: {exDS.Message}");
                        }
                    }
                }

                trans.Commit();
            }

            _log.AppendLine($"\n✓ DirectShapes creados: {directShapesCreados}");
            _log.AppendLine($"✓ Objetos procesados: {solidosProcessados}");
            _log.AppendLine($"⚠ Objetos no procesados: {todosSolidos.Count - solidosProcessados}");

            if (todosSolidos.Count - solidosProcessados > 0)
            {
                int objetosRechazados = todosSolidos.Count - solidosProcessados;
                double porcentajeExito = (solidosProcessados * 100.0) / todosSolidos.Count;
                _log.AppendLine($"\n⚠ GEOMETRÍA INVÁLIDA: {objetosRechazados} objetos rechazados por Revit ({porcentajeExito:F1}% importado exitosamente)");
                _log.AppendLine($"  → Revit rechaza estos objetos porque no cumplen criterios internos de DirectShape");
                _log.AppendLine($"  → Posibles causas: geometría degenerada, non-manifold, o tolerancias muy pequeñas");
                _log.AppendLine($"  → Solución: Exportar DWG con mayor tesselación o simplificar geometría en SolidWorks");
                _log.AppendLine($"  → Vea mensajes '✗✗✗' arriba para detalles de cada objeto rechazado");
            }

            if (_bloquesRechazados > 0)
            {
                _log.AppendLine($"\n⚠ Objetos rechazados (transformaciones inválidas): {_bloquesRechazados}");
                _log.AppendLine($"  (determinante cercano a 0 - transformaciones degeneradas)");
            }

            DateTime fin = DateTime.Now;
            double tiempoTotal = (fin - inicio).TotalSeconds;

            // Guardar log en archivo
            string rutaLog = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"DWG_Extraction_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            System.IO.File.WriteAllText(rutaLog, _log.ToString());

            return new ResultadoExtraccion
            {
                TotalBloques = todosSolidos.Count,
                DirectShapesCreados = directShapesCreados,
                GruposUnicos = _bloquesPorGrupo.Count,
                TiempoProcesamiento = tiempoTotal,
                RutaLog = rutaLog
            };
        }

        /// <summary>
        /// Filtra bloques duplicados que están en la misma posición exacta
        /// </summary>
        private List<BlockInfo> FiltrarDuplicadosPorPosicion(List<BlockInfo> bloques)
        {
            List<BlockInfo> bloquesUnicos = new List<BlockInfo>();
            const double toleranciaPosicion = 0.001; // ~3mm en pies

            foreach (var bloque in bloques)
            {
                bool esDuplicado = false;

                // Comparar con bloques ya agregados
                foreach (var bloqueExistente in bloquesUnicos)
                {
                    // Comparar posiciones (Origin de Transform)
                    XYZ posActual = bloque.Transform.Origin;
                    XYZ posExistente = bloqueExistente.Transform.Origin;

                    double distancia = posActual.DistanceTo(posExistente);

                    if (distancia < toleranciaPosicion)
                    {
                        // Mismo lugar, es duplicado
                        esDuplicado = true;
                        break;
                    }
                }

                if (!esDuplicado)
                {
                    bloquesUnicos.Add(bloque);
                }
            }

            return bloquesUnicos;
        }

        /// <summary>
        /// Valida transformación - MODO PERMISIVO (solo rechaza transformaciones completamente inválidas)
        /// </summary>
        private Transform ValidarYNormalizarTransform(Transform transform, out bool esValido)
        {
            esValido = true;

            try
            {
                // Calcular determinante para detectar transformaciones degeneradas
                double determinante = transform.Determinant;

                // SOLO rechazar si el determinante es cercano a cero (transformación degenerada)
                if (Math.Abs(determinante) < 0.000001)
                {
                    esValido = false;
                    return transform;
                }

                // NO RECHAZAR escalas extremas - dejar que Revit las maneje
                // NO RECHAZAR reflexiones - dejar que se creen y el usuario decide
                // MODO PERMISIVO: aceptar casi todo

                // Transformación es válida
                return transform;
            }
            catch (Exception ex)
            {
                esValido = false;
                return transform;
            }
        }

        /// <summary>
        /// Extrae bloques recursivamente manejando anidación múltiple
        /// </summary>
        private void ExtraerBloquesRecursivo(GeometryElement geoElement, Transform transformAcumulado,
            List<BlockInfo> bloques, int nivel)
        {
            if (geoElement == null) return;

            string indentacion = new string(' ', nivel * 2);

            foreach (GeometryObject geoObj in geoElement)
            {
                // Caso 1: GeometryInstance (bloque anidado)
                if (geoObj is GeometryInstance geoInstance)
                {
                    // Combinar transformaciones acumuladas
                    Transform transformLocal = geoInstance.Transform;
                    Transform transformNuevo = transformAcumulado.Multiply(transformLocal);

                    // Validar y normalizar la transformación acumulada
                    bool transformValido;
                    Transform transformValidado = ValidarYNormalizarTransform(transformNuevo, out transformValido);

                    // Si la transformación no es válida, omitir este bloque
                    if (!transformValido)
                    {
                        _bloquesRechazados++;
                        continue;
                    }

                    // Obtener geometría del símbolo (definición del bloque)
                    GeometryElement symbolGeometry = geoInstance.GetSymbolGeometry();

                    if (symbolGeometry != null)
                    {
                        // Extraer información del bloque con la transformación validada
                        BlockInfo bloque = ExtraerInformacionBloque(geoInstance, transformValidado);

                        if (bloque != null && bloque.TieneSolidos)
                        {
                            bloques.Add(bloque);

                            // Solo mostrar en log cada 100 bloques para no saturar
                            if (nivel == 0 && bloques.Count % 100 == 0)
                            {
                                _log.AppendLine($"  ... {bloques.Count} bloques procesados");
                            }
                        }

                        // Continuar recursión para bloques anidados con transformación validada
                        ExtraerBloquesRecursivo(symbolGeometry, transformValidado, bloques, nivel + 1);
                    }
                }
                // Caso 2: Solid directo (geometría base)
                else if (geoObj is Solid solid)
                {
                    try
                    {
                        double volumen = solid.Volume;
                        if (volumen > 0.0001)
                        {
                            _log.AppendLine($"{indentacion}└─ Sólido directo | Vol: {volumen:F4} ft³");
                        }
                    }
                    catch
                    {
                        // Sólido directo inválido, ignorar
                    }
                }
            }
        }

        /// <summary>
        /// Extrae información de un bloque individual
        /// </summary>
        private BlockInfo ExtraerInformacionBloque(GeometryInstance geoInstance, Transform transform)
        {
            try
            {
                GeometryElement symbolGeometry = geoInstance.GetSymbolGeometry();
                if (symbolGeometry == null) return null;

                BlockInfo bloque = new BlockInfo
                {
                    // NOTA: El nombre del bloque DWG está embebido en la geometría
                    // No se puede extraer fácilmente desde la API de Revit para ImportInstance
                    // Los nombres chinos (图7s248ATF, etc.) estarían en la definición del bloque
                    // La agrupación se hará por firma geométrica, no por nombre
                    Nombre = "DWG_Block",
                    Transform = transform
                };

                // Extraer sólidos
                List<Solid> solidos = new List<Solid>();
                ExtraerSolidosRecursivo(symbolGeometry, solidos);

                if (solidos.Count == 0) return null;

                bloque.Solidos = solidos;

                // Calcular propiedades geométricas
                CalcularPropiedadesGeometricas(bloque);

                return bloque;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ⚠ Error extrayendo bloque: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extrae todos los sólidos de una geometría (puede tener múltiples niveles)
        /// </summary>
        private void ExtraerSolidosRecursivo(GeometryElement geoElement, List<Solid> solidos)
        {
            if (geoElement == null) return;

            foreach (GeometryObject geoObj in geoElement)
            {
                if (geoObj is Solid solid)
                {
                    try
                    {
                        // Validar que el sólido sea válido antes de acceder a Volume
                        double volumen = solid.Volume;
                        // Filtro MUY permisivo - aceptar incluso piezas muy pequeñas
                        if (volumen > 0.000000001)
                        {
                            solidos.Add(solid);
                        }
                    }
                    catch
                    {
                        // Sólido inválido o degenerado, ignorar
                    }
                }
                else if (geoObj is GeometryInstance nestedInstance)
                {
                    try
                    {
                        GeometryElement nestedGeometry = nestedInstance.GetSymbolGeometry();
                        ExtraerSolidosRecursivo(nestedGeometry, solidos);
                    }
                    catch
                    {
                        // Geometría anidada inválida, ignorar
                    }
                }
            }
        }

        /// <summary>
        /// Calcula propiedades geométricas del bloque
        /// </summary>
        private void CalcularPropiedadesGeometricas(BlockInfo bloque)
        {
            // Volumen total (en pies cúbicos, convertir a m³)
            // Proteger contra sólidos inválidos
            double volumenFt3 = 0.0;
            foreach (Solid solid in bloque.Solidos)
            {
                try
                {
                    volumenFt3 += solid.Volume;
                }
                catch
                {
                    // Sólido con volumen inaccesible, ignorar
                }
            }
            bloque.Volumen = volumenFt3 * 0.0283168; // ft³ a m³

            // BoundingBox combinado de todos los sólidos
            BoundingBoxXYZ bboxCombinado = null;

            foreach (Solid solid in bloque.Solidos)
            {
                try
                {
                    BoundingBoxXYZ bbox = solid.GetBoundingBox();
                    if (bbox != null)
                    {
                        if (bboxCombinado == null)
                        {
                            bboxCombinado = bbox;
                        }
                        else
                        {
                            // Expandir bbox combinado
                            bboxCombinado.Min = new XYZ(
                                Math.Min(bboxCombinado.Min.X, bbox.Min.X),
                                Math.Min(bboxCombinado.Min.Y, bbox.Min.Y),
                                Math.Min(bboxCombinado.Min.Z, bbox.Min.Z)
                            );
                            bboxCombinado.Max = new XYZ(
                                Math.Max(bboxCombinado.Max.X, bbox.Max.X),
                                Math.Max(bboxCombinado.Max.Y, bbox.Max.Y),
                                Math.Max(bboxCombinado.Max.Z, bbox.Max.Z)
                            );
                        }
                    }
                }
                catch { }
            }

            if (bboxCombinado != null)
            {
                // Dimensiones en mm (ordenadas para comparación)
                double dimX = (bboxCombinado.Max.X - bboxCombinado.Min.X) * 304.8;
                double dimY = (bboxCombinado.Max.Y - bboxCombinado.Min.Y) * 304.8;
                double dimZ = (bboxCombinado.Max.Z - bboxCombinado.Min.Z) * 304.8;

                var dimensiones = new[] { dimX, dimY, dimZ }.OrderByDescending(d => d).ToArray();

                bloque.BBoxLargo = dimensiones[0];
                bloque.BBoxAncho = dimensiones[1];
                bloque.BBoxAlto = dimensiones[2];
            }

            // Información adicional de geometría
            bloque.NumeroSolidos = bloque.Solidos.Count;

            // Calcular hash de geometría (número de caras, aristas, vértices)
            int totalCaras = 0;
            int totalAristas = 0;

            foreach (Solid solid in bloque.Solidos)
            {
                try
                {
                    totalCaras += solid.Faces.Size;
                    totalAristas += solid.Edges.Size;
                }
                catch
                {
                    // Sólido con topología inaccesible, ignorar
                }
            }

            bloque.NumeroCaras = totalCaras;
            bloque.NumeroAristas = totalAristas;
        }

        /// <summary>
        /// Agrupa bloques por firma geométrica
        /// </summary>
        private void AgruparBloquesPorFirma(List<BlockInfo> bloques)
        {
            foreach (var bloque in bloques)
            {
                // Generar firma única basada en geometría
                string firma = GenerarFirmaGeometrica(bloque);
                bloque.FirmaGeometrica = firma;

                // Buscar grupo existente con firma similar
                string grupoId = null;

                foreach (var grupo in _bloquesPorGrupo)
                {
                    if (grupo.Value.Count > 0)
                    {
                        BlockInfo bloqueReferencia = grupo.Value[0];

                        if (SonGeometricamenteIguales(bloque, bloqueReferencia))
                        {
                            grupoId = grupo.Key;
                            break;
                        }
                    }
                }

                // Si no se encontró grupo, crear uno nuevo
                if (grupoId == null)
                {
                    _contadorGrupos++;
                    grupoId = $"GRUPO_{_contadorGrupos:D4}";
                    _bloquesPorGrupo[grupoId] = new List<BlockInfo>();
                }

                _bloquesPorGrupo[grupoId].Add(bloque);
            }
        }

        /// <summary>
        /// Genera firma geométrica única del bloque
        /// </summary>
        private string GenerarFirmaGeometrica(BlockInfo bloque)
        {
            // Firma basada en: nombre, volumen, dimensiones, topología
            string firma = $"{bloque.Nombre}_{bloque.Volumen:F6}_{bloque.BBoxLargo:F2}x{bloque.BBoxAncho:F2}x{bloque.BBoxAlto:F2}_S{bloque.NumeroSolidos}_C{bloque.NumeroCaras}_A{bloque.NumeroAristas}";
            return firma;
        }

        /// <summary>
        /// Compara dos bloques geométricamente (con tolerancia)
        /// </summary>
        private bool SonGeometricamenteIguales(BlockInfo bloque1, BlockInfo bloque2)
        {
            const double toleranciaVolumen = 0.000001; // 1 mm³
            const double toleranciaDimension = 0.1; // 0.1 mm

            // NOTA: No comparamos por nombre porque no podemos extraerlo del ImportInstance
            // La agrupación se basa completamente en geometría

            // 1. Volumen similar
            if (Math.Abs(bloque1.Volumen - bloque2.Volumen) > toleranciaVolumen)
                return false;

            // 2. Dimensiones similares
            if (Math.Abs(bloque1.BBoxLargo - bloque2.BBoxLargo) > toleranciaDimension)
                return false;
            if (Math.Abs(bloque1.BBoxAncho - bloque2.BBoxAncho) > toleranciaDimension)
                return false;
            if (Math.Abs(bloque1.BBoxAlto - bloque2.BBoxAlto) > toleranciaDimension)
                return false;

            // 3. Misma topología
            if (bloque1.NumeroSolidos != bloque2.NumeroSolidos)
                return false;
            if (bloque1.NumeroCaras != bloque2.NumeroCaras)
                return false;
            if (bloque1.NumeroAristas != bloque2.NumeroAristas)
                return false;

            return true;
        }

        /// <summary>
        /// Crea DirectShape desde un sólido individual (divide completamente las piezas)
        /// </summary>
        private bool CrearDirectShapeDesdeSolido(Solid solid, Transform transform, string nombreBloque, string grupoId, int indiceSolido)
        {
            try
            {
                // Validar que el sólido tenga volumen
                double volumen = 0;
                try
                {
                    volumen = solid.Volume;
                }
                catch
                {
                    return false; // Sólido inválido
                }

                if (volumen < 0.0001)
                {
                    return false; // Sólido degenerado
                }

                // Transformar el sólido
                Solid solidTransformado = null;
                try
                {
                    solidTransformado = SolidUtils.CreateTransformed(solid, transform);
                }
                catch
                {
                    return false; // No se pudo transformar
                }

                if (solidTransformado == null || solidTransformado.Volume < 0.0001)
                {
                    return false; // Transformación resultó en sólido inválido
                }

                // Crear DirectShape con el sólido único
                DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));

                // Asignar geometría (solo este sólido)
                List<GeometryObject> geometria = new List<GeometryObject> { solidTransformado };
                ds.SetShape(geometria);

                // Asignar nombre descriptivo (nombre del bloque + índice del sólido dentro del bloque)
                ds.Name = $"{nombreBloque}_Solid{indiceSolido}_{grupoId}";

                // Asignar parámetro Comentarios (ID del grupo para identificar piezas idénticas)
                Parameter comentarios = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comentarios != null && !comentarios.IsReadOnly)
                {
                    comentarios.Set(grupoId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ✗ Error creando DirectShape desde sólido: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Crea DirectShape desde BlockInfo (MÉTODO LEGACY - ya no se usa, ahora se crean por sólido individual)
        /// </summary>
        private bool CrearDirectShapeDesdeBloque(BlockInfo bloque, string grupoId)
        {
            try
            {
                // Crear DirectShape
                DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));

                // Aplicar transformación a los sólidos
                List<GeometryObject> geometriaTransformada = new List<GeometryObject>();

                foreach (Solid solid in bloque.Solidos)
                {
                    try
                    {
                        // Validar que el sólido tenga volumen antes de transformar - filtro MUY permisivo
                        if (solid.Volume > 0.000000001)
                        {
                            Solid solidTransformado = SolidUtils.CreateTransformed(solid, bloque.Transform);
                            if (solidTransformado != null && solidTransformado.Volume > 0.000000001)
                            {
                                geometriaTransformada.Add(solidTransformado);
                            }
                        }
                    }
                    catch
                    {
                        // Sólido no se pudo transformar, ignorar
                    }
                }

                // Si no hay geometría válida, cancelar creación
                if (geometriaTransformada.Count == 0)
                {
                    return false;
                }

                ds.SetShape(geometriaTransformada);

                // Asignar nombre (nombre del bloque en chino)
                ds.Name = $"{bloque.Nombre}_{grupoId}";

                // Asignar parámetro Comentarios (ID del grupo)
                Parameter comentarios = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comentarios != null && !comentarios.IsReadOnly)
                {
                    comentarios.Set(grupoId);
                }

                // Intentar asignar parámetros adicionales (requieren parámetros compartidos)
                // Estos se pueden crear posteriormente si se necesitan
                // Para este ejemplo, usamos solo Comentarios

                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ✗ Error creando DirectShape: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extrae TODOS los objetos geométricos individuales (Solids + Meshes) - RECURSIÓN PROFUNDA
        /// </summary>
        private void ExtraerSolidosIndividuales(GeometryElement geoElement, Transform transformAcumulado, List<SolidoIndividual> solidos, int nivel, List<string> jerarquiaBloques = null, Element elementoPadre = null)
        {
            if (geoElement == null) return;

            // Inicializar jerarquía si es null (primera llamada)
            if (jerarquiaBloques == null)
                jerarquiaBloques = new List<string>();

            // Actualizar nivel máximo de recursión
            if (nivel > _nivelMaximoRecursion)
            {
                _nivelMaximoRecursion = nivel;
            }

            // Log niveles de recursión profundos
            if (nivel > 10 && nivel % 5 == 0)
            {
                _log.AppendLine($"  → Nivel recursión {nivel} alcanzado - procesando bloques muy anidados");
            }

            foreach (GeometryObject geoObj in geoElement)
            {
                if (geoObj is GeometryInstance geoInstance)
                {
                    _instanciasGeometriaProcesadas++;

                    // 🔬 Contar GeometryInstances por nivel
                    if (!_geometryInstancesPorNivel.ContainsKey(nivel))
                        _geometryInstancesPorNivel[nivel] = 0;
                    _geometryInstancesPorNivel[nivel]++;

                    // Combinar transformaciones
                    Transform transformLocal = geoInstance.Transform;
                    Transform transformNuevo = transformAcumulado.Multiply(transformLocal);

                    // Validar transformación
                    bool transformValido;
                    Transform transformValidado = ValidarYNormalizarTransform(transformNuevo, out transformValido);

                    if (!transformValido)
                    {
                        _bloquesRechazados++;
                        continue;
                    }

                    // CRÍTICO: Usar GetSymbolGeometry() para RECURSIÓN PROFUNDA en bloques anidados
                    // GetSymbolGeometry() contiene la definición del bloque y permite entrar en subensamblajes
                    // IMPORTANTE: GetSymbolGeometry() devuelve geometría en espacio LOCAL del símbolo
                    // Por lo tanto, DEBEMOS acumular el Transform de la instancia

                    GeometryElement symbolGeometry = geoInstance.GetSymbolGeometry();
                    if (symbolGeometry != null)
                    {
                        // Contar objetos en symbolGeometry
                        int objetosEnSymbol = 0;
                        foreach (GeometryObject obj in symbolGeometry) { objetosEnSymbol++; }

                        // 🔬 LOGGING MEJORADO: Mostrar TODOS los niveles
                        if (nivel >= 2 && objetosEnSymbol > 0)
                        {
                            _log.AppendLine($"  📊 Nivel {nivel}: GetSymbolGeometry() OK - {objetosEnSymbol} objetos, recursando a nivel {nivel + 1}");
                        }

                        // Obtener el Transform de esta instancia de bloque
                        Transform instTransform = geoInstance.Transform;

                        // ACUMULAR transformaciones: nuevo = actual × instancia
                        Transform nuevoTransformAcumulado = transformAcumulado.Multiply(instTransform);

                        // Pasar el transform acumulado a la recursión - SIN LÍMITE de profundidad
                        ExtraerSolidosIndividuales(symbolGeometry, nuevoTransformAcumulado, solidos, nivel + 1, jerarquiaBloques, elementoPadre);
                    }
                    else
                    {
                        // 🔬 Contar GetSymbolGeometry NULL por nivel
                        if (!_symbolGeometryNullPorNivel.ContainsKey(nivel))
                            _symbolGeometryNullPorNivel[nivel] = 0;
                        _symbolGeometryNullPorNivel[nivel]++;

                        // Fallback para CUALQUIER nivel: intentar GetInstanceGeometry si no hay symbol
                        if (nivel <= 5)  // Solo mostrar log para primeros niveles
                            _log.AppendLine($"  ⚠ Nivel {nivel}: GetSymbolGeometry() = NULL, intentando GetInstanceGeometry()");

                        GeometryElement instanceGeometry = geoInstance.GetInstanceGeometry();
                        if (instanceGeometry != null)
                        {
                            int objetosEnInstance = 0;
                            foreach (GeometryObject obj in instanceGeometry) { objetosEnInstance++; }

                            if (nivel <= 5)  // Solo mostrar log para primeros niveles
                                _log.AppendLine($"  ✓ Nivel {nivel}: GetInstanceGeometry() OK - {objetosEnInstance} objetos, recursando a nivel {nivel + 1}");

                            // GetInstanceGeometry() ya devuelve geometría en espacio mundial
                            // Usar el transform actual sin multiplicar
                            ExtraerSolidosIndividuales(instanceGeometry, transformAcumulado, solidos, nivel + 1, jerarquiaBloques, elementoPadre);
                        }
                        else
                        {
                            // 🔬 Contar GetInstanceGeometry NULL por nivel
                            if (!_instanceGeometryNullPorNivel.ContainsKey(nivel))
                                _instanceGeometryNullPorNivel[nivel] = 0;
                            _instanceGeometryNullPorNivel[nivel]++;

                            if (nivel <= 5)  // Solo mostrar log para primeros niveles
                                _log.AppendLine($"  ✗ Nivel {nivel}: GetInstanceGeometry() = NULL también - GeometryInstance NO PROCESABLE");
                        }
                    }
                }
                else if (geoObj is Solid solid)
                {
                    // 🔬 Contar Solids por nivel
                    if (!_solidosEncontradosPorNivel.ContainsKey(nivel))
                        _solidosEncontradosPorNivel[nivel] = 0;
                    _solidosEncontradosPorNivel[nivel]++;

                    try
                    {
                        double volumen = solid.Volume;

                        // ESTRATEGIA: Aceptar TODOS los Solids, incluso con volumen = 0
                        // Los Solids sin volumen podrían ser superficies, chapas, fajas transportadoras

                        if (volumen > 0.000000001)
                        {
                            // Sólido con volumen válido - agregar normalmente
                            solidos.Add(new SolidoIndividual
                            {
                                Geometria = solid,
                                Transform = transformAcumulado
                            });

                            // Log cada 1000 sólidos
                            if (solidos.Count % 1000 == 0)
                            {
                                _log.AppendLine($"  ... {solidos.Count} objetos geométricos extraídos");
                            }
                        }
                        else
                        {
                            // NUEVO: Solid con volumen = 0 o muy pequeño
                            // Verificar si tiene caras (superficie válida)
                            if (solid.Faces != null && solid.Faces.Size > 0)
                            {
                                // Es una superficie válida sin volumen (chapa, faja, panel, etc.)
                                // Agregar como Solid - luego lo convertiremos a Mesh si falla DirectShape
                                solidos.Add(new SolidoIndividual
                                {
                                    Geometria = solid,
                                    Transform = transformAcumulado
                                });

                                _solidsSinVolumenCapturados++;

                                // Log cada 1000 objetos
                                if (solidos.Count % 1000 == 0)
                                {
                                    _log.AppendLine($"  ... {solidos.Count} objetos geométricos extraídos (incluye {_solidsSinVolumenCapturados} superficies sin volumen)");
                                }
                            }
                            else
                            {
                                // 🆕 FORZAR CREACIÓN de Solids vacíos (Vol=0, Faces=0)
                                // Estos son los 4,675 elementos "perdidos" que el usuario necesita ver

                                try
                                {
                                    // Intentar obtener BoundingBox del Solid
                                    BoundingBoxXYZ bbox = solid.GetBoundingBox();

                                    if (bbox != null && bbox.Min != null && bbox.Max != null)
                                    {
                                        // Tiene BoundingBox - crear geometría visual (box wireframe)
                                        // Agregar como marcador especial que luego convertiremos a mesh
                                        solidos.Add(new SolidoIndividual
                                        {
                                            Geometria = solid, // Guardamos el Solid original
                                            Transform = transformAcumulado,
                                            EsSolidVacio = true // Flag especial
                                        });

                                        _solidsVaciosForzados++;

                                        // Log cada 500 forzados
                                        if (_solidsVaciosForzados % 500 == 0)
                                        {
                                            _log.AppendLine($"  ⚡ {_solidsVaciosForzados} Solids vacíos FORZADOS con BoundingBox");
                                        }
                                    }
                                    else
                                    {
                                        // Sin BoundingBox - FORZAR con Transform.Origin
                                        // Crear un marcador puntual en la posición del Transform
                                        solidos.Add(new SolidoIndividual
                                        {
                                            Geometria = solid,
                                            Transform = transformAcumulado,
                                            EsSolidVacio = true,
                                            UsarSoloPunto = true // Flag para crear solo un punto/marcador
                                        });

                                        _solidsVaciosForzados++;
                                        _solidsRechazadosVolumen++; // También contamos como rechazado para diagnóstico

                                        // DIAGNÓSTICO: Guardar info de los primeros 10
                                        if (_diagnosticoSolidsRechazados.Count < 10)
                                        {
                                            int numCaras = (solid.Faces != null) ? solid.Faces.Size : -1;
                                            int numEdges = (solid.Edges != null) ? solid.Edges.Size : -1;
                                            _diagnosticoSolidsRechazados.Add(
                                                $"Solid #{_solidsVaciosForzados}: Vol={solid.Volume:F9}, Faces={numCaras}, Edges={numEdges}, SurfaceArea={solid.SurfaceArea:F6} [FORZADO SIN BBOX]"
                                            );
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Si falla obtener BoundingBox, intentar forzar de todos modos
                                    solidos.Add(new SolidoIndividual
                                    {
                                        Geometria = solid,
                                        Transform = transformAcumulado,
                                        EsSolidVacio = true,
                                        UsarSoloPunto = true
                                    });

                                    _solidsVaciosForzados++;
                                    _solidsRechazadosVolumen++;

                                    if (_diagnosticoSolidsRechazados.Count < 10)
                                    {
                                        _diagnosticoSolidsRechazados.Add(
                                            $"Solid #{_solidsVaciosForzados}: EXCEPCIÓN al obtener BoundingBox: {ex.Message} [FORZADO COMO PUNTO]"
                                        );
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Sólido completamente inválido - ya no rechazamos, forzamos creación
                        _solidsRechazadosVolumen++;
                    }
                }
                else if (geoObj is Mesh mesh)
                {
                    try
                    {
                        // CAPTURAR MESHES - Superficies sin volumen
                        // Validar que tenga triángulos
                        if (mesh.NumTriangles > 0)
                        {
                            // Agregar mesh a la lista CON la transformación acumulada
                            solidos.Add(new SolidoIndividual
                            {
                                Geometria = mesh,
                                Transform = transformAcumulado
                            });

                            // Log cada 1000 objetos
                            if (solidos.Count % 1000 == 0)
                            {
                                _log.AppendLine($"  ... {solidos.Count} objetos geométricos extraídos (incluye meshes)");
                            }
                        }
                        else
                        {
                            // Mesh rechazado por no tener triángulos
                            _meshesRechazadosSinTriangulos++;
                        }
                    }
                    catch
                    {
                        // Mesh inválido
                        _meshesRechazadosSinTriangulos++;
                    }
                }
                else if (geoObj is Curve curve)
                {
                    // 🆕 PROCESAR CURVES (polylines, líneas, arcos)
                    // Las estructuras metálicas y perfiles podrían estar como curves
                    _curvesEncontradas++;

                    try
                    {
                        // Agregar la Curve a la lista para procesarla
                        solidos.Add(new SolidoIndividual
                        {
                            Geometria = curve,
                            Transform = transformAcumulado,
                            EsCurve = true // Flag especial para identificar Curves
                        });

                        // Log cada 1000 objetos
                        if (solidos.Count % 1000 == 0)
                        {
                            _log.AppendLine($"  ... {solidos.Count} objetos geométricos extraídos (incluye {_curvesEncontradas} curves)");
                        }
                    }
                    catch
                    {
                        // Curve inválida
                    }
                }
                else if (geoObj is GeometryElement nestedGeoElement)
                {
                    // PROCESAR GeometryElement anidado directamente (sin GeometryInstance)
                    // Esto puede ocurrir en algunos DWG complejos
                    try
                    {
                        ExtraerSolidosIndividuales(nestedGeoElement, transformAcumulado, solidos, nivel + 1, jerarquiaBloques, elementoPadre);
                    }
                    catch
                    {
                        // GeometryElement inválido
                    }
                }
                else
                {
                    // Registrar tipos de geometría no procesados (solo primeras 10 veces para no saturar log)
                    string tipoGeometria = geoObj.GetType().Name;
                    _otrosGeometriaEncontrados++;

                    if (nivel < 2 && solidos.Count < 100)
                    {
                        _log.AppendLine($"  ⚠ Nivel {nivel}: Tipo geometría no procesado: {tipoGeometria}");
                    }
                }
            }
        }

        /// <summary>
        /// Agrupa objetos geométricos por geometría idéntica (no por posición)
        /// </summary>
        private Dictionary<string, List<SolidoIndividual>> AgruparPorGeometriaIdentica(List<SolidoIndividual> solidos)
        {
            Dictionary<string, List<SolidoIndividual>> grupos = new Dictionary<string, List<SolidoIndividual>>();

            foreach (var solido in solidos)
            {
                // Calcular firma geométrica del objeto (Solid o Mesh)
                string firma = CalcularFirmaGeometrica(solido.Geometria);

                if (!grupos.ContainsKey(firma))
                {
                    grupos[firma] = new List<SolidoIndividual>();
                }

                grupos[firma].Add(solido);
            }

            return grupos;
        }

        /// <summary>
        /// Calcula una firma única para identificar geometrías idénticas (Solid o Mesh)
        /// </summary>
        private string CalcularFirmaGeometrica(GeometryObject geoObj)
        {
            try
            {
                if (geoObj is Solid solid)
                {
                    // Volumen
                    double volumen = Math.Round(solid.Volume * 1000000, 2); // ft³ a mm³ con 2 decimales

                    // BoundingBox dimensions
                    BoundingBoxXYZ bbox = solid.GetBoundingBox();
                    double largo = 0, ancho = 0, alto = 0;

                    if (bbox != null)
                    {
                        XYZ size = bbox.Max - bbox.Min;
                        largo = Math.Round(size.X * 304.8, 2); // ft a mm
                        ancho = Math.Round(size.Y * 304.8, 2);
                        alto = Math.Round(size.Z * 304.8, 2);
                    }

                    // Topología
                    int numCaras = solid.Faces.Size;
                    int numAristas = solid.Edges.Size;

                    // Crear firma única
                    return $"SOLID_V{volumen}_L{largo}_A{ancho}_H{alto}_F{numCaras}_E{numAristas}";
                }
                else if (geoObj is Mesh mesh)
                {
                    // Para meshes, usar número de triángulos y vértices
                    int numTriangulos = mesh.NumTriangles;
                    int numVertices = mesh.Vertices.Count;

                    // BoundingBox dimensions (si disponible)
                    double largo = 0, ancho = 0, alto = 0;

                    // Calcular bounding box manual desde vértices
                    if (mesh.Vertices.Count > 0)
                    {
                        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

                        foreach (XYZ vertex in mesh.Vertices)
                        {
                            minX = Math.Min(minX, vertex.X);
                            minY = Math.Min(minY, vertex.Y);
                            minZ = Math.Min(minZ, vertex.Z);
                            maxX = Math.Max(maxX, vertex.X);
                            maxY = Math.Max(maxY, vertex.Y);
                            maxZ = Math.Max(maxZ, vertex.Z);
                        }

                        largo = Math.Round((maxX - minX) * 304.8, 2);
                        ancho = Math.Round((maxY - minY) * 304.8, 2);
                        alto = Math.Round((maxZ - minZ) * 304.8, 2);
                    }

                    // Crear firma única para mesh
                    return $"MESH_T{numTriangulos}_V{numVertices}_L{largo}_A{ancho}_H{alto}";
                }

                // Si no es ni Solid ni Mesh, usar GUID único
                return Guid.NewGuid().ToString();
            }
            catch
            {
                // Si falla, usar un GUID único (cada objeto será único)
                return Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// Obtiene el centro de un sólido (promedio de vértices del BoundingBox)
        /// </summary>
        private XYZ ObtenerCentroSolido(Solid solid)
        {
            try
            {
                BoundingBoxXYZ bbox = solid.GetBoundingBox();
                if (bbox != null)
                {
                    return (bbox.Min + bbox.Max) / 2.0;
                }
            }
            catch
            {
            }

            // Fallback: origen
            return XYZ.Zero;
        }

        /// <summary>
        /// Crea DirectShape desde un sólido individual (SIMPLIFICADO)
        /// </summary>
        private bool CrearDirectShapeDesdeSolidoSimple(Solid solid, Transform transform, int indice)
        {
            try
            {
                // Validar que el sólido tenga volumen
                double volumen = 0;
                try
                {
                    volumen = solid.Volume;
                }
                catch
                {
                    return false;
                }

                // Filtro MUY permisivo - aceptar incluso piezas muy pequeñas
                if (volumen < 0.000000001)
                {
                    return false;
                }

                // Transformar el sólido
                Solid solidTransformado = null;
                try
                {
                    solidTransformado = SolidUtils.CreateTransformed(solid, transform);
                }
                catch
                {
                    return false;
                }

                if (solidTransformado == null || solidTransformado.Volume < 0.0001)
                {
                    return false;
                }

                // Crear DirectShape
                DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));

                // Asignar geometría
                List<GeometryObject> geometria = new List<GeometryObject> { solidTransformado };
                ds.SetShape(geometria);

                // Asignar nombre simple
                ds.Name = $"DWG_Solid_{indice:D6}";

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        /// <summary>
        /// Crea DirectShape desde un LOTE de objetos geométricos (Solids o Meshes - OPTIMIZADO)
        /// </summary>
        private bool CrearDirectShapeConLote(List<GeometryObject> geometrias, List<Transform> transforms, int indiceLote, string firmaGeometrica = "")
        {
            // DIAGNÓSTICO: Lista para guardar información de escala de cada solid
            List<string> infoEscalas = new List<string>();

            try
            {
                if (geometrias.Count == 0)
                {
                    return false;
                }

                // 🆕 SEPARAR Sólidos 3D y Superficies en listas diferentes
                List<GeometryObject> solidosConVolumen = new List<GeometryObject>();
                List<GeometryObject> superficiesSinVolumen = new List<GeometryObject>();
                List<GeometryObject> meshes = new List<GeometryObject>();

                int objetosRechazadosVolumen = 0;
                int objetosRechazadosTransform = 0;

                for (int i = 0; i < geometrias.Count; i++)
                {
                    GeometryObject geoObj = geometrias[i];
                    Transform transform = transforms[i];

                    try
                    {
                        // PROCESAR SOLID
                        if (geoObj is Solid solid)
                        {
                            // CRÍTICO: Cuando usamos GetSymbolGeometry(), la geometría está en espacio LOCAL
                            // y DEBE ser transformada a espacio MUNDIAL usando el transform acumulado

                            try
                            {
                                Solid solidTransformado;

                                if (!transform.IsIdentity)
                                {
                                    // DIAGNÓSTICO DE ESCALA: Calcular factor de escala del transform
                                    double scaleX = transform.BasisX.GetLength();
                                    double scaleY = transform.BasisY.GetLength();
                                    double scaleZ = transform.BasisZ.GetLength();
                                    double scalePromedio = (scaleX + scaleY + scaleZ) / 3.0;

                                    // GUARDAR información de escala para diagnóstico posterior
                                    infoEscalas.Add($"Solid {i}: ScaleX={scaleX:F6}, ScaleY={scaleY:F6}, ScaleZ={scaleZ:F6}, Promedio={scalePromedio:F6}, VolOrig={solid.Volume:F9}");

                                    // Transformar el solid a coordenadas mundiales
                                    solidTransformado = SolidUtils.CreateTransformed(solid, transform);

                                    // DIAGNÓSTICO: Si la escala es muy pequeña o muy grande, registrar
                                    if (scalePromedio < 0.001 || scalePromedio > 1000.0)
                                    {
                                        _log.AppendLine($"  ⚠ ESCALA EXTREMA en Lote {indiceLote} solid {i}:");
                                        _log.AppendLine($"     ScaleX={scaleX:F6}, ScaleY={scaleY:F6}, ScaleZ={scaleZ:F6}");
                                        _log.AppendLine($"     Volumen original: {solid.Volume:F9} pies³");
                                        _log.AppendLine($"     Volumen transformado: {solidTransformado.Volume:F9} pies³");
                                    }
                                }
                                else
                                {
                                    solidTransformado = solid;
                                }

                                // Validar que el Solid sea válido
                                if (solidTransformado == null)
                                {
                                    _log.AppendLine($"  ✗ Lote {indiceLote} solid {i} - SolidUtils.CreateTransformed devolvió NULL");
                                    objetosRechazadosTransform++;
                                    continue;
                                }

                                // Validar con la función de validación
                                if (!EsSolidValidoParaDirectShape(solidTransformado, indiceLote, i))
                                {
                                    objetosRechazadosTransform++;
                                    continue;
                                }

                                // 🆕 CLASIFICAR: ¿Es sólido 3D o superficie?
                                double volumen = solidTransformado.Volume;

                                if (volumen > 0.000000001)
                                {
                                    // SÓLIDO 3D (con volumen)
                                    solidosConVolumen.Add(solidTransformado);
                                }
                                else
                                {
                                    // SUPERFICIE (sin volumen, solo caras)
                                    superficiesSinVolumen.Add(solidTransformado);
                                }
                            }
                            catch (Exception exSolid)
                            {
                                _log.AppendLine($"  ✗ Lote {indiceLote} solid {i} - EXCEPCIÓN al transformar: {exSolid.Message}");
                                _log.AppendLine($"     Volumen original: {solid.Volume:F6} pies³");
                                objetosRechazadosTransform++;
                            }
                        }
                        // PROCESAR MESH
                        else if (geoObj is Mesh mesh)
                        {
                            // Para meshes, validar que tenga triángulos
                            if (mesh.NumTriangles == 0)
                            {
                                objetosRechazadosVolumen++;
                                continue;
                            }

                            // CRUCIAL: Cuando usamos GetSymbolGeometry(), los meshes también están en espacio LOCAL
                            // y DEBEN ser transformados a espacio MUNDIAL

                            try
                            {
                                Mesh meshTransformado;

                                if (!transform.IsIdentity)
                                {
                                    // Transformar el mesh a coordenadas mundiales
                                    meshTransformado = mesh.get_Transformed(transform);

                                    if (meshTransformado == null || meshTransformado.NumTriangles == 0)
                                    {
                                        // LOGGING DETALLADO: Identificar por qué se rechaza
                                        if (meshTransformado == null)
                                        {
                                            _log.AppendLine($"  ✗ Lote {indiceLote} mesh {i} - mesh.get_Transformed devolvió NULL");
                                            _log.AppendLine($"     Triángulos original: {mesh.NumTriangles}");
                                        }
                                        else
                                        {
                                            _log.AppendLine($"  ✗ Lote {indiceLote} mesh {i} - Mesh transformado perdió triángulos");
                                            _log.AppendLine($"     Triángulos original: {mesh.NumTriangles}");
                                            _log.AppendLine($"     Triángulos transformado: {meshTransformado.NumTriangles}");
                                        }
                                        objetosRechazadosTransform++;
                                        continue;
                                    }
                                }
                                else
                                {
                                    // Si el transform es Identity, usar el mesh original
                                    meshTransformado = mesh;
                                }

                                // Agregar a lista de meshes
                                meshes.Add(meshTransformado);
                            }
                            catch (Exception exMesh)
                            {
                                _log.AppendLine($"  ✗ Lote {indiceLote} mesh {i} - EXCEPCIÓN al transformar: {exMesh.Message}");
                                _log.AppendLine($"     Triángulos original: {mesh.NumTriangles}");
                                objetosRechazadosTransform++;
                            }
                        }
                        else
                        {
                            objetosRechazadosTransform++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.AppendLine($"  ✗ Lote {indiceLote} objeto {i} error: {ex.Message}");
                        objetosRechazadosTransform++;
                    }
                }

                // Logging de rechazados
                if (objetosRechazadosVolumen > 0 || objetosRechazadosTransform > 0)
                {
                    _log.AppendLine($"  ⚠ Lote {indiceLote}: rechazados {objetosRechazadosVolumen} sin volumen/triángulos, {objetosRechazadosTransform} fallo transform");
                }

                // 📊 RESUMEN DE CLASIFICACIÓN
                _log.AppendLine($"  📊 CLASIFICACIÓN - Lote {indiceLote}:");
                _log.AppendLine($"     🔷 Sólidos 3D (con volumen): {solidosConVolumen.Count}");
                _log.AppendLine($"     🔶 Superficies (sin volumen): {superficiesSinVolumen.Count}");
                _log.AppendLine($"     🔵 Meshes: {meshes.Count}");

                // 🆕 Contadores de DirectShapes creados
                int directShapesCreados = 0;

                // 🆕 CREAR DIRECTSHAPE PARA SÓLIDOS 3D (con volumen)
                if (solidosConVolumen.Count > 0)
                {
                    try
                    {
                        DirectShape dsSolidos = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                        dsSolidos.SetShape(solidosConVolumen);
                        dsSolidos.Name = $"DWG_Solids_{indiceLote:D6}_{solidosConVolumen.Count}objs";
                        directShapesCreados++;
                        _log.AppendLine($"  ✓ DirectShape SÓLIDOS creado: {solidosConVolumen.Count} objetos con volumen");
                    }
                    catch (Exception exSolidos)
                    {
                        _log.AppendLine($"  ⚠ Error creando DirectShape grupal para SÓLIDOS: {exSolidos.Message}");
                        _log.AppendLine($"  🔄 Intentando crear DirectShapes INDIVIDUALES para {solidosConVolumen.Count} sólidos...");

                        // FALLBACK: Crear DirectShapes individuales
                        int solidosCreados = 0;
                        int meshesCreados = 0;
                        int solidosFallidosEnDirectShape = 0;
                        int solidosFallidosEnMesh = 0;

                        for (int i = 0; i < solidosConVolumen.Count; i++)
                        {
                            try
                            {
                                DirectShape dsIndividual = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                                dsIndividual.SetShape(new List<GeometryObject> { solidosConVolumen[i] });
                                dsIndividual.Name = $"DWG_Solid_{indiceLote:D6}_{i:D3}";
                                solidosCreados++;
                                directShapesCreados++;
                            }
                            catch (Exception exDirectShape)
                            {
                                solidosFallidosEnDirectShape++;

                                // 🔍 DIAGNÓSTICO: Analizar el Solid que falló
                                Solid solidFallido = solidosConVolumen[i] as Solid;
                                if (solidFallido != null)
                                {
                                    _log.AppendLine($"    🔍 DIAGNÓSTICO Solid {i} - FALLÓ DirectShape.SetShape()");
                                    _log.AppendLine($"       Error: {exDirectShape.Message}");
                                    _log.AppendLine($"       Volume: {solidFallido.Volume:F9} pies³");
                                    _log.AppendLine($"       SurfaceArea: {solidFallido.SurfaceArea:F9} pies²");
                                    _log.AppendLine($"       Faces: {solidFallido.Faces.Size}");
                                    _log.AppendLine($"       Edges: {solidFallido.Edges.Size}");
                                }
                                else
                                {
                                    _log.AppendLine($"    🔍 DIAGNÓSTICO Solid {i} - NO ES UN SOLID (tipo: {solidosConVolumen[i]?.GetType().Name ?? "null"})");
                                }
                                // FALLBACK FINAL: Convertir Solid inválido a Mesh
                                try
                                {
                                    Solid solid = solidosConVolumen[i] as Solid;
                                    if (solid != null && solid.Faces != null && solid.Faces.Size > 0)
                                    {
                                        // Triangular todas las caras del Solid
                                        TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
                                        builder.OpenConnectedFaceSet(false);

                                        foreach (Face face in solid.Faces)
                                        {
                                            try
                                            {
                                                Mesh faceMesh = face.Triangulate();
                                                if (faceMesh != null && faceMesh.NumTriangles > 0)
                                                {
                                                    for (int t = 0; t < faceMesh.NumTriangles; t++)
                                                    {
                                                        MeshTriangle triangle = faceMesh.get_Triangle(t);
                                                        XYZ v0 = triangle.get_Vertex(0);
                                                        XYZ v1 = triangle.get_Vertex(1);
                                                        XYZ v2 = triangle.get_Vertex(2);

                                                        TessellatedFace tessellatedFace = new TessellatedFace(
                                                            new List<XYZ> { v0, v1, v2 },
                                                            ElementId.InvalidElementId
                                                        );
                                                        builder.AddFace(tessellatedFace);
                                                    }
                                                }
                                            }
                                            catch { }
                                        }

                                        builder.CloseConnectedFaceSet();
                                        builder.Build();
                                        TessellatedShapeBuilderResult result = builder.GetBuildResult();

                                        if (result != null && result.GetGeometricalObjects() != null)
                                        {
                                            List<GeometryObject> meshGeometries = new List<GeometryObject>();
                                            foreach (GeometryObject geo in result.GetGeometricalObjects())
                                            {
                                                meshGeometries.Add(geo);
                                            }

                                            if (meshGeometries.Count > 0)
                                            {
                                                DirectShape dsMesh = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                                                dsMesh.SetShape(meshGeometries);
                                                dsMesh.Name = $"DWG_Mesh_{indiceLote:D6}_{i:D3}";
                                                meshesCreados++;
                                                directShapesCreados++;
                                            }
                                        }
                                    }
                                }
                                catch (Exception exMesh)
                                {
                                    solidosFallidosEnMesh++;
                                    _log.AppendLine($"    🔍 DIAGNÓSTICO Solid {i} - FALLÓ también conversión a Mesh: {exMesh.Message}");
                                }
                            }
                        }

                        // 📊 RESUMEN DEL FALLBACK INDIVIDUAL
                        _log.AppendLine($"  📊 RESUMEN FALLBACK INDIVIDUAL (de {solidosConVolumen.Count} sólidos):");
                        _log.AppendLine($"     ✓ DirectShapes creados exitosamente: {solidosCreados}");
                        _log.AppendLine($"     ✓ Meshes creados (fallback): {meshesCreados}");
                        _log.AppendLine($"     ✗ Fallidos en DirectShape.SetShape(): {solidosFallidosEnDirectShape}");
                        _log.AppendLine($"     ✗ Fallidos completamente (incluido Mesh): {solidosFallidosEnMesh}");

                        if (solidosCreados > 0 || meshesCreados > 0)
                        {
                            _log.AppendLine($"  ✓ DirectShapes individuales creados: {solidosCreados} sólidos + {meshesCreados} meshes (de {solidosConVolumen.Count})");
                        }
                        else
                        {
                            _log.AppendLine($"  ✗ No se pudo crear ningún DirectShape individual para sólidos");
                        }
                    }
                }

                // 🆕 CREAR DIRECTSHAPE PARA SUPERFICIES (sin volumen)
                if (superficiesSinVolumen.Count > 0)
                {
                    try
                    {
                        DirectShape dsSuperficies = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                        dsSuperficies.SetShape(superficiesSinVolumen);
                        dsSuperficies.Name = $"DWG_Surfaces_{indiceLote:D6}_{superficiesSinVolumen.Count}objs";
                        directShapesCreados++;
                        _log.AppendLine($"  ✓ DirectShape SUPERFICIES creado: {superficiesSinVolumen.Count} objetos sin volumen");
                    }
                    catch (Exception exSuperficies)
                    {
                        _log.AppendLine($"  ⚠ Error creando DirectShape grupal para SUPERFICIES: {exSuperficies.Message}");
                        _log.AppendLine($"  🔄 Intentando crear DirectShapes INDIVIDUALES para {superficiesSinVolumen.Count} superficies...");

                        // FALLBACK: Crear DirectShapes individuales
                        int superficiesCreadas = 0;
                        int meshesSuperficieCreados = 0;
                        for (int i = 0; i < superficiesSinVolumen.Count; i++)
                        {
                            try
                            {
                                DirectShape dsIndividual = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                                dsIndividual.SetShape(new List<GeometryObject> { superficiesSinVolumen[i] });
                                dsIndividual.Name = $"DWG_Surface_{indiceLote:D6}_{i:D3}";
                                superficiesCreadas++;
                                directShapesCreados++;
                            }
                            catch
                            {
                                // FALLBACK FINAL: Convertir superficie inválida a Mesh
                                try
                                {
                                    Solid solid = superficiesSinVolumen[i] as Solid;
                                    if (solid != null && solid.Faces != null && solid.Faces.Size > 0)
                                    {
                                        // Triangular todas las caras de la superficie
                                        TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
                                        builder.OpenConnectedFaceSet(false);

                                        foreach (Face face in solid.Faces)
                                        {
                                            try
                                            {
                                                Mesh faceMesh = face.Triangulate();
                                                if (faceMesh != null && faceMesh.NumTriangles > 0)
                                                {
                                                    for (int t = 0; t < faceMesh.NumTriangles; t++)
                                                    {
                                                        MeshTriangle triangle = faceMesh.get_Triangle(t);
                                                        XYZ v0 = triangle.get_Vertex(0);
                                                        XYZ v1 = triangle.get_Vertex(1);
                                                        XYZ v2 = triangle.get_Vertex(2);

                                                        TessellatedFace tessellatedFace = new TessellatedFace(
                                                            new List<XYZ> { v0, v1, v2 },
                                                            ElementId.InvalidElementId
                                                        );
                                                        builder.AddFace(tessellatedFace);
                                                    }
                                                }
                                            }
                                            catch { }
                                        }

                                        builder.CloseConnectedFaceSet();
                                        builder.Build();
                                        TessellatedShapeBuilderResult result = builder.GetBuildResult();

                                        if (result != null && result.GetGeometricalObjects() != null)
                                        {
                                            List<GeometryObject> meshGeometries = new List<GeometryObject>();
                                            foreach (GeometryObject geo in result.GetGeometricalObjects())
                                            {
                                                meshGeometries.Add(geo);
                                            }

                                            if (meshGeometries.Count > 0)
                                            {
                                                DirectShape dsMesh = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                                                dsMesh.SetShape(meshGeometries);
                                                dsMesh.Name = $"DWG_SurfaceMesh_{indiceLote:D6}_{i:D3}";
                                                meshesSuperficieCreados++;
                                                directShapesCreados++;
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    // Completamente inválida, ignorar
                                }
                            }
                        }

                        if (superficiesCreadas > 0 || meshesSuperficieCreados > 0)
                        {
                            _log.AppendLine($"  ✓ DirectShapes individuales creados: {superficiesCreadas} superficies + {meshesSuperficieCreados} meshes (de {superficiesSinVolumen.Count})");
                        }
                        else
                        {
                            _log.AppendLine($"  ✗ No se pudo crear ningún DirectShape individual para superficies");
                        }
                    }
                }

                // 🆕 CREAR DIRECTSHAPE PARA MESHES
                if (meshes.Count > 0)
                {
                    try
                    {
                        DirectShape dsMeshes = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                        dsMeshes.SetShape(meshes);
                        dsMeshes.Name = $"DWG_Meshes_{indiceLote:D6}_{meshes.Count}objs";
                        directShapesCreados++;
                        _log.AppendLine($"  ✓ DirectShape MESHES creado: {meshes.Count} objetos");
                    }
                    catch (Exception exMeshes)
                    {
                        _log.AppendLine($"  ⚠ Error creando DirectShape grupal para MESHES: {exMeshes.Message}");
                        _log.AppendLine($"  🔄 Intentando crear DirectShapes INDIVIDUALES para {meshes.Count} meshes...");

                        // FALLBACK: Crear DirectShapes individuales
                        int meshesCreados = 0;
                        for (int i = 0; i < meshes.Count; i++)
                        {
                            try
                            {
                                DirectShape dsIndividual = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                                dsIndividual.SetShape(new List<GeometryObject> { meshes[i] });
                                dsIndividual.Name = $"DWG_Mesh_{indiceLote:D6}_{i:D3}";
                                meshesCreados++;
                                directShapesCreados++;
                            }
                            catch
                            {
                                // Este mesh específico es inválido, continuar con el siguiente
                            }
                        }

                        if (meshesCreados > 0)
                        {
                            _log.AppendLine($"  ✓ DirectShapes MESHES individuales creados: {meshesCreados}/{meshes.Count}");
                        }
                        else
                        {
                            _log.AppendLine($"  ✗ No se pudo crear ningún DirectShape individual para meshes");
                        }
                    }
                }

                // Verificar si se creó al menos un DirectShape
                if (directShapesCreados == 0)
                {
                    _log.AppendLine($"  ✗ Lote {indiceLote} SIN GEOMETRÍA VÁLIDA - Ningún DirectShape creado (de {geometrias.Count} objetos intentados)");
                    return false;
                }

                _log.AppendLine($"  ✓ Lote {indiceLote}: {directShapesCreados} DirectShapes creados (Sólidos: {solidosConVolumen.Count}, Superficies: {superficiesSinVolumen.Count}, Meshes: {meshes.Count})");
                return true;
            }
            catch (Exception ex)
            {
                // DirectShape falló con Solids - DIAGNÓSTICO primero
                _log.AppendLine($"  ⚠ Lote {indiceLote} - DirectShape FALLÓ con Solids: {ex.Message}");

                // Mostrar diagnóstico de escala
                if (infoEscalas.Count > 0)
                {
                    _log.AppendLine($"  📊 DIAGNÓSTICO DE ESCALA para Lote {indiceLote} ({infoEscalas.Count} solids):");
                    foreach (string info in infoEscalas)
                    {
                        _log.AppendLine($"     {info}");
                    }
                }

                // SOLO intentar fallback con Mesh si detectamos escala problemática (~0.039370)
                bool tieneEscalaProblematica = false;
                if (infoEscalas.Count > 0)
                {
                    foreach (string info in infoEscalas)
                    {
                        if (info.Contains("0.039370") || info.Contains("0.03937"))
                        {
                            tieneEscalaProblematica = true;
                            break;
                        }
                    }
                }

                if (!tieneEscalaProblematica)
                {
                    // No es problema de escala, rechazar directamente
                    _log.AppendLine($"  ✗ Lote {indiceLote} RECHAZADO (no es problema de escala)");

                    // 🔬 DIAGNÓSTICO: Guardar información de este Solid válido que falla
                    if (_diagnosticoSolidsValidosQueFallan.Count < 50 && geometrias.Count > 0)
                    {
                        GeometryObject primerObj = geometrias[0];
                        Transform primerTransform = transforms[0];

                        if (primerObj is Solid solidDiag)
                        {
                            string diagnostico = $"\n  ⚠ SOLID VÁLIDO QUE FALLA DirectShape (Lote {indiceLote}):\n";
                            diagnostico += $"     Volume: {solidDiag.Volume:F9}\n";
                            diagnostico += $"     SurfaceArea: {solidDiag.SurfaceArea:F9}\n";
                            diagnostico += $"     Faces: {(solidDiag.Faces != null ? solidDiag.Faces.Size : -1)}\n";
                            diagnostico += $"     Edges: {(solidDiag.Edges != null ? solidDiag.Edges.Size : -1)}\n";
                            diagnostico += $"     Transform.IsIdentity: {primerTransform.IsIdentity}\n";
                            diagnostico += $"     Transform.Origin: ({primerTransform.Origin.X:F2},{primerTransform.Origin.Y:F2},{primerTransform.Origin.Z:F2})\n";

                            if (!primerTransform.IsIdentity)
                            {
                                double scaleX = primerTransform.BasisX.GetLength();
                                double scaleY = primerTransform.BasisY.GetLength();
                                double scaleZ = primerTransform.BasisZ.GetLength();
                                diagnostico += $"     Scale: X={scaleX:F6}, Y={scaleY:F6}, Z={scaleZ:F6}\n";
                                diagnostico += $"     Determinant: {primerTransform.Determinant:F9}\n";
                            }

                            diagnostico += $"     Error: {ex.Message}";

                            _diagnosticoSolidsValidosQueFallan.Add(diagnostico);
                        }
                    }

                    return false;
                }

                // ESTRATEGIA DE FALLBACK: Solo para objetos con escala problemática
                _log.AppendLine($"  🔄 Escala problemática detectada → Intentando FALLBACK con Mesh...");
                _log.AppendLine($"     Creando un DirectShape POR CADA MESH individual...");

                // Esta función retornará el número de DirectShapes de mesh creados
                int meshDirectShapesCreados = CrearDirectShapesMeshIndividuales(geometrias, transforms, indiceLote);

                if (meshDirectShapesCreados > 0)
                {
                    _log.AppendLine($"  ✓ ÉXITO con FALLBACK: {meshDirectShapesCreados} DirectShapes individuales creados como Mesh para Lote {indiceLote}");
                    return true;
                }
                else
                {
                    _log.AppendLine($"  ✗ FALLBACK FALLÓ: No se pudieron generar Meshes válidos");
                    return false;
                }
            }
        }

        /// <summary>
        /// Crea DirectShapes individuales para cada Mesh (un DirectShape por mesh)
        /// </summary>
        private int CrearDirectShapesMeshIndividuales(List<GeometryObject> geometrias, List<Transform> transforms, int indiceLoteBase)
        {
            int directShapesCreados = 0;

            try
            {
                for (int i = 0; i < geometrias.Count; i++)
                {
                    GeometryObject geoObj = geometrias[i];
                    Transform transform = transforms[i];

                    if (geoObj is Solid solid && solid.Volume > 0.000000001)
                    {
                        // Detectar si el Transform invierte la geometría (determinante negativo)
                        double determinante = transform.Determinant;
                        bool necesitaInversionNormales = determinante < 0;

                        Solid solidTransformado;

                        if (necesitaInversionNormales)
                        {
                            // Aplicar un Transform de reflexión para corregir las normales
                            Plane planoReflexion = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, transform.Origin);
                            Transform reflexion = Transform.CreateReflection(planoReflexion);
                            Transform transformCorregido = transform.Multiply(reflexion);
                            solidTransformado = SolidUtils.CreateTransformed(solid, transformCorregido);
                        }
                        else
                        {
                            // Transformar normalmente
                            solidTransformado = SolidUtils.CreateTransformed(solid, transform);
                        }

                        if (solidTransformado != null && solidTransformado.Faces.Size > 0)
                        {
                            // Crear un DirectShape POR CADA MESH (cada face triangulada)
                            foreach (Face face in solidTransformado.Faces)
                            {
                                Mesh mesh = face.Triangulate();
                                if (mesh != null && mesh.NumTriangles > 0)
                                {
                                    try
                                    {
                                        // Crear DirectShape individual con este mesh
                                        DirectShape dsMesh = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                                        dsMesh.SetShape(new List<GeometryObject> { mesh });
                                        dsMesh.Name = $"DWG_Mesh_{indiceLoteBase:D6}_{directShapesCreados:D3}";

                                        directShapesCreados++;
                                    }
                                    catch (Exception exMesh)
                                    {
                                        _log.AppendLine($"     ⚠ Mesh individual falló: {exMesh.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                return directShapesCreados;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ✗ Error en CrearDirectShapesMeshIndividuales: {ex.Message}");
                return directShapesCreados;
            }
        }

        /// <summary>
        /// 🆕 Crea un DirectShape o ModelCurve a partir de una Curve del DWG
        /// </summary>
        private bool CrearDirectShapeParaCurve(SolidoIndividual solidoIndividual, int indice)
        {
            try
            {
                Curve curve = solidoIndividual.Geometria as Curve;
                if (curve == null) return false;

                Transform transform = solidoIndividual.Transform;

                // Transformar la curva al espacio mundial
                Curve curveTransformed = curve.CreateTransformed(transform);

                // OPCIÓN 1: Intentar crear DirectShape con la Curve
                try
                {
                    DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                    ds.SetShape(new List<GeometryObject> { curveTransformed });
                    ds.Name = $"DWG_Curve_{indice:D6}";
                    return true;
                }
                catch
                {
                    // Si DirectShape falla, intentar ModelCurve
                }

                // OPCIÓN 2: Crear ModelCurve (wireframe visible)
                try
                {
                    // Crear un plano de trabajo apropiado
                    XYZ origin = curveTransformed.GetEndPoint(0);
                    XYZ normal = XYZ.BasisZ;

                    // Si la curva es vertical u horizontal, ajustar el plano
                    XYZ direction = (curveTransformed.GetEndPoint(1) - curveTransformed.GetEndPoint(0)).Normalize();
                    if (Math.Abs(direction.DotProduct(XYZ.BasisZ)) > 0.99)
                    {
                        normal = XYZ.BasisX;
                    }

                    Plane plane = Plane.CreateByNormalAndOrigin(normal, origin);
                    SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);

                    ModelCurve modelCurve = _doc.Create.NewModelCurve(curveTransformed, sketchPlane);
                    return true;
                }
                catch (Exception ex)
                {
                    if (indice % 1000 == 0)
                        _log.AppendLine($"  → Error creando Curve {indice}: {ex.Message}");
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 🆕 Crea un DirectShape visual para Solids vacíos (Vol=0, Faces=0)
        /// Técnicas en cascada: EDGES → TessellatedShapeBuilder → CURVES → Model Lines
        /// </summary>
        private bool CrearDirectShapeParaSolidVacio(SolidoIndividual solidoIndividual, int indice)
        {
            try
            {
                Solid solid = solidoIndividual.Geometria as Solid;
                if (solid == null) return false;

                Transform transform = solidoIndividual.Transform;

                // ═══════════════════════════════════════════════════════════════
                // TÉCNICA 1: EXTRAER EDGES (Aristas) del Solid vacío
                // ═══════════════════════════════════════════════════════════════
                try
                {
                    if (solid.Edges != null && solid.Edges.Size > 0)
                    {
                        List<Curve> curves = new List<Curve>();

                        foreach (Edge edge in solid.Edges)
                        {
                            try
                            {
                                Curve curve = edge.AsCurve();
                                if (curve != null)
                                {
                                    // Transformar la curva al espacio mundial
                                    Curve curveTransformed = curve.CreateTransformed(transform);
                                    curves.Add(curveTransformed);
                                }
                            }
                            catch { }
                        }

                        if (curves.Count > 0)
                        {
                            // Crear DirectShape con las curves como wireframe
                            DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));

                            // Intentar crear geometría desde las curves
                            List<GeometryObject> geometries = new List<GeometryObject>();
                            foreach (var curve in curves)
                            {
                                geometries.Add(curve);
                            }

                            ds.SetShape(geometries);
                            ds.Name = $"DWG_Edges_{indice:D6}";

                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Continuar a siguiente técnica
                    if (indice % 1000 == 0)
                        _log.AppendLine($"  → Técnica EDGES falló para índice {indice}: {ex.Message}");
                }

                // ═══════════════════════════════════════════════════════════════
                // TÉCNICA 2: TessellatedShapeBuilder (Construcción de bajo nivel)
                // ═══════════════════════════════════════════════════════════════
                try
                {
                    // Intentar extraer vértices o cualquier dato geométrico del Solid
                    if (solid.Edges != null && solid.Edges.Size > 0)
                    {
                        TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
                        builder.OpenConnectedFaceSet(false);

                        List<XYZ> vertices = new List<XYZ>();

                        // Extraer todos los vértices únicos de las aristas
                        foreach (Edge edge in solid.Edges)
                        {
                            try
                            {
                                Curve curve = edge.AsCurve();
                                if (curve != null)
                                {
                                    XYZ p0 = transform.OfPoint(curve.GetEndPoint(0));
                                    XYZ p1 = transform.OfPoint(curve.GetEndPoint(1));

                                    if (!vertices.Any(v => v.IsAlmostEqualTo(p0, 0.001)))
                                        vertices.Add(p0);
                                    if (!vertices.Any(v => v.IsAlmostEqualTo(p1, 0.001)))
                                        vertices.Add(p1);
                                }
                            }
                            catch { }
                        }

                        // Si tenemos al menos 3 vértices, intentar crear una cara triangular
                        if (vertices.Count >= 3)
                        {
                            TessellatedFace tessellatedFace = new TessellatedFace(
                                new List<XYZ> { vertices[0], vertices[1], vertices[2] },
                                ElementId.InvalidElementId
                            );

                            builder.AddFace(tessellatedFace);
                            builder.CloseConnectedFaceSet();

                            builder.Build();
                            TessellatedShapeBuilderResult result = builder.GetBuildResult();

                            if (result != null && (result.Outcome == TessellatedShapeBuilderOutcome.Mesh ||
                                result.Outcome == TessellatedShapeBuilderOutcome.Solid))
                            {
                                DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));

                                List<GeometryObject> geometries = new List<GeometryObject>();
                                foreach (GeometryObject geo in result.GetGeometricalObjects())
                                {
                                    geometries.Add(geo);
                                }

                                if (geometries.Count > 0)
                                {
                                    ds.SetShape(geometries);
                                    ds.Name = $"DWG_Tessellated_{indice:D6}";
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Continuar a siguiente técnica
                    if (indice % 1000 == 0)
                        _log.AppendLine($"  → Técnica TessellatedShapeBuilder falló para índice {indice}: {ex.Message}");
                }

                // ═══════════════════════════════════════════════════════════════
                // TÉCNICA 3: CURVES del DWG (si el objeto original era una Curve)
                // ═══════════════════════════════════════════════════════════════
                // Esta técnica se maneja mejor en la extracción inicial
                // Aquí solo intentamos si tenemos Edges que podamos convertir

                // ═══════════════════════════════════════════════════════════════
                // TÉCNICA 4: Model Lines (Wireframe visible como último recurso)
                // ═══════════════════════════════════════════════════════════════
                try
                {
                    if (solid.Edges != null && solid.Edges.Size > 0)
                    {
                        // Crear Model Curves para cada Edge
                        int curvesCreadas = 0;

                        foreach (Edge edge in solid.Edges)
                        {
                            try
                            {
                                Curve curve = edge.AsCurve();
                                if (curve != null)
                                {
                                    Curve curveTransformed = curve.CreateTransformed(transform);

                                    // Crear ModelCurve en el SketchPlane apropiado
                                    // Necesitamos un plano de trabajo
                                    XYZ normal = XYZ.BasisZ;
                                    XYZ origin = curveTransformed.GetEndPoint(0);

                                    Plane plane = Plane.CreateByNormalAndOrigin(normal, origin);
                                    SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);

                                    ModelCurve modelCurve = _doc.Create.NewModelCurve(curveTransformed, sketchPlane);
                                    curvesCreadas++;
                                }
                            }
                            catch { }
                        }

                        if (curvesCreadas > 0)
                        {
                            // Las Model Curves se crearon exitosamente
                            // No necesitamos DirectShape, las curves ya son visibles
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Falló también Model Lines
                    if (indice % 1000 == 0)
                        _log.AppendLine($"  → Técnica Model Lines falló para índice {indice}: {ex.Message}");
                }

                // ═══════════════════════════════════════════════════════════════
                // DIAGNÓSTICO: Investigar qué información tiene este Solid vacío
                // ═══════════════════════════════════════════════════════════════

                // Si llegamos aquí, el Solid NO tiene Edges útiles
                // Loggear información detallada para los primeros 50
                if (indice < 50)
                {
                    _log.AppendLine($"\n  🔬 DIAGNÓSTICO SOLID VACÍO #{indice}:");
                    _log.AppendLine($"     Volume: {solid.Volume}");
                    _log.AppendLine($"     SurfaceArea: {solid.SurfaceArea}");
                    _log.AppendLine($"     Faces: {(solid.Faces != null ? solid.Faces.Size : -1)}");
                    _log.AppendLine($"     Edges: {(solid.Edges != null ? solid.Edges.Size : -1)}");

                    try
                    {
                        BoundingBoxXYZ bbox = solid.GetBoundingBox();
                        if (bbox != null)
                        {
                            _log.AppendLine($"     BoundingBox: Min({bbox.Min.X:F2},{bbox.Min.Y:F2},{bbox.Min.Z:F2}) Max({bbox.Max.X:F2},{bbox.Max.Y:F2},{bbox.Max.Z:F2})");
                        }
                        else
                        {
                            _log.AppendLine($"     BoundingBox: NULL");
                        }
                    }
                    catch
                    {
                        _log.AppendLine($"     BoundingBox: ERROR");
                    }

                    _log.AppendLine($"     Transform.Origin: ({transform.Origin.X:F2},{transform.Origin.Y:F2},{transform.Origin.Z:F2})");
                }

                // ❌ NO CREAR CUBOS GRANDES - estos Solids están realmente vacíos
                // Si no tienen Edges/Faces, no hay geometría real que extraer

                return false; // No se pudo crear nada

                /* CÓDIGO ELIMINADO - BoundingBox crea cubos demasiado grandes
                try
                {
                    BoundingBoxXYZ bbox = solid.GetBoundingBox();

                    if (bbox != null && bbox.Min != null && bbox.Max != null)
                    {
                        // Crear un box visual representando el BoundingBox
                        // Transformar los puntos Min/Max al espacio mundial
                        XYZ minTransformed = transform.OfPoint(bbox.Min);
                        XYZ maxTransformed = transform.OfPoint(bbox.Max);

                        // Crear las 8 esquinas del box
                        List<XYZ> corners = new List<XYZ>
                        {
                            minTransformed,
                            new XYZ(maxTransformed.X, minTransformed.Y, minTransformed.Z),
                            new XYZ(maxTransformed.X, maxTransformed.Y, minTransformed.Z),
                            new XYZ(minTransformed.X, maxTransformed.Y, minTransformed.Z),
                            new XYZ(minTransformed.X, minTransformed.Y, maxTransformed.Z),
                            new XYZ(maxTransformed.X, minTransformed.Y, maxTransformed.Z),
                            maxTransformed,
                            new XYZ(minTransformed.X, maxTransformed.Y, maxTransformed.Z)
                        };

                        // Crear un box sólido simple
                        List<Curve> edges = new List<Curve>();

                        // Base inferior (z min)
                        edges.Add(Line.CreateBound(corners[0], corners[1]));
                        edges.Add(Line.CreateBound(corners[1], corners[2]));
                        edges.Add(Line.CreateBound(corners[2], corners[3]));
                        edges.Add(Line.CreateBound(corners[3], corners[0]));

                        // Base superior (z max)
                        edges.Add(Line.CreateBound(corners[4], corners[5]));
                        edges.Add(Line.CreateBound(corners[5], corners[6]));
                        edges.Add(Line.CreateBound(corners[6], corners[7]));
                        edges.Add(Line.CreateBound(corners[7], corners[4]));

                        // Verticales
                        edges.Add(Line.CreateBound(corners[0], corners[4]));
                        edges.Add(Line.CreateBound(corners[1], corners[5]));
                        edges.Add(Line.CreateBound(corners[2], corners[6]));
                        edges.Add(Line.CreateBound(corners[3], corners[7]));

                        // Crear CurveLoop para formar un perfil cerrado
                        CurveLoop bottomLoop = new CurveLoop();
                        bottomLoop.Append(Line.CreateBound(corners[0], corners[1]));
                        bottomLoop.Append(Line.CreateBound(corners[1], corners[2]));
                        bottomLoop.Append(Line.CreateBound(corners[2], corners[3]));
                        bottomLoop.Append(Line.CreateBound(corners[3], corners[0]));

                        // Crear Solid mediante extrusión
                        double altura = maxTransformed.Z - minTransformed.Z;
                        if (altura < 0.01) altura = 0.1; // Altura mínima

                        Solid boxSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                            new List<CurveLoop> { bottomLoop },
                            XYZ.BasisZ,
                            altura
                        );

                        // Crear DirectShape con el box
                        DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                        ds.SetShape(new List<GeometryObject> { boxSolid });
                        ds.Name = $"DWG_EmptyBox_{indice:D6}";

                        return true;
                    }
                }
                catch
                {
                    // Si falla BoundingBox, continuar a OPCIÓN 2
                }

                // OPCIÓN 2: Crear un marcador puntual pequeño en Transform.Origin
                if (solidoIndividual.UsarSoloPunto || true) // Fallback siempre
                {
                    try
                    {
                        // Crear una esfera pequeña (0.1 pies = ~3cm) en la posición del Transform
                        XYZ center = transform.Origin;
                        double radius = 0.1; // 0.1 pies

                        // Crear esfera mediante Frame y CreateRevolvedGeometry
                        XYZ profileCenter = center + XYZ.BasisX * radius;
                        Arc arc = Arc.Create(
                            center,
                            radius,
                            0,
                            Math.PI * 2,
                            XYZ.BasisX,
                            XYZ.BasisY
                        );

                        CurveLoop profile = new CurveLoop();
                        profile.Append(arc);

                        Frame frame = new Frame(center, XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ);
                        Solid sphere = GeometryCreationUtilities.CreateRevolvedGeometry(
                            frame,
                            new List<CurveLoop> { profile },
                            0,
                            Math.PI * 2
                        );

                        DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                        ds.SetShape(new List<GeometryObject> { sphere });
                        ds.Name = $"DWG_EmptyMarker_{indice:D6}";

                        return true;
                    }
                    catch
                    {
                        // Si hasta la esfera falla, crear un cubo pequeño simple
                        XYZ center = transform.Origin;
                        double size = 0.1;

                        XYZ min = center - new XYZ(size, size, size) / 2;
                        XYZ max = center + new XYZ(size, size, size) / 2;

                        CurveLoop loop = new CurveLoop();
                        loop.Append(Line.CreateBound(min, new XYZ(max.X, min.Y, min.Z)));
                        loop.Append(Line.CreateBound(new XYZ(max.X, min.Y, min.Z), new XYZ(max.X, max.Y, min.Z)));
                        loop.Append(Line.CreateBound(new XYZ(max.X, max.Y, min.Z), new XYZ(min.X, max.Y, min.Z)));
                        loop.Append(Line.CreateBound(new XYZ(min.X, max.Y, min.Z), min));

                        Solid cube = GeometryCreationUtilities.CreateExtrusionGeometry(
                            new List<CurveLoop> { loop },
                            XYZ.BasisZ,
                            size * 2
                        );

                        DirectShape ds = DirectShape.CreateElement(_doc, new ElementId(_categoryForDirectShapes));
                        ds.SetShape(new List<GeometryObject> { cube });
                        ds.Name = $"DWG_EmptyCube_{indice:D6}";

                        return true;
                    }
                }

                return false;
                */ // FIN CÓDIGO ELIMINADO - BoundingBox
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ✗ Error creando DirectShape para Solid vacío: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Valida si un Solid cumple los criterios de DirectShape de Revit
        /// </summary>
        private bool EsSolidValidoParaDirectShape(Solid solid, int indiceLote, int indiceSolid)
        {
            try
            {
                // Validación 1: Verificar que tenga caras
                if (solid.Faces == null || solid.Faces.Size == 0)
                {
                    _log.AppendLine($"  ✗ Lote {indiceLote} solid {indiceSolid} - INVÁLIDO para DirectShape: Sin caras");
                    _log.AppendLine($"     Volumen: {solid.Volume:F6} pies³");
                    return false;
                }

                // Validación 2: Verificar que tenga área superficial (MUY PERMISIVA)
                double surfaceArea = solid.SurfaceArea;
                if (surfaceArea <= 0.0)  // Solo rechazar si área es exactamente 0
                {
                    _log.AppendLine($"  ✗ Lote {indiceLote} solid {indiceSolid} - INVÁLIDO para DirectShape: Área superficial = 0");
                    _log.AppendLine($"     Volumen: {solid.Volume:F6} pies³");
                    _log.AppendLine($"     Caras: {solid.Faces.Size}");
                    _log.AppendLine($"     Área: {surfaceArea:F9} pies²");
                    return false;
                }

                // Validación 3: Verificar que tenga aristas
                if (solid.Edges == null || solid.Edges.Size == 0)
                {
                    _log.AppendLine($"  ✗ Lote {indiceLote} solid {indiceSolid} - INVÁLIDO para DirectShape: Sin aristas");
                    _log.AppendLine($"     Volumen: {solid.Volume:F6} pies³");
                    _log.AppendLine($"     Caras: {solid.Faces.Size}");
                    _log.AppendLine($"     Área: {surfaceArea:F6} pies²");
                    return false;
                }

                // Todas las validaciones pasaron
                return true;
            }
            catch (Exception ex)
            {
                _log.AppendLine($"  ✗ Lote {indiceLote} solid {indiceSolid} - EXCEPCIÓN al validar: {ex.Message}");
                _log.AppendLine($"     Volumen: {solid.Volume:F6} pies³");
                return false;
            }
        }
    }

    /// <summary>
    /// Información de un objeto geométrico individual extraído (Solid o Mesh)
    /// </summary>
    public class SolidoIndividual
    {
        public GeometryObject Geometria { get; set; } // Puede ser Solid, Mesh o Curve
        public Transform Transform { get; set; }

        // Flags para Solids vacíos forzados
        public bool EsSolidVacio { get; set; } = false; // Solid con Vol=0, Faces=0 forzado a crear
        public bool UsarSoloPunto { get; set; } = false; // Crear solo un marcador puntual (sin BoundingBox)
        public bool EsCurve { get; set; } = false; // Objeto es una Curve (línea/polyline/arco)

        // Propiedad helper para compatibilidad
        public Solid Solido
        {
            get { return Geometria as Solid; }
            set { Geometria = value; }
        }
    }

    /// <summary>
    /// Información de un bloque extraído
    /// </summary>
    public class BlockInfo
    {
        public string Nombre { get; set; }
        public Transform Transform { get; set; }
        public List<Solid> Solidos { get; set; }
        public double Volumen { get; set; } // m³
        public double BBoxLargo { get; set; } // mm
        public double BBoxAncho { get; set; } // mm
        public double BBoxAlto { get; set; } // mm
        public int NumeroSolidos { get; set; }
        public int NumeroCaras { get; set; }
        public int NumeroAristas { get; set; }
        public string FirmaGeometrica { get; set; }

        public bool TieneSolidos => Solidos != null && Solidos.Count > 0;
    }

    /// <summary>
    /// Resultado de la extracción
    /// </summary>
    public class ResultadoExtraccion
    {
        public int TotalBloques { get; set; }
        public int DirectShapesCreados { get; set; }
        public int GruposUnicos { get; set; }
        public double TiempoProcesamiento { get; set; }
        public string RutaLog { get; set; }
    }
}
