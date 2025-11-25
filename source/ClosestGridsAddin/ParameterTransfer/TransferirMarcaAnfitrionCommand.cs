using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ParameterTransfer;

/// <summary>
/// Lee el parámetro "Host_ID", busca ese elemento y transfiere su marca al parámetro "Host_Name"
/// </summary>
[Transaction(TransactionMode.Manual)]
public class TransferirMarcaAnfitrionCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            ICollection<ElementId> elementosSeleccionados = commandData.Application.ActiveUIDocument.Selection.GetElementIds();

            string nombreParametroHostId = "Host_ID";
            string nombreParametroDestino = "Host_Name";

            List<Element> elementosAProcesar = new List<Element>();

            if (elementosSeleccionados.Count > 0)
            {
                foreach (ElementId id in elementosSeleccionados)
                {
                    Element elemento = doc.GetElement(id);
                    if (elemento != null)
                    {
                        elementosAProcesar.Add(elemento);
                    }
                }
            }
            else
            {
                TaskDialogResult resultado = TaskDialog.Show("Confirmar acción",
                    "No hay elementos seleccionados. ¿Desea transferir marcas de elementos anfitrión para todos los elementos del modelo?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (resultado == TaskDialogResult.Yes)
                {
                    FilteredElementCollector collector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType();
                    elementosAProcesar = collector.ToElements().ToList();
                }
                else
                {
                    return Result.Cancelled;
                }
            }

            int elementosProcesados = 0;
            int elementosConError = 0;
            int elementosSinHostId = 0;
            int elementosHostNoEncontrado = 0;
            int elementosHostSinMarca = 0;

            using (Transaction trans = new Transaction(doc))
            {
                trans.Start("Transferir Marca de Elemento Anfitrión");

                foreach (Element elemento in elementosAProcesar)
                {
                    try
                    {
                        Parameter paramHostId = elemento.LookupParameter(nombreParametroHostId);
                        Parameter paramDestino = elemento.LookupParameter(nombreParametroDestino);

                        // Verificar en el tipo si no se encuentra en la instancia
                        ElementId typeId = elemento.GetTypeId();
                        Element tipoElemento = doc.GetElement(typeId);

                        if (paramHostId == null && tipoElemento != null)
                        {
                            paramHostId = tipoElemento.LookupParameter(nombreParametroHostId);
                        }

                        if (paramDestino == null && tipoElemento != null)
                        {
                            paramDestino = tipoElemento.LookupParameter(nombreParametroDestino);
                        }

                        if (paramHostId == null || paramDestino == null)
                        {
                            elementosSinHostId++;
                            continue;
                        }

                        if (paramDestino.StorageType != StorageType.String || paramDestino.IsReadOnly)
                        {
                            elementosConError++;
                            continue;
                        }

                        // Obtener el valor del parámetro Host_ID
                        string hostIdString = "";
                        switch (paramHostId.StorageType)
                        {
                            case StorageType.String:
                                hostIdString = paramHostId.AsString();
                                break;
                            case StorageType.Integer:
                                hostIdString = paramHostId.AsInteger().ToString();
                                break;
                            case StorageType.ElementId:
                                hostIdString = paramHostId.AsElementId().Value.ToString();
                                break;
                            default:
                                elementosConError++;
                                continue;
                        }

                        if (string.IsNullOrEmpty(hostIdString) || hostIdString == "0" || hostIdString == "-1")
                        {
                            elementosSinHostId++;
                            continue;
                        }

                        // Convertir el string a ElementId
                        if (!long.TryParse(hostIdString, out long hostIdLong))
                        {
                            elementosConError++;
                            continue;
                        }

                        ElementId hostElementId = new ElementId(hostIdLong);

                        // Buscar el elemento anfitrión
                        Element elementoAnfitrion = doc.GetElement(hostElementId);

                        if (elementoAnfitrion == null)
                        {
                            paramDestino.Set("Elemento no encontrado");
                            elementosHostNoEncontrado++;
                            continue;
                        }

                        // Obtener la marca del elemento anfitrión
                        string marcaAnfitrion = ObtenerMarcaDelElemento(elementoAnfitrion);

                        if (!string.IsNullOrEmpty(marcaAnfitrion))
                        {
                            paramDestino.Set(marcaAnfitrion);
                            elementosProcesados++;
                        }
                        else
                        {
                            paramDestino.Set("Sin marca");
                            elementosHostSinMarca++;
                        }
                    }
                    catch
                    {
                        elementosConError++;
                    }
                }

                trans.Commit();
            }

            TaskDialog.Show("Transferencia de Marcas Completada",
                $"Resumen de la transferencia de marcas de elementos anfitrión:\n\n" +
                $"Parámetro Host ID: {nombreParametroHostId}\n" +
                $"Parámetro destino: {nombreParametroDestino}\n\n" +
                $"Elementos procesados exitosamente: {elementosProcesados}\n" +
                $"Elementos sin parámetro Host_Id válido: {elementosSinHostId}\n" +
                $"Elementos anfitrión no encontrados: {elementosHostNoEncontrado}\n" +
                $"Elementos anfitrión sin marca: {elementosHostSinMarca}\n" +
                $"Elementos con errores: {elementosConError}\n\n" +
                $"Total de elementos analizados: {elementosAProcesar.Count}");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    private string ObtenerMarcaDelElemento(Element elemento)
    {
        try
        {
            string[] parametrosMarca = {
                "Mark", "Marca", "MARK", "Assembly Code",
                "Type Mark", "Panel", "Number", "Tag"
            };

            foreach (string nombreParam in parametrosMarca)
            {
                Parameter param = elemento.LookupParameter(nombreParam);
                if (param != null && param.StorageType == StorageType.String)
                {
                    string valor = param.AsString();
                    if (!string.IsNullOrEmpty(valor))
                    {
                        return valor;
                    }
                }
            }

            // Intentar obtener marca del tipo
            ElementId typeId = elemento.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                Element tipoElemento = elemento.Document.GetElement(typeId);
                if (tipoElemento != null)
                {
                    foreach (string nombreParam in parametrosMarca)
                    {
                        Parameter param = tipoElemento.LookupParameter(nombreParam);
                        if (param != null && param.StorageType == StorageType.String)
                        {
                            string valor = param.AsString();
                            if (!string.IsNullOrEmpty(valor))
                            {
                                return valor;
                            }
                        }
                    }
                }
            }

            return "";
        }
        catch
        {
            return "";
        }
    }
}
