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
public class EncofradoVigaCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        try
        {
            var selectedRef = uiDoc.Selection.PickObject(ObjectType.Element, 
                new StructuralFramingFilter(), "Seleccione una viga");
            var viga = doc.GetElement(selectedRef);

            using (var trans = new Transaction(doc, "Crear Encofrado de Viga"))
            {
                trans.Start();

                // Obtener el sólido principal de la viga
                Solid solidoViga = EncofradoBaseHelper.ObtenerSolidoPrincipal(viga);
                if (solidoViga == null)
                {
                    message = "No se pudo obtener la geometría de la viga";
                    trans.RollBack();
                    return Result.Failed;
                }

                // Obtener elementos adyacentes que podrían estar en contacto
                var elementosAdyacentes = EncofradoBaseHelper.ObtenerElementosAdyacentes(doc, viga);
                
                int carasEncofradas = 0;
                int carasConDescuentos = 0;
                int carasOmitidas = 0;
                double areaTotal = 0;
                double areaDescontada = 0;
                double volumenTotalOriginal = 0;
                double volumenTotalFinal = 0;

                // Procesar cada cara de la viga
                foreach (Face face in solidoViga.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        // Para vigas, encofrar:
                        // - Cara inferior (normal apunta hacia abajo)
                        // - Caras laterales (normales horizontales)
                        // No encofrar:
                        // - Cara superior (normal apunta hacia arriba)
                        
                        bool esCaraSuperior = planarFace.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ);
                        
                        if (!esCaraSuperior)
                        {
                            double areaOriginal = planarFace.Area;
                            areaTotal += areaOriginal;
                            double volumenEsperado = areaOriginal * 0.01; // espesor 2cm
                            volumenTotalOriginal += volumenEsperado;

                            // Intentar primero con el método de recortes directos
                            DirectShape ds = null;
                            
                            // Método 1: Recortes directos (más preciso)
                            ds = EncofradoBaseHelper.CrearEncofradoConRecortes(
                                doc, planarFace, elementosAdyacentes, "Encofrado Viga - Recortado");
                            
                            // Si falla, usar método 2: Descuentos inteligentes
                            if (ds == null)
                            {
                                ds = EncofradoBaseHelper.CrearEncofradoInteligente(
                                    doc, planarFace, elementosAdyacentes, "Encofrado Viga");
                            }
                            
                            if (ds != null)
                            {
                                carasEncofradas++;
                                
                                // Verificar si hubo descuentos midiendo el volumen
                                var geoElem = ds.get_Geometry(new Options());
                                if (geoElem != null)
                                {
                                    foreach (var geo in geoElem)
                                    {
                                        if (geo is Solid s && s.Volume > 0)
                                        {
                                            double volumenReal = s.Volume;
                                            volumenTotalFinal += volumenReal;
                                            
                                            // Si el volumen es menor al esperado, hubo descuentos
                                            if (volumenReal < volumenEsperado * 0.98) // 2% de tolerancia
                                            {
                                                carasConDescuentos++;
                                                double areaDescontadaCara = (volumenEsperado - volumenReal) / 0.02;
                                                areaDescontada += areaDescontadaCara;
                                            }
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            carasOmitidas++;
                        }
                    }
                }

                trans.Commit();

                // Calcular porcentajes
                double porcentajeDescontado = areaTotal > 0 ? areaDescontada * 100 / areaTotal : 0;
                double porcentajeVolumen = volumenTotalOriginal > 0 ? 
                    (volumenTotalOriginal - volumenTotalFinal) * 100 / volumenTotalOriginal : 0;

                // Mostrar resumen detallado
                string mensaje = $"ENCOFRADO DE VIGA COMPLETADO\n" +
                               $"════════════════════════════\n" +
                               $"📊 RESUMEN DE CARAS:\n" +
                               $"• Caras encofradas: {carasEncofradas}\n" +
                               $"• Caras con recortes: {carasConDescuentos}\n" +
                               $"• Caras omitidas (superior): {carasOmitidas}\n" +
                               $"\n" +
                               $"📐 ANÁLISIS DE ÁREAS:\n" +
                               $"• Área total original: {areaTotal:F2} m²\n" +
                               $"• Área descontada: {areaDescontada:F2} m²\n" +
                               $"• Área neta encofrada: {areaTotal - areaDescontada:F2} m²\n" +
                               $"• Porcentaje descontado: {porcentajeDescontado:F1}%\n" +
                               $"\n" +
                               $"📦 ANÁLISIS DE VOLUMEN:\n" +
                               $"• Volumen original: {volumenTotalOriginal:F4} m³\n" +
                               $"• Volumen final: {volumenTotalFinal:F4} m³\n" +
                               $"• Volumen descontado: {volumenTotalOriginal - volumenTotalFinal:F4} m³\n" +
                               $"• Reducción: {porcentajeVolumen:F1}%\n" +
                               $"\n" +
                               $"🔍 ELEMENTOS DETECTADOS:\n" +
                               $"• Elementos adyacentes: {elementosAdyacentes.Count}\n" +
                               $"════════════════════════════";
                
                var td = new TaskDialog("Encofrado de Viga")
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
}

public class StructuralFramingFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        return elem.Category?.Id.Value == (int)BuiltInCategory.OST_StructuralFraming;
    }

    public bool AllowReference(Reference reference, XYZ position)
    {
        return false;
    }
}
