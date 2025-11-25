using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ClosestGridsAddinVANILLA.Commands;

namespace ClosestGridsAddinVANILLA.ACERO
{
    [Transaction(TransactionMode.Manual)]
    public class ACEROMUROS : LicensedCommand
    {
        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //Crear objetos de la clase Document y Selection
            Document Doc = commandData.Application.ActiveUIDocument.Document;
            UIDocument UIDoc = commandData.Application.ActiveUIDocument;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;
            Autodesk.Revit.ApplicationServices.Application Application = commandData.Application.Application;


            
            // Filtro de selección
            WallSelectionFilter FILTRO = new WallSelectionFilter();

            // Seleccionar muros
            IList<Reference> REFERENCIAS = UIDoc.Selection.PickObjects(ObjectType.Element, FILTRO, "Seleccione los muros");

            //00) Colección de información a pasar a la interfaz
            List<string> REBARTYPES = new FilteredElementCollector(Doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().Select(x => x.Name).ToList();

            ACEROMUROSUI ACERODEMUROSUI = new ACEROMUROSUI(REBARTYPES);
            ACERODEMUROSUI.ShowDialog();

            if (ACERODEMUROSUI.DialogResult == false)
            {
                return Result.Cancelled;
            }

            // ? CAPTURAR EL TIPO SELECCIONADO POR EL USUARIO
            string rebarTypeSelected = ACERODEMUROSUI.TIPODEBARRAS.SelectedItem.ToString(); // Ajusta el nombre de la propiedad según tu interfaz

            //double RCOVER = 0.082;//25mm
            double RCOVER = double.Parse(ACERODEMUROSUI.RECUBRIMIENTO.Text);

            double verticalLegLength = double.Parse(ACERODEMUROSUI.BARRASVERTICALES.Text); //feet
            double horizontalLegLength = double.Parse(ACERODEMUROSUI.BARRASVERTICALES.Text);

            //double DIAGLEG = 1.96;//feet
            double DIAGLEG = double.Parse(ACERODEMUROSUI.BARRASDIAGONALES.Text);

            //double ULENGTH = 1.64;
            double ULENGTH = double.Parse(ACERODEMUROSUI.BARRASENU.Text);






            if (ACERODEMUROSUI.DialogResult == false)
            {
                return Result.Cancelled;
            }


            if (REFERENCIAS == null || REFERENCIAS.Count == 0)
            {
                TaskDialog.Show("Error", "No se seleccionaron muros. Por favor, seleccione al menos un muro.");
                return Result.Failed;
            }


            // Crear una lista para almacenar los muros seleccionados
            List<Wall> murosSeleccionados = new List<Wall>();
            foreach (Reference referencia in REFERENCIAS)
            {
                Element elemento = Doc.GetElement(referencia);
                if (elemento is Wall muro)
                {
                    murosSeleccionados.Add(muro);
                }
            }
            if (murosSeleccionados.Count == 0)
            {
                TaskDialog.Show("Error", "No se encontraron muros válidos en la selección.");
                return Result.Failed;
            }

            // Colección de barras de acero
            RebarBarType REBARTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .FirstOrDefault(x => x.Name == rebarTypeSelected);

            RebarHookType HOOKTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>()
                .FirstOrDefault();

            AreaReinforcementType AREAREINFTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(AreaReinforcementType))
                .Cast<AreaReinforcementType>()
                .FirstOrDefault();

