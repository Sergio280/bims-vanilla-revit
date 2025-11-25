using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.SANITARIAS;

/// <summary>
/// Asigna la cota Z máxima del BoundingBox al parámetro "OIP_COTA_TAPA" de aparatos sanitarios seleccionados
/// </summary>
[Transaction(TransactionMode.Manual)]
public class AsignarCotaTapaCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIDocument uidoc = commandData.Application.ActiveUIDocument;
        Document doc = uidoc.Document;

        try
        {
            // Obtener aparatos sanitarios seleccionados
            var seleccion = uidoc.Selection.GetElementIds();
            var aparatosSanitarios = seleccion
                .Select(id => doc.GetElement(id))
                .Where(e => e.Category != null && e.Category.Id.Value == (long)BuiltInCategory.OST_PlumbingFixtures)
                .ToList();

            if (!aparatosSanitarios.Any())
            {
                TaskDialog.Show("Aviso", "No hay aparatos sanitarios seleccionados. Por favor, seleccione uno o más aparatos sanitarios.");
                return Result.Cancelled;
            }

            using (Transaction trans = new Transaction(doc, "Asignar Cota Tapa"))
            {
                trans.Start();

                int contadorProcesados = 0;
                int contadorSinParametro = 0;
                List<string> elementosSinParametro = new List<string>();

                foreach (var aparato in aparatosSanitarios)
                {
                    try
                    {
                        // Obtener BoundingBox del aparato
                        BoundingBoxXYZ bb = aparato.get_BoundingBox(null);

                        if (bb == null)
                        {
                            continue; // Saltar si no tiene BoundingBox
                        }

                        // Obtener coordenada Z máxima
                        double cotaZPies = bb.Max.Z;

                        // Convertir de pies a metros usando UnitUtils
                        double cotaZMetros = UnitUtils.Convert(
                            cotaZPies,
                            UnitTypeId.Feet,
                            UnitTypeId.Meters);

                        // Buscar parámetro "OIP_COTA_TAPA"
                        Parameter paramCotaTapa = aparato.LookupParameter("OIP_COTA_TAPA");

                        if (paramCotaTapa != null && !paramCotaTapa.IsReadOnly)
                        {
                            // Asignar valor como double
                            paramCotaTapa.Set(cotaZMetros);
                            contadorProcesados++;
                        }
                        else if (paramCotaTapa == null)
                        {
                            // Registrar elementos sin el parámetro
                            contadorSinParametro++;
                            string nombreElemento = aparato.Name ?? "Sin nombre";
                            elementosSinParametro.Add($"ID {aparato.Id}: {nombreElemento}");
                        }
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Error", $"Error al procesar aparato {aparato.Id}: {ex.Message}");
                    }
                }

                trans.Commit();

                // Mostrar resultados
                string mensaje = $"Proceso completado:\n\n";
                mensaje += $"• Aparatos procesados correctamente: {contadorProcesados}\n";
                mensaje += $"• Total de aparatos seleccionados: {aparatosSanitarios.Count}\n";

                if (contadorSinParametro > 0)
                {
                    mensaje += $"\n⚠ {contadorSinParametro} aparato(s) sin el parámetro 'OIP_COTA_TAPA':\n";
                    mensaje += string.Join("\n", elementosSinParametro.Take(10)); // Mostrar máximo 10

                    if (elementosSinParametro.Count > 10)
                    {
                        mensaje += $"\n... y {elementosSinParametro.Count - 10} más.";
                    }
                }

                TaskDialog.Show(contadorSinParametro > 0 ? "Completado con advertencias" : "Completado", mensaje);
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
