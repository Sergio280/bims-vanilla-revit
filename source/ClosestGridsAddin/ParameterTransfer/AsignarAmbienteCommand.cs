using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ParameterTransfer;

/// <summary>
/// Asigna el nombre de una habitación seleccionada a todos los muros y suelos que forman sus límites
/// en el parámetro "Ambiente"
/// </summary>
[Transaction(TransactionMode.Manual)]
public class AsignarAmbienteCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            ICollection<ElementId> elementosSeleccionados = commandData.Application.ActiveUIDocument.Selection.GetElementIds();

            string nombreParametroDestino = "Ambiente";

            if (elementosSeleccionados.Count == 0)
            {
                TaskDialog.Show("Error", "No hay elementos seleccionados.\nSeleccione una habitación (Room).");
                return Result.Cancelled;
            }

            // Buscar habitación en la selección
            Room habitacionSeleccionada = null;
            foreach (ElementId id in elementosSeleccionados)
            {
                Element elemento = doc.GetElement(id);
                if (elemento is Room)
                {
                    habitacionSeleccionada = elemento as Room;
                    break;
                }
            }

            if (habitacionSeleccionada == null)
            {
                TaskDialog.Show("Error",
                    "No se encontró ninguna habitación en la selección.\n" +
                    "Seleccione una habitación (Room) del modelo.");
                return Result.Cancelled;
            }

            // Obtener el nombre de la habitación
            string nombreHabitacion = habitacionSeleccionada.Name;
            if (string.IsNullOrEmpty(nombreHabitacion))
            {
                Parameter paramName = habitacionSeleccionada.get_Parameter(BuiltInParameter.ROOM_NAME);
                if (paramName != null && paramName.StorageType == StorageType.String)
                {
                    nombreHabitacion = paramName.AsString();
                }
            }

            if (string.IsNullOrEmpty(nombreHabitacion))
            {
                nombreHabitacion = $"Habitación {habitacionSeleccionada.Id}";
            }

            // Obtener número de habitación
            string numeroHabitacion = "";
            Parameter paramNumero = habitacionSeleccionada.get_Parameter(BuiltInParameter.ROOM_NUMBER);
            if (paramNumero != null && paramNumero.StorageType == StorageType.String)
            {
                numeroHabitacion = paramNumero.AsString();
            }

            // Crear texto del ambiente
            string textoAmbiente = string.IsNullOrEmpty(numeroHabitacion)
                ? nombreHabitacion
                : $"{numeroHabitacion} - {nombreHabitacion}";

            // Obtener los límites de la habitación
            SpatialElementBoundaryOptions boundaryOptions = new SpatialElementBoundaryOptions();
            boundaryOptions.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;

            IList<IList<BoundarySegment>> boundaries = habitacionSeleccionada.GetBoundarySegments(boundaryOptions);

            if (boundaries == null || boundaries.Count == 0)
            {
                TaskDialog.Show("Error",
                    "No se pudieron obtener los límites de la habitación.\n" +
                    "Verifique que la habitación esté correctamente delimitada.");
                return Result.Failed;
            }

            // Recopilar IDs de elementos que forman los límites
            HashSet<ElementId> elementosEnLimite = new HashSet<ElementId>();

            foreach (IList<BoundarySegment> segmentLoop in boundaries)
            {
                foreach (BoundarySegment segment in segmentLoop)
                {
                    ElementId elementoId = segment.ElementId;
                    if (elementoId != ElementId.InvalidElementId)
                    {
                        elementosEnLimite.Add(elementoId);
                    }
                }
            }

            // Obtener BoundingBox de la habitación para búsqueda adicional
            BoundingBoxXYZ bboxHabitacion = habitacionSeleccionada.get_BoundingBox(null);
            if (bboxHabitacion != null)
            {
                XYZ expansion = new XYZ(2, 2, 2);
                bboxHabitacion.Min = bboxHabitacion.Min - expansion;
                bboxHabitacion.Max = bboxHabitacion.Max + expansion;
            }

            List<Element> elementosAProcesar = new List<Element>();

            // Recopilar muros y suelos en los límites
            BuiltInCategory[] categoriasAProcesar = new BuiltInCategory[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors
            };

            foreach (BuiltInCategory cat in categoriasAProcesar)
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType();

                foreach (Element elemento in collector)
                {
                    // Verificar si está en el límite directo
                    if (elementosEnLimite.Contains(elemento.Id))
                    {
                        elementosAProcesar.Add(elemento);
                        continue;
                    }

                    // Verificar BoundingBox si no está en límite directo
                    if (bboxHabitacion != null)
                    {
                        BoundingBoxXYZ bboxElemento = elemento.get_BoundingBox(null);
                        if (bboxElemento != null && BoundingBoxesIntersect(bboxHabitacion, bboxElemento))
                        {
                            elementosAProcesar.Add(elemento);
                        }
                    }
                }
            }

            int elementosProcesados = 0;
            int elementosSinParametro = 0;

            using (Transaction trans = new Transaction(doc))
            {
                trans.Start($"Asignar Ambiente: {textoAmbiente}");

                foreach (Element elemento in elementosAProcesar)
                {
                    try
                    {
                        Parameter paramDestino = elemento.LookupParameter(nombreParametroDestino);

                        if (paramDestino != null && paramDestino.StorageType == StorageType.String && !paramDestino.IsReadOnly)
                        {
                            paramDestino.Set(textoAmbiente);
                            elementosProcesados++;
                        }
                        else
                        {
                            elementosSinParametro++;
                        }
                    }
                    catch
                    {
                        elementosSinParametro++;
                    }
                }

                trans.Commit();
            }

            TaskDialog.Show("Asignación de Ambiente Completada",
                $"Habitación: {textoAmbiente}\n\n" +
                $"Elementos procesados: {elementosProcesados}\n" +
                $"Elementos sin parámetro 'Ambiente': {elementosSinParametro}\n\n" +
                $"Total de elementos en límites: {elementosAProcesar.Count}");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    private bool BoundingBoxesIntersect(BoundingBoxXYZ bb1, BoundingBoxXYZ bb2)
    {
        return !(bb1.Max.X < bb2.Min.X || bb1.Min.X > bb2.Max.X ||
                 bb1.Max.Y < bb2.Min.Y || bb1.Min.Y > bb2.Max.Y ||
                 bb1.Max.Z < bb2.Min.Z || bb1.Min.Z > bb2.Max.Z);
    }
}
