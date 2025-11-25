using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ClosestGridsAddinVANILLA.Commands;
using Nice3point.Revit.Extensions;

namespace ClosestGridsAddinVANILLA;

[Transaction(TransactionMode.Manual)]
public class ClosestGridsCommandVANILLA : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        try
        {
            var selectedRef = uiDoc.Selection.PickObject(ObjectType.Element, "Seleccione un elemento");
            var selectedElement = doc.GetElement(selectedRef);

            if (selectedElement == null)
            {
                message = "No se seleccionó ningún elemento válido";
                return Result.Failed;
            }

            var elementLocation = GetElementLocation(selectedElement);
            if (elementLocation == null)
            {
                message = "No se pudo obtener la ubicación del elemento";
                return Result.Failed;
            }

            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            if (grids.Count == 0)
            {
                TaskDialog.Show("Resultado", "No se encontraron ejes en el proyecto");
                return Result.Succeeded;
            }

            var closestGrids = FindClosestGrids(elementLocation, grids);

            DisplayResults(closestGrids, elementLocation);

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

    private XYZ? GetElementLocation(Element element)
    {
        if (element.Location is LocationPoint locationPoint)
        {
            return locationPoint.Point;
        }
        else if (element.Location is LocationCurve locationCurve)
        {
            var curve = locationCurve.Curve;
            return curve.Evaluate(0.5, true);
        }
        else
        {
            var bbox = element.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) / 2;
            }
        }
        return null;
    }

    private List<(Grid grid, double distance, string axis)> FindClosestGrids(XYZ point, List<Grid> grids)
    {
        var gridDistances = new List<(Grid grid, double distance, string axis)>();

        foreach (var grid in grids)
        {
            var curve = grid.Curve;
            var closestPoint = curve.Project(point).XYZPoint;
            var distance = point.DistanceTo(closestPoint);

            var gridDirection = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
            string axis;

            if (Math.Abs(gridDirection.X) > Math.Abs(gridDirection.Y))
            {
                axis = "Horizontal";
            }
            else
            {
                axis = "Vertical";
            }

            gridDistances.Add((grid, distance, axis));
        }

        var closestHorizontal = gridDistances
            .Where(g => g.axis == "Horizontal")
            .OrderBy(g => g.distance)
            .FirstOrDefault();

        var closestVertical = gridDistances
            .Where(g => g.axis == "Vertical")
            .OrderBy(g => g.distance)
            .FirstOrDefault();
        
        var result = new List<(Grid grid, double distance, string axis)>();
        
        if (closestHorizontal != default)
            result.Add(closestHorizontal);
        
        if (closestVertical != default)
            result.Add(closestVertical);

        return result;
    }

    private void DisplayResults(List<(Grid grid, double distance, string axis)> closestGrids, XYZ elementLocation)
    {
        if (closestGrids.Count == 0)
        {
            TaskDialog.Show("Resultado", "No se encontraron ejes cercanos");
            return;
        }

        var resultText = $"Ubicación del elemento: X={elementLocation.X:F2}, Y={elementLocation.Y:F2}, Z={elementLocation.Z:F2}\n\n";
        resultText += "Ejes más cercanos:\n\n";

        foreach (var (grid, distance, axis) in closestGrids)
        {
            resultText += $"Eje {axis}: {grid.Name}\n";
            resultText += $"Distancia: {UnitUtils.ConvertFromInternalUnits(distance, UnitTypeId.Meters):F3} m\n\n";
        }

        TaskDialog.Show("Ejes Cercanos", resultText);
    }
}
