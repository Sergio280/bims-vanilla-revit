using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ParameterTransfer;

/// <summary>
/// Transfiere el ID de cada elemento al parámetro especificado por el usuario
/// </summary>
[Transaction(TransactionMode.Manual)]
public class TransferirIdElementoCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            ICollection<ElementId> elementosSeleccionados = commandData.Application.ActiveUIDocument.Selection.GetElementIds();

            // Mostrar ventana de configuración
            var configWindow = new TransferirIdConfigWindow();
            bool? dialogResult = configWindow.ShowDialog();

            if (dialogResult != true)
            {
                return Result.Cancelled;
            }

            // Obtener configuración
            string nombreParametroDestino = configWindow.ParameterName;
            BuiltInCategory? categoriaSeleccionada = configWindow.SelectedCategory;
            bool soloSeleccion = configWindow.ProcessOnlySelection;

            List<Element> elementosAProcesar = new List<Element>();

            // Determinar elementos a procesar
            if (soloSeleccion && elementosSeleccionados.Count > 0)
            {
                // Procesar solo elementos seleccionados
                foreach (ElementId id in elementosSeleccionados)
                {
                    Element elemento = doc.GetElement(id);
                    if (elemento != null && !elemento.GetType().IsSubclassOf(typeof(ElementType)))
                    {
                        // Filtrar por categoría si está especificada
                        if (categoriaSeleccionada == null ||
                            (elemento.Category != null && elemento.Category.Id.Value == (long)categoriaSeleccionada.Value))
                        {
                            elementosAProcesar.Add(elemento);
                        }
                    }
                }

                if (elementosAProcesar.Count == 0)
                {
                    string mensajeCategoria = categoriaSeleccionada == null
                        ? ""
                        : $" de la categoría seleccionada";
                    TaskDialog.Show("Aviso",
                        $"No hay elementos{mensajeCategoria} en la selección actual.");
                    return Result.Cancelled;
                }
            }
            else
            {
                // Procesar todo el modelo o categoría específica
                if (categoriaSeleccionada == null)
                {
                    // Todas las categorías
                    FilteredElementCollector collector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType();
                    elementosAProcesar = collector.ToElements().ToList();
                }
                else
                {
                    // Categoría específica
                    FilteredElementCollector collector = new FilteredElementCollector(doc)
                        .OfCategory(categoriaSeleccionada.Value)
                        .WhereElementIsNotElementType();
                    elementosAProcesar = collector.ToElements().ToList();
                }

                if (elementosAProcesar.Count == 0)
                {
                    TaskDialog.Show("Aviso",
                        "No se encontraron elementos para procesar.");
                    return Result.Cancelled;
                }

                // Confirmar con el usuario
                string mensajeCategoria = categoriaSeleccionada == null
                    ? "todas las categorías"
                    : GetCategoryDisplayName(categoriaSeleccionada.Value);

                TaskDialogResult confirmResult = TaskDialog.Show(
                    "Confirmar acción",
                    $"Se procesarán {elementosAProcesar.Count} elementos de {mensajeCategoria}.\n\n" +
                    $"Parámetro destino: {nombreParametroDestino}\n\n" +
                    $"¿Desea continuar?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirmResult != TaskDialogResult.Yes)
                {
                    return Result.Cancelled;
                }
            }

            // Procesar elementos
            int contadorProcesados = 0;
            int contadorOmitidos = 0;
            int contadorSinParametro = 0;

            using (Transaction trans = new Transaction(doc))
            {
                trans.Start($"Transferir ID a {nombreParametroDestino}");

                foreach (Element elemento in elementosAProcesar)
                {
                    try
                    {
                        ElementId idElemento = elemento.Id;
                        string idElementoStr = idElemento.ToString();

                        Parameter paramDestino = elemento.LookupParameter(nombreParametroDestino);

                        if (paramDestino == null)
                        {
                            contadorSinParametro++;
                            continue;
                        }

                        if (paramDestino.StorageType == StorageType.String && !paramDestino.IsReadOnly)
                        {
                            paramDestino.Set(idElementoStr);
                            contadorProcesados++;
                        }
                        else
                        {
                            contadorOmitidos++;
                        }
                    }
                    catch
                    {
                        contadorOmitidos++;
                    }
                }

                trans.Commit();
            }

            // Mostrar resultados
            string mensajeCategoriaFinal = categoriaSeleccionada == null
                ? "todas las categorías"
                : GetCategoryDisplayName(categoriaSeleccionada.Value);

            TaskDialog resultDialog = new TaskDialog("Transferencia Completada");
            resultDialog.MainInstruction = "Proceso completado exitosamente";
            resultDialog.MainContent =
                $"Parámetro: {nombreParametroDestino}\n" +
                $"Categoría: {mensajeCategoriaFinal}\n\n" +
                $"• Elementos procesados: {contadorProcesados}\n" +
                $"• Elementos sin parámetro: {contadorSinParametro}\n" +
                $"• Elementos omitidos (read-only): {contadorOmitidos}\n" +
                $"• Total analizado: {elementosAProcesar.Count}";

            if (contadorSinParametro > 0)
            {
                resultDialog.ExpandedContent =
                    $"⚠ {contadorSinParametro} elemento(s) no tienen el parámetro '{nombreParametroDestino}'.\n\n" +
                    "Asegúrese de que el parámetro compartido esté agregado a las categorías correspondientes.";
            }

            resultDialog.Show();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show("Error", $"Error al transferir IDs:\n\n{ex.Message}");
            return Result.Failed;
        }
    }

    private string GetCategoryDisplayName(BuiltInCategory category)
    {
        var categoryNames = new Dictionary<BuiltInCategory, string>
        {
            { BuiltInCategory.OST_Walls, "Muros" },
            { BuiltInCategory.OST_Floors, "Suelos" },
            { BuiltInCategory.OST_Roofs, "Techos" },
            { BuiltInCategory.OST_Doors, "Puertas" },
            { BuiltInCategory.OST_Windows, "Ventanas" },
            { BuiltInCategory.OST_StructuralColumns, "Columnas estructurales" },
            { BuiltInCategory.OST_StructuralFraming, "Vigas estructurales" },
            { BuiltInCategory.OST_StructuralFoundation, "Cimentaciones" },
            { BuiltInCategory.OST_Stairs, "Escaleras" },
            { BuiltInCategory.OST_Railings, "Barandillas" },
            { BuiltInCategory.OST_Furniture, "Mobiliario" },
            { BuiltInCategory.OST_SpecialityEquipment, "Equipos especiales" },
            { BuiltInCategory.OST_GenericModel, "Modelo genérico" },
            { BuiltInCategory.OST_PlumbingFixtures, "Aparatos de fontanería" },
            { BuiltInCategory.OST_ElectricalEquipment, "Aparatos eléctricos" },
            { BuiltInCategory.OST_MechanicalEquipment, "Equipos mecánicos" },
            { BuiltInCategory.OST_ElectricalFixtures, "Luminarias" },
            { BuiltInCategory.OST_DuctCurves, "Conductos" },
            { BuiltInCategory.OST_PipeCurves, "Tuberías" },
            { BuiltInCategory.OST_CableTray, "Bandejas de cables" },
            { BuiltInCategory.OST_Rooms, "Habitaciones" },
            { BuiltInCategory.OST_Areas, "Áreas" },
            { BuiltInCategory.OST_Grids, "Rejillas" },
            { BuiltInCategory.OST_Levels, "Niveles" }
        };

        return categoryNames.ContainsKey(category)
            ? categoryNames[category]
            : category.ToString();
    }
}
