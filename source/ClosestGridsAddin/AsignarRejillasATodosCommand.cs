using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClosestGridsAddinVANILLA;

/// <summary>
/// Comando para asignar rejillas a todos los elementos del proyecto automáticamente
/// </summary>
[Transaction(TransactionMode.Manual)]
public class AsignarRejillasATodosCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            if (doc == null)
            {
                TaskDialog.Show("Error", "No hay documento activo. Por favor, abra un proyecto antes de ejecutar el comando.");
                return Result.Failed;
            }

            // Obtener todas las rejillas del proyecto
            FilteredElementCollector colectorRejillas = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid));

            List<Grid> rejillasHorizontales = new List<Grid>();
            List<Grid> rejillasVerticales = new List<Grid>();
            List<Grid> rejillasOtras = new List<Grid>();

            // Clasificar rejillas por orientación
            foreach (Grid rejilla in colectorRejillas)
            {
                ClasificarRejillaPorOrientacion(rejilla, rejillasHorizontales, rejillasVerticales, rejillasOtras);
            }

            using (Transaction tx = new Transaction(doc, "Asignar Rejillas a Todos los Elementos"))
            {
                tx.Start();

                int elementosActualizados = 0;
                StringBuilder logResultados = new StringBuilder();
                logResultados.AppendLine("INFORME DE ASIGNACIÓN DE REJILLAS A TODOS LOS ELEMENTOS");
                logResultados.AppendLine("=========================================================");
                logResultados.AppendLine();

                // COLUMNAS ESTRUCTURALES
                elementosActualizados += AsignarRejillasAColumnas(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // MUROS
                elementosActualizados += AsignarRejillasAMuros(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // VIGAS
                elementosActualizados += AsignarRejillasAVigas(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // SUELOS
                elementosActualizados += AsignarRejillasASuelos(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // PUERTAS
                elementosActualizados += AsignarRejillasAPuertas(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // VENTANAS
                elementosActualizados += AsignarRejillasAVentanas(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // ELEMENTOS GENÉRICOS
                elementosActualizados += AsignarRejillasAGenéricos(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // ESCALERAS
                elementosActualizados += AsignarRejillasAEscaleras(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // BARANDILLAS
                elementosActualizados += AsignarRejillasABarandillas(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // MOBILIARIO
                elementosActualizados += AsignarRejillasAMobiliario(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // CASEWORK (MUEBLES EMPOTRADOS)
                elementosActualizados += AsignarRejillasACasework(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // APARATOS DE FONTANERÍA
                elementosActualizados += AsignarRejillasAFontaneria(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // VEGETACIÓN/PLANTAS
                elementosActualizados += AsignarRejillasAVegetacion(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // APARATOS ELÉCTRICOS
                elementosActualizados += AsignarRejillasAElectricos(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // APARATOS MECÁNICOS
                elementosActualizados += AsignarRejillasAMecanicos(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                // LUMINARIAS ELÉCTRICAS
                elementosActualizados += AsignarRejillasALuminarias(doc, rejillasHorizontales, rejillasVerticales, rejillasOtras, logResultados);

                tx.Commit();

                // Mostrar resultados
                TaskDialog dialogoResultado = new TaskDialog("Asignación de Rejillas Completada");
                dialogoResultado.MainInstruction = $"Se procesaron {elementosActualizados} elementos con éxito";
                dialogoResultado.MainContent = $"Rejillas identificadas:\n" +
                    $"- Horizontales: {rejillasHorizontales.Count}\n" +
                    $"- Verticales: {rejillasVerticales.Count}\n" +
                    $"- Otras orientaciones: {rejillasOtras.Count}\n\n" +
                    "Las rejillas han sido asignadas a todos los elementos compatibles del proyecto.";
                dialogoResultado.ExpandedContent = logResultados.ToString();
                dialogoResultado.CommonButtons = TaskDialogCommonButtons.Ok;
                dialogoResultado.Show();
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = $"Error en AsignarRejillasATodosCommand: {ex.Message}\n\n{ex.StackTrace}";
            TaskDialog.Show("Error", message);
            return Result.Failed;
        }
    }

    #region Clasificación de Rejillas

    private void ClasificarRejillaPorOrientacion(Grid rejilla, List<Grid> horizontales, List<Grid> verticales, List<Grid> otras)
    {
        try
        {
            Curve curva = rejilla.Curve;

            if (curva is Line linea)
            {
                XYZ direccion = linea.Direction.Normalize();

                // Horizontal (paralela al eje X)
                if (Math.Abs(direccion.Y) < 0.1 && Math.Abs(direccion.Z) < 0.1)
                    horizontales.Add(rejilla);
                // Vertical (paralela al eje Y)
                else if (Math.Abs(direccion.X) < 0.1 && Math.Abs(direccion.Z) < 0.1)
                    verticales.Add(rejilla);
                else
                    otras.Add(rejilla);
            }
            else
            {
                otras.Add(rejilla);
            }
        }
        catch
        {
            otras.Add(rejilla);
        }
    }

    #endregion

    #region Asignación por Categoría

    private int AsignarRejillasAColumnas(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== COLUMNAS ESTRUCTURALES ===");

            foreach (Element columna in collector)
            {
                LocationPoint locPoint = columna.Location as LocationPoint;
                if (locPoint == null) continue;

                XYZ puntoColumna = locPoint.Point;

                // Encontrar rejilla más cercana en X e Y
                Grid rejillaX = EncontrarRejillaMasCercanaEnDireccion(verticales, puntoColumna, "X");
                Grid rejillaY = EncontrarRejillaMasCercanaEnDireccion(horizontales, puntoColumna, "Y");

                if (rejillaX != null && rejillaY != null)
                {
                    string ubicacion = $"{rejillaX.Name}-{rejillaY.Name}";

                    if (AsignarUbicacionAElemento(columna, rejillaX.Name, rejillaY.Name, ubicacion))
                    {
                        actualizados++;
                        log.AppendLine($"  Columna ID {columna.Id.Value}: {ubicacion}");
                    }
                }
            }

            log.AppendLine($"  Total columnas actualizadas: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en columnas: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAMuros(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== MUROS ===");

            foreach (Element muro in collector)
            {
                if (AsignarRejillasAMuroIndividual(doc, muro, horizontales, verticales, otras))
                {
                    actualizados++;

                    Parameter paramUbicacion = muro.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Muro ID {muro.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total muros actualizados: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en muros: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAVigas(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== VIGAS ESTRUCTURALES ===");

            foreach (Element viga in collector)
            {
                if (AsignarRejillasAVigaIndividual(doc, viga, horizontales, verticales, otras))
                {
                    actualizados++;

                    Parameter paramUbicacion = viga.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Viga ID {viga.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total vigas actualizadas: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en vigas: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasASuelos(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== SUELOS ===");

            foreach (Element suelo in collector)
            {
                if (AsignarRejillasASueloIndividual(doc, suelo, horizontales, verticales, otras))
                {
                    actualizados++;

                    Parameter paramUbicacion = suelo.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Suelo ID {suelo.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total suelos actualizados: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en suelos: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAPuertas(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== PUERTAS ===");

            foreach (Element puerta in collector)
            {
                if (AsignarRejillasAElementoPuntual(puerta, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = puerta.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Puerta ID {puerta.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total puertas actualizadas: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en puertas: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAVentanas(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== VENTANAS ===");

            foreach (Element ventana in collector)
            {
                if (AsignarRejillasAElementoPuntual(ventana, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = ventana.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Ventana ID {ventana.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total ventanas actualizadas: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en ventanas: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAGenéricos(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== ELEMENTOS GENÉRICOS ===");

            foreach (Element elemento in collector)
            {
                if (AsignarRejillasAElementoPuntual(elemento, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = elemento.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Elemento ID {elemento.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total elementos genéricos actualizados: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en elementos genéricos: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAEscaleras(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Stairs)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== ESCALERAS ===");

            foreach (Element escalera in collector)
            {
                if (AsignarRejillasAElementoLineal(escalera, horizontales, verticales, otras))
                {
                    actualizados++;

                    Parameter paramUbicacion = escalera.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Escalera ID {escalera.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total escaleras actualizadas: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en escaleras: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasABarandillas(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Railings)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== BARANDILLAS ===");

            foreach (Element barandilla in collector)
            {
                if (AsignarRejillasAElementoLineal(barandilla, horizontales, verticales, otras))
                {
                    actualizados++;

                    Parameter paramUbicacion = barandilla.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Barandilla ID {barandilla.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total barandillas actualizadas: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en barandillas: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAMobiliario(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Furniture)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== MOBILIARIO ===");

            foreach (Element mobiliario in collector)
            {
                if (AsignarRejillasAElementoPuntual(mobiliario, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = mobiliario.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Mobiliario ID {mobiliario.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total mobiliario actualizado: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en mobiliario: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasACasework(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Casework)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== MUEBLES EMPOTRADOS (CASEWORK) ===");

            foreach (Element casework in collector)
            {
                if (AsignarRejillasAElementoPuntual(casework, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = casework.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Casework ID {casework.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total casework actualizado: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en casework: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAFontaneria(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== APARATOS DE FONTANERÍA ===");

            foreach (Element fontaneria in collector)
            {
                if (AsignarRejillasAElementoPuntual(fontaneria, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = fontaneria.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Fontanería ID {fontaneria.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total aparatos de fontanería actualizados: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en aparatos de fontanería: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAVegetacion(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Planting)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== VEGETACIÓN/PLANTAS ===");

            foreach (Element planta in collector)
            {
                if (AsignarRejillasAElementoPuntual(planta, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = planta.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Planta ID {planta.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total vegetación actualizada: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en vegetación: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAElectricos(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== APARATOS ELÉCTRICOS ===");

            foreach (Element electrico in collector)
            {
                if (AsignarRejillasAElementoPuntual(electrico, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = electrico.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Aparato eléctrico ID {electrico.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total aparatos eléctricos actualizados: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en aparatos eléctricos: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasAMecanicos(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== APARATOS MECÁNICOS ===");

            foreach (Element mecanico in collector)
            {
                if (AsignarRejillasAElementoPuntual(mecanico, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = mecanico.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Aparato mecánico ID {mecanico.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total aparatos mecánicos actualizados: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en aparatos mecánicos: {ex.Message}");
            return 0;
        }
    }

    private int AsignarRejillasALuminarias(Document doc, List<Grid> horizontales, List<Grid> verticales,
        List<Grid> otras, StringBuilder log)
    {
        try
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                .WhereElementIsNotElementType();

            int actualizados = 0;
            log.AppendLine("\n=== LUMINARIAS ELÉCTRICAS ===");

            foreach (Element luminaria in collector)
            {
                if (AsignarRejillasAElementoPuntual(luminaria, horizontales, verticales))
                {
                    actualizados++;

                    Parameter paramUbicacion = luminaria.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");
                    string ubicacion = paramUbicacion?.AsString() ?? "No asignada";
                    log.AppendLine($"  Luminaria ID {luminaria.Id.Value}: {ubicacion}");
                }
            }

            log.AppendLine($"  Total luminarias actualizadas: {actualizados}");
            return actualizados;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  Error en luminarias: {ex.Message}");
            return 0;
        }
    }

    #endregion

    #region Métodos Auxiliares - Asignación por Tipo de Elemento

    private bool AsignarRejillasAElementoPuntual(Element elemento, List<Grid> horizontales, List<Grid> verticales)
    {
        try
        {
            XYZ punto = ObtenerPuntoRepresentativoElemento(elemento);
            if (punto == null) return false;

            Grid rejillaX = EncontrarRejillaMasCercanaEnDireccion(verticales, punto, "X");
            Grid rejillaY = EncontrarRejillaMasCercanaEnDireccion(horizontales, punto, "Y");

            if (rejillaX != null && rejillaY != null)
            {
                string ubicacion = $"{rejillaX.Name}-{rejillaY.Name}";
                return AsignarUbicacionAElemento(elemento, rejillaX.Name, rejillaY.Name, ubicacion);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool AsignarRejillasAElementoLineal(Element elemento, List<Grid> horizontales, List<Grid> verticales, List<Grid> otras)
    {
        try
        {
            BoundingBoxXYZ bbox = elemento.get_BoundingBox(null);
            if (bbox == null) return false;

            XYZ centro = new XYZ(
                (bbox.Min.X + bbox.Max.X) / 2,
                (bbox.Min.Y + bbox.Max.Y) / 2,
                (bbox.Min.Z + bbox.Max.Z) / 2);

            LocationCurve locCurve = elemento.Location as LocationCurve;
            if (locCurve != null)
            {
                Curve curva = locCurve.Curve;
                XYZ inicio = curva.GetEndPoint(0);
                XYZ fin = curva.GetEndPoint(1);

                List<Grid> rejillasRelevantes = new List<Grid>();
                rejillasRelevantes.AddRange(horizontales);
                rejillasRelevantes.AddRange(verticales);
                rejillasRelevantes.AddRange(otras);

                var rejillaInicio = EncontrarRejillaMasCercana(rejillasRelevantes, inicio);
                var rejillaFin = EncontrarRejillaMasCercana(rejillasRelevantes, fin, rejillaInicio?.Item1?.Id);

                if (rejillaInicio != null && rejillaFin != null)
                {
                    string ubicacion = $"{rejillaInicio.Item1.Name} - {rejillaFin.Item1.Name}";

                    Parameter paramInicial = elemento.LookupParameter("REJILLA INICIAL");
                    Parameter paramFinal = elemento.LookupParameter("REJILLA FINAL");
                    Parameter paramUbicacion = elemento.LookupParameter("UBICACIÓN POR REJILLAS");

                    bool actualizado = false;

                    if (paramInicial != null && !paramInicial.IsReadOnly)
                    {
                        paramInicial.Set(rejillaInicio.Item1.Name);
                        actualizado = true;
                    }

                    if (paramFinal != null && !paramFinal.IsReadOnly)
                    {
                        paramFinal.Set(rejillaFin.Item1.Name);
                        actualizado = true;
                    }

                    if (paramUbicacion != null && !paramUbicacion.IsReadOnly)
                    {
                        paramUbicacion.Set(ubicacion);
                        actualizado = true;
                    }

                    return actualizado;
                }
            }

            return AsignarRejillasAElementoPuntual(elemento, horizontales, verticales);
        }
        catch
        {
            return false;
        }
    }

    private bool AsignarRejillasAMuroIndividual(Document doc, Element muro, List<Grid> horizontales, List<Grid> verticales, List<Grid> otras)
    {
        try
        {
            LocationCurve locCurve = muro.Location as LocationCurve;
            if (locCurve == null) return false;

            Curve curvaMuro = locCurve.Curve;
            XYZ ptoInicio = curvaMuro.GetEndPoint(0);
            XYZ ptoFin = curvaMuro.GetEndPoint(1);
            XYZ direccionMuro = (ptoFin - ptoInicio).Normalize();

            List<Grid> rejillasParalelas = new List<Grid>();
            List<Grid> rejillasPerpendiculares = new List<Grid>();

            if (Math.Abs(direccionMuro.X) > Math.Abs(direccionMuro.Y))
            {
                rejillasParalelas.AddRange(verticales);
                rejillasPerpendiculares.AddRange(horizontales);
                if (rejillasParalelas.Count < 2) rejillasParalelas.AddRange(otras);
            }
            else
            {
                rejillasParalelas.AddRange(horizontales);
                rejillasPerpendiculares.AddRange(verticales);
                if (rejillasParalelas.Count < 2) rejillasParalelas.AddRange(otras);
            }

            if (rejillasParalelas.Count < 2) return false;

            var rejillaInicio = EncontrarRejillaMasCercana(rejillasParalelas, ptoInicio);
            if (rejillaInicio == null) return false;

            var rejillaFin = EncontrarRejillaMasCercana(rejillasParalelas, ptoFin, rejillaInicio.Item1.Id);
            if (rejillaFin == null) return false;

            Grid rejillaParalelaAlMuro = null;
            if (rejillasPerpendiculares.Count > 0)
            {
                XYZ puntoCentro = curvaMuro.Evaluate(0.5, true);
                var rejillaParalela = EncontrarRejillaMasCercana(rejillasPerpendiculares, puntoCentro);
                if (rejillaParalela != null) rejillaParalelaAlMuro = rejillaParalela.Item1;
            }

            string textoUbicacion;
            if (rejillaParalelaAlMuro != null)
            {
                textoUbicacion = $"{rejillaParalelaAlMuro.Name} / {rejillaInicio.Item1.Name} - {rejillaFin.Item1.Name}";
            }
            else
            {
                textoUbicacion = $"{rejillaInicio.Item1.Name} - {rejillaFin.Item1.Name}";
            }

            return AsignarParametrosDeRejilla(doc, muro, rejillaInicio.Item1, rejillaFin.Item1, textoUbicacion);
        }
        catch
        {
            return false;
        }
    }

    private bool AsignarRejillasAVigaIndividual(Document doc, Element viga, List<Grid> horizontales, List<Grid> verticales, List<Grid> otras)
    {
        return AsignarRejillasAMuroIndividual(doc, viga, horizontales, verticales, otras);
    }

    private bool AsignarRejillasASueloIndividual(Document doc, Element suelo, List<Grid> horizontales, List<Grid> verticales, List<Grid> otras)
    {
        try
        {
            BoundingBoxXYZ bbox = suelo.get_BoundingBox(null);
            if (bbox == null) return false;

            XYZ centerPoint = new XYZ(
                (bbox.Min.X + bbox.Max.X) / 2,
                (bbox.Min.Y + bbox.Max.Y) / 2,
                (bbox.Min.Z + bbox.Max.Z) / 2);

            Grid rejillaInicialX = null;
            Grid rejillaFinalX = null;
            Grid rejillaInicialY = null;
            Grid rejillaFinalY = null;

            if (verticales.Count >= 2)
            {
                XYZ minXPoint = new XYZ(bbox.Min.X, centerPoint.Y, centerPoint.Z);
                XYZ maxXPoint = new XYZ(bbox.Max.X, centerPoint.Y, centerPoint.Z);

                var rejillaMinX = EncontrarRejillaMasCercana(verticales, minXPoint);
                if (rejillaMinX != null) rejillaInicialX = rejillaMinX.Item1;

                var rejillaMaxX = EncontrarRejillaMasCercana(verticales, maxXPoint,
                                                   rejillaInicialX != null ? rejillaInicialX.Id : null);
                if (rejillaMaxX != null) rejillaFinalX = rejillaMaxX.Item1;
            }

            if (horizontales.Count >= 2)
            {
                XYZ minYPoint = new XYZ(centerPoint.X, bbox.Min.Y, centerPoint.Z);
                XYZ maxYPoint = new XYZ(centerPoint.X, bbox.Max.Y, centerPoint.Z);

                var rejillaMinY = EncontrarRejillaMasCercana(horizontales, minYPoint);
                if (rejillaMinY != null) rejillaInicialY = rejillaMinY.Item1;

                var rejillaMaxY = EncontrarRejillaMasCercana(horizontales, maxYPoint,
                                                   rejillaInicialY != null ? rejillaInicialY.Id : null);
                if (rejillaMaxY != null) rejillaFinalY = rejillaMaxY.Item1;
            }

            if (rejillaInicialX != null && rejillaFinalX != null && rejillaInicialY != null && rejillaFinalY != null)
            {
                string textoUbicacion = $"{rejillaInicialX.Name}-{rejillaFinalX.Name} / {rejillaInicialY.Name}-{rejillaFinalY.Name}";
                return AsignarParametrosDeRejillaSuelo(doc, suelo, rejillaInicialX, rejillaFinalX, rejillaInicialY, rejillaFinalY, textoUbicacion);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Métodos Auxiliares - Búsqueda de Rejillas

    private XYZ ObtenerPuntoRepresentativoElemento(Element elemento)
    {
        try
        {
            LocationPoint locPoint = elemento.Location as LocationPoint;
            if (locPoint != null)
                return locPoint.Point;

            LocationCurve locCurve = elemento.Location as LocationCurve;
            if (locCurve != null)
                return locCurve.Curve.Evaluate(0.5, true);

            BoundingBoxXYZ bbox = elemento.get_BoundingBox(null);
            if (bbox != null)
            {
                return new XYZ(
                    (bbox.Min.X + bbox.Max.X) / 2,
                    (bbox.Min.Y + bbox.Max.Y) / 2,
                    (bbox.Min.Z + bbox.Max.Z) / 2);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private Grid EncontrarRejillaMasCercanaEnDireccion(List<Grid> rejillas, XYZ punto, string direccion)
    {
        if (rejillas.Count == 0) return null;

        Grid rejillaMasCercana = null;
        double distanciaMinima = double.MaxValue;

        foreach (Grid rejilla in rejillas)
        {
            try
            {
                Curve curvaRejilla = rejilla.Curve;
                IntersectionResult resultado = curvaRejilla.Project(punto);

                if (resultado != null)
                {
                    double distancia = resultado.Distance;

                    if (distancia < distanciaMinima)
                    {
                        distanciaMinima = distancia;
                        rejillaMasCercana = rejilla;
                    }
                }
            }
            catch
            {
                continue;
            }
        }

        return rejillaMasCercana;
    }

    private Tuple<Grid, double> EncontrarRejillaMasCercana(List<Grid> rejillas, XYZ punto, ElementId rejillaExcluida = null)
    {
        Grid rejillaMasCercana = null;
        double distanciaMinima = double.MaxValue;

        foreach (Grid rejilla in rejillas)
        {
            if (rejillaExcluida != null && rejilla.Id.Equals(rejillaExcluida))
                continue;

            try
            {
                Curve curvaRejilla = rejilla.Curve;
                double distancia = double.MaxValue;

                if (curvaRejilla is Line lineaRejilla)
                {
                    IntersectionResult resultado = lineaRejilla.Project(punto);
                    if (resultado != null)
                    {
                        XYZ proyeccion = resultado.XYZPoint;
                        distancia = punto.DistanceTo(proyeccion);
                    }
                }
                else
                {
                    IntersectionResult resultado = curvaRejilla.Project(punto);
                    if (resultado != null)
                        distancia = resultado.Distance;
                }

                if (distancia < distanciaMinima)
                {
                    distanciaMinima = distancia;
                    rejillaMasCercana = rejilla;
                }
            }
            catch
            {
                continue;
            }
        }

        if (rejillaMasCercana == null)
            return null;

        return new Tuple<Grid, double>(rejillaMasCercana, distanciaMinima);
    }

    #endregion

    #region Métodos Auxiliares - Asignación de Parámetros

    private bool AsignarUbicacionAElemento(Element elemento, string rejillaX, string rejillaY, string ubicacionCompleta)
    {
        bool actualizado = false;

        try
        {
            string[] parametrosRejillaX = { "OIP_REJILLA_X", "Rejilla X", "REJILLA INICIAL X", "Grid X", "Start Grid X" };
            string[] parametrosRejillaY = { "OIP_REJILLA_Y", "Rejilla Y", "REJILLA INICIAL Y", "Grid Y", "Start Grid Y" };
            string[] parametrosUbicacion = { "OIP_UBICACIÓN_POR_REJILLAS", "UBICACIÓN POR REJILLAS", "Grid Location", "Ubicación", "Location" };

            foreach (string paramName in parametrosRejillaX)
            {
                Parameter param = elemento.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                {
                    param.Set(rejillaX);
                    actualizado = true;
                    break;
                }
            }

            foreach (string paramName in parametrosRejillaY)
            {
                Parameter param = elemento.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                {
                    param.Set(rejillaY);
                    actualizado = true;
                    break;
                }
            }

            foreach (string paramName in parametrosUbicacion)
            {
                Parameter param = elemento.LookupParameter(paramName);
                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                {
                    param.Set(ubicacionCompleta);
                    actualizado = true;
                    break;
                }
            }

            return actualizado;
        }
        catch
        {
            return false;
        }
    }

    private bool AsignarParametrosDeRejilla(Document doc, Element elemento, Grid rejillaInicial, Grid rejillaFinal, string textoUbicacion)
    {
        bool actualizacionExitosa = false;

        try
        {
            Parameter paramInicial = elemento.LookupParameter("OIP_REJILLA_INICIAL");
            Parameter paramFinal = elemento.LookupParameter("OIP_REJILLA_FINAL");
            Parameter paramUbicacion = elemento.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");

            if (paramInicial == null) paramInicial = elemento.LookupParameter("Start Grid");
            if (paramFinal == null) paramFinal = elemento.LookupParameter("End Grid");
            if (paramUbicacion == null) paramUbicacion = elemento.LookupParameter("Grid Location");

            if (paramInicial != null && paramFinal != null &&
                !paramInicial.IsReadOnly && !paramFinal.IsReadOnly &&
                paramInicial.StorageType == StorageType.String && paramFinal.StorageType == StorageType.String)
            {
                paramInicial.Set(rejillaInicial.Name);
                paramFinal.Set(rejillaFinal.Name);
                actualizacionExitosa = true;
            }

            if (paramUbicacion != null && !paramUbicacion.IsReadOnly && paramUbicacion.StorageType == StorageType.String)
            {
                paramUbicacion.Set(textoUbicacion);
                actualizacionExitosa = true;
            }
        }
        catch
        {
            actualizacionExitosa = false;
        }

        return actualizacionExitosa;
    }

    private bool AsignarParametrosDeRejillaSuelo(Document doc, Element suelo, Grid rejillaInicialX, Grid rejillaFinalX,
                                                 Grid rejillaInicialY, Grid rejillaFinalY, string textoUbicacion)
    {
        bool actualizacionExitosa = false;

        try
        {
            Parameter paramInicialX = suelo.LookupParameter("OIP_REJILLA_INICIAL_X");
            Parameter paramFinalX = suelo.LookupParameter("OIP_REJILLA_FINAL_X");
            Parameter paramInicialY = suelo.LookupParameter("OIP_REJILLA_INICIAL_Y");
            Parameter paramFinalY = suelo.LookupParameter("OIP_REJILLA_FINAL_Y");
            Parameter paramUbicacion = suelo.LookupParameter("OIP_UBICACIÓN_POR_REJILLAS");

            if (paramInicialX == null) paramInicialX = suelo.LookupParameter("Start Grid X");
            if (paramFinalX == null) paramFinalX = suelo.LookupParameter("End Grid X");
            if (paramInicialY == null) paramInicialY = suelo.LookupParameter("Start Grid Y");
            if (paramFinalY == null) paramFinalY = suelo.LookupParameter("End Grid Y");
            if (paramUbicacion == null) paramUbicacion = suelo.LookupParameter("Grid Location");

            if (paramInicialX != null && !paramInicialX.IsReadOnly && rejillaInicialX != null)
            {
                paramInicialX.Set(rejillaInicialX.Name);
                actualizacionExitosa = true;
            }

            if (paramFinalX != null && !paramFinalX.IsReadOnly && rejillaFinalX != null)
            {
                paramFinalX.Set(rejillaFinalX.Name);
                actualizacionExitosa = true;
            }

            if (paramInicialY != null && !paramInicialY.IsReadOnly && rejillaInicialY != null)
            {
                paramInicialY.Set(rejillaInicialY.Name);
                actualizacionExitosa = true;
            }

            if (paramFinalY != null && !paramFinalY.IsReadOnly && rejillaFinalY != null)
            {
                paramFinalY.Set(rejillaFinalY.Name);
                actualizacionExitosa = true;
            }

            if (paramUbicacion != null && !paramUbicacion.IsReadOnly)
            {
                paramUbicacion.Set(textoUbicacion);
                actualizacionExitosa = true;
            }
        }
        catch
        {
            actualizacionExitosa = false;
        }

        return actualizacionExitosa;
    }

    #endregion
}
