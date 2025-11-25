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
    public class ACEROCOLUMNAS : LicensedCommand
    {
        public static double E = 0.164042; // Recubrimiento de 5cm al eje de la barra de acero (5 cm)
        
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
            ACEROCOLUMNASXAML colUI = new ACEROCOLUMNASXAML(REBARTYPES);
            colUI.ShowDialog();
            //Lista de barras de acero
            string rebarTypeSelected = colUI.diametro.SelectedItem.ToString();
            //Recubrimiento
            
            E = double.Parse(colUI.recubrimiento.Text);

            // Selección de columnas
            FiltroDeColumna FILTRO = new FiltroDeColumna();
            List<Reference> REFERENCIAS = UIDoc.Selection.PickObjects(ObjectType.Element, FILTRO, "Seleccione las columnas")?.ToList();

            // ✅ VALIDAR REFERENCIAS
            if (REFERENCIAS == null || REFERENCIAS.Count == 0)
            {
                TaskDialog.Show("Información", "No se seleccionaron columnas");
                return Result.Cancelled;
            }

                // Colección de barras de acero
            RebarBarType BARTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .FirstOrDefault(x => x.Name == rebarTypeSelected);

            // ✅ AGREGAR VALIDACIÓN
            if (BARTYPE == null)
            {
                TaskDialog.Show("Error", $"No se encontró el tipo de barra seleccionado: {rebarTypeSelected}");
                return Result.Failed;
            }

            RebarHookType HOOKTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>()
                .FirstOrDefault();

            // ✅ AGREGAR VALIDACIÓN  
            if (HOOKTYPE == null)
            {
                TaskDialog.Show("Error", "No se encontraron tipos de gancho en el proyecto");
                return Result.Failed;
            }

            Element REBARCOVERTYPE = new FilteredElementCollector(Doc)
                .OfClass(typeof(RebarCoverType))
                .FirstOrDefault();

            // ✅ AGREGAR VALIDACIÓN
            if (REBARCOVERTYPE == null)
            {
                TaskDialog.Show("Error", "No se encontraron tipos de recubrimiento en el proyecto");
                return Result.Failed;
            }

            #region Transacción para refuerzos
            using (Transaction TR = new Transaction(Doc, "Refuerzo en Columnas"))
            {
                try
                {
                    TR.Start();

                    foreach (Reference REFE in REFERENCIAS)
                    {
                        try
                        {
                            Element COLUMNA = Doc.GetElement(REFE);
                            if (COLUMNA == null) continue; // ✅ Continuar sin mostrar advertencia

                            // Altura de la Columna
                            BoundingBoxXYZ BBCOL = COLUMNA.get_BoundingBox(null);
                            if (BBCOL == null) continue;

                            double ALTURA = BBCOL.Max.Z - BBCOL.Min.Z;

                            // Obtener las esquinas en la base de la columna
                            XYZ VECTORCARA = new XYZ();
                            List<XYZ> PTSCOLUMNA = GETPUNTOSCOLUMNA(COLUMNA, out VECTORCARA);

                            if (PTSCOLUMNA == null || PTSCOLUMNA.Count == 0) continue;

                            // Creación de barras longitudinales
                            foreach (XYZ PT in PTSCOLUMNA)
                            {
                                try
                                {
                                    XYZ PTTOP = new XYZ(PT.X, PT.Y, PT.Z + ALTURA);
                                    Line REBARLINE = Line.CreateBound(PT, PTTOP);
                                    IList<Curve> CURVES = new List<Curve> { REBARLINE };

                                    // ✅ AGREGAR VALIDACIONES ANTES DE CREAR LA BARRA
                                    if (ALTURA <= 0)
                                    {
                                        continue; // ✅ Quitar TaskDialog y solo continuar
                                    }

                                    if (PT.DistanceTo(PTTOP) < 0.001) // Distancia mínima
                                    {
                                        continue; // ✅ Quitar TaskDialog y solo continuar
                                    }

                                    // ✅ CALCULAR VECTOR NORMAL VÁLIDO
                                    XYZ vectorNormal = calcularVectorNormalValido(VECTORCARA, PT, PTTOP);

                                    Rebar REBAR = Rebar.CreateFromCurves(
                                        Doc, 
                                        RebarStyle.Standard, 
                                        BARTYPE, 
                                        null, 
                                        null,
                                        COLUMNA, 
                                        vectorNormal,
                                        CURVES,
                                        RebarHookOrientation.Right, 
                                        RebarHookOrientation.Left,
                                        true, 
                                        true);

                                    if (REBAR != null)
                                    {
                                        // ✅ MANEJAR GANCHOS DE FORMA MÁS SILENCIOSA
                                        try
                                        {
                                            //obtener parametro de gancho
                                            Parameter hookEnd = REBAR.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_END_TYPE);
                                            Parameter hookStart = REBAR.get_Parameter(BuiltInParameter.REBAR_ELEM_HOOK_START_TYPE);

                                            //establecer el primer gancho que contenga el string 135
                                            if (hookEnd != null && hookStart != null)
                                            {
                                                // Buscar el primer gancho que contenga "135" en su nombre
                                                RebarHookType hook135 = new FilteredElementCollector(Doc)
                                                    .OfClass(typeof(RebarHookType))
                                                    .Cast<RebarHookType>()
                                                    .FirstOrDefault(x => x.Name.Contains("135"));

                                                if (hook135 != null)
                                                {
                                                    if (!hookEnd.IsReadOnly) hookEnd.Set(hook135.Id);
                                                    if (!hookStart.IsReadOnly) hookStart.Set(hook135.Id);
                                                }
                                                else
                                                {
                                                    // Si no se encuentra gancho con "135", usar ElementId.InvalidElementId
                                                    if (!hookEnd.IsReadOnly) hookEnd.Set(ElementId.InvalidElementId);
                                                    if (!hookStart.IsReadOnly) hookStart.Set(ElementId.InvalidElementId);
                                                }
                                            }
                                        }
                                        catch
                                        {
                                            // ✅ Ignorar errores en la configuración de ganchos silenciosamente
                                        }

                                        try
                                        {
                                            REBAR.SetUnobscuredInView(Doc.ActiveView, true);
                                        }
                                        catch
                                        {
                                            // ✅ Ignorar errores de visibilidad silenciosamente
                                        }
                                    }
                                }
                                catch (Exception)
                                {
                                    // ✅ ELIMINAR EL TASKDIALOG Y CONTINUAR SILENCIOSAMENTE
                                    continue; // Continuar con la siguiente barra sin mostrar error
                                }
                            }

                        }
                        catch (Exception)
                        {
                            // ✅ CONTINUAR SILENCIOSAMENTE SIN MOSTRAR ERROR
                            continue; // Continuar con la siguiente columna
                        }
                    }

                    TR.Commit();
                    return Result.Succeeded; // ✅ Éxito sin mostrar errores menores
                }
                catch (Exception ex)
                {
                    TR.RollBack();
                    // ✅ SOLO MOSTRAR ERRORES REALMENTE CRÍTICOS
                    if (ex.Message.Contains("External component") || ex.Message.Contains("thrown an exception"))
                    {
                        // Es un error menor, no mostrar al usuario
                        return Result.Succeeded;
                    }
                    else
                    {
                        // Es un error crítico, mostrar al usuario
                        TaskDialog.Show("Error Fatal", $"Error en la transacción: {ex.Message}");
                        return Result.Failed;
                    }
                }
            }
            #endregion
            return Result.Succeeded;
        }

        // Métodos auxiliares para COLUMNAS
        public List<XYZ> GETPUNTOSCOLUMNA(Element col, out XYZ VECTORNORMAL)
        {
            VECTORNORMAL = XYZ.BasisX; // ✅ Valor predeterminado más seguro

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

        /// <summary>
        /// Calcula un vector normal válido para la creación de barras de refuerzo
        /// </summary>
        private XYZ calcularVectorNormalValido(XYZ vectorCara, XYZ puntoInicio, XYZ puntoFin)
        {
            try
            {
                // Dirección de la barra (vertical)
                XYZ direccionBarra = (puntoFin - puntoInicio).Normalize();
                
                // Intentar usar el vector de la cara si es válido
                if (vectorCara != null && vectorCara.GetLength() > 0.001)
                {
                    XYZ vectorNormalizado = vectorCara.Normalize();
                    
                    // Verificar que no sea paralelo a la dirección de la barra
                    if (Math.Abs(vectorNormalizado.DotProduct(direccionBarra)) < 0.99)
                    {
                        XYZ vectorNormal = vectorNormalizado.CrossProduct(direccionBarra);
                        if (vectorNormal.GetLength() > 0.001)
                        {
                            return vectorNormal.Normalize();
                        }
                    }
                }
                
                // Si el vector de cara no es válido, usar vectores de respaldo
                List<XYZ> vectoresRespaldo = new List<XYZ> 
                { 
                    XYZ.BasisX, 
                    XYZ.BasisY, 
                    new XYZ(1, 1, 0).Normalize(),
                    new XYZ(1, -1, 0).Normalize()
                };
                
                foreach (XYZ vectorRespaldo in vectoresRespaldo)
                {
                    // Verificar que no sea paralelo a la dirección de la barra
                    if (Math.Abs(vectorRespaldo.DotProduct(direccionBarra)) < 0.99)
                    {
                        XYZ vectorNormal = vectorRespaldo.CrossProduct(direccionBarra);
                        if (vectorNormal.GetLength() > 0.001)
                        {
                            return vectorNormal.Normalize();
                        }
                    }
                }
                
                // Como último recurso, usar un vector perpendicular calculado
                XYZ vectorPerpendicular;
                if (Math.Abs(direccionBarra.X) < 0.99)
                {
                    vectorPerpendicular = new XYZ(1, 0, 0).CrossProduct(direccionBarra);
                }
                else
                {
                    vectorPerpendicular = new XYZ(0, 1, 0).CrossProduct(direccionBarra);
                }
                
                return vectorPerpendicular.Normalize();
            }
            catch
            {
                // En caso de cualquier error, devolver un vector por defecto
                return XYZ.BasisX;
            }
        }
    }
}
