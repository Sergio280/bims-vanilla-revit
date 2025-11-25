using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ParameterTransfer;

/// <summary>
/// Encuentra el elemento estructural que más área cubre de un muro/suelo y asigna su ID al parámetro "Host_ID"
/// Versión simplificada basada en intersección de BoundingBox
/// </summary>
[Transaction(TransactionMode.Manual)]
public class AsignarHostIdCommand : LicensedCommand
{
    protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            ICollection<ElementId> elementosSeleccionados = commandData.Application.ActiveUIDocument.Selection.GetElementIds();

            string nombreParametroDestino = "Host_ID";

            BuiltInCategory[] categoriasHost = new BuiltInCategory[]
            {
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Walls
            };

            BuiltInCategory[] categoriasAProcesar = new BuiltInCategory[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors
            };

            List<Element> elementosAProcesar = new List<Element>();

            if (elementosSeleccionados.Count > 0)
            {
                foreach (ElementId id in elementosSeleccionados)
                {
                    Element elemento = doc.GetElement(id);
                    if (elemento != null && elemento.Category != null)
                    {
                        foreach (BuiltInCategory cat in categoriasAProcesar)
                        {
                            if (elemento.Category.Id.Value == (long)cat)
                            {
                                elementosAProcesar.Add(elemento);
                                break;
                            }
                        }
                    }
                }

                if (elementosAProcesar.Count == 0)
                {
                    TaskDialog.Show("Advertencia", "No se encontraron muros o suelos en la selección actual.");
                    return Result.Cancelled;
                }
            }
            else
            {
                TaskDialogResult resultado = TaskDialog.Show("Confirmar acción",
                    "No hay elementos seleccionados. ¿Desea procesar todos los muros y suelos del modelo?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (resultado == TaskDialogResult.Yes)
                {
                    foreach (BuiltInCategory cat in categoriasAProcesar)
                    {
                        FilteredElementCollector collector = new FilteredElementCollector(doc)
                            .OfCategory(cat)
                            .WhereElementIsNotElementType();
                        elementosAProcesar.AddRange(collector.ToElements());
                    }
                }
                else
                {
                    return Result.Cancelled;
                }
            }

            // Obtener elementos estructurales
            List<Element> elementosEstructurales = new List<Element>();
            foreach (BuiltInCategory cat in categoriasHost)
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType();
                elementosEstructurales.AddRange(collector.ToElements());
            }

            if (elementosEstructurales.Count == 0)
            {
                TaskDialog.Show("Advertencia",
                    "No se encontraron elementos estructurales en el modelo para asignar como host.");
                return Result.Failed;
            }

            int elementosProcesados = 0;
            int elementosSinParametro = 0;
            int elementosSinHost = 0;

            using (Transaction trans = new Transaction(doc))
            {
                trans.Start("Asignar Host_ID por Intersección");

                foreach (Element elemento in elementosAProcesar)
                {
                    try
                    {
                        Parameter paramDestino = elemento.LookupParameter(nombreParametroDestino);
                        if (paramDestino == null || paramDestino.IsReadOnly)
                        {
                            elementosSinParametro++;
                            continue;
                        }

                        BoundingBoxXYZ bbox = elemento.get_BoundingBox(null);
                        if (bbox == null)
                        {
                            elementosSinHost++;
                            continue;
                        }

                        // Expandir BoundingBox para búsqueda
                        XYZ expansion = new XYZ(2, 2, 2);
                        BoundingBoxXYZ bboxExpandido = new BoundingBoxXYZ
                        {
                            Min = bbox.Min - expansion,
                            Max = bbox.Max + expansion
                        };

                        ElementId mejorHostId = ElementId.InvalidElementId;
                        double mayorVolumenInterseccion = 0.0;

                        // Buscar el elemento estructural con mayor intersección
                        foreach (Element estructural in elementosEstructurales)
                        {
                            if (estructural.Id.Equals(elemento.Id))
                                continue;

                            BoundingBoxXYZ bboxEstructural = estructural.get_BoundingBox(null);
                            if (bboxEstructural == null)
                                continue;

                            if (!BoundingBoxesIntersect(bboxExpandido, bboxEstructural))
                                continue;

                            // Calcular volumen de intersección aproximado
                            double volumenInterseccion = CalcularVolumenInterseccion(bboxExpandido, bboxEstructural);

                            if (volumenInterseccion > mayorVolumenInterseccion)
                            {
                                mayorVolumenInterseccion = volumenInterseccion;
                                mejorHostId = estructural.Id;
                            }
                        }

                        if (mejorHostId != ElementId.InvalidElementId)
                        {
                            if (paramDestino.StorageType == StorageType.String)
                            {
                                paramDestino.Set(mejorHostId.ToString());
                            }
                            else if (paramDestino.StorageType == StorageType.Integer)
                            {
                                paramDestino.Set((int)mejorHostId.Value);
                            }
                            elementosProcesados++;
                        }
                        else
                        {
                            elementosSinHost++;
                        }
                    }
                    catch
                    {
                        elementosSinHost++;
                    }
                }

                trans.Commit();
            }

            TaskDialog.Show("Proceso Completado",
                $"Asignación de Host_ID completada:\n\n" +
                $"Elementos procesados: {elementosProcesados}\n" +
                $"Elementos sin parámetro: {elementosSinParametro}\n" +
                $"Elementos sin host encontrado: {elementosSinHost}\n\n" +
                $"Total analizado: {elementosAProcesar.Count}");

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

    private double CalcularVolumenInterseccion(BoundingBoxXYZ bb1, BoundingBoxXYZ bb2)
    {
        double xMin = Math.Max(bb1.Min.X, bb2.Min.X);
        double xMax = Math.Min(bb1.Max.X, bb2.Max.X);
        double yMin = Math.Max(bb1.Min.Y, bb2.Min.Y);
        double yMax = Math.Min(bb1.Max.Y, bb2.Max.Y);
        double zMin = Math.Max(bb1.Min.Z, bb2.Min.Z);
        double zMax = Math.Min(bb1.Max.Z, bb2.Max.Z);

        if (xMax <= xMin || yMax <= yMin || zMax <= zMin)
            return 0.0;

        return (xMax - xMin) * (yMax - yMin) * (zMax - zMin);
    }
}
