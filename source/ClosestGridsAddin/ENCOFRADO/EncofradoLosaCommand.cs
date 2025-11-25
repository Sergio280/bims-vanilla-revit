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
public class EncofradoLosaCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        try
        {
            var selectedRef = uiDoc.Selection.PickObject(ObjectType.Element, 
                new FloorFilter(), "Seleccione una losa");
            var losa = doc.GetElement(selectedRef);

            using (var trans = new Transaction(doc, "Crear Encofrado de Losa"))
            {
                trans.Start();

                // Obtener el sólido principal de la losa
                Solid solidoLosa = EncofradoBaseHelper.ObtenerSolidoPrincipal(losa);
                if (solidoLosa == null)
                {
                    message = "No se pudo obtener la geometría de la losa";
                    trans.RollBack();
                    return Result.Failed;
                }

                // Obtener elementos adyacentes que podrían estar en contacto
                var elementosAdyacentes = EncofradoBaseHelper.ObtenerElementosAdyacentes(doc, losa);
                
                int carasEncofradas = 0;
                int carasConDescuentos = 0;
                int carasOmitidas = 0;
                bool fondoEncofrado = false;
                double areaTotal = 0;
                double areaDescontada = 0;

                // Procesar cada cara de la losa
                foreach (Face face in solidoLosa.Faces)
                {
                    if (face is PlanarFace planarFace)
                    {
                        // Para losas, encofrar:
                        // - Cara inferior (fondo)
                        // - Caras laterales (bordes)
                        
                        bool esCaraInferior = planarFace.FaceNormal.IsAlmostEqualTo(-XYZ.BasisZ);
                        bool esCaraSuperior = planarFace.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ);
                        bool esCaraLateral = Math.Abs(planarFace.FaceNormal.Z) < 0.1;
                        
                        if (esCaraInferior || esCaraLateral)
                        {
                            double areaOriginal = planarFace.Area;
                            areaTotal += areaOriginal;

                            // Crear encofrado inteligente con descuentos automáticos
                            var nombreEncofrado = esCaraInferior ? "Encofrado Losa - Fondo" : "Encofrado Losa - Lateral";
                            var ds = EncofradoBaseHelper.CrearEncofradoInteligente(
                                doc, planarFace, elementosAdyacentes, nombreEncofrado);
                            
                            if (ds != null)
                            {
                                carasEncofradas++;
                                if (esCaraInferior) fondoEncofrado = true;
                                
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
                        else if (esCaraSuperior)
                        {
                            // La cara superior nunca se encofra
                            carasOmitidas++;
                        }
                    }
                }

                trans.Commit();

                // Mostrar resumen detallado
                string mensaje = $"Encofrado de losa creado:\n" +
                               $"━━━━━━━━━━━━━━━━━━━━\n" +
                               $"✓ Caras encofradas: {carasEncofradas}\n" +
                               $"✓ Fondo encofrado: {(fondoEncofrado ? "Sí" : "No")}\n" +
                               $"✓ Caras con descuentos: {carasConDescuentos}\n" +
                               $"✗ Caras omitidas (superior): {carasOmitidas}\n" +
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

public class FloorFilter : ISelectionFilter
{
    public bool AllowElement(Element elem)
    {
        return elem.Category?.Id.Value == (int)BuiltInCategory.OST_Floors;
    }

    public bool AllowReference(Reference reference, XYZ position)
    {
        return false;
    }
}
