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
    [Transaction(TransactionMode.Manual)]
    public class FormwBimsCommand : LicensedCommand
    {
        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            try
            {
                // PASO 0: Elegir modo de operación
                TaskDialog modoDialog = new TaskDialog("FORMWBIMS - Modo de Encofrado");
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
                    return ProcesarEncofradoCarasIndividuales(uiDoc, doc, ref message);
                }
                else if (modoResult != TaskDialogResult.CommandLink1)
                {
                    return Result.Cancelled;
                }

                // MODO: Encofrado de elementos completos (flujo original)
                // PASO 1: Mostrar diálogo de selección de categorías
                var dialog = new FormwBimsDialog();
                bool? dialogResult = dialog.ShowDialog();

                if (dialogResult != true || !dialog.UserAccepted || !dialog.CategoriasSeleccionadas.Any())
                {
                    return Result.Cancelled;
                }

                var categoriasSeleccionadas = dialog.CategoriasSeleccionadas;

                // PASO 2: Crear filtro dinámico para las categorías seleccionadas
                var filtro = new DynamicCategoryFilter(categoriasSeleccionadas);

                // PASO 3: Permitir selección múltiple con loop
                var elementosSeleccionados = new List<Element>();
                TaskDialog.Show("FORMWBIMS",
                    $"Seleccione los elementos a encofrar.\n\n" +
                    $"Categorías habilitadas:\n" +
                    $"{string.Join("\n", categoriasSeleccionadas.Select(c => "• " + GetCategoryName(c)))}\n\n" +
                    $"Presione ESC cuando termine de seleccionar.");

                try
                {
                    var selection = uiDoc.Selection.PickObjects(
                        ObjectType.Element,
                        filtro,
                        "Seleccione elementos (ESC para finalizar y procesar)");

                    if (!selection.Any())
                    {
                        TaskDialog.Show("FORMWBIMS", "No se seleccionaron elementos.");
                        return Result.Cancelled;
                    }

                    elementosSeleccionados = selection.Select(r => doc.GetElement(r)).ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    if (!elementosSeleccionados.Any())
                    {
                        TaskDialog.Show("FORMWBIMS", "Operación cancelada.");
                        return Result.Cancelled;
                    }
                }

                // PASO 4: Procesar encofrado
                return ProcesarEncofrado(doc, elementosSeleccionados, ref message);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }

        private Result ProcesarEncofradoCarasIndividuales(UIDocument uiDoc, Document doc, ref string message)
        {
            try
            {
                TaskDialog.Show("FORMWBIMS - Selección de Caras",
                    "Seleccione las caras individuales que desea encofrar.\n\n" +
                    "• Puede seleccionar múltiples caras de diferentes elementos\n" +
                    "• Presione ESC cuando termine de seleccionar\n" +
                    "• El encofrado se creará automáticamente con descuentos inteligentes");

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
                    TaskDialog.Show("FORMWBIMS", "No se seleccionaron caras.");
                    return Result.Cancelled;
                }

                // Procesar las caras seleccionadas
                using (var trans = new Transaction(doc, "FORMWBIMS - Encofrar Caras Individuales"))
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

                    // Mostrar resultados
                    string mensaje = $"✅ FORMWBIMS - CARAS INDIVIDUALES\n" +
                                   $"═══════════════════════════════════════\n\n" +
                                   $"📊 RESUMEN:\n" +
                                   $"  • Caras seleccionadas: {carasSeleccionadas.Count}\n" +
                                   $"  • Caras encofradas exitosamente: {carasEncofradas}\n" +
                                   $"  • Errores: {errores}\n" +
                                   $"  • Área total encofrada: {(areaTotalEncofrada * 0.09290304):F2} m²\n\n" +
                                   $"═══════════════════════════════════════";

                    TaskDialog.Show("FORMWBIMS - Resultados", mensaje);
                    return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }

        private Result ProcesarEncofrado(Document doc, List<Element> elementosSeleccionados, ref string message)
        {
            using (var trans = new Transaction(doc, "FORMWBIMS - Crear Encofrado"))
            {
                trans.Start();

                int totalElementos = 0;
                int totalCarasEncofradas = 0;
                int totalCarasConDescuentos = 0;
                int totalCarasOmitidas = 0;
                double areaTotalProcesada = 0;
                double areaTotalDescontada = 0;

                // Estadísticas por categoría
                var estadisticasPorCategoria = new Dictionary<string, (int elementos, int caras, double area)>();

                // Obtener todos los elementos estructurales para verificar contactos
                var todosLosElementos = ObtenerTodosLosElementosEstructurales(doc);

                foreach (var elemento in elementosSeleccionados)
                {
                    totalElementos++;

                    // Obtener el sólido principal
                    Solid solido = EncofradoBaseHelper.ObtenerSolidoPrincipal(elemento);
                    if (solido == null) continue;

                    // Obtener elementos adyacentes (excluyendo el elemento actual)
                    var elementosAdyacentes = todosLosElementos
                        .Where(e => e.Id != elemento.Id)
                        .ToList();

                    // Determinar el tipo de elemento
                    long categoria = elemento.Category.Id.Value;
                    string tipoElemento = DeterminarTipoElemento(categoria);

                    // Inicializar estadísticas si no existen
                    if (!estadisticasPorCategoria.ContainsKey(tipoElemento))
                    {
                        estadisticasPorCategoria[tipoElemento] = (0, 0, 0);
                    }

                    var stats = estadisticasPorCategoria[tipoElemento];
                    stats.elementos++;

                    // Procesar cada cara según el tipo de elemento
                    foreach (Face face in solido.Faces)
                    {
                        if (face is PlanarFace planarFace)
                        {
                            if (DebeEncofrarCara(planarFace, categoria))
                            {
                                double areaOriginal = planarFace.Area;
                                areaTotalProcesada += areaOriginal;
                                stats.area += areaOriginal;
                                stats.caras++;

                                // Crear encofrado inteligente con descuentos automáticos
                                var ds = EncofradoBaseHelper.CrearEncofradoInteligente(
                                    doc, planarFace, elementosAdyacentes, $"Encofrado {tipoElemento}", elemento);

                                if (ds != null)
                                {
                                    totalCarasEncofradas++;

                                    // Verificar si hubo descuentos
                                    var geoElem = ds.get_Geometry(new Options());
                                    if (geoElem != null)
                                    {
                                        foreach (var geo in geoElem)
                                        {
                                            if (geo is Solid s && s.Volume > 0)
                                            {
                                                // Estimar el área descontada
                                                double volumenEsperado = areaOriginal * 0.02;
                                                double volumenReal = s.Volume;

                                                if (volumenReal < volumenEsperado * 0.95)
                                                {
                                                    totalCarasConDescuentos++;
                                                    areaTotalDescontada += (volumenEsperado - volumenReal) / 0.02;
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                totalCarasOmitidas++;
                            }
                        }
                    }

                    estadisticasPorCategoria[tipoElemento] = stats;
                }

                trans.Commit();

                // Calcular estadísticas generales
                double eficiencia = totalCarasEncofradas > 0 ?
                    totalCarasEncofradas * 100.0 / (totalCarasEncofradas + totalCarasOmitidas) : 0;
                double porcentajeDescuento = areaTotalProcesada > 0 ?
                    areaTotalDescontada * 100.0 / areaTotalProcesada : 0;

                // Construir resumen por categoría
                string resumenCategorias = "";
                foreach (var cat in estadisticasPorCategoria.OrderByDescending(x => x.Value.area))
                {
                    resumenCategorias += $"  • {cat.Key}: {cat.Value.elementos} elementos, " +
                                       $"{cat.Value.caras} caras, {cat.Value.area:F2} m²\n";
                }

                // Mostrar resumen detallado
                string mensaje = $"✅ FORMWBIMS - PROCESO COMPLETADO\n" +
                               $"═══════════════════════════════════════\n\n" +
                               $"📊 RESUMEN GENERAL:\n" +
                               $"  • Elementos procesados: {totalElementos}\n" +
                               $"  • Caras encofradas: {totalCarasEncofradas}\n" +
                               $"  • Caras con descuentos: {totalCarasConDescuentos}\n" +
                               $"  • Caras omitidas: {totalCarasOmitidas}\n" +
                               $"  • Eficiencia: {eficiencia:F1}%\n\n" +
                               $"📐 RESUMEN POR CATEGORÍA:\n{resumenCategorias}\n" +
                               $"📏 ÁREAS:\n" +
                               $"  • Área total procesada: {areaTotalProcesada:F2} m²\n" +
                               $"  • Área descontada: {areaTotalDescontada:F2} m²\n" +
                               $"  • Área neta encofrada: {areaTotalProcesada - areaTotalDescontada:F2} m²\n" +
                               $"  • Porcentaje descontado: {porcentajeDescuento:F1}%\n\n" +
                               $"═══════════════════════════════════════";

                var td = new TaskDialog("FORMWBIMS - Resultados")
                {
                    MainContent = mensaje,
                    MainIcon = TaskDialogIcon.TaskDialogIconInformation,
                    CommonButtons = TaskDialogCommonButtons.Ok
                };
                td.Show();

                return Result.Succeeded;
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

        private string DeterminarTipoElemento(long categoriaId)
        {
            switch (categoriaId)
            {
                case (int)BuiltInCategory.OST_StructuralColumns:
                    return "Columna";
                case (int)BuiltInCategory.OST_StructuralFraming:
                    return "Viga";
                case (int)BuiltInCategory.OST_Walls:
                    return "Muro";
                case (int)BuiltInCategory.OST_Floors:
                    return "Losa";
                case (int)BuiltInCategory.OST_Stairs:
                    return "Escalera";
                case (int)BuiltInCategory.OST_StructuralFoundation:
                    return "Cimentación";
                default:
                    return "Elemento";
            }
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
    }

    /// <summary>
    /// Filtro de selección dinámico que permite múltiples categorías
    /// </summary>
    public class DynamicCategoryFilter : ISelectionFilter
    {
        private readonly HashSet<long> _categoriasPermitidas;

        public DynamicCategoryFilter(List<BuiltInCategory> categorias)
        {
            _categoriasPermitidas = new HashSet<long>(categorias.Select(c => (long)c));
        }

        public bool AllowElement(Element elem)
        {
            if (elem == null || elem.Category == null)
                return false;

            return _categoriasPermitidas.Contains(elem.Category.Id.Value);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
