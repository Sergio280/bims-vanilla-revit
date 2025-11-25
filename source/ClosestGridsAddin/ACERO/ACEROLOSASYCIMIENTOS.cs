using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ClosestGridsAddinVANILLA.ACERO
{
    [Transaction(TransactionMode.Manual)]
    public class ACEROLOSASYCIMIENTOS : LicensedCommand
    {
        public static double ESPACIAMIENTO = 0.164042; // Recubrimiento de 5cm al eje de la barra de acero (5 cm)

        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //Crear objetos de la clase Document y Selection
            Document Doc = commandData.Application.ActiveUIDocument.Document;
            UIDocument UIDoc = commandData.Application.ActiveUIDocument;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;
            Autodesk.Revit.ApplicationServices.Application Application = commandData.Application.Application;

            //0.2) Recibir los valores de entrada
            List<string> REBARTYPES = new FilteredElementCollector(Doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().Select(x => x.Name).ToList();
            List<string> REBARHOOKTYPES = new FilteredElementCollector(Doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().Select(x => x.Name).ToList();
            List<string> RECUBRIMIENTO = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarCoverType))
                .Cast<RebarCoverType>()
                .Select(x => x.Name)
                .ToList();

            bool inferiorActivado = false;
            bool superiorActivado = false;


            //0.1) Invocar la interfaz
            ACEROLOSASYCIMIENTOSXAML cimUI = new ACEROLOSASYCIMIENTOSXAML(REBARTYPES, REBARHOOKTYPES, inferiorActivado, superiorActivado, RECUBRIMIENTO);
            cimUI.ShowDialog();

            if (cimUI.DialogResult == false)
            {
                return Result.Cancelled;

            }
            ESPACIAMIENTO = double.Parse(cimUI.espaciamiento.Text);

            string rebarTypeSelected = cimUI.diametroBarra.SelectedItem.ToString();
            string rebarHookTypeSelected = cimUI.tipodegancho.SelectedItem.ToString();
            string recubrimientoSelected = cimUI.recubrimiento.SelectedItem.ToString();
            inferiorActivado = cimUI.activarganchoinferior.IsChecked ?? false; // Asignar valor por defecto si no está marcado
            superiorActivado = cimUI.activarganchosuperior.IsChecked ?? false; // Asignar valor por defecto si no está marcado
            //1.1) Filtro de Losas y Cimientos
            FiltroDeLosaYCimentacion FILTRO = new FiltroDeLosaYCimentacion();

            //1.2) Seleccionar las Losas y Cimientos
            List<Reference> REFERENCIAS = UIDoc.Selection.PickObjects(ObjectType.Element, FILTRO, "Seleccione losas y cimientos").ToList();

            //02) Colección de tipos de varillas
            RebarBarType REBARTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .FirstOrDefault(x => x.Name == rebarTypeSelected);

            RebarHookType HOOKTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>()
                .FirstOrDefault(x=> x.Name == rebarHookTypeSelected);

            AreaReinforcementType AREAREINFTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(AreaReinforcementType))
                .Cast<AreaReinforcementType>()
                .FirstOrDefault();

            Element REBARCOVERTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarCoverType))
                .FirstOrDefault(x => x.Name == recubrimientoSelected); 


            #region Transacción para refuerzos
            using (Transaction TR = new Transaction(Doc, "Rebars en muros"))
            {
                TR.Start();

                foreach (Reference refe in REFERENCIAS)
                {
                    Element EL = Doc.GetElement(refe);

                    //03 Generar áreas de refuerzo
                    Parameter TOPFACE = EL.get_Parameter(BuiltInParameter.CLEAR_COVER_TOP);
                    TOPFACE.Set(REBARCOVERTYPE.Id);

                    Parameter BOTTOMFACE = EL.get_Parameter(BuiltInParameter.CLEAR_COVER_BOTTOM);
                    BOTTOMFACE.Set(REBARCOVERTYPE.Id);

                    Parameter OTHERFACE = EL.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER);
                    if (OTHERFACE != null && !OTHERFACE.IsReadOnly)
                        OTHERFACE.Set(REBARCOVERTYPE.Id);

                    AreaReinforcement AREAREINF = AreaReinforcement.Create(
                        Doc,
                        EL,
                        XYZ.BasisX,
                        AREAREINFTYPE.Id,
                        REBARTYPE.Id,
                        ElementId.InvalidElementId);

                    // Establecer espaciamiento bottom dir 1 (X)
                    Parameter bottomdir1 = AREAREINF.get_Parameter(BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_1);
                    if (bottomdir1 != null)
                    {
                        bottomdir1.Set(metrosaPies(ESPACIAMIENTO));
                    }

                    // Establecer espaciamiento top dir 1 (Y)
                    Parameter topdir1 = AREAREINF.get_Parameter(BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_1);
                    if (topdir1 != null)
                    {
                        topdir1.Set(metrosaPies(ESPACIAMIENTO));
                    }

                    // Establecer espaciamiento top dir 2 (Y)
                    Parameter topdir2 = AREAREINF.get_Parameter(BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_2);
                    if (topdir2 != null)
                    {
                        topdir2.Set(metrosaPies(ESPACIAMIENTO));
                    }

                    // Establecer espaciamiento bottom dir 2 (Y)
                    Parameter bottomdir2 = AREAREINF.get_Parameter(BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_2);
                    if (bottomdir2 != null)
                    {
                        bottomdir2.Set(metrosaPies(ESPACIAMIENTO));
                    }

                    // Hacer visible en la vista actual
                    AREAREINF.SetUnobscuredInView(Doc.ActiveView, true);

                    //04) Agregar ganchos 90° en la malla
                    if (EL.Category.Name == "Cimentación estructural")
                    {

                        //Activar gancho superior
                           if (inferiorActivado == true)
                            {
                            Parameter MAJORBOTTOM = AREAREINF.get_Parameter(BuiltInParameter.REBAR_SYSTEM_HOOK_TYPE_BOTTOM_DIR_1);
                            MAJORBOTTOM.Set(HOOKTYPE.Id);

                            Parameter MINORBOTTOM = AREAREINF.get_Parameter(BuiltInParameter.REBAR_SYSTEM_HOOK_TYPE_BOTTOM_DIR_2);
                            MINORBOTTOM.Set(HOOKTYPE.Id);
                        }
                        else
                        {
                            inferiorActivado = false;
                        }
                                                      
                            //Activar gancho inferior
                            if(superiorActivado == true)
                            {
                                Parameter MAJORTOP = AREAREINF.get_Parameter(BuiltInParameter.REBAR_SYSTEM_HOOK_TYPE_TOP_DIR_1);
                                MAJORTOP.Set(HOOKTYPE.Id);

                                Parameter MINORTOP = AREAREINF.get_Parameter(BuiltInParameter.REBAR_SYSTEM_HOOK_TYPE_TOP_DIR_2);
                                MINORTOP.Set(HOOKTYPE.Id);
                            }
                            else
                            {
                                superiorActivado = false;
                        }
                        
                    }

                    // Regenerar el documento para actualizar la geometría
                    Doc.Regenerate();

                    // 3. Obtener las barras individuales
                    IList<ElementId> barras = AREAREINF.GetRebarInSystemIds();

                    // Obtener coordenada z de un paquete de barras de zapatas
                    XYZ coordenadaZ = null;
                    if (EL.Category.Name == "Cimentación estructural")
                    {
                        // Obtener la coordenada Z de la primera barra
                        ElementId firstBarId = barras.FirstOrDefault();
                        if (firstBarId != ElementId.InvalidElementId)
                        {
                            Element firstBar = Doc.GetElement(firstBarId);
                            Solid solid = getSolidFromElement(firstBar);
                            if (solid != null)
                            {
                                coordenadaZ = solid.ComputeCentroid();
                            }
                        }
                    }

                    // Remover el sistema de refuerzo de área
                    AreaReinforcement.RemoveAreaReinforcementSystem(Doc, AREAREINF);
                }

                TR.Commit();
            }

            #endregion
                        
                       
            
            
            return Result.Succeeded;
        }




        // Método para obtener el sólido de un elemento - actualizado con validaciones
        public Solid getSolidFromElement(Element famIn)
        {
            if (famIn == null) return null;

            // Eliminada la variable 'solid' que no se utilizaba
            GeometryElement geo = famIn.get_Geometry(new Options());

            if (geo == null) return null;

            // Buscar primero sólidos directos
            foreach (GeometryObject geomObj in geo)
            {
                Solid solidGeo = geomObj as Solid;
                if (solidGeo != null && solidGeo.Volume > 0.001)
                {
                    return solidGeo;
                }
            }

            // Si no se encontraron sólidos directos, buscar en instancias de geometría
            foreach (GeometryObject geomObj in geo)
            {
                GeometryInstance instance = geomObj as GeometryInstance;
                if (instance != null)
                {
                    GeometryElement geoElement = instance.GetInstanceGeometry();
                    if (geoElement != null)
                    {
                        foreach (GeometryObject geomEl in geoElement)
                        {
                            Solid solidGeo = geomEl as Solid;
                            if (solidGeo != null && solidGeo.Volume > 0.01)
                            {
                                return solidGeo;
                            }
                        }
                    }
                }
            }

            return null;
        }

        public double metrosaPies(double metros)
        {
            // Conversión de metros a pies
            return metros * 3.28084;

        }

        // Clases de filtro
        public class FiltroDeLosaYCimentacion : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (element != null && element.Category != null &&
                    (element.Category.Id.Value == (int)BuiltInCategory.OST_Floors ||
                     element.Category.Id.Value == (int)BuiltInCategory.OST_StructuralFoundation))
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
