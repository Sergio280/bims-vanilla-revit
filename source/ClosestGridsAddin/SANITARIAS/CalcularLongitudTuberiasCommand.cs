using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.SANITARIAS;

/// <summary>
/// Calcula la longitud total de tuberías incluyendo distancias a cajas de registro y aparatos sanitarios
/// </summary>
[Transaction(TransactionMode.Manual)]
public class CalcularLongitudTuberiasCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        try
        {
            // Obtener tuberías seleccionadas
            var seleccion = uidoc.Selection.GetElementIds();
            var tuberias = seleccion
                .Select(id => doc.GetElement(id))
                .Where(e => e is Pipe)
                .Cast<Pipe>()
                .ToList();

            if (!tuberias.Any())
            {
                TaskDialog.Show("Aviso", "No hay tuberías seleccionadas. Por favor, seleccione una o más tuberías.");
                return Result.Cancelled;
            }

            using (Transaction trans = new Transaction(doc, "Calcular Longitud Total Tuberías"))
            {
                trans.Start();

                int contadorProcesadas = 0;

                foreach (var tuberia in tuberias)
                {
                    try
                    {
                        double longitudTotal = 0;

                        // Obtener la longitud de la tubería UNA SOLA VEZ
                        LocationCurve locCurve = tuberia.Location as LocationCurve;
                        if (locCurve != null)
                        {
                            longitudTotal = locCurve.Curve.Length;
                        }

                        // Crear BoundingBox y filtro
                        BoundingBoxXYZ bb = tuberia.get_BoundingBox(null);
                        if (bb != null)
                        {
                            Outline outline = new Outline(bb.Min, bb.Max);
                            BoundingBoxIntersectsFilter bbFilter = new BoundingBoxIntersectsFilter(outline);

                            // Encontrar aparatos sanitarios
                            List<Element> aparatosSanitarios = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                                .WherePasses(bbFilter)
                                .WhereElementIsNotElementType()
                                .ToElements()
                                .Where(e => e.Name.Contains("CONCRETO")) // Excluir la tubería misma
                                .ToList();

                            // Calcular distancias a aparatos sanitarios (solo la distancia, sin la longitud de tubería)
                            foreach (var aparato in aparatosSanitarios)
                            {
                                XYZ centroAparato = ObtenerCentroElemento(aparato);
                                if (centroAparato != null)
                                {
                                    double distancia = CalcularDistanciaDesdeTuberia(tuberia, centroAparato);
                                    longitudTotal += distancia;
                                }
                            }
                        }

                        // Convertir de pies a metros y escribir en parámetro Comentarios
                        double longitudMetros = longitudTotal * 0.3048;
                        Parameter paramComentarios = tuberia.LookupParameter("Comentarios");

                        if (paramComentarios != null && !paramComentarios.IsReadOnly)
                        {
                            paramComentarios.Set($"{longitudMetros:F2}");
                            contadorProcesadas++;
                        }
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Error", $"Error al procesar tubería {tuberia.Id}: {ex.Message}");
                    }
                }

                trans.Commit();

                TaskDialog.Show("Completado",
                    $"Se procesaron {contadorProcesadas} de {tuberias.Count} tuberías correctamente.");
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    /// <summary>
    /// Obtiene el centro planar de un elemento
    /// </summary>
    private XYZ ObtenerCentroElemento(Element elemento)
    {
        // Intentar obtener punto medio de LocationCurve
        if (elemento.Location is LocationCurve locCurve)
        {
            Curve curva = locCurve.Curve;
            return curva.Evaluate(0.5, true);
        }

        // Usar centro del BoundingBox como último recurso
        BoundingBoxXYZ bb = elemento.get_BoundingBox(null);
        if (bb != null)
        {
            return (bb.Min + bb.Max) / 2;
        }

        return null;
    }

    /// <summary>
    /// Calcula SOLO la distancia horizontal desde el extremo más cercano
    /// de la tubería al centro del aparato sanitario (SIN incluir la longitud de la tubería)
    /// </summary>
    private double CalcularDistanciaDesdeTuberia(Pipe tuberia, XYZ punto)
    {
        LocationCurve locCurve = tuberia.Location as LocationCurve;
        if (locCurve != null)
        {
            Curve curva = locCurve.Curve;

            // Obtener puntos inicial y final de la tubería
            XYZ puntoInicial = curva.GetEndPoint(0);
            XYZ puntoFinal = curva.GetEndPoint(1);

            // Convertir a coordenadas UV (plano horizontal XY, ignorando Z)
            UV uvInicial = new UV(puntoInicial.X, puntoInicial.Y);
            UV uvFinal = new UV(puntoFinal.X, puntoFinal.Y);
            UV uvCentroAparato = new UV(punto.X, punto.Y);

            // Calcular distancias horizontales desde cada extremo al centro del aparato
            double distanciaDesdeInicial = uvInicial.DistanceTo(uvCentroAparato);
            double distanciaDesdeFinal = uvFinal.DistanceTo(uvCentroAparato);

            // Retornar SOLO la distancia mínima (sin sumar la longitud de la tubería)
            return Math.Min(distanciaDesdeInicial, distanciaDesdeFinal);
        }

        return 0;
    }
}
