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
    public class ACEROESTRIBOSCOLUMNAS : LicensedCommand
    {
        public static double E = 0.164042; // Recubrimiento de 5cm al eje de la barra de acero (5 cm)
        public static double confinamiento = 0.98; // 0.30m - longitud de confinamiento en los extremos
        public static double espStirrupsLUZ = 0.65; // 0.20m - espaciamiento en la luz
        public static double espStirrupsCONF = 0.32; // 0.10m - espaciamiento en el confinamiento
        public double alturaViga = 1.9685; //Altura de viga (ft.)


        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //Crear objetos de la clase Document y Selection
            Document Doc = commandData.Application.ActiveUIDocument.Document;
            UIDocument UIDoc = commandData.Application.ActiveUIDocument;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;
            Autodesk.Revit.ApplicationServices.Application Application = commandData.Application.Application;



            //0.) Recibir los valores de entrada
            List<string> REBARTYPES = new FilteredElementCollector(Doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().Select(x => x.Name).ToList();



            //0.) Invocar la interfaz
            ACEROESTCOLXAML estcolUI = new ACEROESTCOLXAML(REBARTYPES);
            estcolUI.ShowDialog();
            //Lista de barras de acero
            string rebarTypeSelected = estcolUI.diametro.SelectedItem.ToString();

            //Recubrimiento            
            E = double.Parse(estcolUI.recubrimiento.Text);
            confinamiento = double.Parse(estcolUI.confinamientoLonG.Text);
            espStirrupsLUZ = double.Parse(estcolUI.espaciamientoenLuz.Text);
            espStirrupsCONF = double.Parse(estcolUI.espaciamientoenConf.Text);
            alturaViga = double.Parse(estcolUI.Hviga.Text);

            // Selección de columnas
            FiltroDeColumna FILTRO = new FiltroDeColumna();
            List<Reference> REFERENCIAS = UIDoc.Selection.PickObjects(ObjectType.Element, FILTRO, "Seleccione las columnas")?.ToList();

            // Colección de barras de acero
            RebarBarType BARTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .FirstOrDefault(x => x.Name == rebarTypeSelected);

            RebarHookType HOOKTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>()
                .FirstOrDefault();
        
            Element REBARCOVERTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarCoverType))
                .FirstOrDefault();


            #region Transacción para refuerzos
            Transaction TR = new Transaction(Doc, "Refuerzo en Columnas");
            TR.Start();
            foreach (Reference refe in REFERENCIAS)
            {
                Element columna = Doc.GetElement(refe);

                // Column height
                BoundingBoxXYZ bbCol = columna.get_BoundingBox(null);
                double altura = bbCol.Max.Z - bbCol.Min.Z;

                // Get corner points at column base
                XYZ vectorCara = new XYZ();
                List<XYZ> ptsColumna = GETPUNTOSCOLUMNAESTRIBOS(columna, out vectorCara);


                // Apply 5cm bottom cover
                ptsColumna[0] = new XYZ(ptsColumna[0].X, ptsColumna[0].Y, ptsColumna[0].Z + 0.1804462);
                ptsColumna[1] = new XYZ(ptsColumna[1].X, ptsColumna[1].Y, ptsColumna[1].Z + 0.1804462);
                ptsColumna[2] = new XYZ(ptsColumna[2].X, ptsColumna[2].Y, ptsColumna[2].Z + 0.1804462);
                ptsColumna[3] = new XYZ(ptsColumna[3].X, ptsColumna[3].Y, ptsColumna[3].Z + 0.1804462);

                altura = altura - metrosaPies(espStirrupsCONF) - metrosaPies(alturaViga);

                // Create bottom stirrup curve
                Curve c1 = Line.CreateBound(ptsColumna[0], ptsColumna[1]);
                Curve c2 = Line.CreateBound(ptsColumna[1], ptsColumna[2]);
                Curve c3 = Line.CreateBound(ptsColumna[2], ptsColumna[3]);
                Curve c4 = Line.CreateBound(ptsColumna[3], ptsColumna[0]);
                List<Curve> curvasEstriboBase = new List<Curve> { c1, c2, c3, c4 };

                // Create stirrups
                Rebar rebarStirrup = Rebar.CreateFromCurves(Doc,
                                                           RebarStyle.StirrupTie,
                                                           BARTYPE,
                                                           null,
                                                           null,
                                                           columna,
                                                           -vectorCara,
                                                           curvasEstriboBase,
                                                           RebarHookOrientation.Right,
                                                           RebarHookOrientation.Left,
                                                           true,
                                                           true);

                // We'll continue using SetUnobscuredInView as it appears to still be available in Revit 2025
                View3D view3D = Doc.ActiveView as View3D;
                if (view3D != null)
                {
                    rebarStirrup.SetUnobscuredInView(view3D, true);
                }

                rebarStirrup.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(espStirrupsCONF), metrosaPies(confinamiento), true, true, true);

                // Create top stirrup curve
                XYZ ptSup1 = new XYZ(ptsColumna[0].X, ptsColumna[0].Y, ptsColumna[0].Z + altura);
                XYZ ptSup2 = new XYZ(ptsColumna[1].X, ptsColumna[1].Y, ptsColumna[1].Z + altura);
                XYZ ptSup3 = new XYZ(ptsColumna[2].X, ptsColumna[2].Y, ptsColumna[2].Z + altura);
                XYZ ptSup4 = new XYZ(ptsColumna[3].X, ptsColumna[3].Y, ptsColumna[3].Z + altura);

                Curve cs1 = Line.CreateBound(ptSup1, ptSup2);
                Curve cs2 = Line.CreateBound(ptSup2, ptSup3);
                Curve cs3 = Line.CreateBound(ptSup3, ptSup4);
                Curve cs4 = Line.CreateBound(ptSup4, ptSup1);
                List<Curve> curvasEstriboSuperior = new List<Curve> { cs1, cs2, cs3, cs4 };

                rebarStirrup = Rebar.CreateFromCurves(Doc,
                                                     RebarStyle.StirrupTie,
                                                     BARTYPE,
                                                     null,
                                                     null,
                                                     columna,
                                                     vectorCara,
                                                     curvasEstriboSuperior,
                                                     RebarHookOrientation.Right,
                                                     RebarHookOrientation.Left,
                                                     true,
                                                     true);


                if (view3D != null)
                {
                    rebarStirrup.SetUnobscuredInView(view3D, true);
                }

                rebarStirrup.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(espStirrupsCONF), metrosaPies(confinamiento), true, true, true);

                //obtener parametro de gancho
                Parameter hookEnd = rebarStirrup.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                Parameter hookStart = rebarStirrup.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);
                Parameter rotacionFinal = rebarStirrup.LookupParameter("Rotación del gancho al final");
                if (rotacionFinal != null)
                {
                    // Convertir 180° a radianes
                    double rotacionEnRadianes = Math.PI;

                    // Establecer el valor de rotación
                    rotacionFinal.Set(rotacionEnRadianes);
                }
                else
                {
                    TaskDialog.Show("Error", "No se pudo acceder al parámetro 'Rotación del gancho al final'.");
                }
                //establecer el primer gancho que contenga el string 135
                if (hookEnd != null && hookStart != null)
                {
                    // Buscar gancho con diferentes variaciones del nombre
                    RebarHookType hook135 = new FilteredElementCollector(Doc)
                        .OfClass(typeof(RebarHookType))
                        .Cast<RebarHookType>()
                        .FirstOrDefault(x => x.Name.Contains("135") ||
                                            x.Name.Contains("135°") ||
                                            x.Name.ToLower().Contains("135"));

                    ElementId hookId = hook135?.Id ?? ElementId.InvalidElementId;

                    hookEnd.Set(hookId);
                    hookStart.Set(hookId);
                }


                // Create stirrups in the confined zone
                XYZ ptConf1 = new XYZ(ptsColumna[0].X, ptsColumna[0].Y, ptsColumna[0].Z + metrosaPies(confinamiento) + metrosaPies(espStirrupsCONF));
                XYZ ptConf2 = new XYZ(ptsColumna[1].X, ptsColumna[1].Y, ptsColumna[1].Z + metrosaPies(confinamiento) + metrosaPies(espStirrupsCONF));
                XYZ ptConf3 = new XYZ(ptsColumna[2].X, ptsColumna[2].Y, ptsColumna[2].Z + metrosaPies(confinamiento) + metrosaPies(espStirrupsCONF));
                XYZ ptConf4 = new XYZ(ptsColumna[3].X, ptsColumna[3].Y, ptsColumna[3].Z + metrosaPies(confinamiento) + metrosaPies(espStirrupsCONF));

                Curve cf1 = Line.CreateBound(ptConf1, ptConf2);
                Curve cf2 = Line.CreateBound(ptConf2, ptConf3);
                Curve cf3 = Line.CreateBound(ptConf3, ptConf4);
                Curve cf4 = Line.CreateBound(ptConf4, ptConf1);
                List<Curve> curvasEstriboConfinamiento = new List<Curve> { cf1, cf2, cf3, cf4 };

                rebarStirrup = Rebar.CreateFromCurves(Doc,
                                                     RebarStyle.StirrupTie,
                                                     BARTYPE,
                                                     null,
                                                     null,
                                                     columna,
                                                     -vectorCara,
                                                     curvasEstriboConfinamiento,
                                                     RebarHookOrientation.Right,
                                                     RebarHookOrientation.Left,
                                                     true,
                                                     true);

                if (view3D != null)
                {
                    rebarStirrup.SetUnobscuredInView(view3D, true);
                }

                rebarStirrup.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(espStirrupsLUZ), altura - 2 * metrosaPies(confinamiento) - 3 * metrosaPies(espStirrupsCONF), true, true, true);

                //obtener parametro de gancho
                Parameter hookEndconf = rebarStirrup.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                Parameter hookStartconf = rebarStirrup.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);
                Parameter rotacionFinalconf = rebarStirrup.LookupParameter("Rotación del gancho al final");
                if (rotacionFinal != null)
                {
                    // Convertir 180° a radianes
                    double rotacionEnRadianes = Math.PI;

                    // Establecer el valor de rotación
                    rotacionFinal.Set(rotacionEnRadianes);
                }
                else
                {
                    TaskDialog.Show("Error", "No se pudo acceder al parámetro 'Rotación del gancho al final'.");
                }
                //establecer el primer gancho que contenga el string 135
                if (hookEndconf != null && hookStartconf != null)
                {
                    // Buscar gancho con diferentes variaciones del nombre
                    RebarHookType hook135 = new FilteredElementCollector(Doc)
                        .OfClass(typeof(RebarHookType))
                        .Cast<RebarHookType>()
                        .FirstOrDefault(x => x.Name.Contains("135") ||
                                            x.Name.Contains("135°") ||
                                            x.Name.ToLower().Contains("135"));

                    ElementId hookId = hook135?.Id ?? ElementId.InvalidElementId;

                    hookEndconf.Set(hookId);
                    hookStartconf.Set(hookId);
                }
            }

            TR.Commit();
            return Result.Succeeded;

            #endregion


        }

        // Métodos auxiliares para COLUMNAS
        public List<XYZ> GETPUNTOSCOLUMNAESTRIBOS(Element col, out XYZ VECTORNORMAL)
        {
            VECTORNORMAL = XYZ.BasisZ; // Valor predeterminado

            if (col == null) return null;

            Options OPT = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Coarse,
                IncludeNonVisibleObjects = false
            };

            Face CARABUSCADA = null;

            GeometryElement GEO = col.get_Geometry(OPT);
            if (GEO == null) return null;

            // Buscar cara inferior de la columna
            foreach (GeometryObject OBJ in GEO)
            {
                Solid SOLD = OBJ as Solid;
                if (SOLD != null && SOLD.Faces.Size > 0)
                {
                    foreach (Face FACE in SOLD.Faces)
                    {
                        if (FACE is PlanarFace planarFace &&
                            planarFace.FaceNormal.IsAlmostEqualTo(-XYZ.BasisZ, 0.01))
                        {
                            CARABUSCADA = FACE;
                            break;
                        }
                    }

                    if (CARABUSCADA != null) break;
                }
            }

            if (CARABUSCADA == null) return null;

            // Calcular puntos
            BoundingBoxUV BBOXUV = CARABUSCADA.GetBoundingBox();
            if (BBOXUV == null) return null;

            UV U1 = BBOXUV.Min;
            UV U2 = BBOXUV.Max;

            
            UV UN1 = U1 + new UV(metrosaPies(E), metrosaPies(E));
            UV UN2 = new UV(U2.U, U1.V) + new UV(-metrosaPies(E), metrosaPies(E));
            UV UN3 = U2 + new UV(-metrosaPies(E), -metrosaPies(E));
            UV UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E), -metrosaPies(E));

            List<XYZ> PTS = new List<XYZ>
            {
                CARABUSCADA.Evaluate(UN1),
                CARABUSCADA.Evaluate(UN2),
                CARABUSCADA.Evaluate(UN3),
                CARABUSCADA.Evaluate(UN4)
            };

            if (CARABUSCADA is PlanarFace planarCara)
            {
                VECTORNORMAL = planarCara.FaceNormal;
            }

            return PTS;
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
