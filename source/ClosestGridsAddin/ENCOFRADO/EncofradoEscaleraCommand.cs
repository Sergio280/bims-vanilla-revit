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
public class EncofradoEscaleraCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        try
        {
            var selectedRef = uiDoc.Selection.PickObject(ObjectType.Element, 
                new StairsFilter(), "Seleccione una escalera");
            var escalera = doc.GetElement(selectedRef);

            using (var trans = new Transaction(doc, "Crear Encofrado de Escalera"))
            {
                trans.Start();

                // Obtener el sólido principal de la escalera
                Solid solidoEscalera = EncofradoBaseHelper.ObtenerSolidoPrincipal(escalera);
                if (solidoEscalera == null)
                {
                    message = "No se pudo obtener la geometría de la escalera";
                    trans.RollBack();
                    return Result.Failed;
                }

                // Obtener elementos adyacentes que podrían estar en contacto
                var elementosAdyacentes = EncofradoBaseHelper.ObtenerElementosAdyacentes(doc, escalera);
                
                int carasEncofradas = 0;
                int carasConDescuentos = 0;
                int carasOmitidas = 0;
                double areaTotal = 0;
                double areaDescontada = 0;

                // Procesar cada cara de la escalera
                foreach (Face face in solidoEscalera.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        var normal = planarFace.FaceNormal;
                        
                        // Para escaleras, encofrar:
                        // - Caras inferiores (normal apunta hacia abajo)
                        // - Caras inclinadas (huellas y contrahuellas)
                        // - Caras laterales
                        // No encofrar:
                        // - Caras superiores horizontales (peldaños)
                        
                        bool esCaraSuperiorHorizontal = normal.Z > 0.9;
                        bool debeEncofrar = normal.Z < -0.1 || // Caras inferiores
                                          Math.Abs(normal.Z) < 0.9 && Math.Abs(normal.Z) > 0.1 || // Caras inclinadas
                                          Math.Abs(normal.X) > 0.9 || Math.Abs(normal.Y) > 0.9; // Caras laterales
                        
                        if (debeEncofrar && !esCaraSuperiorHorizontal)
                        {
                            double areaOriginal = planarFace.Area;
                            areaTotal += areaOriginal;

                            // Crear encofrado inteligente con descuentos automáticos
                            var ds = EncofradoBaseHelper.CrearEncofradoInteligente(
                                doc, planarFace, elementosAdyacentes, "Encofrado Escalera");
                            
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
                                            double volumenEsperado = areaOriginal * 0.02; // espesor 2cm
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
                string mensaje = $"Encofrado de escalera creado:\n" +
                               $"━━━━━━━━━━━━━━━━━━━━\n" +
                               $"✓ Caras encofradas: {carasEncofradas}\n" +
                               $"✓ Caras con descuentos: {carasConDescuentos}\n" +
                               $"✗ Caras omitidas: {carasOmitidas}\n" +
                               $"  (peldaños superiores)\n" +
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

public class StairsFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        return elem.Category?.Id.Value == (int)BuiltInCategory.OST_Stairs;
    }

    public bool AllowReference(Reference reference, XYZ position)
    {
        return false;
    }
}
