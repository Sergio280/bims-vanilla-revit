using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ClosestGridsAddin
{
    [Transaction(TransactionMode.Manual)]
    public class DIVISORDECOLUMNAS : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //Crear objetos de la clase Document y Selection
            Document Doc = commandData.Application.ActiveUIDocument.Document;
            UIDocument UIDoc = commandData.Application.ActiveUIDocument;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;
            Autodesk.Revit.ApplicationServices.Application? Application = commandData.Application.Application;
            ICollection<ElementId> elementosSeleccionados = sel.GetElementIds();

            FiltroDeColumna FILTRO = new FiltroDeColumna();
            List<Reference>? REFERENCIAS = UIDoc.Selection.PickObjects(ObjectType.Element, FILTRO, "Seleccione las columnas")?.ToList();

            #region Transacción de valores de parametros
            using (Transaction TR = new Transaction(Doc, "Dividir Columnas por Niveles")) 
            {
                TR.Start();
                foreach (Reference REFE in REFERENCIAS)
                {
                    Element? COLUMNA = Doc.GetElement(REFE);
                    FamilyInstance? columnafi = COLUMNA as FamilyInstance;
                    if (columnafi == null) continue;

                    LocationPoint? LOCPOINT = columnafi.Location as LocationPoint;
                    if (LOCPOINT == null) continue;

                    XYZ puntoBase = LOCPOINT.Point;

                    // Obtener los niveles base y tope originales del pilar
                    Parameter? baseLevelParam = columnafi.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_PARAM);
                    Parameter? topLevelParam = columnafi.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_PARAM);

                    if (baseLevelParam == null || topLevelParam == null) continue;

                    ElementId baseLevelId = baseLevelParam.AsElementId();
                    ElementId topLevelId = topLevelParam.AsElementId();

                    // Obtener los niveles ordenados por elevación
                    IList<Level> NIVELES = new FilteredElementCollector(Doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .ToList();

                    // Buscar los índices de los niveles base y tope
                    int idxBase = NIVELES.ToList().FindIndex(l => l.Id == baseLevelId);
                    int idxTope = NIVELES.ToList().FindIndex(l => l.Id == topLevelId);

                    // Validar índices
                    if (idxBase == -1 || idxTope == -1 || idxBase >= idxTope) continue;

                    // Obtener desfases originales
                    Parameter? baseOffsetParam = columnafi.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_OFFSET_PARAM);
                    Parameter? topOffsetParam = columnafi.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_OFFSET_PARAM);
                    double baseOffset = baseOffsetParam != null ? baseOffsetParam.AsDouble() : 0;
                    double topOffset = topOffsetParam != null ? topOffsetParam.AsDouble() : 0;

                    // Crear columnas solo entre los niveles donde existía el pilar original
                    for (int i = idxBase; i < idxTope; i++)
                    {
                        Level nivelBase = NIVELES[i];
                        Level nivelTope = NIVELES[i + 1];

                        // Por defecto, sin desfase
                        double newBaseOffset = 0;
                        double newTopOffset = 0;
                        XYZ basePoint = new XYZ(puntoBase.X, puntoBase.Y, nivelBase.Elevation);

                        // Si es el primer tramo y hay desfase de base, crear un pilar desde el nivel base hasta el desfase
                        if (i == idxBase && Math.Abs(baseOffset) > 1e-6)
                        {
                            // Pilar desde nivel base hasta el desfase
                            double alturaDesfase = baseOffset;
                            XYZ puntoTopeDesfase = new XYZ(puntoBase.X, puntoBase.Y, nivelBase.Elevation + alturaDesfase);

                            FamilyInstance pilarDesfase = Doc.Create.NewFamilyInstance(
                                basePoint,
                                columnafi.Symbol,
                                nivelBase,
                                StructuralType.Column);

                            pilarDesfase.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_PARAM).Set(nivelBase.Id);
                            pilarDesfase.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_PARAM).Set(nivelBase.Id);
                            pilarDesfase.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_OFFSET_PARAM).Set(0);
                            pilarDesfase.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_OFFSET_PARAM).Set(alturaDesfase);

                            // Ahora, el siguiente pilar irá desde el desfase hasta el siguiente nivel
                            basePoint = puntoTopeDesfase;
                            newBaseOffset = 0;
                        }

                        // Si es el último tramo y hay desfase de tope, crear un pilar desde el último nivel hasta el desfase superior
                        if (i + 1 == idxTope && Math.Abs(topOffset) > 1e-6)
                        {
                            // Pilar desde nivelTope hasta el desfase superior
                            double alturaDesfase = topOffset;
                            XYZ basePointTope = new XYZ(puntoBase.X, puntoBase.Y, nivelTope.Elevation);

                            FamilyInstance pilarTope = Doc.Create.NewFamilyInstance(
                                basePointTope,
                                columnafi.Symbol,
                                nivelTope,
                                StructuralType.Column);

                            pilarTope.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_PARAM).Set(nivelTope.Id);
                            pilarTope.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_PARAM).Set(nivelTope.Id);
                            pilarTope.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_OFFSET_PARAM).Set(0);
                            pilarTope.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_OFFSET_PARAM).Set(alturaDesfase);

                            // El pilar principal de este tramo irá solo hasta el nivelTope (sin desfase)
                            newTopOffset = 0;
                        }

                        // Pilar principal entre niveles (sin desfase)
                        FamilyInstance nuevaColumna = Doc.Create.NewFamilyInstance(
                            basePoint,
                            columnafi.Symbol,
                            nivelBase,
                            StructuralType.Column);

                        nuevaColumna.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_PARAM).Set(nivelBase.Id);
                        nuevaColumna.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_PARAM).Set(nivelTope.Id);
                        nuevaColumna.get_Parameter(BuiltInParameter.SCHEDULE_BASE_LEVEL_OFFSET_PARAM).Set(newBaseOffset);
                        nuevaColumna.get_Parameter(BuiltInParameter.SCHEDULE_TOP_LEVEL_OFFSET_PARAM).Set(newTopOffset);
                    }

                    // Eliminar la columna original
                    Doc.Delete(columnafi.Id);
                }

                TR.Commit();
            }
            #endregion

            return Result.Succeeded;
        }

        // Clases de filtro
        public class FiltroDeColumna : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (element != null && element.Category != null &&
                    element.Category.Id.Value == (int)BuiltInCategory.OST_StructuralColumns)
                {
                    return true;
                }
                return false;
            }

            public bool AllowReference(Reference refer, XYZ point)
            {
                return false;
            }
        }
    }
}
