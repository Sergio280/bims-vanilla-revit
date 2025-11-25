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
public class EncofradoColumnaCommand : IExternalCommand
{
    

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        try
        {
            var selectedRef = uiDoc.Selection.PickObject(ObjectType.Element, 
                new StructuralColumnFilter(), "Seleccione una columna");
            var columna = doc.GetElement(selectedRef);

            using (var trans = new Transaction(doc, "Crear Encofrado de Columna"))
            {
                trans.Start();

                // Obtener el sólido principal de la columna
                Solid solidoColumna = EncofradoBaseHelper.ObtenerSolidoPrincipal(columna);
                
                if (solidoColumna == null)
                {
                    message = "No se pudo obtener la geometría de la columna";
                    trans.RollBack();
                    return Result.Failed;
                }

                // Obtener elementos adyacentes que podrían estar en contacto
                var elementosAdyacentes = EncofradoBaseHelper.ObtenerElementosAdyacentes(doc, columna);
                
                int carasEncofradas = 0;
                int carasConDescuentos = 0;
                int carasOmitidas = 0;
                double areaTotal = 0;
                double areaDescontada = 0;

                // Procesar cada cara de la columna
                foreach (Face face in solidoColumna.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        // Para columnas, encofrar solo las caras verticales
                        // No encofrar:
                        // - Caras superiores e inferiores (horizontales)
                        
                        bool esCaraHorizontal = planarFace.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ) || 
                                               planarFace.FaceNormal.IsAlmostEqualTo(-XYZ.BasisZ);
                        
                        if (!esCaraHorizontal)
                        {
                            double areaOriginal = planarFace.Area;
                            areaTotal += areaOriginal;

                            // Crear encofrado inteligente con descuentos automáticos
                            var ds = EncofradoBaseHelper.CrearEncofradoInteligente(
                                doc, planarFace, elementosAdyacentes, "Encofrado Columna");

                           // List<Wall> listaMuros = EncofradoBaseHelper.CrearMurosDesdeDirectShapeConDescuentos(doc, ds, );

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
                            // Las caras horizontales siempre se omiten
                            carasOmitidas++;
                        }
                    }
                }

                trans.Commit();

                // Mostrar resumen detallado
                string mensaje = $"Encofrado de columna creado:\n" +
                               $"━━━━━━━━━━━━━━━━━━━━\n" +
                               $"✓ Caras encofradas: {carasEncofradas}\n" +
                               $"✓ Caras con descuentos: {carasConDescuentos}\n" +
                               $"✗ Caras omitidas (tapas): {carasOmitidas}\n" +
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

public class StructuralColumnFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        return elem.Category?.Id.Value == (int)BuiltInCategory.OST_StructuralColumns;
    }

    public bool AllowReference(Reference reference, XYZ position)
    {
        return false;
    }
}
