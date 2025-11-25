using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA;

/// <summary>
/// Comando para transferir valores entre parámetros de elementos
/// </summary>
[Transaction(TransactionMode.Manual)]
public class TransferParametrosCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        try
        {
            // Mostrar ventana de interfaz para capturar parámetros
            var ventana = new PARAMETERTRANSFERXAML();
            bool? resultado = ventana.ShowDialog();
            
            if (resultado != true)
            {
                return Result.Cancelled;
            }

            // Obtener los nombres de los parámetros ingresados
            string parametroOrigen = ventana.parametroOrigen.Text;
            string parametroDestino = ventana.parametroDestino.Text;

            // Validar que se ingresaron ambos parámetros
            if (string.IsNullOrWhiteSpace(parametroOrigen) || string.IsNullOrWhiteSpace(parametroDestino))
            {
                TaskDialog.Show("Error", "Debe ingresar ambos nombres de parámetros.");
                return Result.Failed;
            }

            // Lista para elementos a procesar
            var elementosAProcesar = new List<Element>();
            var elementosSeleccionados = uiDoc.Selection.GetElementIds();

            // Verificar si hay elementos seleccionados
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
                // Si no hay selección, preguntar si desea procesar todo el modelo
                var tdConfirm = new TaskDialog("Confirmar Acción")
                {
                    MainInstruction = "No hay elementos seleccionados",
                    MainContent = "¿Desea transferir valores de parámetros para todos los elementos del modelo?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                };

                var resultConfirm = tdConfirm.Show();
                if (resultConfirm == TaskDialogResult.Yes)
                {
                    var collector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType();
                    elementosAProcesar = collector.ToElements().ToList();
                }
                else
                {
                    TaskDialog.Show("Operación Cancelada", "No se realizó ninguna transferencia de valores.");
                    return Result.Cancelled;
                }
            }

            int elementosProcesados = 0;
            int elementosConError = 0;
            int elementosSinParametros = 0;

            using (var trans = new Transaction(doc, "Transferir Parámetros"))
            {
                trans.Start();

                foreach (var elemento in elementosAProcesar)
                {
                    try
                    {
                        var paramOrigen = elemento.LookupParameter(parametroOrigen);
                        var paramDestino = elemento.LookupParameter(parametroDestino);

                        if (paramOrigen == null || paramDestino == null)
                        {
                            // Buscar también en el tipo
                            ElementId typeId = elemento.GetTypeId();
                            Element tipoElemento = doc.GetElement(typeId);

                            if (paramOrigen == null && tipoElemento != null)
                                paramOrigen = tipoElemento.LookupParameter(parametroOrigen);

                            if (paramDestino == null && tipoElemento != null)
                                paramDestino = tipoElemento.LookupParameter(parametroDestino);
                        }

                        if (paramOrigen != null && paramDestino != null)
                        {
                            if (paramDestino.IsReadOnly)
                            {
                                elementosConError++;
                                continue;
                            }

                            bool ok = false;

                            switch (paramOrigen.StorageType)
                            {
                                case StorageType.String:
                                    if (paramDestino.StorageType == StorageType.String)
                                        ok = paramDestino.Set(paramOrigen.AsString() ?? string.Empty);
                                    break;

                                case StorageType.Integer:
                                    if (paramDestino.StorageType == StorageType.Integer)
                                        ok = paramDestino.Set(paramOrigen.AsInteger());
                                    else if (paramDestino.StorageType == StorageType.String)
                                        ok = paramDestino.Set(paramOrigen.AsInteger().ToString());
                                    break;

                                case StorageType.Double:
                                    if (paramDestino.StorageType == StorageType.Double)
                                    {
                                        // Para dobles, intenta mantener el mismo DataType/unidades
                                        var srcDt = paramOrigen.Definition.GetDataType();
                                        var dstDt = paramDestino.Definition.GetDataType();
                                        if (srcDt == null || dstDt == null || srcDt == dstDt)
                                            ok = paramDestino.Set(paramOrigen.AsDouble());
                                    }
                                    else if (paramDestino.StorageType == StorageType.String)
                                    {
                                        // Texto legible con unidades formateadas
                                        ok = paramDestino.Set(paramOrigen.AsValueString() ?? string.Empty);
                                    }
                                    break;

                                case StorageType.ElementId:
                                {
                                    var srcId = paramOrigen.AsElementId();
                                    if (paramDestino.StorageType == StorageType.ElementId)
                                    {
                                        ok = paramDestino.Set(srcId);
                                    }
                                    else if (paramDestino.StorageType == StorageType.String)
                                    {
                                        // Resolver el Id y escribir el nombre del elemento (p. ej., Level.Name)
                                        var refElem = doc.GetElement(srcId);
                                        var name = refElem?.Name ?? string.Empty;
                                        ok = paramDestino.Set(name);
                                    }
                                    break;
                                }
                            }

                            if (ok)
                            {
                                elementosProcesados++;
                            }
                            else
                            {
                                elementosConError++;
                            }
                        }
                        else
                        {
                            elementosSinParametros++;
                        }
                    }
                    catch
                    {
                        elementosConError++;
                    }
                }

                trans.Commit();
            }

            // Mostrar informe final
            var tdResumen = new TaskDialog("Transferencia Completada")
            {
                MainInstruction = "✅ Transferencia Completada",
                MainContent = $"Resumen de la transferencia:\n\n" +
                             $"📋 Parámetro origen: {parametroOrigen}\n" +
                             $"📋 Parámetro destino: {parametroDestino}\n\n" +
                             $"✅ Elementos procesados: {elementosProcesados}\n" +
                             $"⚠️ Elementos sin parámetros: {elementosSinParametros}\n" +
                             $"❌ Elementos con errores: {elementosConError}\n\n" +
                             $"📊 Total analizado: {elementosAProcesar.Count}",
                CommonButtons = TaskDialogCommonButtons.Ok
            };
            tdResumen.Show();

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = $"Error: {ex.Message}";
            TaskDialog.Show("Error", message);
            return Result.Failed;
        }
    }
}
