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
public class EncofradoMuroCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        try
        {
            var selectedRef = uiDoc.Selection.PickObject(ObjectType.Element, 
                new WallFilter(), "Seleccione un muro");
            var muro = doc.GetElement(selectedRef);

            using (var trans = new Transaction(doc, "Crear Encofrado de Muro"))
            {
                trans.Start();

                // Obtener el sólido principal del muro
                Solid solidoMuro = EncofradoBaseHelper.ObtenerSolidoPrincipal(muro);
                if (solidoMuro == null)
                {
                    message = "No se pudo obtener la geometría del muro";
                    trans.RollBack();
                    return Result.Failed;
                }

                // Obtener elementos adyacentes que podrían estar en contacto
                var elementosAdyacentes = EncofradoBaseHelper.ObtenerElementosAdyacentes(doc, muro);
                
                int carasEncofradas = 0;
                int carasConDescuentos = 0;
                int carasOmitidas = 0;
                double areaTotal = 0;
                double areaDescontada = 0;

                // Procesar cada cara del muro
                foreach (Face face in solidoMuro.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        // Para muros, encofrar las caras verticales principales
                        // No encofrar:
                        // - Caras superiores e inferiores (horizontales)
                        // - Caras muy pequeñas (bordes)
                        
                        bool esCaraHorizontal = Math.Abs(planarFace.FaceNormal.Z) > 0.9;
                        double areaCara = planarFace.Area;
                        
                        // Filtrar caras muy pequeñas (menos de 0.1 m²)
                        if (!esCaraHorizontal && areaCara > 0.1)
                        {
                            areaTotal += areaCara;

                            // Crear encofrado inteligente con descuentos automáticos
                            var ds = EncofradoBaseHelper.CrearEncofradoInteligente(
                                doc, planarFace, elementosAdyacentes, "Encofrado Muro");
                            
                            if (ds != null)
                            {
                                carasEncofradas++;
                                
                                // Verificar si hubo descuentos
                                var geoElem = ds.get_Geometry(new Options());
                                if (geoElem != null)
                                {
                                    foreach (var geo in geoElem)
                                    {
                                        if (geo is Solid s && s.Volume > 0)
                                        {
                                            // Estimar el área descontada comparando volúmenes
                                            double volumenEsperado = areaCara * 0.02; // espesor 2cm
                                            double volumenReal = s.Volume;
                                            
                                            if (volumenReal < volumenEsperado * 0.95) // 5% de tolerancia
                                            {
                                                carasConDescuentos++;
                                                areaDescontada += (volumenEsperado - volumenReal) / 0.02;
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

                // Mostrar resumen detallado
                string mensaje = $"Encofrado de muro creado:\n" +
                               $"━━━━━━━━━━━━━━━━━━━━\n" +
                               $"✓ Caras encofradas: {carasEncofradas}\n" +
                               $"✓ Caras con descuentos: {carasConDescuentos}\n" +
                               $"✗ Caras omitidas: {carasOmitidas}\n" +
                               $"  (tapas, bordes pequeños)\n" +
                               $"━━━━━━━━━━━━━━━━━━━━\n" +
                               $"Área total procesada: {areaTotal:F2} m²\n" +
                               $"Área descontada (aprox.): {areaDescontada:F2} m²\n" +
                               $"Área neta encofrada: {areaTotal - areaDescontada:F2} m²\n" +
                               $"━━━━━━━━━━━━━━━━━━━━\n" +
                               $"Elementos adyacentes: {elementosAdyacentes.Count}";
                
                TaskDialog.Show("Encofrado Completado", mensaje);
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

public class WallFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        return elem.Category?.Id.Value == (int)BuiltInCategory.OST_Walls;
    }

    public bool AllowReference(Reference reference, XYZ position)
    {
        return false;
    }
}