            Element REBARCOVERTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarCoverType))
                .FirstOrDefault();


            #region COMPROBRACIONES
            // Verificar que se encontraron todos los tipos
            if (REBARTYPE == null)
            {
                TaskDialog.Show("Error", "No se encontró el tipo de barra de refuerzo 'Ø 1/2\"'");
                return Result.Failed;
            }

            if (HOOKTYPE == null)
            {
                TaskDialog.Show("Error", "No se encontró el tipo de gancho");
                return Result.Failed;
            }

            if (AREAREINFTYPE == null)
            {
                TaskDialog.Show("Error", "No se encontró el tipo de refuerzo de área");
                return Result.Failed;
            }

            if (REBARCOVERTYPE == null)
            {
                TaskDialog.Show("Error", "No se encontró el tipo de recubrimiento");
                return Result.Failed;
            }

            #endregion


            #region Transacción para refuerzos
            using (Transaction TR = new Transaction(Doc, "Rebars en muros"))
            {
                TR.Start();

                foreach (Reference refe in REFERENCIAS)
                {
                    Element EL = Doc.GetElement(refe);
                    if (EL == null) continue;

                    // Actualizar parámetros de recubrimiento
                    Parameter EXTFACE = EL.get_Parameter(BuiltInParameter.CLEAR_COVER_EXTERIOR);
                    if (EXTFACE != null) EXTFACE.Set(REBARCOVERTYPE.Id);

                    Parameter INTFACE = EL.get_Parameter(BuiltInParameter.CLEAR_COVER_INTERIOR);
                    if (INTFACE != null) INTFACE.Set(REBARCOVERTYPE.Id);

                    Parameter OTHERFACE = EL.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER);
                    if (OTHERFACE != null && !OTHERFACE.IsReadOnly) OTHERFACE.Set(REBARCOVERTYPE.Id);

                    // Crear refuerzo de área
                    AreaReinforcement AREAREINF = AreaReinforcement.Create(Doc, EL, XYZ.BasisZ,
                        AREAREINFTYPE.Id, REBARTYPE.Id, ElementId.InvalidElementId);

                    if (AREAREINF != null)
                    {
                        AREAREINF.SetUnobscuredInView(Doc.ActiveView, true);
                        Doc.Regenerate();
                        AreaReinforcement.RemoveAreaReinforcementSystem(Doc, AREAREINF);
                    }

                    // Obtener aberturas
                    Wall wall = EL as Wall;
                    if (wall != null)
                    {
                        List<ElementId> OPENIDS = wall.FindInserts(true, false, false, true)?.ToList();

                        if (OPENIDS != null && OPENIDS.Any())
                        {
                            foreach (ElementId id in OPENIDS)
                            {
                                Solid WALLSOLID = getSolidFromElement(EL);
                                if (WALLSOLID == null) continue;

                                Opening OPENING = Doc.GetElement(id) as Opening;
                                if (OPENING == null || !OPENING.IsRectBoundary) continue;

                                // Obtener las esquinas
                                List<XYZ> PUNTOS = OPENING.BoundaryRect?.ToList();
                                if (PUNTOS == null || PUNTOS.Count < 2) continue;

                                // Obtener la cara principal del muro
                                List<Tuple<double, Face>> FACESWALL = new List<Tuple<double, Face>>();
                                foreach (Face FC in WALLSOLID.Faces)
                                {
                                    if (FC is PlanarFace planarFace &&
                                        (!planarFace.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ) ||
                                         !planarFace.FaceNormal.IsAlmostEqualTo(-XYZ.BasisZ)))
                                    {
                                        FACESWALL.Add(new Tuple<double, Face>(FC.Area, FC));
                                    }
                                }

                                if (FACESWALL.Count == 0) continue;

                                FACESWALL = FACESWALL.OrderByDescending(x => x.Item1).ToList();
                                Face CARAPRINCIPAL = FACESWALL[0].Item2;
                                XYZ FNORMAL = (CARAPRINCIPAL as PlanarFace)?.FaceNormal ?? XYZ.BasisX;

                                // Proyectar puntos a la cara
                                XYZ PT1 = CARAPRINCIPAL.Project(PUNTOS[0])?.XYZPoint;
                                XYZ PT2 = CARAPRINCIPAL.Project(PUNTOS[1])?.XYZPoint;

                                if (PT1 == null || PT2 == null) continue;

                                // Obtener las esquinas de las aberturas
                                XYZ v1 = PT1; // inferior izquierda
                                XYZ v2 = new XYZ(PT2.X, PT2.Y, PT1.Z); // inferior derecha
                                XYZ v3 = PT2; // superior derecha
                                XYZ v4 = new XYZ(PT1.X, PT1.Y, PT2.Z); // superior izquierda

                               
                                XYZ WALLDIR = (v2 - v1).Normalize();
                                v1 = v1 - WALLDIR * metrosaPies(RCOVER) - XYZ.BasisZ * metrosaPies(RCOVER);
                                v2 = v2 + WALLDIR * metrosaPies(RCOVER) - XYZ.BasisZ * metrosaPies(RCOVER);
                                v3 = v3 + WALLDIR * metrosaPies(RCOVER) + XYZ.BasisZ * metrosaPies(RCOVER);
                                v4 = v4 - WALLDIR * metrosaPies(RCOVER) + XYZ.BasisZ * metrosaPies(RCOVER);

                                // Esquinas hacia el interior del muro
                                v1 = v1 - FNORMAL * metrosaPies(RCOVER);
                                v2 = v2 - FNORMAL * metrosaPies(RCOVER);
                                v3 = v3 - FNORMAL * metrosaPies(RCOVER);
                                v4 = v4 - FNORMAL * metrosaPies(RCOVER);

                                // Barras verticales y horizontales en aberturas
                                Line LN1 = Line.CreateBound(v1, v2);
                                Line LN2 = Line.CreateBound(v2, v3);
                                Line LN3 = Line.CreateBound(v3, v4);
                                Line LN4 = Line.CreateBound(v4, v1);
                                CurveLoop CVLOOP = CurveLoop.Create(new List<Curve>{
                                        LN1 as Curve, LN2 as Curve, LN3 as Curve, LN4 as Curve,
                                    });

                                foreach (Curve CV in CVLOOP)
                                {
                                    // Verticales
                                    // Corregido: Ahora accedemos directamente a la coordenada Z de la dirección
                                    if ((CV as Line)?.Direction.IsAlmostEqualTo(XYZ.BasisZ, 0.01) == true ||
                                        (CV as Line)?.Direction.IsAlmostEqualTo(-XYZ.BasisZ, 0.01) == true)
                                    {
                                        // Ordenar los puntos
                                        XYZ EP1 = CV.GetEndPoint(0);
                                        XYZ EP2 = CV.GetEndPoint(1);

                                        if (EP1.Z > EP2.Z)
                                        {
                                            EP1 = CV.GetEndPoint(1);
                                            EP2 = CV.GetEndPoint(0);
                                        }
                                        
                                        EP1 = new XYZ(EP1.X, EP1.Y, EP1.Z - metrosaPies(verticalLegLength));
                                        EP2 = new XYZ(EP2.X, EP2.Y, EP2.Z + metrosaPies(verticalLegLength));

                                        // Crear barras
                                        double WALLWIDTH = 0;
                                        if (EL is Wall wall1)
                                        {
                                            WALLWIDTH = wall1.Width;
                                        }
                                        Line LNINT = Line.CreateBound(EP1, EP2);

                                        Rebar REBARVERTICAL = Rebar.CreateFromCurves(Doc, RebarStyle.Standard, REBARTYPE, HOOKTYPE, HOOKTYPE, EL,
                                                                                 -FNORMAL,
                                                                                 new List<Curve> { LNINT as Curve },
                                                                      RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);

                                        if (REBARVERTICAL != null)
                                        {
                                            Parameter HOOKEND = REBARVERTICAL.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                                            Parameter HOOKSTART = REBARVERTICAL.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);

                                            if (HOOKEND != null && HOOKSTART != null)
                                            {
                                                HOOKEND.Set(ElementId.InvalidElementId);
                                                HOOKSTART.Set(ElementId.InvalidElementId);
                                            }

                                            REBARVERTICAL.SetUnobscuredInView(Doc.ActiveView, true);
                                            REBARVERTICAL.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(0.328034, WALLWIDTH - 2 * RCOVER, true, true, true);
                                        }
                                    }
                                    // Horizontales
                                    // Corregido: Comparamos directamente la coordenada Z de la dirección con 0
                                    else
                                    {
                                        Line lineCV = CV as Line;
                                        if (lineCV != null && Math.Abs(lineCV.Direction.Z) < 0.01)
                                        {
                                            Wall WALL = EL as Wall;
                                            if (WALL == null) continue;

                                            LocationCurve LCCV = WALL.Location as LocationCurve;
                                            double BASEWALLELEV = 0;

                                            if (LCCV != null)
                                            {
                                                BASEWALLELEV = LCCV.Curve.GetEndPoint(0).Z;
                                            }

                                            if (CV.GetEndPoint(0).Z <= BASEWALLELEV)
                                            {
                                                continue;
                                            }

                                            // Endpoints
                                            XYZ ep1 = CV.GetEndPoint(0);
                                            XYZ ep2 = CV.GetEndPoint(1);

                                            // Agregar longitud extra
                                            
                                            Line LNint = Line.CreateBound(ep1, ep2);
                                            XYZ LNdir = LNint.Direction;

                                            ep1 = ep1 - LNdir * metrosaPies(horizontalLegLength);
                                            ep2 = ep2 + LNdir * metrosaPies(horizontalLegLength);
                                            LNint = Line.CreateBound(ep1, ep2);

                                            // Crear barra
                                            double WALLWIDTH = 0;
                                            if (EL is Wall wall2)
                                            {
                                                WALLWIDTH = wall2.Width;
                                            }

                                            Rebar REBARVERTICAL = Rebar.CreateFromCurves(Doc, RebarStyle.Standard, REBARTYPE, HOOKTYPE, HOOKTYPE, EL,
                                                                                      -FNORMAL,
                                                                                      new List<Curve> { LNint as Curve },
                                                                           RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);

                                            if (REBARVERTICAL != null)
                                            {
                                                Parameter HOOKEND = REBARVERTICAL.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                                                Parameter HOOKSTART = REBARVERTICAL.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);

                                                if (HOOKEND != null && HOOKSTART != null)
                                                {
                                                    HOOKEND.Set(ElementId.InvalidElementId);
                                                    HOOKSTART.Set(ElementId.InvalidElementId);
                                                }

                                                REBARVERTICAL.SetUnobscuredInView(Doc.ActiveView, true);
                                                REBARVERTICAL.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(0.328034, WALLWIDTH - 2 * metrosaPies(RCOVER), true, true, true);
                                            }
                                        }
                                    }
                                }

                                // Barras diagonales
                                

                                List<Line> DIAGBARS = new List<Line>();

                                // Encontrar puntos de diagonal en la dirección hacia arriba
                                XYZ V2P1 = v2 + new XYZ(WALLDIR.X, WALLDIR.Y, 1);
                                XYZ DIAGDIRUP = (V2P1 - v2).Normalize();

                                // Barra en la zona inferior derecha
                                Wall WALL2 = EL as Wall;
                                if (WALL2 == null) continue;

                                LocationCurve LCCV2 = WALL2.Location as LocationCurve;
                                double BASEWALLELEV2 = 0;

                                if (LCCV2 != null)
                                {
                                    BASEWALLELEV2 = LCCV2.Curve.GetEndPoint(0).Z;
                                }

                                if (v2.Z > BASEWALLELEV2)
                                {
                                    XYZ DIAG1BR = v2 + DIAGDIRUP * metrosaPies(DIAGLEG);
                                    XYZ DIAG2BR = v2 - DIAGDIRUP * metrosaPies(DIAGLEG);
                                    Line DIAGBR = Line.CreateBound(DIAG2BR, DIAG1BR);

                                    DIAGBARS.Add(DIAGBR);
                                }

                                // Barra diagonal superior izquierda
                                XYZ DIAG1TL = v4 + DIAGDIRUP * metrosaPies(DIAGLEG);
                                XYZ DIAG2TL = v4 - DIAGDIRUP * metrosaPies(DIAGLEG);
                                Line DIAGTL = Line.CreateBound(DIAG2TL, DIAG1TL);
                                DIAGBARS.Add(DIAGTL);

                                // Dirección diagonal hacia abajo                              
                                XYZ V1P1 = v1 + new XYZ(WALLDIR.X, WALLDIR.Y, -1);
                                XYZ DIAGDIRDOWN = (V1P1 - v1).Normalize();

                                // Barra inferior izquierda                                   
                                if (v1.Z > BASEWALLELEV2)
                                {
                                    XYZ DIAG1BL = v1 + DIAGDIRDOWN * metrosaPies(DIAGLEG);
                                    XYZ DIAG2BL = v1 - DIAGDIRDOWN * metrosaPies(DIAGLEG);
                                    Line DIAGBL = Line.CreateBound(DIAG2BL, DIAG1BL);

                                    DIAGBARS.Add(DIAGBL);
                                }

                                // Barra superior derecha
                                XYZ DIAG1TR = v3 + DIAGDIRDOWN * metrosaPies(DIAGLEG);
                                XYZ DIAG2TR = v3 - DIAGDIRDOWN * metrosaPies(DIAGLEG);
                                Line DIAGTR = Line.CreateBound(DIAG2TR, DIAG1TR);

                                DIAGBARS.Add(DIAGTR);

                                // CREAR BARRAS DIAGONALES
                                double WALLWIDTH2 = 0;
                                if (EL is Wall wall3)
                                {
                                    WALLWIDTH2 = wall3.Width;
                                }

                                foreach (Line LNDG in DIAGBARS)
                                {
                                    Rebar REBARDIAG = Rebar.CreateFromCurves(Doc, RebarStyle.Standard, REBARTYPE, HOOKTYPE, HOOKTYPE, EL,
                                                     -FNORMAL,
                                                     new List<Curve> { LNDG as Curve },
                                                          RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);

                                    if (REBARDIAG != null)
                                    {
                                        Parameter HOOKENDD = REBARDIAG.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                                        Parameter HOOKSTARTD = REBARDIAG.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);

                                        if (HOOKENDD != null && HOOKSTARTD != null)
                                        {
                                            HOOKENDD.Set(ElementId.InvalidElementId);
                                            HOOKSTARTD.Set(ElementId.InvalidElementId);
                                        }

                                        REBARDIAG.SetUnobscuredInView(Doc.ActiveView, true);
                                        REBARDIAG.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(0.328034, WALLWIDTH2 - 2 * metrosaPies(RCOVER), true, true, true);
                                    }
                                }

                                // Barras en U
                                
                                double OPENINGHEIGHT = v4.Z - v1.Z;
                                double OPENINGWIDTH = v2.DistanceTo(v1);

                                double WALLWIDTH3 = 0;
                                if (EL is Wall wall4)
                                {
                                    WALLWIDTH3 = wall4.Width;
                                }

                                List<List<Curve>> USIDECURVES = new List<List<Curve>>();
                                List<List<Curve>> UVERTICALSCURVES = new List<List<Curve>>();

                                // Puntos de BARRA LATERAL IZQUIERDA
                                XYZ U1 = v1 - WALLDIR * metrosaPies(ULENGTH);
                                XYZ U2 = v1;
                                XYZ U3 = v1 - FNORMAL * (WALLWIDTH3 - 2 * metrosaPies(RCOVER));
                                XYZ U4 = U3 - WALLDIR * metrosaPies(ULENGTH);

                                // Lineas de BARRA LATERAL IZQUIERDA
                                List<Curve> LEFTCURVE = new List<Curve>();
                                LEFTCURVE.Add(Line.CreateBound(U1, U2));
                                LEFTCURVE.Add(Line.CreateBound(U2, U3));
                                LEFTCURVE.Add(Line.CreateBound(U3, U4));
                                USIDECURVES.Add(LEFTCURVE);

                                // Puntos de BARRA LATERAL DERECHA
                                U1 = v2 + WALLDIR * metrosaPies(ULENGTH);
                                U2 = v2;
                                U3 = v2 - FNORMAL * (WALLWIDTH3 - 2 * metrosaPies(RCOVER));
                                U4 = U3 + WALLDIR * metrosaPies(ULENGTH);

                                // Lineas de BARRA LATERAL DERECHA
                                List<Curve> RIGHTCURVE = new List<Curve>();
                                RIGHTCURVE.Add(Line.CreateBound(U1, U2));
                                RIGHTCURVE.Add(Line.CreateBound(U2, U3));
                                RIGHTCURVE.Add(Line.CreateBound(U3, U4));
                                USIDECURVES.Add(RIGHTCURVE);

                                // Puntos de BARRA SUPERIOR 
                                U1 = v4 + XYZ.BasisZ * metrosaPies(ULENGTH);
                                U2 = v4;
                                U3 = v4 - FNORMAL * (WALLWIDTH3 - 2 * metrosaPies(RCOVER));
                                U4 = U3 + XYZ.BasisZ * metrosaPies(ULENGTH);

                                // Lineas de BARRA SUPERIOR
                                List<Curve> TOPCURVE = new List<Curve>();
                                TOPCURVE.Add(Line.CreateBound(U1, U2));
                                TOPCURVE.Add(Line.CreateBound(U2, U3));
                                TOPCURVE.Add(Line.CreateBound(U3, U4));
                                UVERTICALSCURVES.Add(TOPCURVE);

                                // Puntos de BARRA INFERIOR
                                Wall WALL3 = EL as Wall;
                                if (WALL3 == null) continue;

                                LocationCurve LCCV3 = WALL3.Location as LocationCurve;
                                double BASEWALLELEV3 = 0;
                                if (LCCV3 != null)
                                {
                                    BASEWALLELEV3 = LCCV3.Curve.GetEndPoint(0).Z;
                                }

                                if (v1.Z > BASEWALLELEV3)
                                {
                                    U1 = v1 - XYZ.BasisZ * metrosaPies(ULENGTH);
                                    U2 = v1;
                                    U3 = v1 - FNORMAL * (WALLWIDTH3 - 2 * metrosaPies(RCOVER));
                                    U4 = U3 - XYZ.BasisZ * metrosaPies(ULENGTH);

                                    // Lineas de BARRA INFERIOR
                                    List<Curve> BOTTOMCURVE = new List<Curve>();
                                    BOTTOMCURVE.Add(Line.CreateBound(U1, U2));
                                    BOTTOMCURVE.Add(Line.CreateBound(U2, U3));
                                    BOTTOMCURVE.Add(Line.CreateBound(U3, U4));
                                    UVERTICALSCURVES.Add(BOTTOMCURVE);
                                }

                                // Generación de barras en U
                                foreach (List<Curve> LISTCV in USIDECURVES)
                                {
                                    Rebar REBARU = Rebar.CreateFromCurves(Doc, RebarStyle.StirrupTie, REBARTYPE, null, null, EL,
                                                                  XYZ.BasisZ,
                                                                  LISTCV, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);

                                    if (REBARU != null)
                                    {
                                        Parameter HOOKENDD = REBARU.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                                        Parameter HOOKSTARTD = REBARU.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);

                                        if (HOOKENDD != null && HOOKSTARTD != null)
                                        {
                                            HOOKENDD.Set(ElementId.InvalidElementId);
                                            HOOKSTARTD.Set(ElementId.InvalidElementId);
                                        }

                                        REBARU.SetUnobscuredInView(Doc.ActiveView, true);
                                        REBARU.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(0.328034, OPENINGHEIGHT - 2 * metrosaPies(RCOVER), true, true, true);
                                    }
                                }

                                foreach (List<Curve> LISTCV in UVERTICALSCURVES)
                                {
                                    Rebar REBARU = Rebar.CreateFromCurves(Doc, RebarStyle.StirrupTie, REBARTYPE, null, null, EL,
                                                                  WALLDIR,
                                                                  LISTCV, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);

                                    if (REBARU != null)
                                    {
                                        Parameter HOOKENDD = REBARU.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                                        Parameter HOOKSTARTD = REBARU.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);

                                        if (HOOKENDD != null && HOOKSTARTD != null)
                                        {
                                            HOOKENDD.Set(ElementId.InvalidElementId);
                                            HOOKSTARTD.Set(ElementId.InvalidElementId);
                                        }

                                        REBARU.SetUnobscuredInView(Doc.ActiveView, true);
                                        REBARU.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(0.328034, OPENINGWIDTH - 2 * metrosaPies(RCOVER), true, true, true);
                                    }
                                }

                                }
                        }
                    }
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
        public class WallSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (element != null && element.Category != null && element.Category.Name == "Muros")
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
