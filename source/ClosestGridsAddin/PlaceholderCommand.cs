using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ClosestGridsAddinVANILLA;

/// <summary>
/// Comando placeholder para funciones que requieren implementación completa
/// </summary>
[Transaction(TransactionMode.Manual)]
public class PlaceholderCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        
        return Result.Succeeded;
    }
}

/// <summary>
/// Clase de disponibilidad para comandos placeholder
/// </summary>
public class PlaceholderAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        // Siempre disponible pero mostrará mensaje de no implementado
        return true;
    }
}
