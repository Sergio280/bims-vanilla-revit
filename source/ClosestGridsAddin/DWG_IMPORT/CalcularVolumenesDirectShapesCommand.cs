using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClosestGridsAddinVANILLA.DWG_IMPORT
{
    /// <summary>
    /// Comando para calcular volúmenes de DirectShapes y asignar identificadores por volumen
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CalcularVolumenesDirectShapesCommand : LicensedCommand
    {
        protected override Result ExecuteCommand(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                StringBuilder log = new StringBuilder();
                log.AppendLine("═══════════════════════════════════════════════════════════");
                log.AppendLine("  CALCULADOR DE VOLÚMENES PARA DIRECTSHAPES");
                log.AppendLine("═══════════════════════════════════════════════════════════\n");

                // PASO 1: Obtener todos los DirectShapes del documento
                log.AppendLine("PASO 1: Obteniendo DirectShapes del documento...");

                FilteredElementCollector collector = new FilteredElementCollector(doc);
                List<DirectShape> directShapes = collector
                    .OfClass(typeof(DirectShape))
                    .Cast<DirectShape>()
                    .ToList();

                log.AppendLine($"  ✓ Total DirectShapes encontrados: {directShapes.Count}\n");

                if (directShapes.Count == 0)
                {
                    TaskDialog.Show("Aviso", "No se encontraron DirectShapes en el documento.");
                    return Result.Cancelled;
                }

                // PASO 2: Calcular volúmenes y agrupar por tolerancia
                log.AppendLine("PASO 2: Calculando volúmenes y agrupando elementos similares...");

                Dictionary<string, List<DirectShape>> gruposPorVolumen = AgruparPorVolumen(directShapes, log);

                log.AppendLine($"  ✓ Grupos únicos de volumen (con tolerancia): {gruposPorVolumen.Count}\n");

                // PASO 3: Asignar parámetros en una transacción
                log.AppendLine("PASO 3: Asignando volúmenes e identificadores a parámetros...");

                using (Transaction trans = new Transaction(doc, "Calcular Volúmenes DirectShapes"))
                {
                    trans.Start();

                    int elementosActualizados = AsignarParametrosVolumen(gruposPorVolumen, log);

                    trans.Commit();

                    log.AppendLine($"\n✓ Total elementos actualizados: {elementosActualizados}");
                }

                // Mostrar log
                log.AppendLine("\n═══════════════════════════════════════════════════════════");
                log.AppendLine("  PROCESO COMPLETADO EXITOSAMENTE");
                log.AppendLine("═══════════════════════════════════════════════════════════");

                TaskDialog dialog = new TaskDialog("Cálculo de Volúmenes Completado");
                dialog.MainInstruction = $"Se actualizaron {directShapes.Count} DirectShapes";
                dialog.MainContent = $"Se identificaron {gruposPorVolumen.Count} grupos únicos por volumen.\n\n" +
                                    "Los parámetros actualizados:\n" +
                                    "• Comentarios: Volumen en m³\n" +
                                    "• Mark: ID del grupo de volumen";
                dialog.ExpandedContent = log.ToString();
                dialog.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}";
                TaskDialog.Show("Error", $"Error al calcular volúmenes:\n\n{ex.Message}\n\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Agrupa DirectShapes por volumen con una tolerancia pequeña
        /// </summary>
        private Dictionary<string, List<DirectShape>> AgruparPorVolumen(List<DirectShape> directShapes, StringBuilder log)
        {
            // Tolerancia: 0.0001 m³ (0.1 litros)
            const double TOLERANCIA_M3 = 0.0001;

            Dictionary<string, List<DirectShape>> grupos = new Dictionary<string, List<DirectShape>>();
            int sinGeometria = 0;

            foreach (DirectShape ds in directShapes)
            {
                try
                {
                    // Obtener geometría del DirectShape
                    GeometryElement geoElement = ds.get_Geometry(new Options());
                    if (geoElement == null)
                    {
                        sinGeometria++;
                        continue;
                    }

                    double volumenTotalPies3 = 0.0;

                    // Calcular volumen sumando todos los Solids
                    foreach (GeometryObject geoObj in geoElement)
                    {
                        if (geoObj is Solid solid)
                        {
                            volumenTotalPies3 += solid.Volume;
                        }
                        else if (geoObj is GeometryInstance geoInstance)
                        {
                            GeometryElement instanceGeo = geoInstance.GetInstanceGeometry();
                            if (instanceGeo != null)
                            {
                                foreach (GeometryObject instanceObj in instanceGeo)
                                {
                                    if (instanceObj is Solid instanceSolid)
                                    {
                                        volumenTotalPies3 += instanceSolid.Volume;
                                    }
                                }
                            }
                        }
                    }

                    // Convertir pies³ a m³ (1 pie³ = 0.0283168 m³)
                    double volumenM3 = volumenTotalPies3 * 0.0283168;

                    // Redondear a 4 decimales para agrupar
                    double volumenRedondeado = Math.Round(volumenM3, 4);

                    // Buscar grupo existente con volumen similar (dentro de tolerancia)
                    string claveGrupo = null;
                    foreach (string clave in grupos.Keys)
                    {
                        double volumenGrupo = double.Parse(clave);
                        if (Math.Abs(volumenGrupo - volumenRedondeado) <= TOLERANCIA_M3)
                        {
                            claveGrupo = clave;
                            break;
                        }
                    }

                    // Si no existe grupo, crear uno nuevo
                    if (claveGrupo == null)
                    {
                        claveGrupo = volumenRedondeado.ToString("F4");
                        grupos[claveGrupo] = new List<DirectShape>();
                    }

                    // Agregar DirectShape al grupo
                    grupos[claveGrupo].Add(ds);
                }
                catch (Exception ex)
                {
                    log.AppendLine($"  ⚠ Error procesando DirectShape {ds.Id}: {ex.Message}");
                }
            }

            if (sinGeometria > 0)
            {
                log.AppendLine($"  ⚠ {sinGeometria} DirectShapes sin geometría fueron ignorados");
            }

            return grupos;
        }

        /// <summary>
        /// Asigna parámetros de volumen e ID a cada DirectShape
        /// </summary>
        private int AsignarParametrosVolumen(Dictionary<string, List<DirectShape>> gruposPorVolumen, StringBuilder log)
        {
            int elementosActualizados = 0;
            int grupoID = 1;

            // Ordenar grupos por volumen (menor a mayor)
            var gruposOrdenados = gruposPorVolumen
                .OrderBy(kvp => double.Parse(kvp.Key))
                .ToList();

            foreach (var grupo in gruposOrdenados)
            {
                double volumenM3 = double.Parse(grupo.Key);
                List<DirectShape> elementos = grupo.Value;

                string idGrupo = $"VOL_{grupoID:D4}";

                log.AppendLine($"  Grupo {idGrupo}: {volumenM3:F4} m³ ({elementos.Count} elementos)");

                foreach (DirectShape ds in elementos)
                {
                    try
                    {
                        // Asignar volumen a parámetro "Comentarios"
                        Parameter paramComentarios = ds.LookupParameter("Comentarios");
                        if (paramComentarios != null && !paramComentarios.IsReadOnly)
                        {
                            string textoVolumen = $"{volumenM3:F4}";
                            paramComentarios.Set(textoVolumen);
                        }

                        // Asignar ID de grupo a parámetro "Mark" (Marca)
                        Parameter paramMark = ds.LookupParameter("Mark");
                        if (paramMark != null && !paramMark.IsReadOnly)
                        {
                            paramMark.Set(idGrupo);
                        }

                        elementosActualizados++;
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine($"    ✗ Error actualizando elemento {ds.Id}: {ex.Message}");
                    }
                }

                grupoID++;
            }

            return elementosActualizados;
        }
    }
}
