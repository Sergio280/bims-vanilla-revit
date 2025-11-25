using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ENCOFRADO;

[Transaction(TransactionMode.Manual)]
public class EncofradoMultipleCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        try
        {
            // Permitir selección múltiple
            var selection = uiDoc.Selection.PickObjects(ObjectType.Element, 
                new StructuralElementFilter(), 
                "Seleccione elementos estructurales para encofrar (ESC para terminar)");
            
            if (!selection.Any())
            {
                TaskDialog.Show("Aviso", "No se seleccionaron elementos");
                return Result.Cancelled;
            }

            var elementosSeleccionados = selection.Select(r => doc.GetElement(r)).ToList();

            using (var trans = new Transaction(doc, "Crear Encofrado Múltiple"))
            {
                trans.Start();

                int totalElementos = 0;
                int totalCarasEncofradas = 0;
                int totalCarasConDescuentos = 0;
                int totalCarasOmitidas = 0;
                double areaTotalProcesada = 0;
                double areaTotalDescontada = 0;

                // Obtener todos los elementos estructurales del modelo para verificar contactos
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

                    // Procesar cada cara según el tipo de elemento
                    foreach (Face face in solido.Faces)
                    {
                        if (face is PlanarFace planarFace)
                        {
                            if (DebeEncofrarCara(planarFace, categoria))
                            {
                                double areaOriginal = planarFace.Area;
                                areaTotalProcesada += areaOriginal;

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
                }

                trans.Commit();

                // Calcular estadísticas
                double eficiencia = totalCarasEncofradas > 0 ? 
                    totalCarasEncofradas * 100.0 / (totalCarasEncofradas + totalCarasOmitidas) : 0;
                double porcentajeDescuento = areaTotalProcesada > 0 ?
                    areaTotalDescontada * 100.0 / areaTotalProcesada : 0;

                // Mostrar resumen detallado
                string mensaje = $"ENCOFRADO MÚLTIPLE COMPLETADO\n" +
                               $"═══════════════════════════════\n" +
                               $"📊 RESUMEN DE ELEMENTOS:\n" +
                               $"• Elementos procesados: {totalElementos}\n" +
                               $"• Elementos estructurales en modelo: {todosLosElementos.Count}\n" +
                               $"\n" +
                               $"📐 RESUMEN DE CARAS:\n" +
                               $"• Caras encofradas: {totalCarasEncofradas}\n" +
                               $"• Caras con descuentos: {totalCarasConDescuentos}\n" +
                               $"• Caras omitidas: {totalCarasOmitidas}\n" +
                               $"• Eficiencia: {eficiencia:F1}%\n" +
                               $"\n" +
                               $"📏 RESUMEN DE ÁREAS:\n" +
                               $"• Área total procesada: {areaTotalProcesada:F2} m²\n" +
                               $"• Área descontada: {areaTotalDescontada:F2} m²\n" +
                               $"• Área neta encofrada: {areaTotalProcesada - areaTotalDescontada:F2} m²\n" +
                               $"• Porcentaje descontado: {porcentajeDescuento:F1}%\n" +
                               $"\n" +
                               $"═══════════════════════════════\n" +
                               $"✅ Proceso completado con éxito";
                
                var td = new TaskDialog("Encofrado Múltiple")
                {
                    MainContent = mensaje,
                    MainIcon = TaskDialogIcon.TaskDialogIconInformation,
                    CommonButtons = TaskDialogCommonButtons.Ok
                };
                td.Show();
                
                return Result.Succeeded;
            }
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
}

public class StructuralElementFilter : ISelectionFilter
{
    private readonly HashSet<long> _categorias = new HashSet<long>
    {
        (int)BuiltInCategory.OST_StructuralColumns,
        (int)BuiltInCategory.OST_StructuralFraming,
        (int)BuiltInCategory.OST_Walls,
        (int)BuiltInCategory.OST_Floors,
        (int)BuiltInCategory.OST_Stairs,
        (int)BuiltInCategory.OST_StructuralFoundation
    };

    public bool AllowElement(Element elem)
    {
        return elem.Category != null && _categorias.Contains((long)elem.Category.Id.Value);
    }

    public bool AllowReference(Reference reference, XYZ position)
    {
        return false;
    }
}
