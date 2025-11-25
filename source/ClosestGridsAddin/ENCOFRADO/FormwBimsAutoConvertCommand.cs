using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ClosestGridsAddinVANILLA.Commands;
using ClosestGridsAddinVANILLA.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Comando combinado: FORMWBIMS + Auto-Conversión a Wall/Floor
    /// 1. Crea encofrados DirectShapes usando lógica de FormwBIMS
    /// 2. Automáticamente convierte esos DirectShapes a muros/suelos nativos
    /// 3. Usa los 5 métodos avanzados de conversión
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FormwBimsAutoConvertCommand : LicensedCommand
    {
        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            try
            {
                // ══════════════════════════════════════════════════════════════
                // PASO 0: Selección de tipos de muro y suelo
                // ══════════════════════════════════════════════════════════════
                var typeSelectionDialog = new WallFloorTypeSelectionWindow(doc);
                bool? typeDialogResult = typeSelectionDialog.ShowDialog();

                if (typeDialogResult != true || !typeSelectionDialog.UserAccepted)
                {
                    return Result.Cancelled;
                }

                WallType wallType = typeSelectionDialog.SelectedWallType;
                FloorType floorType = typeSelectionDialog.SelectedFloorType;

                // ══════════════════════════════════════════════════════════════
                // PASO 0.5: Elegir modo de operación
                // ══════════════════════════════════════════════════════════════
                TaskDialog modoDialog = new TaskDialog("FORMWBIMS Auto-Convert - Modo");
                modoDialog.MainInstruction = "¿Cómo deseas crear el encofrado?";
                modoDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Encofrar elementos completos",
                    "Selecciona elementos (muros, losas, vigas, etc.) y encofra todas sus caras automáticamente");
                modoDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Encofrar caras individuales",
                    "Selecciona manualmente las caras específicas que deseas encofrar");

                TaskDialogResult modoResult = modoDialog.Show();

                if (modoResult == TaskDialogResult.CommandLink2)
                {
                    // MODO: Selección de caras individuales
                    return ProcesarEncofradoCarasIndividualesAutoConvert(uiDoc, doc, wallType, floorType, ref message);
                }
                else if (modoResult != TaskDialogResult.CommandLink1)
                {
                    return Result.Cancelled;
                }

                // MODO: Elementos completos (flujo original)
                // ══════════════════════════════════════════════════════════════
                // PASO 1: Diálogo de selección de categorías (igual que FormwBIMS)
                // ══════════════════════════════════════════════════════════════
                var dialog = new FormwBimsDialog();
                bool? dialogResult = dialog.ShowDialog();

                if (dialogResult != true || !dialog.UserAccepted || !dialog.CategoriasSeleccionadas.Any())
                {
                    return Result.Cancelled;
                }

                var categoriasSeleccionadas = dialog.CategoriasSeleccionadas;

                // Crear filtro dinámico para las categorías seleccionadas
                var filtro = new DynamicCategoryFilter(categoriasSeleccionadas);

                // ══════════════════════════════════════════════════════════════
                // PASO 2: Selección de elementos
                // ══════════════════════════════════════════════════════════════
                var elementosSeleccionados = new List<Element>();
                TaskDialog.Show("FORMWBIMS Auto-Convert",
                    $"Seleccione los elementos a encofrar y convertir.\n\n" +
                    $"Categorías habilitadas:\n" +
                    $"{string.Join("\n", categoriasSeleccionadas.Select(c => "• " + GetCategoryName(c)))}\n\n" +
                    $"El proceso:\n" +
                    $"1️⃣ Creará encofrados (DirectShapes)\n" +
                    $"2️⃣ Los convertirá automáticamente a muros/suelos\n\n" +
                    $"Presione ESC cuando termine de seleccionar.");

                try
                {
                    var selection = uiDoc.Selection.PickObjects(
                        ObjectType.Element,
                        filtro,
                        "Seleccione elementos (ESC para finalizar)");

                    if (!selection.Any())
                    {
                        TaskDialog.Show("FORMWBIMS Auto-Convert", "No se seleccionaron elementos.");
                        return Result.Cancelled;
                    }

                    elementosSeleccionados = selection.Select(r => doc.GetElement(r)).ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                // ══════════════════════════════════════════════════════════════
                // PASO 3: FORMWBIMS - Crear DirectShapes
                // ══════════════════════════════════════════════════════════════
                var directShapesCreados = new List<DirectShape>();
                int totalCarasEncofradas = 0;

                using (var trans = new Transaction(doc, "FORMWBIMS - Crear Encofrados"))
                {
                    trans.Start();

                    // Obtener todos los elementos estructurales
                    var todosLosElementos = ObtenerTodosLosElementosEstructurales(doc);

                    foreach (var elemento in elementosSeleccionados)
                    {
                        // Obtener el sólido principal
                        Solid solido = EncofradoBaseHelper.ObtenerSolidoPrincipal(elemento);
                        if (solido == null) continue;

                        // Obtener elementos adyacentes
                        var elementosAdyacentes = todosLosElementos
                            .Where(e => e.Id != elemento.Id)
                            .ToList();

                        // Determinar el tipo de elemento
                        long categoria = elemento.Category.Id.Value;

                        // Procesar cada cara
                        foreach (Face face in solido.Faces)
                        {
                            if (face is PlanarFace planarFace)
                            {
                                if (DebeEncofrarCara(planarFace, categoria))
                                {
                                    // Crear encofrado inteligente
                                    var ds = EncofradoBaseHelper.CrearEncofradoInteligente(
                                        doc, planarFace, elementosAdyacentes,
                                        $"Encofrado_AutoConvert", elemento);

                                    if (ds != null)
                                    {
                                        directShapesCreados.Add(ds);
                                        totalCarasEncofradas++;
                                    }
                                }
                            }
                        }
                    }

                    trans.Commit();
                }

                if (directShapesCreados.Count == 0)
                {
                    TaskDialog.Show("FORMWBIMS Auto-Convert",
                        "No se pudieron crear encofrados.\n" +
                        "Verifique que los elementos seleccionados tengan geometría válida.");
                    return Result.Failed;
                }

                // Pequeña pausa para asegurar que Revit procese los DirectShapes
                System.Threading.Thread.Sleep(100);

                // ══════════════════════════════════════════════════════════════
                // PASO 4: AUTO-CONVERSIÓN - Convertir DirectShapes a Wall/Floor
                // Flujo: Extraer geometría → Eliminar DirectShapes → Crear Wall/Floor
                // ══════════════════════════════════════════════════════════════
                int murosCreados = 0;
                int suelosCreados = 0;
                int erroresConversion = 0;

                using (var transConvert = new Transaction(doc, "Auto-Convertir a Wall/Floor"))
                {
                    transConvert.Start();

                    // PASO 4.1: EXTRAER datos geométricos de todos los DirectShapes
                    var datosExtraidos = new List<DirectShapeData>();

                    foreach (var ds in directShapesCreados)
                    {
                        try
                        {
                            var datos = DirectShapeGeometryExtractor.ExtraerDatos(doc, ds);
                            if (datos != null)
                            {
                                datosExtraidos.Add(datos);
                            }
                            else
                            {
                                erroresConversion++;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error extrayendo datos de {ds.Id}: {ex.Message}");
                            erroresConversion++;
                        }
                    }

                    // PASO 4.2: ELIMINAR todos los DirectShapes
                    int directShapesEliminados = 0;
                    foreach (var ds in directShapesCreados)
                    {
                        try
                        {
                            doc.Delete(ds.Id);
                            directShapesEliminados++;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error eliminando DirectShape {ds.Id}: {ex.Message}");
                        }
                    }

                    // PASO 4.3: CREAR muros/suelos desde los datos extraídos
                    foreach (var datos in datosExtraidos)
                    {
                        try
                        {
                            if (datos.EsVertical)
                            {
                                // Crear muro
                                Wall muro = DirectShapeGeometryExtractor.CrearMuro(doc, datos, wallType);
                                if (muro != null)
                                {
                                    murosCreados++;
                                }
                                else
                                {
                                    erroresConversion++;
                                }
                            }
                            else
                            {
                                // Crear suelo
                                Floor suelo = DirectShapeGeometryExtractor.CrearSuelo(doc, datos, floorType);
                                if (suelo != null)
                                {
                                    suelosCreados++;
                                }
                                else
                                {
                                    erroresConversion++;
                                }
                            }
                        }
                        catch (Exception exConv)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error creando elemento desde DirectShape {datos.DirectShapeId}: {exConv.Message}");
                            erroresConversion++;
                        }
                    }

                    transConvert.Commit();
                }

                // Mostrar resultados
                TaskDialog.Show("✅ FORMWBIMS Auto-Convert - COMPLETADO",
                    $"Proceso completado exitosamente:\n\n" +
                    $"📊 RESUMEN:\n" +
                    $"  • Elementos procesados: {elementosSeleccionados.Count}\n" +
                    $"  • Caras encofradas: {totalCarasEncofradas}\n" +
                    $"  • DirectShapes creados: {directShapesCreados.Count}\n" +
                    $"  • Muros creados: {murosCreados}\n" +
                    $"  • Suelos creados: {suelosCreados}\n" +
                    $"  • Errores: {erroresConversion}\n\n" +
                    $"✓ Los DirectShapes han sido convertidos a muros y suelos nativos.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}\n{ex.StackTrace}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }

        private List<Element> ObtenerTodosLosElementosEstructurales(Document doc)
        {
            var categorias = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Stairs
            };

            var elementos = new List<Element>();

            foreach (var categoria in categorias)
            {
                var elementosCategoria = new FilteredElementCollector(doc)
                    .OfCategory(categoria)
                    .WhereElementIsNotElementType()
                    .ToList();

                elementos.AddRange(elementosCategoria);
            }

            return elementos;
        }

        private bool DebeEncofrarCara(PlanarFace cara, long categoriaId)
        {
            var normal = cara.FaceNormal;

            switch (categoriaId)
            {
                case (int)BuiltInCategory.OST_StructuralColumns:
                    // Columnas: solo caras verticales
                    return Math.Abs(normal.Z) < 0.1;

                case (int)BuiltInCategory.OST_StructuralFraming:
                    // Vigas: inferior y laterales (no superior)
                    return !normal.IsAlmostEqualTo(XYZ.BasisZ);

                case (int)BuiltInCategory.OST_Walls:
                    // Muros: caras verticales principales
                    return Math.Abs(normal.Z) < 0.1 && cara.Area > 0.1;

                case (int)BuiltInCategory.OST_Floors:
                    // Losas: inferior y bordes
                    return normal.IsAlmostEqualTo(-XYZ.BasisZ) || Math.Abs(normal.Z) < 0.1;

                case (int)BuiltInCategory.OST_Stairs:
                    // Escaleras: inferior, inclinadas y laterales
                    return normal.Z < -0.1 ||
                           Math.Abs(normal.Z) < 0.9 && Math.Abs(normal.Z) > 0.1 ||
                           Math.Abs(normal.X) > 0.9 || Math.Abs(normal.Y) > 0.9;

                case (int)BuiltInCategory.OST_StructuralFoundation:
                    // Cimentación: laterales (no superior ni inferior)
                    return Math.Abs(normal.Z) < 0.1;

                default:
                    // Por defecto: todas las caras excepto la superior
                    return !normal.IsAlmostEqualTo(XYZ.BasisZ);
            }
        }

        private string GetCategoryName(BuiltInCategory category)
        {
            switch (category)
            {
                case BuiltInCategory.OST_StructuralColumns: return "Columnas";
                case BuiltInCategory.OST_Walls: return "Muros";
                case BuiltInCategory.OST_StructuralFraming: return "Vigas";
                case BuiltInCategory.OST_Floors: return "Losas";
                case BuiltInCategory.OST_Stairs: return "Escaleras";
                case BuiltInCategory.OST_StructuralFoundation: return "Cimentación";
                default: return category.ToString();
            }
        }

        /// <summary>
        /// Procesa encofrado de caras individuales + Auto-conversión
        /// </summary>
        private Result ProcesarEncofradoCarasIndividualesAutoConvert(UIDocument uiDoc, Document doc,
            WallType wallType, FloorType floorType, ref string message)
        {
            try
            {
                TaskDialog.Show("FORMWBIMS Auto-Convert - Selección de Caras",
                    "Seleccione las caras individuales que desea encofrar y convertir.\n\n" +
                    "• Puede seleccionar múltiples caras de diferentes elementos\n" +
                    "• Presione ESC cuando termine de seleccionar\n" +
                    "• El encofrado se creará y convertirá automáticamente a muros/suelos");

                // Recolectar caras seleccionadas
                var carasSeleccionadas = new List<(Element elemento, PlanarFace cara)>();

                while (true)
                {
                    try
                    {
                        // Permitir selección de cara
                        Reference faceRef = uiDoc.Selection.PickObject(
                            ObjectType.Face,
                            "Seleccione una cara para encofrar (ESC para finalizar)");

                        if (faceRef != null)
                        {
                            Element elemento = doc.GetElement(faceRef.ElementId);
                            GeometryObject geoObj = elemento.GetGeometryObjectFromReference(faceRef);

                            if (geoObj is Face face && face is PlanarFace planarFace)
                            {
                                carasSeleccionadas.Add((elemento, planarFace));
                                TaskDialog.Show("Cara Agregada",
                                    $"Cara #{carasSeleccionadas.Count} agregada\n" +
                                    $"Elemento: {elemento.Category?.Name ?? "Desconocido"}\n" +
                                    $"Área: {(planarFace.Area * 0.09290304):F2} m²\n\n" +
                                    $"Continúe seleccionando o presione ESC para procesar");
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        break; // Usuario presionó ESC
                    }
                }

                if (!carasSeleccionadas.Any())
                {
                    TaskDialog.Show("FORMWBIMS Auto-Convert", "No se seleccionaron caras.");
                    return Result.Cancelled;
                }

                var directShapesCreados = new List<DirectShape>();

                // ══════════════════════════════════════════════════════════════
                // PASO 1: Crear DirectShapes
                // ══════════════════════════════════════════════════════════════
                using (var trans = new Transaction(doc, "FORMWBIMS - Encofrar Caras"))
                {
                    trans.Start();

                    int carasEncofradas = 0;
                    int errores = 0;
                    double areaTotalEncofrada = 0;

                    // Obtener todos los elementos estructurales para verificar contactos
                    var todosLosElementos = ObtenerTodosLosElementosEstructurales(doc);

                    foreach (var (elemento, cara) in carasSeleccionadas)
                    {
                        try
                        {
                            // Obtener elementos adyacentes
                            var elementosAdyacentes = todosLosElementos
                                .Where(e => e.Id != elemento.Id)
                                .ToList();

                            // Crear encofrado inteligente
                            var ds = EncofradoBaseHelper.CrearEncofradoInteligente(
                                doc, cara, elementosAdyacentes,
                                $"Encofrado_Cara_{carasEncofradas + 1}", elemento);

                            if (ds != null)
                            {
                                directShapesCreados.Add(ds);
                                carasEncofradas++;
                                areaTotalEncofrada += cara.Area;
                            }
                            else
                            {
                                errores++;
                            }
                        }
                        catch (Exception ex)
                        {
                            errores++;
                            System.Diagnostics.Debug.WriteLine($"Error encofrado cara: {ex.Message}");
                        }
                    }

                    trans.Commit();
                }

                if (directShapesCreados.Count == 0)
                {
                    TaskDialog.Show("FORMWBIMS Auto-Convert",
                        "No se pudieron crear encofrados.\n" +
                        "Verifique que las caras seleccionadas sean válidas.");
                    return Result.Failed;
                }

                // Pequeña pausa
                System.Threading.Thread.Sleep(100);

                // ══════════════════════════════════════════════════════════════
                // PASO 2: AUTO-CONVERSIÓN - Convertir DirectShapes a Wall/Floor
                // Flujo: Extraer geometría → Eliminar DirectShapes → Crear Wall/Floor
                // ══════════════════════════════════════════════════════════════
                int murosConvertidos = 0;
                int suelosConvertidos = 0;
                int erroresConversion = 0;

                using (var transConvert = new Transaction(doc, "Auto-Convertir a Wall/Floor"))
                {
                    transConvert.Start();

                    // PASO 2.1: EXTRAER datos geométricos de todos los DirectShapes
                    var datosExtraidos = new List<DirectShapeData>();

                    foreach (var ds in directShapesCreados)
                    {
                        try
                        {
                            var datos = DirectShapeGeometryExtractor.ExtraerDatos(doc, ds);
                            if (datos != null)
                            {
                                datosExtraidos.Add(datos);
                            }
                            else
                            {
                                erroresConversion++;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error extrayendo datos de {ds.Id}: {ex.Message}");
                            erroresConversion++;
                        }
                    }

                    // PASO 2.2: ELIMINAR todos los DirectShapes
                    int directShapesEliminados = 0;
                    foreach (var ds in directShapesCreados)
                    {
                        try
                        {
                            doc.Delete(ds.Id);
                            directShapesEliminados++;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error eliminando DirectShape {ds.Id}: {ex.Message}");
                        }
                    }

                    // PASO 2.3: CREAR muros/suelos desde los datos extraídos
                    foreach (var datos in datosExtraidos)
                    {
                        try
                        {
                            if (datos.EsVertical)
                            {
                                // Crear muro
                                Wall muro = DirectShapeGeometryExtractor.CrearMuro(doc, datos, wallType);
                                if (muro != null)
                                {
                                    murosConvertidos++;
                                }
                                else
                                {
                                    erroresConversion++;
                                }
                            }
                            else
                            {
                                // Crear suelo
                                Floor suelo = DirectShapeGeometryExtractor.CrearSuelo(doc, datos, floorType);
                                if (suelo != null)
                                {
                                    suelosConvertidos++;
                                }
                                else
                                {
                                    erroresConversion++;
                                }
                            }
                        }
                        catch (Exception exConv)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error creando elemento desde DirectShape {datos.DirectShapeId}: {exConv.Message}");
                            erroresConversion++;
                        }
                    }

                    transConvert.Commit();
                }

                var convertResult = (murosConvertidos + suelosConvertidos > 0) ? Result.Succeeded : Result.Failed;

                TaskDialog.Show("✅ FORMWBIMS Auto-Convert - COMPLETADO",
                    $"Proceso completado exitosamente:\n\n" +
                    $"📊 RESUMEN:\n" +
                    $"  • Caras seleccionadas: {carasSeleccionadas.Count}\n" +
                    $"  • DirectShapes creados: {directShapesCreados.Count}\n" +
                    $"  • Muros creados: {murosConvertidos}\n" +
                    $"  • Suelos creados: {suelosConvertidos}\n\n" +
                    $"✓ Los encofrados han sido convertidos a elementos nativos de Revit.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}\n{ex.StackTrace}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }
    }
}
