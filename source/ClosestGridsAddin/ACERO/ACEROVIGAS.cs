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
    public class ACEROVIGAS : LicensedCommand
    {
        public static double E = 0.164042; // Recubrimiento de 5cm al eje de la barra de acero (5 cm)
        public static double CONFINAMIENTO = 0.98; // 0.30m - longitud de confinamiento en los extremos
        public static double ESPSTIRRUPSLUZ = 0.65; // 0.20m - espaciamiento en la luz
        public static double ESPSTIRRUPSCONF = 0.32; // 0.10m - espaciamiento en el confinamiento
        public static double alavigaT = 0.5; // Retiro positivo de 0.5m para vigas T, configurable según necesidad
        public static string valorSuperior = "2";

        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //Crear objetos de la clase Document y Selection
            Document Doc = commandData.Application.ActiveUIDocument.Document;
            UIDocument UIDoc = commandData.Application.ActiveUIDocument;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;
            Autodesk.Revit.ApplicationServices.Application Application = commandData.Application.Application;


            //0.) Recibir los valores de entrada
            List<string> REBARTYPES = new FilteredElementCollector(Doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().Select(x => x.Name).ToList();
            bool estribosActivado = false;
            bool longitudActivada = false;
            


            //0.) Invocar la interfaz
            ACEROVIGASXAML vigUI = new ACEROVIGASXAML(REBARTYPES, estribosActivado, longitudActivada);
            vigUI.ShowDialog();
            //Lista de barras de acero
            string rebarTypeSelected = vigUI.barras.SelectedItem.ToString();
            string estriborebarTypeSelected = vigUI.estribos.SelectedItem.ToString();
            estribosActivado = vigUI.activarEstribos.IsChecked ?? false;
            longitudActivada = vigUI.activarLong.IsChecked ?? false;

            // Para obtener los valores de los ComboBoxes actualizados:
            string valorSuperior = ((ComboBoxItem)vigUI.Superior.SelectedItem)?.Content?.ToString() ?? "2";
            string valorCentral = ((ComboBoxItem)vigUI.Central.SelectedItem)?.Content?.ToString() ?? "2";
            string valorInferior = ((ComboBoxItem)vigUI.Inferior.SelectedItem)?.Content?.ToString() ?? "2";



            //Recubrimiento
            E = double.Parse(vigUI.recubrimiento.Text);
            CONFINAMIENTO = double.Parse(vigUI.confinamientoLonG.Text);
            ESPSTIRRUPSLUZ = double.Parse(vigUI.espaciamientoenLuz.Text);
            ESPSTIRRUPSCONF = double.Parse(vigUI.espaciamientoenConf.Text);
            alavigaT = double.Parse(vigUI.longitudVigaTUI.Text);

            // Selección de columnas
            FiltroDeViga FILTRO = new FiltroDeViga();
            List<Reference> REFERENCIAS = UIDoc.Selection.PickObjects(ObjectType.Element, FILTRO, "Seleccione las vigas")?.ToList();

            // Colección de barras de acero
            RebarBarType BARTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .FirstOrDefault(x => x.Name == rebarTypeSelected);

            RebarBarType ESTRIBOBARTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .FirstOrDefault(x => x.Name == estriborebarTypeSelected);

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

            foreach (Reference REFE in REFERENCIAS)
            {
                Element VIGA = Doc.GetElement(REFE);
                
                //3) Longitud de la viga
                Parameter LENGTHP = VIGA.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH);
                double LONGITUD; // en pies (unidades internas de Revit)
                try
                {
                    double minMetros = 1.2; // ajusta el mínimo deseado
                    double minPies = metrosaPies(minMetros);
                    double? len = LENGTHP?.AsDouble();

                    if (len is null || len.Value < minPies)
                    {
                        TaskDialog.Show("Longitud inválida",
                            $"La longitud de la viga es demasiado pequeña o inexistente. " +
                            $"Se usará el mínimo permitido: {minMetros:0.###} m.");
                        LONGITUD = minPies;
                    }
                    else
                    {
                        LONGITUD = len.Value;
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Error al leer longitud",
                        ex.Message + Environment.NewLine + "Se usará un valor mínimo para continuar.");
                    LONGITUD = metrosaPies(1.2);
                    continue;
                }

                //4) Obtener las esquinas en una de las caras laterales
                XYZ VECTORCARA = new XYZ();
                List<XYZ> PTSVIGA = GETPUNTOSVIGAS(VIGA, out VECTORCARA, valorSuperior, valorInferior, valorCentral);
                if (PTSVIGA == null || PTSVIGA.Count == 0)
                {
                    TaskDialog.Show("Error", "No se encontraron puntos válidos para la viga.");
                    continue;
                }

                //5) Creación de barras longitudinales
                int i = 1;
                double retiropositivoVigaT = 0; //0.5m, configurable según necesidad
                double RETIROPOSITIVO = 0; //0.5m, configurable según necesidad

                bool esVigaT = esvigaT(VIGA);
                if (esVigaT== true)
                {
                    if (longitudActivada == true)
                    {
                        //4) Obtener las esquinas en una de las caras laterales
                        XYZ vectorcaraVigaT = new XYZ();
                        List<XYZ> ptsVigaT = GETPUNTOSVIGAST(VIGA, out vectorcaraVigaT);  

                        foreach (XYZ ptVigaT in ptsVigaT)
                        { 
                            XYZ ptfarVigaT = ptVigaT + -vectorcaraVigaT * LONGITUD;
                                Line rebarlineVigaT = Line.CreateBound(ptVigaT, ptfarVigaT);
                                // Para barras con retiro (última barra)
                                XYZ initVigaT = ptVigaT + -vectorcaraVigaT * retiropositivoVigaT;
                                XYZ endVigaT = ptVigaT + -vectorcaraVigaT * (LONGITUD - retiropositivoVigaT);
                            rebarlineVigaT = Line.CreateBound(initVigaT, endVigaT);
                            
                            rebarlineVigaT = Line.CreateBound(ptVigaT + vectorcaraVigaT * metrosaPies(E), ptfarVigaT + vectorcaraVigaT * metrosaPies(E));
                                IList<Curve> curvesVigaT = [rebarlineVigaT];
                                // Crear barra de refuerzo
                                Rebar REBAR = Rebar.CreateFromCurves(Doc,
                                                                  RebarStyle.Standard,
                                                                  BARTYPE,
                                                                  null,
                                                                  null,
                                                                  VIGA,
                                                                  vectorcaraVigaT.CrossProduct(XYZ.BasisZ),
                                                                  curvesVigaT,
                                                                  RebarHookOrientation.Right,
                                                                  RebarHookOrientation.Left,
                                                                  true,
                                                                  true);
                                // Configurar visibilidad en vista
                                REBAR.SetUnobscuredInView(Doc.ActiveView, true);
                                i++;
                            //obtener parametro de gancho
                            Parameter hookEnd = REBAR.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                            Parameter hookStart = REBAR.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);
                            Parameter rotacionFinal = REBAR.LookupParameter("Rotación del gancho al final");
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
                            Doc.Regenerate();   

                        }                        
                    }

                    else
                    {
                        longitudActivada = false;
                        Doc.Regenerate();
                    }

                    if (estribosActivado == true)
                    {

                        Element estVIGA = Doc.GetElement(REFE);

                        //3) Longitud de la viga
                        Parameter estLENGTHP = VIGA.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH);
                        double estLONGITUD = 0;
                        if (estLONGITUD != null) estLONGITUD = estLENGTHP.AsDouble();

                        //4) Obtener las esquinas en una de las caras laterales
                        XYZ VECTORCARAESTRIBOS = new XYZ();
                        List<XYZ> PTSVIGAESTRIBOS = GETPUNTOSESTRIBOSVIGAST(estVIGA, out VECTORCARAESTRIBOS);
                        if (PTSVIGAESTRIBOS == null || PTSVIGAESTRIBOS.Count == 0)
                        {
                            TaskDialog.Show("Error", "No se encontraron puntos válidos para los estribos de la viga.");
                            continue;
                        }

                        //6.1) Recubrimiento superior e inferior de 5 cm
                        PTSVIGAESTRIBOS[0] = PTSVIGAESTRIBOS[0] - VECTORCARAESTRIBOS * 0.164;
                        PTSVIGAESTRIBOS[1] = PTSVIGAESTRIBOS[1] - VECTORCARAESTRIBOS * 0.164;
                        PTSVIGAESTRIBOS[2] = PTSVIGAESTRIBOS[2] - VECTORCARAESTRIBOS * 0.164;
                        PTSVIGAESTRIBOS[3] = PTSVIGAESTRIBOS[3] - VECTORCARAESTRIBOS * 0.164;

                        estLONGITUD = estLONGITUD - metrosaPies(ESPSTIRRUPSCONF);

                        //6.2 OBTENER LA CURVA DEL ESTRIBO EN EL CONFINAMIENTO INICIAL
                        Curve C1 = Line.CreateBound(PTSVIGAESTRIBOS[0], PTSVIGAESTRIBOS[1]);
                        Curve C2 = Line.CreateBound(PTSVIGAESTRIBOS[1], PTSVIGAESTRIBOS[2]);
                        Curve C3 = Line.CreateBound(PTSVIGAESTRIBOS[2], PTSVIGAESTRIBOS[3]);
                        Curve C4 = Line.CreateBound(PTSVIGAESTRIBOS[3], PTSVIGAESTRIBOS[0]);
                        List<Curve> CURVASESTRIBOINICIAL = new List<Curve> { C1, C2, C3, C4 };

                        Rebar REBARSTIRRUP = Rebar.CreateFromCurves(Doc,
                                                                RebarStyle.StirrupTie,
                                                                ESTRIBOBARTYPE,
                                                                null,
                                                                null,
                                                                VIGA,
                                                                -VECTORCARAESTRIBOS,
                                                                CURVASESTRIBOINICIAL,
                                                                RebarHookOrientation.Right,
                                                                RebarHookOrientation.Left,
                                                                true,
                                                                true);

                        REBARSTIRRUP.SetUnobscuredInView(Doc.ActiveView, true);
                        REBARSTIRRUP.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(ESPSTIRRUPSCONF), metrosaPies(CONFINAMIENTO), true, true, true);

                        //6.3) Curva en la distribución de la luz
                        XYZ PTLUZ1 = PTSVIGAESTRIBOS[0] - VECTORCARAESTRIBOS * (metrosaPies(CONFINAMIENTO) + metrosaPies(ESPSTIRRUPSCONF));
                        XYZ PTLUZ2 = PTSVIGAESTRIBOS[1] - VECTORCARAESTRIBOS * (metrosaPies(CONFINAMIENTO) + metrosaPies(ESPSTIRRUPSCONF));
                        XYZ PTLUZ3 = PTSVIGAESTRIBOS[2] - VECTORCARAESTRIBOS * (metrosaPies(CONFINAMIENTO) + metrosaPies(ESPSTIRRUPSCONF));
                        XYZ PTLUZ4 = PTSVIGAESTRIBOS[3] - VECTORCARAESTRIBOS * (metrosaPies(CONFINAMIENTO) + metrosaPies(ESPSTIRRUPSCONF));

                        Curve CL1 = Line.CreateBound(PTLUZ1, PTLUZ2);
                        Curve CL2 = Line.CreateBound(PTLUZ2, PTLUZ3);
                        Curve CL3 = Line.CreateBound(PTLUZ3, PTLUZ4);
                        Curve CL4 = Line.CreateBound(PTLUZ4, PTLUZ1);
                        List<Curve> CURVASESTRIBOLUZ = new List<Curve> { CL1, CL2, CL3, CL4 };

                        REBARSTIRRUP = Rebar.CreateFromCurves(Doc,
                                                           RebarStyle.StirrupTie,
                                                           ESTRIBOBARTYPE,
                                                           null,
                                                           null,
                                                           VIGA,
                                                           -VECTORCARAESTRIBOS,
                                                           CURVASESTRIBOLUZ,
                                                           RebarHookOrientation.Right,
                                                           RebarHookOrientation.Left,
                                                           true,
                                                           true);

                        REBARSTIRRUP.SetUnobscuredInView(Doc.ActiveView, true);
                        REBARSTIRRUP.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(ESPSTIRRUPSLUZ), estLONGITUD - 2 * metrosaPies(CONFINAMIENTO) - 2 * metrosaPies(ESPSTIRRUPSCONF), true, true, true);

                        //6.4) Obtener la curva en el confinamiento opuesto
                        XYZ PTSUP1 = PTSVIGAESTRIBOS[0] - estLONGITUD * VECTORCARAESTRIBOS;
                        XYZ PTSUP2 = PTSVIGAESTRIBOS[1] - estLONGITUD * VECTORCARAESTRIBOS;
                        XYZ PTSUP3 = PTSVIGAESTRIBOS[2] - estLONGITUD * VECTORCARAESTRIBOS;
                        XYZ PTSUP4 = PTSVIGAESTRIBOS[3] - estLONGITUD * VECTORCARAESTRIBOS;

                        Curve CS1 = Line.CreateBound(PTSUP1, PTSUP2);
                        Curve CS2 = Line.CreateBound(PTSUP2, PTSUP3);
                        Curve CS3 = Line.CreateBound(PTSUP3, PTSUP4);
                        Curve CS4 = Line.CreateBound(PTSUP4, PTSUP1);

                        List<Curve> CURVASESTRIBOSUPERIOR = new List<Curve> { CS1, CS2, CS3, CS4 };

                        REBARSTIRRUP = Rebar.CreateFromCurves(Doc,
                                                           RebarStyle.StirrupTie,
                                                           ESTRIBOBARTYPE,
                                                           null,
                                                           null,
                                                           VIGA,
                                                           VECTORCARAESTRIBOS,
                                                           CURVASESTRIBOSUPERIOR,
                                                           RebarHookOrientation.Right,
                                                           RebarHookOrientation.Left,
                                                           true,
                                                           true);

                        REBARSTIRRUP.SetUnobscuredInView(Doc.ActiveView, true);
                        REBARSTIRRUP.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(ESPSTIRRUPSCONF), metrosaPies(CONFINAMIENTO), true, true, true);
                        //obtener parametro de gancho
                        Parameter hookEnd = REBARSTIRRUP.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                        Parameter hookStart = REBARSTIRRUP.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);
                        Parameter rotacionFinal = REBARSTIRRUP.LookupParameter("Rotación del gancho al final");
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
                        Doc.Regenerate();
                    }

                    else
                    {
                        estribosActivado = false;
                        Doc.Regenerate();
                    }


                }
                #region VIGAS RECTANGULARES
                else
                {

                    foreach (XYZ PT in PTSVIGA)
                    {

                        if (longitudActivada == true)
                        {
                            //Almacenar vigas seleccionadas

                            
                            XYZ PTFAR = PT + -VECTORCARA * LONGITUD;
                            Line REBARLINE = Line.CreateBound(PT, PTFAR);

                            // Para barras con retiro (última barra)

                            XYZ INIT = PT + -VECTORCARA * RETIROPOSITIVO;
                            XYZ END = PT + -VECTORCARA * (LONGITUD - RETIROPOSITIVO);

                            REBARLINE = Line.CreateBound(INIT, END);

                            
                            IList<Curve> CURVES = [REBARLINE];

                            // Crear barra de refuerzo
                            Rebar REBAR = Rebar.CreateFromCurves(Doc,
                                                              RebarStyle.Standard,
                                                              BARTYPE,
                                                              null,
                                                              null,
                                                              VIGA,
                                                              VECTORCARA.CrossProduct(XYZ.BasisZ),
                                                              CURVES,
                                                              RebarHookOrientation.Right,
                                                              RebarHookOrientation.Left,
                                                              true,
                                                              true);

                            // Configurar visibilidad en vista
                            REBAR.SetUnobscuredInView(Doc.ActiveView, true);


                            i++;
                            Doc.Regenerate();
                        }
                        else
                        {
                            longitudActivada = false;
                            Doc.Regenerate();
                        }

                    }

                    if (estribosActivado == true)
                    {

                        Element estVIGA = Doc.GetElement(REFE);

                        //3) Longitud de la viga
                        Parameter estLENGTHP = VIGA.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH);
                        double estLONGITUD = 0;
                        if (estLONGITUD != null) estLONGITUD = estLENGTHP.AsDouble();

                        //4) Obtener las esquinas en una de las caras laterales
                        XYZ VECTORCARAESTRIBOS = new XYZ();
                        List<XYZ> PTSVIGAESTRIBOS = GETPUNTOSESTRIBOSVIGAS(estVIGA, out VECTORCARAESTRIBOS);
                        if (PTSVIGAESTRIBOS == null || PTSVIGAESTRIBOS.Count == 0)
                        {
                            TaskDialog.Show("Error", "No se encontraron puntos válidos para los estribos de la viga.");
                            continue;
                        }

                        //6.1) Recubrimiento superior e inferior de 5 cm
                        PTSVIGAESTRIBOS[0] = PTSVIGAESTRIBOS[0] - VECTORCARAESTRIBOS * 0.164;
                        PTSVIGAESTRIBOS[1] = PTSVIGAESTRIBOS[1] - VECTORCARAESTRIBOS * 0.164;
                        PTSVIGAESTRIBOS[2] = PTSVIGAESTRIBOS[2] - VECTORCARAESTRIBOS * 0.164;
                        PTSVIGAESTRIBOS[3] = PTSVIGAESTRIBOS[3] - VECTORCARAESTRIBOS * 0.164;

                        estLONGITUD = estLONGITUD - metrosaPies(ESPSTIRRUPSCONF);

                        //6.2 OBTENER LA CURVA DEL ESTRIBO EN EL CONFINAMIENTO INICIAL
                        Curve C1 = Line.CreateBound(PTSVIGAESTRIBOS[0], PTSVIGAESTRIBOS[1]);
                        Curve C2 = Line.CreateBound(PTSVIGAESTRIBOS[1], PTSVIGAESTRIBOS[2]);
                        Curve C3 = Line.CreateBound(PTSVIGAESTRIBOS[2], PTSVIGAESTRIBOS[3]);
                        Curve C4 = Line.CreateBound(PTSVIGAESTRIBOS[3], PTSVIGAESTRIBOS[0]);
                        List<Curve> CURVASESTRIBOINICIAL = new List<Curve> { C1, C2, C3, C4 };

                        Rebar REBARSTIRRUP = Rebar.CreateFromCurves(Doc,
                                                                RebarStyle.StirrupTie,
                                                                ESTRIBOBARTYPE,
                                                                null,
                                                                null,
                                                                VIGA,
                                                                -VECTORCARAESTRIBOS,
                                                                CURVASESTRIBOINICIAL,
                                                                RebarHookOrientation.Right,
                                                                RebarHookOrientation.Left,
                                                                true,
                                                                true);

                        REBARSTIRRUP.SetUnobscuredInView(Doc.ActiveView, true);
                        REBARSTIRRUP.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(ESPSTIRRUPSCONF), metrosaPies(CONFINAMIENTO), true, true, true);

                        //obtener parametro de gancho
                        Parameter hookEnd = REBARSTIRRUP.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                        Parameter hookStart = REBARSTIRRUP.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);
                        Parameter rotacionFinal = REBARSTIRRUP.LookupParameter("Rotación del gancho al final");
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

                        //6.3) Curva en la distribución de la luz
                        XYZ PTLUZ1 = PTSVIGAESTRIBOS[0] - VECTORCARAESTRIBOS * (metrosaPies(CONFINAMIENTO) + metrosaPies(ESPSTIRRUPSCONF));
                        XYZ PTLUZ2 = PTSVIGAESTRIBOS[1] - VECTORCARAESTRIBOS * (metrosaPies(CONFINAMIENTO) + metrosaPies(ESPSTIRRUPSCONF));
                        XYZ PTLUZ3 = PTSVIGAESTRIBOS[2] - VECTORCARAESTRIBOS * (metrosaPies(CONFINAMIENTO) + metrosaPies(ESPSTIRRUPSCONF));
                        XYZ PTLUZ4 = PTSVIGAESTRIBOS[3] - VECTORCARAESTRIBOS * (metrosaPies(CONFINAMIENTO) + metrosaPies(ESPSTIRRUPSCONF));

                        Curve CL1 = Line.CreateBound(PTLUZ1, PTLUZ2);
                        Curve CL2 = Line.CreateBound(PTLUZ2, PTLUZ3);
                        Curve CL3 = Line.CreateBound(PTLUZ3, PTLUZ4);
                        Curve CL4 = Line.CreateBound(PTLUZ4, PTLUZ1);
                        List<Curve> CURVASESTRIBOLUZ = new List<Curve> { CL1, CL2, CL3, CL4 };

                        REBARSTIRRUP = Rebar.CreateFromCurves(Doc,
                                                           RebarStyle.StirrupTie,
                                                           ESTRIBOBARTYPE,
                                                           null,
                                                           null,
                                                           VIGA,
                                                           -VECTORCARAESTRIBOS,
                                                           CURVASESTRIBOLUZ,
                                                           RebarHookOrientation.Right,
                                                           RebarHookOrientation.Left,
                                                           true,
                                                           true);

                        REBARSTIRRUP.SetUnobscuredInView(Doc.ActiveView, true);
                        REBARSTIRRUP.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(ESPSTIRRUPSLUZ), estLONGITUD - 2 * metrosaPies(CONFINAMIENTO) - 2 * metrosaPies(ESPSTIRRUPSCONF), true, true, true);

                        //obtener parametro de gancho
                        Parameter hookEnd2 = REBARSTIRRUP.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                        Parameter hookStart2 = REBARSTIRRUP.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);
                        Parameter rotacionFinal2 = REBARSTIRRUP.LookupParameter("Rotación del gancho al final");
                        if (rotacionFinal2 != null)
                        {
                            // Convertir 180° a radianes
                            double rotacionEnRadianes = Math.PI;

                            // Establecer el valor de rotación
                            rotacionFinal2.Set(rotacionEnRadianes);
                        }
                        else
                        {
                            TaskDialog.Show("Error", "No se pudo acceder al parámetro 'Rotación del gancho al final'.");
                        }
                        //establecer el primer gancho que contenga el string 135
                        if (hookEnd2 != null && hookStart2 != null)
                        {
                            // Buscar gancho con diferentes variaciones del nombre
                            RebarHookType hook135 = new FilteredElementCollector(Doc)
                                .OfClass(typeof(RebarHookType))
                                .Cast<RebarHookType>()
                                .FirstOrDefault(x => x.Name.Contains("135") ||
                                                    x.Name.Contains("135°") ||
                                                    x.Name.ToLower().Contains("135"));

                            ElementId hookId = hook135?.Id ?? ElementId.InvalidElementId;

                            hookEnd2.Set(hookId);
                            hookStart2.Set(hookId);
                        }

                        //6.4) Obtener la curva en el confinamiento opuesto
                        XYZ PTSUP1 = PTSVIGAESTRIBOS[0] - estLONGITUD * VECTORCARAESTRIBOS;
                        XYZ PTSUP2 = PTSVIGAESTRIBOS[1] - estLONGITUD * VECTORCARAESTRIBOS;
                        XYZ PTSUP3 = PTSVIGAESTRIBOS[2] - estLONGITUD * VECTORCARAESTRIBOS;
                        XYZ PTSUP4 = PTSVIGAESTRIBOS[3] - estLONGITUD * VECTORCARAESTRIBOS;

                        Curve CS1 = Line.CreateBound(PTSUP1, PTSUP2);
                        Curve CS2 = Line.CreateBound(PTSUP2, PTSUP3);
                        Curve CS3 = Line.CreateBound(PTSUP3, PTSUP4);
                        Curve CS4 = Line.CreateBound(PTSUP4, PTSUP1);

                        List<Curve> CURVASESTRIBOSUPERIOR = new List<Curve> { CS1, CS2, CS3, CS4 };

                        REBARSTIRRUP = Rebar.CreateFromCurves(Doc,
                                                           RebarStyle.StirrupTie,
                                                           ESTRIBOBARTYPE,
                                                           null,
                                                           null,
                                                           VIGA,
                                                           VECTORCARAESTRIBOS,
                                                           CURVASESTRIBOSUPERIOR,
                                                           RebarHookOrientation.Right,
                                                           RebarHookOrientation.Left,
                                                           true,
                                                           true);

                        REBARSTIRRUP.SetUnobscuredInView(Doc.ActiveView, true);
                        REBARSTIRRUP.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(metrosaPies(ESPSTIRRUPSCONF), metrosaPies(CONFINAMIENTO), true, true, true);

                        //obtener parametro de gancho
                        Parameter hookEndconf = REBARSTIRRUP.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                        Parameter hookStartconf = REBARSTIRRUP.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);
                        Parameter rotacionFinalconf = REBARSTIRRUP.LookupParameter("Rotación del gancho al final");
                        if (rotacionFinalconf != null)
                        {
                            // Convertir 180° a radianes
                            double rotacionEnRadianes = Math.PI;

                            // Establecer el valor de rotación
                            rotacionFinalconf.Set(rotacionEnRadianes);
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
                        Doc.Regenerate();
                    }

                    else
                    {
                        estribosActivado = false;
                        Doc.Regenerate();
                    }

                }
                #endregion VIGAS RECTANGULARES
            }
            Doc.Regenerate();
            TR.Commit();
            return Result.Succeeded;
        }
        #endregion
        // Métodos auxiliares para VIGAS
        public List<XYZ> GETPUNTOSVIGAS(Element col, out XYZ VECTORNORMAL,string valorSuperior, string valorInferior,string valorCentral)


        {
            Options OPT = new Options();
            OPT.ComputeReferences = false;
            OPT.DetailLevel = ViewDetailLevel.Coarse;
            OPT.IncludeNonVisibleObjects = false;

            Face CARABUSCADA = null;
            VECTORNORMAL = XYZ.Zero; // Inicializar con un valor por defecto para evitar errores

            GeometryElement GEO = col.get_Geometry(OPT);
            if (GEO == null)
            {
                // Manejar el caso de geometría nula
                return new List<XYZ>();
            }

            List<Tuple<double, Face>> LATERALFACES = new List<Tuple<double, Face>>();

            // Buscar todas las caras laterales
            foreach (GeometryObject OBJ in GEO)
            {
                Solid SOLD = OBJ as Solid;
                if (SOLD != null && SOLD.Volume > 0.0)
                {
                    if (SOLD.Faces.Size > 0)
                    {
                        foreach (Face FACE in SOLD.Faces)
                        {
                            // Verificar que sea una cara planar
                            PlanarFace planarFace = FACE as PlanarFace;
                            if (planarFace != null)
                            {
                                XYZ VFACE = planarFace.FaceNormal.Normalize();

                                // Continuar si la cara es horizontal (excluir caras superior e inferior)
                                if (VFACE.IsAlmostEqualTo(-XYZ.BasisZ, 0.01) ||
                                    VFACE.IsAlmostEqualTo(XYZ.BasisZ, 0.01))
                                    continue;

                                LATERALFACES.Add(new Tuple<double, Face>(FACE.Area, FACE));
                            }
                        }
                    }
                }
            }

            // Verificar si se encontraron caras laterales
            if (LATERALFACES.Count == 0)
            {
                return new List<XYZ>();
            }

            // Ordenar las caras por área para encontrar las laterales
            LATERALFACES = LATERALFACES.OrderBy(x => x.Item1).ToList();
            CARABUSCADA = LATERALFACES[0].Item2;

            // Obtener el contorno de la cara
            BoundingBoxUV BBOXUV = CARABUSCADA.GetBoundingBox();
            UV U1 = BBOXUV.Min;
            UV U2 = BBOXUV.Max;

            UV CENTRO = (U1 + U2) / 2;

            // Obtener el vector normal
            VECTORNORMAL = (CARABUSCADA as PlanarFace).FaceNormal;

            List<XYZ> puntosAcumulados = new List<XYZ>();

            if (valorSuperior == "2")
            {
                // Crear puntos en cada esquina de la cara con desplazamiento
                UV UN2 = new UV(U2.U, U1.V) + new UV(-metrosaPies(E) * 1.8, metrosaPies(E) * 1.8); // top left
                UV UN3 = U2 + new UV(-metrosaPies(E) * 1.8, -metrosaPies(E) * 1.8); // top right


                puntosAcumulados.AddRange(new List<XYZ>
                {
                CARABUSCADA.Evaluate(UN2),
                CARABUSCADA.Evaluate(UN3),
            });

            }

            else if (valorSuperior == "3")
            {
                // Crear puntos en cada esquina de la cara con desplazamiento
                UV UN2 = new UV(U2.U, U1.V) + new UV(-metrosaPies(E) * 1.3, metrosaPies(E) * 1.3); // top left
                UV UN3 = U2 + new UV(-metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3); // top right
                UV UN5 = new UV(UN3.U, CENTRO.V);


                puntosAcumulados.AddRange(new List<XYZ>
                {
                CARABUSCADA.Evaluate(UN2),
                CARABUSCADA.Evaluate(UN3),
                CARABUSCADA.Evaluate(UN5),
                    });
            }

            else if (valorSuperior == "4")
            {
                // Crear puntos en cada esquina de la cara con desplazamiento
                UV UN2 = new UV(U2.U, U1.V) + new UV(-metrosaPies(E) * 1.3, metrosaPies(E) * 1.3); // top left
                UV UN3 = U2 + new UV(-metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3); // top right
                UV UN5 = new UV(UN3.U, CENTRO.V);

                // ? Distribuir 4 puntos uniformemente en la parte superior
                double anchoTotal = U2.U - U1.U; // Ancho total disponible
                double uCoordSuperior = U2.U - metrosaPies(E) * 1.3; // Coordenada Y fija para la parte superior

                UV UN_PUNTO2 = new UV(U1.U + anchoTotal * 0.375, uCoordSuperior); // 3/8 del ancho  
                UV UN_PUNTO3 = new UV(U1.U + anchoTotal * 0.625, uCoordSuperior); // 5/8 del ancho

                puntosAcumulados.AddRange(new List<XYZ>
                {
                    CARABUSCADA.Evaluate(UN2),
                    CARABUSCADA.Evaluate(UN3),
                    CARABUSCADA.Evaluate(UN_PUNTO2),
                    CARABUSCADA.Evaluate(UN_PUNTO3),
                });

                }

                if (valorInferior == "2")
            {
                // Crear puntos en cada esquina de la cara con desplazamiento
                UV UN1 = U1 + new UV(metrosaPies(E) * 1.8, metrosaPies(E) * 1.8); // bottom left
                UV UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E) * 1.8, -metrosaPies(E) * 1.8); // bottom right


                puntosAcumulados.AddRange(new List<XYZ>
                {
                CARABUSCADA.Evaluate(UN1),
                CARABUSCADA.Evaluate(UN4),
            });

            }

            else if (valorInferior == "3")
            {
                // Crear puntos en cada esquina de la cara con desplazamiento
                UV UN1 = U1 + new UV(metrosaPies(E) * 1.3, metrosaPies(E) * 1.3); // bottom left
                UV UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3); // bottom right
                UV UN5 = new UV(UN1.U, CENTRO.V);


                puntosAcumulados.AddRange(new List<XYZ>
                {
                CARABUSCADA.Evaluate(UN1),
                CARABUSCADA.Evaluate(UN4),
                CARABUSCADA.Evaluate(UN5),
                });

            }

            if (valorCentral == "0")
            {
               
                puntosAcumulados.AddRange(new List<XYZ>
                {
            });

            }

            else if (valorCentral == "2")
            {
                // Crear puntos en cada esquina de la cara con desplazamiento
                UV UN1 = U1 + new UV(metrosaPies(E) * 1.3, metrosaPies(E) * 1.3); // bottom left
                UV UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3); // bottom right

                UV UN_CENTRO_IZQ = new UV(CENTRO.V, U1.U)+ new UV(metrosaPies(E) * 1.3, metrosaPies(E) * 1.3);
                UV UN_CENTRO_DER =new UV(CENTRO.V, U2.V) + new UV(metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3);


                puntosAcumulados.AddRange(new List<XYZ>
                {
                CARABUSCADA.Evaluate(UN_CENTRO_IZQ),
                CARABUSCADA.Evaluate(UN_CENTRO_DER),
            });

            }
            /*
            else if (valorInferior == "4")
            {
                // Crear puntos en cada esquina de la cara con desplazamiento
                UV UN1 = U1 + new UV(metrosaPies(E) * 1.3, metrosaPies(E) * 1.3); // bottom left
                UV UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3); // bottom right
                UV UN5 = new UV(CENTRO.U, UN4.V);


                puntosAcumulados.AddRange(new List<XYZ>
                {
                CARABUSCADA.Evaluate(UN1),
                CARABUSCADA.Evaluate(UN4),
                CARABUSCADA.Evaluate(UN5),
                });

            }*/

            if (puntosAcumulados.Count == 0)
            {
                UV DEFAULT_UN1 = U1 + new UV(metrosaPies(E) * 1.3, metrosaPies(E) * 1.3);
                UV DEFAULT_UN2 = new UV(U2.U, U1.V) + new UV(-metrosaPies(E) * 1.3, metrosaPies(E) * 1.3);
                UV DEFAULT_UN3 = U2 + new UV(-metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3);
                UV DEFAULT_UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3);

                puntosAcumulados.AddRange(new List<XYZ>
        {
            CARABUSCADA.Evaluate(DEFAULT_UN1),
            CARABUSCADA.Evaluate(DEFAULT_UN2),
            CARABUSCADA.Evaluate(DEFAULT_UN3),
            CARABUSCADA.Evaluate(DEFAULT_UN4)
        });
            }

            // ? UN SOLO return al final con todos los puntos acumulados
            return puntosAcumulados;
        }
        



        public List<XYZ> GETPUNTOSESTRIBOSVIGAS(Element col, out XYZ VECTORNORMAL)


        {
            Options OPT = new Options();
            OPT.ComputeReferences = false;
            OPT.DetailLevel = ViewDetailLevel.Coarse;
            OPT.IncludeNonVisibleObjects = false;

            Face CARABUSCADA = null;
            VECTORNORMAL = XYZ.Zero; // Inicializar con un valor por defecto para evitar errores

            GeometryElement GEO = col.get_Geometry(OPT);
            if (GEO == null)
            {
                // Manejar el caso de geometría nula
                return new List<XYZ>();
            }

            List<Tuple<double, Face>> LATERALFACES = new List<Tuple<double, Face>>();

            // Buscar todas las caras laterales
            foreach (GeometryObject OBJ in GEO)
            {
                Solid SOLD = OBJ as Solid;
                if (SOLD != null && SOLD.Volume > 0.0)
                {
                    if (SOLD.Faces.Size > 0)
                    {
                        foreach (Face FACE in SOLD.Faces)
                        {
                            // Verificar que sea una cara planar
                            PlanarFace planarFace = FACE as PlanarFace;
                            if (planarFace != null)
                            {
                                XYZ VFACE = planarFace.FaceNormal.Normalize();

                                // Continuar si la cara es horizontal (excluir caras superior e inferior)
                                if (VFACE.IsAlmostEqualTo(-XYZ.BasisZ, 0.01) ||
                                    VFACE.IsAlmostEqualTo(XYZ.BasisZ, 0.01))
                                    continue;

                                LATERALFACES.Add(new Tuple<double, Face>(FACE.Area, FACE));
                            }
                        }
                    }
                }
            }

            // Verificar si se encontraron caras laterales
            if (LATERALFACES.Count == 0)
            {
                return new List<XYZ>();
            }

            // Ordenar las caras por área para encontrar las laterales
            LATERALFACES = LATERALFACES.OrderBy(x => x.Item1).ToList();
            CARABUSCADA = LATERALFACES[0].Item2;

            // Obtener el contorno de la cara
            BoundingBoxUV BBOXUV = CARABUSCADA.GetBoundingBox();
            UV U1 = BBOXUV.Min;
            UV U2 = BBOXUV.Max;



            // Crear puntos en cada esquina de la cara con desplazamiento
            UV UN1 = U1 + new UV(metrosaPies(E), metrosaPies(E)); // bottom left
            UV UN2 = new UV(U2.U, U1.V) + new UV(-metrosaPies(E), metrosaPies(E)); // top left
            UV UN3 = U2 + new UV(-metrosaPies(E), -metrosaPies(E)); // top right
            UV UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E), -metrosaPies(E)); // bottom right

            List<XYZ> PTS =
            [
                CARABUSCADA.Evaluate(UN1),
                CARABUSCADA.Evaluate(UN2),
                CARABUSCADA.Evaluate(UN3),
                CARABUSCADA.Evaluate(UN4),
                // Agregar puntos adicionales para refuerzo positivo
                CARABUSCADA.Evaluate((UN1 + UN2) / 2),
                CARABUSCADA.Evaluate((UN3 + UN4) / 2),
                CARABUSCADA.Evaluate((UN1 + UN4) / 2),
                CARABUSCADA.Evaluate((UN2 + UN3) / 2),
            ];

            // Obtener el vector normal
            VECTORNORMAL = (CARABUSCADA as PlanarFace).FaceNormal;

            return PTS;
        }


        public List<XYZ> GETPUNTOSVIGAST(Element col, out XYZ VECTORNORMAL)


        {
            Options OPT = new Options();
            OPT.ComputeReferences = false;
            OPT.DetailLevel = ViewDetailLevel.Coarse;
            OPT.IncludeNonVisibleObjects = false;

            Face CARABUSCADA = null;
            VECTORNORMAL = XYZ.Zero; // Inicializar con un valor por defecto para evitar errores

            GeometryElement GEO = col.get_Geometry(OPT);
            if (GEO == null)
            {
                // Manejar el caso de geometría nula
                return new List<XYZ>();
            }

            List<Tuple<double, Face>> LATERALFACES = new List<Tuple<double, Face>>();

            // Buscar todas las caras laterales
            foreach (GeometryObject OBJ in GEO)
            {
                Solid SOLD = OBJ as Solid;
                if (SOLD != null && SOLD.Volume > 0.0)
                {
                    if (SOLD.Faces.Size > 0)
                    {
                        foreach (Face FACE in SOLD.Faces)
                        {
                            // Verificar que sea una cara planar
                            PlanarFace planarFace = FACE as PlanarFace;
                            if (planarFace != null)
                            {
                                XYZ VFACE = planarFace.FaceNormal.Normalize();

                                // Continuar si la cara es horizontal (excluir caras superior e inferior)
                                if (VFACE.IsAlmostEqualTo(-XYZ.BasisZ, 0.01) ||
                                    VFACE.IsAlmostEqualTo(XYZ.BasisZ, 0.01))
                                    continue;

                                LATERALFACES.Add(new Tuple<double, Face>(FACE.Area, FACE));
                            }
                        }
                    }
                }
            }

            // Verificar si se encontraron caras laterales
            if (LATERALFACES.Count == 0)
            {
                return new List<XYZ>();
            }

            // Ordenar las caras por área para encontrar las laterales
            LATERALFACES = LATERALFACES.OrderBy(x => x.Item1).ToList();
            CARABUSCADA = LATERALFACES[0].Item2;

            // Obtener el contorno de la cara
            BoundingBoxUV BBOXUV = CARABUSCADA.GetBoundingBox();
            UV U1 = BBOXUV.Min;
            UV U2 = BBOXUV.Max;



            // Crear puntos en cada esquina de la cara con desplazamiento
            UV UN1 = U1 + new UV(metrosaPies(E) * 1.3, metrosaPies(E) * 1.3+ metrosaPies(alavigaT)); // bottom left
            UV UN2 = new UV(U2.U, U1.V) + new UV(-metrosaPies(E) * 1.3, metrosaPies(E) * 1.3+ metrosaPies(alavigaT)); // top left
            UV UN3 = U2 + new UV(-metrosaPies(E) * 1.3, -metrosaPies(E) * 1.3 - metrosaPies(alavigaT)); // top right
            UV UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E) * 1.3 , -metrosaPies(E) * 1.3 - metrosaPies(alavigaT)); // bottom right


            List<XYZ> PTS =
            [
                CARABUSCADA.Evaluate(UN1),
                CARABUSCADA.Evaluate(UN2),
                CARABUSCADA.Evaluate(UN3),
                CARABUSCADA.Evaluate(UN4),
                // Agregar puntos adicionales para refuerzo positivo
                CARABUSCADA.Evaluate((UN1 + UN2) / 2),
                CARABUSCADA.Evaluate((UN3 + UN4) / 2),
                CARABUSCADA.Evaluate((UN1 + UN4) / 2),
                CARABUSCADA.Evaluate((UN2 + UN3) / 2),
            ];

            // Obtener el vector normal
            VECTORNORMAL = (CARABUSCADA as PlanarFace).FaceNormal;

            return PTS;
        }


        public List<XYZ> GETPUNTOSESTRIBOSVIGAST(Element col, out XYZ VECTORNORMAL)


        {
            Options OPT = new Options();
            OPT.ComputeReferences = false;
            OPT.DetailLevel = ViewDetailLevel.Coarse;
            OPT.IncludeNonVisibleObjects = false;

            Face CARABUSCADA = null;
            VECTORNORMAL = XYZ.Zero; // Inicializar con un valor por defecto para evitar errores

            GeometryElement GEO = col.get_Geometry(OPT);
            if (GEO == null)
            {
                // Manejar el caso de geometría nula
                return new List<XYZ>();
            }

            List<Tuple<double, Face>> LATERALFACES = new List<Tuple<double, Face>>();

            // Buscar todas las caras laterales
            foreach (GeometryObject OBJ in GEO)
            {
                Solid SOLD = OBJ as Solid;
                if (SOLD != null && SOLD.Volume > 0.0)
                {
                    if (SOLD.Faces.Size > 0)
                    {
                        foreach (Face FACE in SOLD.Faces)
                        {
                            // Verificar que sea una cara planar
                            PlanarFace planarFace = FACE as PlanarFace;
                            if (planarFace != null)
                            {
                                XYZ VFACE = planarFace.FaceNormal.Normalize();

                                // Continuar si la cara es horizontal (excluir caras superior e inferior)
                                if (VFACE.IsAlmostEqualTo(-XYZ.BasisZ, 0.01) ||
                                    VFACE.IsAlmostEqualTo(XYZ.BasisZ, 0.01))
                                    continue;

                                LATERALFACES.Add(new Tuple<double, Face>(FACE.Area, FACE));
                            }
                        }
                    }
                }
            }

            // Verificar si se encontraron caras laterales
            if (LATERALFACES.Count == 0)
            {
                return new List<XYZ>();
            }

            // Ordenar las caras por área para encontrar las laterales
            LATERALFACES = LATERALFACES.OrderBy(x => x.Item1).ToList();
            CARABUSCADA = LATERALFACES[0].Item2;

            // Obtener el contorno de la cara
            BoundingBoxUV BBOXUV = CARABUSCADA.GetBoundingBox();
            UV U1 = BBOXUV.Min;
            UV U2 = BBOXUV.Max;



            // Crear puntos en cada esquina de la cara con desplazamiento
            UV UN1 = U1 + new UV(metrosaPies(E), metrosaPies(E) + metrosaPies(alavigaT) ); // bottom left
            UV UN2 = new UV(U2.U, U1.V) + new UV(-metrosaPies(E), metrosaPies(E) + metrosaPies(alavigaT) ); // top left
            UV UN3 = U2 + new UV(-metrosaPies(E), -metrosaPies(E) - metrosaPies(alavigaT)); // top right
            UV UN4 = new UV(U1.U, U2.V) + new UV(metrosaPies(E), -metrosaPies(E) - metrosaPies(alavigaT)); // bottom right

            List<XYZ> PTS =
            [
                CARABUSCADA.Evaluate(UN1),
                CARABUSCADA.Evaluate(UN2),
                CARABUSCADA.Evaluate(UN3),
                CARABUSCADA.Evaluate(UN4),
                // Agregar puntos adicionales para refuerzo positivo
                CARABUSCADA.Evaluate((UN1 + UN2) / 2),
                CARABUSCADA.Evaluate((UN3 + UN4) / 2),
                CARABUSCADA.Evaluate((UN1 + UN4) / 2),
                CARABUSCADA.Evaluate((UN2 + UN3) / 2),
            ];

            // Obtener el vector normal
            VECTORNORMAL = (CARABUSCADA as PlanarFace).FaceNormal;

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

        public bool esvigaT (Element element)
        {
            if (element == null) return false;
            
            // Verificar por nombre del elemento
            if (element.Name.ToLower().Contains("vigat") ||
                element.Name.ToLower().Contains("viga t") ||
                element.Name.ToLower().Contains("viga_t"))
            {
                return true;
            }

            //Verificar por nombre de familia
            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null)
                {
                if (
                    familyInstance.Symbol.FamilyName.ToLower().Contains("vigat") ||
                    familyInstance.Symbol.FamilyName.ToLower().Contains("viga t") ||
                    familyInstance.Symbol.FamilyName.ToLower().Contains("viga_t") ||
                    familyInstance.Symbol.FamilyName.ToLower().Contains("VigaT")
                    )
                {
                    return true;
                }
            }


            // Para casos donde la forma es una T pero no tiene un nombre que lo identifica
            // podrías agregar detección por geometría, pero eso requeriría análisis más complejo

            return false;
        }

       public double metrosaPies (double metros)
        {
            // Conversión de metros a pies
            return metros * 3.28084;

        }


        // Filtro mejorado para vigas
        public class FiltroDeViga : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (element == null) return false;

                // Verificar la categoría principal para vigas estructurales
                if (element.Category != null &&
                    element.Category.Id.Value == (int)BuiltInCategory.OST_StructuralFraming)
                {
                    return true;
                }

                // Verificar si es una instancia de familia con tipo estructural de viga
                FamilyInstance familyInstance = element as FamilyInstance;
                if (familyInstance != null && familyInstance.StructuralType == StructuralType.Beam)
                {
                    return true;
                }

                // Verificar por nombre (para elementos personalizados)
                if (element.Name.ToLower().Contains("viga") ||
                    element.Name.ToLower().Contains("beam"))
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
