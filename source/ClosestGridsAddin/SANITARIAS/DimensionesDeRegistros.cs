using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.SANITARIAS
{
    [Transaction(TransactionMode.Manual)]
    public class DimRegistrosSAnitarios : LicensedCommand
    {
        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document Doc = commandData.Application.ActiveUIDocument.Document;
            UIDocument UIDoc = commandData.Application.ActiveUIDocument;
            Selection sel = UIDoc.Selection;

            try
            {
                AplicarPendienteATuberia(Doc, UIDoc);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        public void AplicarPendienteATuberia(Document doc, UIDocument uidoc)
        {
            // Seleccionar tubería
            Reference pipeRef = uidoc.Selection.PickObject(ObjectType.Element, new PipeSelectionFilter(), "Seleccione una tubería");
            Pipe tuberia = doc.GetElement(pipeRef) as Pipe;

            if (tuberia == null)
            {
                TaskDialog.Show("Error", "El elemento seleccionado no es una tubería");
                return;
            }

            using (Transaction trans = new Transaction(doc, "Aplicar pendiente a tubería"))
            {
                trans.Start();

                // Aplicar pendiente según diámetro
                AsignarPendiente(tuberia);

                // Ajustar posición del primer extremo
                AjustarPrimerExtremo(doc, tuberia);

                // Ajustar profundidad de cajas de registro
                AjustarProfundidadCajasRegistro(doc, tuberia);

                trans.Commit();
            }
        }

        private void AsignarPendiente(Pipe tuberia)
        {
            double diametro = tuberia.Diameter * 304.8; // Convertir de pies a mm
            double pendiente;

            if (diametro >= 100) // 4" = 100mm
                pendiente = 0.01; // 1%
            else
                pendiente = 0.015; // 1.5%

            Parameter slopeParam = tuberia.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
            if (slopeParam != null && !slopeParam.IsReadOnly)
            {
                slopeParam.Set(pendiente);
            }
        }

        private void AjustarPrimerExtremo(Document doc, Pipe tuberia)
        {
            // Obtener conectores de la tubería
            ConnectorManager connManager = tuberia.ConnectorManager;
            Connector primerConector = null;
            Connector segundoConector = null;

            foreach (Connector conn in connManager.Connectors)
            {
                if (primerConector == null)
                    primerConector = conn;
                else
                    segundoConector = conn;
            }

            if (primerConector == null) return;

            XYZ puntoInicio = primerConector.Origin;

            // Buscar caja de registro más cercana al primer extremo
            Element cajaRegistroCercana = ObtenerCajaRegistroMasCercana(doc, puntoInicio);

            if (cajaRegistroCercana != null)
            {
                BoundingBoxXYZ bbox = cajaRegistroCercana.get_BoundingBox(null);
                if (bbox != null)
                {
                    double zMinCaja = bbox.Min.Z;
                    double diametroTuberia = tuberia.Diameter;

                    // Calcular nueva elevación: parte más baja + 10cm + mitad del diámetro
                    double nuevaElevacion = zMinCaja + (10.0 / 304.8) + (diametroTuberia / 2.0);

                    // Ajustar elevación del primer punto
                    LocationCurve locCurve = tuberia.Location as LocationCurve;
                    if (locCurve != null)
                    {
                        Line lineaOriginal = locCurve.Curve as Line;
                        if (lineaOriginal != null)
                        {
                            XYZ puntoFinal = lineaOriginal.GetEndPoint(1);
                            XYZ nuevoPuntoInicio = new XYZ(puntoInicio.X, puntoInicio.Y, nuevaElevacion);

                            Line nuevaLinea = Line.CreateBound(nuevoPuntoInicio, puntoFinal);
                            locCurve.Curve = nuevaLinea;
                        }
                    }
                }
            }
        }

        private void AjustarProfundidadCajasRegistro(Document doc, Pipe tuberia)
        {
            LocationCurve locCurve = tuberia.Location as LocationCurve;
            if (locCurve == null) return;

            Line lineaTuberia = locCurve.Curve as Line;
            if (lineaTuberia == null) return;

            // Obtener cajas de registro adyacentes
            List<Element> cajasAdyacentes = ObtenerCajasRegistroAdyacentes(doc, tuberia);

            double diametroTuberia = tuberia.Diameter;

            foreach (Element caja in cajasAdyacentes)
            {
                BoundingBoxXYZ bboxCaja = caja.get_BoundingBox(null);
                if (bboxCaja == null) continue;

                // Calcular el punto de la tubería en la posición de la caja
                XYZ centroCaja = (bboxCaja.Min + bboxCaja.Max) / 2.0;
                XYZ puntoEnTuberia = ObtenerPuntoMasCercanoEnLinea(lineaTuberia, centroCaja);

                // Calcular Z mínimo de la caja necesario
                double zTuberia = puntoEnTuberia.Z;
                double zMinRequerido = zTuberia - (10.0 / 304.8) - (diametroTuberia / 2.0);

                // Obtener nivel actual de la caja
                Parameter paramNivel = caja.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (paramNivel != null)
                {
                    ElementId nivelId = paramNivel.AsElementId();
                    Level nivel = doc.GetElement(nivelId) as Level;

                    if (nivel != null)
                    {
                        double elevacionNivel = nivel.Elevation;
                        double profundidadNecesaria = elevacionNivel - zMinRequerido - (10.0 / 304.8); // Restar espesor de muro

                        // Actualizar parámetro PROFUNDIDAD
                        Parameter paramProfundidad = caja.LookupParameter("PROFUNDIDAD");
                        if (paramProfundidad != null && !paramProfundidad.IsReadOnly)
                        {
                            if (profundidadNecesaria > 0)
                            {
                                paramProfundidad.Set(profundidadNecesaria);
                            }
                        }
                    }
                }
            }
        }

        private Element ObtenerCajaRegistroMasCercana(Document doc, XYZ punto)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType();

            Element cajaMasCercana = null;
            double distanciaMinima = double.MaxValue;

            foreach (Element elem in collector)
            {
                if (EsCajaRegistro(elem))
                {
                    LocationPoint locPt = elem.Location as LocationPoint;
                    if (locPt != null)
                    {
                        double distancia = punto.DistanceTo(locPt.Point);
                        if (distancia < distanciaMinima)
                        {
                            distanciaMinima = distancia;
                            cajaMasCercana = elem;
                        }
                    }
                }
            }

            return cajaMasCercana;
        }

        private List<Element> ObtenerCajasRegistroAdyacentes(Document doc, Pipe tuberia)
        {
            List<Element> cajasAdyacentes = new List<Element>();
            BoundingBoxXYZ bboxTuberia = tuberia.get_BoundingBox(null);

            if (bboxTuberia == null) return cajasAdyacentes;

            double tolerancia = 0.5; // Tolerancia en pies
            Outline outline = new Outline(
                new XYZ(bboxTuberia.Min.X - tolerancia, bboxTuberia.Min.Y - tolerancia, bboxTuberia.Min.Z - tolerancia),
                new XYZ(bboxTuberia.Max.X + tolerancia, bboxTuberia.Max.Y + tolerancia, bboxTuberia.Max.Z + tolerancia)
            );

            BoundingBoxIntersectsFilter bbFilter = new BoundingBoxIntersectsFilter(outline);

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WherePasses(bbFilter);

            foreach (Element elem in collector)
            {
                if (EsCajaRegistro(elem))
                {
                    cajasAdyacentes.Add(elem);
                }
            }

            return cajasAdyacentes;
        }

        private bool EsCajaRegistro(Element elem)
        {
            string familyName = elem.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "";
            return familyName.Contains("Caja Registro", StringComparison.OrdinalIgnoreCase) ||
                   familyName.Contains("OIP_PLM_Caja Registro", StringComparison.OrdinalIgnoreCase);
        }

        private XYZ ObtenerPuntoMasCercanoEnLinea(Line linea, XYZ punto)
        {
            XYZ p0 = linea.GetEndPoint(0);
            XYZ p1 = linea.GetEndPoint(1);
            XYZ v = (p1 - p0).Normalize();

            double t = (punto - p0).DotProduct(v);
            t = Math.Max(0, Math.Min(t, linea.Length));

            return p0 + t * v;
        }
    }

    public class PipeSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            return elem is Pipe;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }

    public static class ElementosAdyacentesHelper
    {
        public static List<Element> ObtenerElementosAdyacentes(Document doc, Element elemento, double toleranciaExtra = 0.3)
        {
            var bbox = elemento.get_BoundingBox(null);
            if (bbox == null) return new List<Element>();

            var min = new XYZ(
                bbox.Min.X - toleranciaExtra,
                bbox.Min.Y - toleranciaExtra,
                bbox.Min.Z - toleranciaExtra);
            var max = new XYZ(
                bbox.Max.X + toleranciaExtra,
                bbox.Max.Y + toleranciaExtra,
                bbox.Max.Z + toleranciaExtra);
            var outline = new Outline(min, max);
            var bbFilter = new BoundingBoxIntersectsFilter(outline);

            var categorias = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Stairs
            };

            var elementosAdyacentes = new List<Element>();

            foreach (var categoria in categorias)
            {
                var elementos = new FilteredElementCollector(doc)
                    .OfCategory(categoria)
                    .WherePasses(bbFilter)
                    .Where(e => e.Id != elemento.Id)
                    .ToList();

                elementosAdyacentes.AddRange(elementos);
            }

            return elementosAdyacentes;
        }
    }
}