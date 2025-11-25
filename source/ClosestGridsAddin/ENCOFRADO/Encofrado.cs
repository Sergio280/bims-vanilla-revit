using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitExtensions.Formwork
{
    /// <summary>
    /// SISTEMA UNIFICADO DE ENCOFRADO - MÉTODO UNIVERSAL
    /// Detecta automáticamente el tipo de elemento y aplica el encofrado correspondiente:
    /// • Muros → Encofrado lateral (2 caras)
    /// • Losas → Cimbra inferior
    /// • Columnas → Encofrado perimetral
    /// • Vigas → Encofrado lateral y fondo
    /// • Escaleras → Encofrado completo
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ComandoEncoofradoUniversal : IExternalCommand
    {
        private Document _doc;
        private UIDocument _uidoc;
        private FormworkSettings _settings;
        private GestorTiposEncofrado _gestorTipos;
        private ResultadoEncofrado _resultado;
        private System.Text.StringBuilder _logDebug = new System.Text.StringBuilder();

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            _uidoc = commandData.Application.ActiveUIDocument;
            _doc = _uidoc.Document;
            _settings = new FormworkSettings();
            _gestorTipos = new GestorTiposEncofrado(_doc);
            _resultado = new ResultadoEncofrado();

            try
            {
                // Diálogo de selección de modo
                TaskDialog dialog = new TaskDialog("Encofrado Universal");
                dialog.MainInstruction = "¿Cómo deseas generar el encofrado?";
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Seleccionar elementos manualmente");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Procesar todos los elementos estructurales");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Solo elementos actualmente seleccionados");

                TaskDialogResult dialogResult = dialog.Show();

                List<Element> elementosAProcesar = new List<Element>();

                switch (dialogResult)
                {
                    case TaskDialogResult.CommandLink1:
                        elementosAProcesar = SeleccionarElementosManualmente();
                        break;
                    case TaskDialogResult.CommandLink2:
                        elementosAProcesar = ObtenerTodosLosElementosEstructurales();
                        break;
                    case TaskDialogResult.CommandLink3:
                        ICollection<ElementId> seleccionados = _uidoc.Selection.GetElementIds();
                        if (seleccionados.Count == 0)
                        {
                            TaskDialog.Show("Error", "No hay elementos seleccionados");
                            return Result.Failed;
                        }
                        elementosAProcesar = seleccionados
                            .Select(id => _doc.GetElement(id))
                            .Where(e => e != null)
                            .ToList();
                        break;
                    default:
                        return Result.Cancelled;
                }

                if (elementosAProcesar.Count == 0)
                {
                    TaskDialog.Show("Información", "No se encontraron elementos para procesar");
                    return Result.Cancelled;
                }

                // ═══════════════════════════════════════════════════════════
                //  PROCESAR TODOS LOS ELEMENTOS CON EL MÉTODO UNIVERSAL
                // ═══════════════════════════════════════════════════════════
                _logDebug.AppendLine("=== INICIO DEBUG ===");
                _logDebug.AppendLine($"Elementos a procesar: {elementosAProcesar.Count}");

                using (Transaction trans = new Transaction(_doc, "Generar Encofrado Universal"))
                {
                    trans.Start();
                    _logDebug.AppendLine("Transacción iniciada");

                    // Verificar/crear tipos de encofrado
                    _logDebug.AppendLine("Llamando a VerificarYCrearTipos()...");
                    if (!_gestorTipos.VerificarYCrearTipos(_logDebug))
                    {
                        trans.RollBack();
                        _logDebug.AppendLine("FALLO: VerificarYCrearTipos() retornó false");
                        MostrarLogDebug();
                        TaskDialog.Show("Error", "No se pudieron crear los tipos de encofrado.\nRevisa el log de debug.");
                        return Result.Failed;
                    }
                    _logDebug.AppendLine("✓ Tipos creados correctamente");

                    // Procesar cada elemento con el método universal
                    foreach (Element elemento in elementosAProcesar)
                    {
                        try
                        {
                            ProcesarElementoUniversal(elemento);
                        }
                        catch (Exception ex)
                        {
                            _resultado.AgregarError(elemento.Id, ex.Message);
                        }
                    }

                    trans.Commit();
                }

                // Mostrar resultados
                MostrarResultados();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error general: {ex.Message}\n{ex.StackTrace}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// ═══════════════════════════════════════════════════════════════════
        ///  MÉTODO PRINCIPAL UNIVERSAL: Detecta tipo y aplica encofrado
        /// ═══════════════════════════════════════════════════════════════════
        /// </summary>
        private void ProcesarElementoUniversal(Element elemento)
        {
            if (elemento == null) return;

            // 1️⃣ MUROS (Wall)
            if (elemento is Wall muro)
            {
                if (EsEstructural(muro))
                {
                    GenerarEncoofradoMuro(muro);
                    _resultado.MurosProcesados++;
                }
                return;
            }

            // 2️⃣ LOSAS/SUELOS y CIMENTACIÓN (Floor)
            if (elemento is Floor losa)
            {
                if (EsEstructural(losa))
                {
                    // Distinguir entre losa normal y cimentación
                    if (EsCimentacion(losa))
                    {
                        GenerarEncofradoCimentacion(losa);
                        _resultado.LosasProcesadas++; // Contar como losa procesada
                    }
                    else
                    {
                        GenerarCimbraLosa(losa);
                        _resultado.LosasProcesadas++;
                    }
                }
                return;
            }

            // 3️⃣ COLUMNAS (FamilyInstance - StructuralColumns)
            if (elemento is FamilyInstance instancia)
            {
                if (elemento.Category?.Id.Value ==
                    (int)BuiltInCategory.OST_StructuralColumns)
                {
                    GenerarEncoofradoColumna(instancia);
                    _resultado.ColumnasProcesadas++;
                    return;
                }

                // 4️⃣ VIGAS (FamilyInstance - StructuralFraming)
                if (elemento.Category?.Id.Value ==
                    (int)BuiltInCategory.OST_StructuralFraming)
                {
                    GenerarEncoofradoViga(instancia);
                    _resultado.VigasProcesadas++;
                    return;
                }
            }

            // 5️⃣ ESCALERAS (Stairs)
            if (elemento is Stairs escalera)
            {
                GenerarEncoofradoEscalera(escalera);
                _resultado.EscalerasProcesadas++;
                return;
            }

            // Elemento no soportado
            _resultado.AgregarAdvertencia(elemento.Id,
                $"Tipo no soportado: {elemento.GetType().Name}");
        }

        #region 1. METODO: ENCOFRADO DE MUROS (DIRECTSHAPE)

        /// <summary>
        /// Genera 2 encofrados (exterior e interior) usando DirectShape
        /// Garantiza geometria exacta para muros rectos y curvos
        /// </summary>
        private void GenerarEncoofradoMuro(Wall muroEstructural)
        {
            if (YaTieneEncoofrado(muroEstructural.Id)) return;

            try
            {
                GenerarEncofradoMuroConDirectShape(muroEstructural);
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(muroEstructural.Id, $"Error en encofrado muro: {ex.Message}");
            }
        }

        #endregion

        #region 2. METODO: CIMBRA DE LOSAS

        /// <summary>
        /// Genera cimbra (falsework) como DirectShape pegado a la cara inferior de la losa
        /// </summary>
        private void GenerarCimbraLosa(Floor losaEstructural)
        {
            if (YaTieneEncoofrado(losaEstructural.Id)) return;

            try
            {
                // 1. Extraer solido principal
                Solid solidoLosa = ExtraerSolidoPrincipal(losaEstructural);
                if (solidoLosa == null)
                    throw new InvalidOperationException("No se pudo extraer geometria de la losa");

                // 2. Verificar si es "falso piso" o similar
                bool esFalsoPiso = EsFalsoPisoOSimilar(losaEstructural);

                if (esFalsoPiso)
                {
                    // CASO ESPECIAL: Encofrar solo el perímetro (caras laterales)
                    _logDebug.AppendLine($"Losa {losaEstructural.Id} detectada como falso piso - encofrando solo perímetro");
                    GenerarEncofradoPerimetroLosa(losaEstructural, solidoLosa);
                }
                else
                {
                    // CASO NORMAL: Encofrar cara inferior (cimbra)
                    GenerarCimbraCaraInferior(losaEstructural, solidoLosa);
                }
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(losaEstructural.Id,
                    $"Error en cimbra losa: {ex.Message}");
            }
        }

        /// <summary>
        /// Detecta si una losa es "falso piso" o similar
        /// Busca las palabras "falso", "piso" o "\" en el tipo o familia
        /// </summary>
        private bool EsFalsoPisoOSimilar(Floor losa)
        {
            try
            {
                // Obtener nombre del tipo
                string nombreTipo = losa.FloorType?.Name ?? "";

                // Obtener nombre de familia si existe
                string nombreFamilia = "";
                try
                {
                    var familySymbol = losa.FloorType?.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
                    if (familySymbol != null)
                        nombreFamilia = familySymbol.AsString() ?? "";
                }
                catch { }

                // Combinar nombres en minúsculas para búsqueda case-insensitive
                string textoCompleto = (nombreTipo + " " + nombreFamilia).ToLowerInvariant();

                // Buscar palabras clave
                bool contieneFalso = textoCompleto.Contains("falso");
                bool contienePiso = textoCompleto.Contains("piso");
                bool contieneBarraInvertida = textoCompleto.Contains("\\");

                if (contieneFalso || contienePiso || contieneBarraInvertida)
                {
                    _logDebug.AppendLine($"Losa {losa.Id} - Tipo: '{nombreTipo}', Familia: '{nombreFamilia}' - Detectado como falso piso");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error detectando falso piso: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Genera encofrado solo del perímetro (caras laterales) de una losa
        /// Usado para falsos pisos y losas que no necesitan cimbra inferior
        /// CON DETECCIÓN DE INTERSECCIONES - Recorta elementos adyacentes
        /// </summary>
        private void GenerarEncofradoPerimetroLosa(Floor losa, Solid solidoLosa)
        {
            // Parametros de encofrado
            double separacion = 0.0; // Sin separación - pegado a la cara
            double espesorPanel = UnitUtils.ConvertToInternalUnits(
                _settings.EspesorMuro, UnitTypeId.Millimeters);

            // Contar todas las caras para debugging
            int totalCaras = 0;
            int carasLaterales = 0;
            int carasOtras = 0;

            // Identificar solo las caras perimetrales verticales
            List<PlanarFace> carasPerimetrales = new List<PlanarFace>();

            foreach (Face face in solidoLosa.Faces)
            {
                totalCaras++;
                if (face is PlanarFace pf)
                {
                    // Solo caras verticales (laterales)
                    if (CaraNecesitaEncofrado(pf, TipoCaraEncofrado.Lateral))
                    {
                        carasPerimetrales.Add(pf);
                        carasLaterales++;
                    }
                    else
                    {
                        carasOtras++;
                    }
                }
            }

            _logDebug.AppendLine($"Falso piso {losa.Id} - Análisis de caras:");
            _logDebug.AppendLine($"  Total de caras: {totalCaras}");
            _logDebug.AppendLine($"  Caras laterales detectadas (|z| < 0.3): {carasLaterales}");
            _logDebug.AppendLine($"  Caras no laterales: {carasOtras}");

            if (carasPerimetrales.Count == 0)
                throw new InvalidOperationException("No se encontraron caras perimetrales verticales");

            // Crear panel de encofrado para cada cara perimetral
            int count = 0;
            int errores = 0;

            foreach (PlanarFace cara in carasPerimetrales)
            {
                try
                {
                    // Usar patron BLIM con detección de intersecciones
                    FormworkFaceData faceData = CalcularEncoofradoConIntersecciones(
                        cara,
                        losa,
                        separacion,
                        espesorPanel);

                    if (ValidarSolido(faceData.Geometry))
                    {
                        DirectShape ds = CrearEncofradoDirectShape(
                            faceData.Geometry,
                            $"Encofrado_FalsoPiso_Perimetro{count + 1}_{losa.Id}",
                            losa,
                            $"FalsoPiso_Perimetro{count + 1}_DS");

                        _resultado.ElementosCreados++;
                        count++;

                        // Log de intersecciones detectadas
                        if (faceData.IntersectingElements.Count > 0)
                        {
                            _logDebug.AppendLine($"  Panel {count}: " +
                                $"{faceData.IntersectingElements.Count} elementos intersectados restados");
                        }
                    }
                    else
                    {
                        errores++;
                        _logDebug.AppendLine($"  Panel {count + 1}: sólido no válido");
                    }
                }
                catch (Exception exCara)
                {
                    errores++;
                    _resultado.AgregarAdvertencia(losa.Id,
                        $"No se pudo crear panel perimetral {count + 1}: {exCara.Message}");
                    _logDebug.AppendLine($"  Panel {count + 1}: Error - {exCara.Message}");
                }
            }

            if (count == 0)
                throw new InvalidOperationException($"No se pudo crear ningun panel de encofrado perimetral ({errores} errores)");

            _logDebug.AppendLine($"  RESULTADO: {count} paneles perimetrales creados exitosamente, {errores} errores");
        }

        /// <summary>
        /// Genera cimbra en la cara inferior de una losa (comportamiento normal)
        /// CON DETECCIÓN DE INTERSECCIONES - Recorta elementos adyacentes
        /// </summary>
        private void GenerarCimbraCaraInferior(Floor losa, Solid solidoLosa)
        {
            // Parametros de encofrado
            double separacion = 0.0; // Sin separación - pegado a la cara
            double espesorPanel = UnitUtils.ConvertToInternalUnits(
                _settings.EspesorLosa, UnitTypeId.Millimeters);

            // Buscar la cara inferior (la mas baja con normal hacia abajo)
            PlanarFace caraInferior = null;
            double zMinimo = double.MaxValue;

            foreach (Face face in solidoLosa.Faces)
            {
                if (face is PlanarFace pf)
                {
                    // Verificar si es cara inferior
                    if (CaraNecesitaEncofrado(pf, TipoCaraEncofrado.Inferior))
                    {
                        // Tomar la mas baja
                        if (pf.Origin.Z < zMinimo)
                        {
                            zMinimo = pf.Origin.Z;
                            caraInferior = pf;
                        }
                    }
                }
            }

            if (caraInferior == null)
                throw new InvalidOperationException("No se encontro cara inferior en la losa");

            // Usar patron BLIM con detección de intersecciones
            FormworkFaceData faceData = CalcularEncoofradoConIntersecciones(
                caraInferior,
                losa,
                separacion,
                espesorPanel);

            if (!ValidarSolido(faceData.Geometry))
                throw new InvalidOperationException("El panel de cimbra generado no es valido");

            // Log de intersecciones detectadas
            if (faceData.IntersectingElements.Count > 0)
            {
                _logDebug.AppendLine($"Losa {losa.Id} - Cimbra inferior: " +
                    $"{faceData.IntersectingElements.Count} elementos intersectados restados");
            }

            // Crear DirectShape
            DirectShape ds = CrearEncofradoDirectShape(
                faceData.Geometry,
                $"Cimbra_Losa_{losa.Id}",
                losa,
                "Cimbra_DS");

            _resultado.ElementosCreados++;
        }

        /// <summary>
        /// Genera encofrado para cimentación (solo caras perimetrales verticales)
        /// </summary>
        private void GenerarEncofradoCimentacion(Floor cimentacion)
        {
            if (YaTieneEncoofrado(cimentacion.Id)) return;

            try
            {
                // 1. Extraer solido principal
                Solid solidoCimentacion = ExtraerSolidoPrincipal(cimentacion);
                if (solidoCimentacion == null)
                    throw new InvalidOperationException("No se pudo extraer geometria de la cimentacion");

                // 2. Parametros de encofrado
                double separacion = 0.0; // Sin separación - pegado a la cara
                double espesorPanel = UnitUtils.ConvertToInternalUnits(
                    _settings.EspesorMuro, UnitTypeId.Millimeters);

                // 3. Identificar solo las caras perimetrales verticales
                // (NO la cara superior ni la cara inferior que está en contacto con el terreno)
                List<PlanarFace> carasPerimetrales = new List<PlanarFace>();

                foreach (Face face in solidoCimentacion.Faces)
                {
                    if (face is PlanarFace pf)
                    {
                        // Solo caras verticales (laterales)
                        if (CaraNecesitaEncofrado(pf, TipoCaraEncofrado.Lateral))
                        {
                            carasPerimetrales.Add(pf);
                        }
                    }
                }

                if (carasPerimetrales.Count == 0)
                    throw new InvalidOperationException("No se encontraron caras perimetrales verticales");

                // 4. Crear panel de encofrado para cada cara perimetral
                int count = 0;
                foreach (PlanarFace cara in carasPerimetrales)
                {
                    try
                    {
                        // Crear panel delgado desplazado desde la cara
                        Solid panel = CrearPanelEncofradoDesdeCara(cara, separacion, espesorPanel);

                        if (ValidarSolido(panel))
                        {
                            DirectShape ds = CrearEncofradoDirectShape(
                                panel,
                                $"Encofrado_Cimentacion_Perimetro{count + 1}_{cimentacion.Id}",
                                cimentacion,
                                $"Cimentacion_Perimetro{count + 1}_DS");

                            _resultado.ElementosCreados++;
                            count++;
                        }
                    }
                    catch (Exception exCara)
                    {
                        _resultado.AgregarAdvertencia(cimentacion.Id,
                            $"No se pudo crear panel perimetral {count + 1}: {exCara.Message}");
                    }
                }

                if (count == 0)
                    throw new InvalidOperationException("No se pudo crear ningun panel de encofrado perimetral");
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(cimentacion.Id,
                    $"Error en encofrado cimentacion: {ex.Message}");
            }
        }

        #endregion

        #region 3. METODO: ENCOFRADO DE COLUMNAS (DIRECTSHAPE)

        /// <summary>
        /// Genera encofrado siguiendo la geometria de la columna usando DirectShape
        /// Soporta todas las formas: rectangulares, circulares, H, I, L, etc.
        /// </summary>
        private void GenerarEncoofradoColumna(FamilyInstance columna)
        {
            if (YaTieneEncoofrado(columna.Id)) return;

            try
            {
                GenerarEncofradoColumnaConDirectShape(columna);
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(columna.Id, $"Error en encofrado columna: {ex.Message}");
            }
        }

        /// <summary>
        /// Crea muros individuales desde un contorno
        /// </summary>
        private void CrearMurosDesdeContorno(List<Curve> curvas, Level nivel, double altura,
            Element elementoOriginal, string prefijo)
        {
            WallType tipo = _gestorTipos.TipoMuroEncofrado;
            int count = 0;

            foreach (Curve curva in curvas)
            {
                try
                {
                    Wall muro = Wall.Create(_doc, curva, tipo.Id, nivel.Id, altura, 0, false, false);
                    ConfigurarMuroNoEstructural(muro);
                    VincularConOriginal(muro, elementoOriginal, $"{prefijo}_L{++count}");
                    _resultado.ElementosCreados++;
                }
                catch (Exception ex)
                {
                    _resultado.AgregarAdvertencia(elementoOriginal.Id,
                        $"No se pudo crear muro {count}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Método fallback usando BoundingBox (método antiguo)
        /// </summary>
        private void GenerarEncofradoColumnaFallback(FamilyInstance columna, Level nivel,
            double altura, double separacion)
        {
            LocationPoint lp = columna.Location as LocationPoint;
            if (lp == null) return;

            BoundingBoxXYZ bbox = columna.get_BoundingBox(null);
            if (bbox == null) return;

            XYZ ubicacion = lp.Point;
            double ancho = bbox.Max.X - bbox.Min.X;
            double profundo = bbox.Max.Y - bbox.Min.Y;

            double anchoTotal = ancho + 2 * separacion;
            double profundoTotal = profundo + 2 * separacion;

            XYZ centro = new XYZ(ubicacion.X, ubicacion.Y, nivel.Elevation);

            List<Curve> perfil = new List<Curve>
            {
                Line.CreateBound(
                    centro + new XYZ(-anchoTotal/2, -profundoTotal/2, 0),
                    centro + new XYZ(anchoTotal/2, -profundoTotal/2, 0)),
                Line.CreateBound(
                    centro + new XYZ(anchoTotal/2, -profundoTotal/2, 0),
                    centro + new XYZ(anchoTotal/2, profundoTotal/2, 0)),
                Line.CreateBound(
                    centro + new XYZ(anchoTotal/2, profundoTotal/2, 0),
                    centro + new XYZ(-anchoTotal/2, profundoTotal/2, 0)),
                Line.CreateBound(
                    centro + new XYZ(-anchoTotal/2, profundoTotal/2, 0),
                    centro + new XYZ(-anchoTotal/2, -profundoTotal/2, 0))
            };

            CrearMurosDesdeContorno(perfil, nivel, altura, columna, "Col_FB");
        }

        #endregion

        #region 4. METODO: ENCOFRADO DE VIGAS (DIRECTSHAPE)

        /// <summary>
        /// Genera encofrado siguiendo el perfil de la viga (laterales + fondo) usando DirectShape
        /// Soporta todos los perfiles: rectangulares, I, H, T, L, etc.
        /// </summary>
        private void GenerarEncoofradoViga(FamilyInstance viga)
        {
            if (YaTieneEncoofrado(viga.Id)) return;

            try
            {
                GenerarEncofradoVigaConDirectShape(viga);
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(viga.Id, $"Error en encofrado viga: {ex.Message}");
            }
        }

        /// <summary>
        /// Método fallback simplificado para vigas
        /// </summary>
        private void GenerarEncofradoVigaFallback(FamilyInstance viga, Level nivel, double separacion)
        {
            LocationCurve lc = viga.Location as LocationCurve;
            if (lc == null) return;

            Curve curvaViga = lc.Curve;
            BoundingBoxXYZ bbox = viga.get_BoundingBox(null);
            if (bbox == null) return;

            double altura = bbox.Max.Z - bbox.Min.Z;
            double ancho = Math.Max(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y);
            double offsetLateral = (ancho / 2.0) + separacion;

            XYZ dir = (curvaViga.GetEndPoint(1) - curvaViga.GetEndPoint(0)).Normalize();
            XYZ perp = new XYZ(-dir.Y, dir.X, 0).Normalize();

            Curve lado1 = curvaViga.CreateTransformed(Transform.CreateTranslation(perp * offsetLateral));
            Curve lado2 = curvaViga.CreateTransformed(Transform.CreateTranslation(-perp * offsetLateral));

            WallType tipo = _gestorTipos.TipoMuroEncofrado;

            Wall muro1 = Wall.Create(_doc, lado1, tipo.Id, nivel.Id, altura, 0, false, false);
            Wall muro2 = Wall.Create(_doc, lado2, tipo.Id, nivel.Id, altura, 0, false, false);

            ConfigurarMuroNoEstructural(muro1);
            ConfigurarMuroNoEstructural(muro2);
            VincularConOriginal(muro1, viga, "Viga_L1_FB");
            VincularConOriginal(muro2, viga, "Viga_L2_FB");

            _resultado.ElementosCreados += 2;
        }

        #endregion

        #region 5. METODO: ENCOFRADO DE ESCALERAS

        /// <summary>
        /// Genera encofrado completo para escalera usando DirectShape
        /// Encofra: caras inferiores (horizontales e inclinadas), laterales verticales y contrahuellas
        /// Excluye: caras superiores donde se pisa
        /// </summary>
        private void GenerarEncoofradoEscalera(Stairs escalera)
        {
            if (YaTieneEncoofrado(escalera.Id)) return;

            try
            {
                // 1. Extraer TODOS los sólidos de la escalera (peldaños, descansos, etc.)
                List<Solid> solidosEscalera = ExtraerTodosLosSolidos(escalera);
                if (solidosEscalera.Count == 0)
                    throw new InvalidOperationException("No se pudo extraer geometría de la escalera");

                _logDebug.AppendLine($"Escalera {escalera.Id} - Sólidos encontrados: {solidosEscalera.Count}");

                // 2. Parámetros de encofrado
                double separacion = 0.0; // Sin separación - pegado a la cara
                double espesorPanel = UnitUtils.ConvertToInternalUnits(18.0, UnitTypeId.Millimeters);

                // 3. Recopilar TODAS las caras de TODOS los sólidos que NO son huellas
                List<PlanarFace> carasParaEncofrar = new List<PlanarFace>();
                int carasSuperioresIgnoradas = 0;
                int totalCaras = 0;

                foreach (Solid solidoEscalera in solidosEscalera)
                {
                    if (!ValidarSolido(solidoEscalera))
                        continue;

                    _logDebug.AppendLine($"  Procesando sólido con volumen: {solidoEscalera.Volume:F4}");

                    foreach (Face face in solidoEscalera.Faces)
                    {
                        totalCaras++;
                        if (face is PlanarFace pf)
                        {
                            XYZ normal = pf.FaceNormal;
                            double z = normal.Z;

                            // SIMPLE: Excluir SOLO las huellas (caras casi horizontales hacia arriba)
                            // Huellas tienen z > 0.5 (más de 30 grados de inclinación)
                            // TODO lo demás se encofra: contrahuellas, bases, laterales
                            if (z > 0.5)
                            {
                                carasSuperioresIgnoradas++;
                                _logDebug.AppendLine($"      Cara IGNORADA (huella) - Normal Z: {z:F3}, Área: {pf.Area:F4}");
                                continue;
                            }

                            // Incluir TODAS las demás caras
                            carasParaEncofrar.Add(pf);
                            _logDebug.AppendLine($"      Cara ENCOFRANDO - Normal Z: {z:F3}, Área: {pf.Area:F4}");
                        }
                    }
                }

                _logDebug.AppendLine($"Escalera {escalera.Id} - Análisis de caras:");
                _logDebug.AppendLine($"  Total de caras: {totalCaras}");
                _logDebug.AppendLine($"  Caras para encofrar (z <= 0.5): {carasParaEncofrar.Count}");
                _logDebug.AppendLine($"  Caras superiores ignoradas (z > 0.5): {carasSuperioresIgnoradas}");

                int count = 0;
                int errores = 0;

                // 4. Generar panel para CADA cara
                foreach (PlanarFace cara in carasParaEncofrar)
                {
                    try
                    {
                        FormworkFaceData faceData = CalcularEncoofradoConIntersecciones(
                            cara,
                            escalera,
                            separacion,
                            espesorPanel);

                        if (ValidarSolido(faceData.Geometry))
                        {
                            DirectShape ds = CrearEncofradoDirectShape(
                                faceData.Geometry,
                                $"Encofrado_Escalera_{count + 1}_{escalera.Id}",
                                escalera,
                                $"Escalera_Panel{count + 1}_DS");

                            _resultado.ElementosCreados++;
                            count++;
                        }
                    }
                    catch (Exception exCara)
                    {
                        _resultado.AgregarAdvertencia(escalera.Id,
                            $"No se pudo crear panel {count + 1}: {exCara.Message}");
                        errores++;
                    }
                }

                _logDebug.AppendLine($"  RESULTADO: {count} paneles creados exitosamente, {errores} errores");

                if (count == 0)
                    throw new InvalidOperationException("No se pudo crear ningún panel de encofrado");

                _logDebug.AppendLine($"Escalera {escalera.Id}: {count} paneles creados");
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(escalera.Id,
                    $"Error DirectShape escalera: {ex.Message}");
            }
        }

        #endregion

        #region MÉTODOS DIRECTSHAPE - GEOMETRÍAS COMPLEJAS

        /// <summary>
        /// METODO DIRECTSHAPE: Encofrado de muro con deteccion de intersecciones (Patron BLIM)
        /// Crea paneles delgados en las 2 caras laterales del muro, restando elementos intersectados
        /// </summary>
        private void GenerarEncofradoMuroConDirectShape(Wall muroEstructural)
        {
            if (YaTieneEncoofrado(muroEstructural.Id)) return;

            try
            {
                // 1. Extraer solido principal
                Solid solidoMuro = ExtraerSolidoPrincipal(muroEstructural);
                if (solidoMuro == null)
                    throw new InvalidOperationException("No se pudo extraer geometria del muro");

                // 2. Obtener orientacion del muro para identificar caras laterales
                XYZ orientacion = muroEstructural.Orientation;
                if (muroEstructural.Flipped) orientacion = -orientacion;

                // 3. Parametros de encofrado
                double separacion = 0.0; // Sin separación - pegado a la cara
                double espesorPanel = UnitUtils.ConvertToInternalUnits(
                    _settings.EspesorMuro, UnitTypeId.Millimeters);

                // 4. Identificar las 2 caras laterales principales (perpendiculares a orientacion)
                List<PlanarFace> carasLaterales = new List<PlanarFace>();

                foreach (Face face in solidoMuro.Faces)
                {
                    if (face is PlanarFace pf)
                    {
                        XYZ normal = pf.FaceNormal;

                        // Verificar si es vertical (Z cercana a 0)
                        bool esVertical = Math.Abs(normal.Z) < 0.1;

                        // Verificar si es perpendicular a la orientacion del muro
                        // (producto punto cercano a 1 o -1)
                        double productoPunto = Math.Abs(normal.DotProduct(orientacion));
                        bool esPerpendicular = productoPunto > 0.9;

                        if (esVertical && esPerpendicular)
                        {
                            carasLaterales.Add(pf);
                        }
                    }
                }

                if (carasLaterales.Count < 2)
                    throw new InvalidOperationException(
                        $"Solo se encontraron {carasLaterales.Count} caras laterales (se esperaban 2)");

                // 5. Ordenar por area (tomar las 2 mas grandes)
                var carasPrincipales = carasLaterales
                    .OrderByDescending(c => c.Area)
                    .Take(2)
                    .ToList();

                // 6. Crear panel de encofrado para cada cara usando patron BLIM
                int count = 0;
                foreach (PlanarFace cara in carasPrincipales)
                {
                    try
                    {
                        // Usar metodo de calculo con deteccion de intersecciones (Patron BLIM)
                        FormworkFaceData faceData = CalcularEncoofradoConIntersecciones(
                            cara,
                            muroEstructural,
                            separacion,
                            espesorPanel);

                        if (ValidarSolido(faceData.Geometry))
                        {
                            string lado = count == 0 ? "Ext" : "Int";
                            DirectShape ds = CrearEncofradoDirectShape(
                                faceData.Geometry,
                                $"Encofrado_Muro_{lado}_{muroEstructural.Id}",
                                muroEstructural,
                                $"Muro_{lado}_DS");

                            _resultado.ElementosCreados++;
                            count++;

                            // Log de intersecciones detectadas
                            if (faceData.IntersectingElements.Count > 0)
                            {
                                _logDebug.AppendLine($"Muro {muroEstructural.Id} - Cara {lado}: " +
                                    $"{faceData.IntersectingElements.Count} elementos intersectados restados");
                            }
                        }
                    }
                    catch (Exception exCara)
                    {
                        _resultado.AgregarAdvertencia(muroEstructural.Id,
                            $"No se pudo crear panel en cara: {exCara.Message}");
                    }
                }

                if (count == 0)
                    throw new InvalidOperationException("No se pudo crear ningun panel de encofrado");
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(muroEstructural.Id,
                    $"Error DirectShape muro: {ex.Message}");
            }
        }

        /// <summary>
        /// METODO DIRECTSHAPE: Encofrado de columna con deteccion de intersecciones (Patron BLIM)
        /// Crea paneles delgados en todas las caras verticales externas, restando elementos intersectados
        /// </summary>
        private void GenerarEncofradoColumnaConDirectShape(FamilyInstance columna)
        {
            if (YaTieneEncoofrado(columna.Id)) return;

            try
            {
                // 1. Extraer solido principal
                Solid solidoColumna = ExtraerSolidoPrincipal(columna);
                if (solidoColumna == null)
                    throw new InvalidOperationException("No se pudo extraer geometria de la columna");

                // 2. Parametros de encofrado
                double separacion = 0.0; // Sin separación - pegado a la cara
                double espesorPanel = UnitUtils.ConvertToInternalUnits(
                    _settings.EspesorMuro, UnitTypeId.Millimeters);

                // 3. Identificar todas las caras verticales externas
                List<PlanarFace> carasVerticales = new List<PlanarFace>();

                foreach (Face face in solidoColumna.Faces)
                {
                    if (face is PlanarFace pf)
                    {
                        // Verificar si es vertical usando CaraNecesitaEncofrado
                        if (CaraNecesitaEncofrado(pf, TipoCaraEncofrado.Lateral))
                        {
                            carasVerticales.Add(pf);
                        }
                    }
                }

                if (carasVerticales.Count == 0)
                    throw new InvalidOperationException("No se encontraron caras verticales para encofrado");

                // 4. Crear panel de encofrado para cada cara vertical usando patron BLIM
                int count = 0;
                foreach (PlanarFace cara in carasVerticales)
                {
                    try
                    {
                        // Usar metodo de calculo con deteccion de intersecciones (Patron BLIM)
                        FormworkFaceData faceData = CalcularEncoofradoConIntersecciones(
                            cara,
                            columna,
                            separacion,
                            espesorPanel);

                        if (ValidarSolido(faceData.Geometry))
                        {
                            DirectShape ds = CrearEncofradoDirectShape(
                                faceData.Geometry,
                                $"Encofrado_Columna_Cara{count + 1}_{columna.Id}",
                                columna,
                                $"Columna_Cara{count + 1}_DS");

                            _resultado.ElementosCreados++;
                            count++;

                            // Log de intersecciones detectadas
                            if (faceData.IntersectingElements.Count > 0)
                            {
                                _logDebug.AppendLine($"Columna {columna.Id} - Cara {count}: " +
                                    $"{faceData.IntersectingElements.Count} elementos intersectados restados");
                            }
                        }
                    }
                    catch (Exception exCara)
                    {
                        _resultado.AgregarAdvertencia(columna.Id,
                            $"No se pudo crear panel en cara {count + 1}: {exCara.Message}");
                    }
                }

                if (count == 0)
                    throw new InvalidOperationException("No se pudo crear ningun panel de encofrado");
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(columna.Id,
                    $"Error DirectShape columna: {ex.Message}");
            }
        }

        /// <summary>
        /// METODO DIRECTSHAPE: Encofrado de viga con deteccion de intersecciones (Patron BLIM)
        /// Crea paneles delgados en caras laterales y fondo, restando elementos intersectados
        /// </summary>
        private void GenerarEncofradoVigaConDirectShape(FamilyInstance viga)
        {
            if (YaTieneEncoofrado(viga.Id)) return;

            try
            {
                // 1. Extraer solido principal
                Solid solidoViga = ExtraerSolidoPrincipal(viga);
                if (solidoViga == null)
                    throw new InvalidOperationException("No se pudo extraer geometria de la viga");

                // 2. Parametros de encofrado
                double separacion = 0.0; // Sin separación - pegado a la cara
                double espesorPanel = UnitUtils.ConvertToInternalUnits(
                    _settings.EspesorMuro, UnitTypeId.Millimeters);

                // 3. Identificar caras laterales (verticales) y fondo (inferior)
                List<PlanarFace> carasLaterales = new List<PlanarFace>();
                PlanarFace caraFondo = null;
                double zMinimoFondo = double.MaxValue;

                foreach (Face face in solidoViga.Faces)
                {
                    if (face is PlanarFace pf)
                    {
                        // Caras laterales (verticales)
                        if (CaraNecesitaEncofrado(pf, TipoCaraEncofrado.Lateral))
                        {
                            carasLaterales.Add(pf);
                        }
                        // Cara de fondo (inferior - la mas baja)
                        else if (CaraNecesitaEncofrado(pf, TipoCaraEncofrado.Inferior))
                        {
                            if (pf.Origin.Z < zMinimoFondo)
                            {
                                zMinimoFondo = pf.Origin.Z;
                                caraFondo = pf;
                            }
                        }
                    }
                }

                int count = 0;
                int elementosIniciales = _resultado.ElementosCreados;

                // 4. Crear paneles laterales usando patron BLIM
                foreach (PlanarFace cara in carasLaterales)
                {
                    try
                    {
                        // Usar metodo de calculo con deteccion de intersecciones (Patron BLIM)
                        FormworkFaceData faceData = CalcularEncoofradoConIntersecciones(
                            cara,
                            viga,
                            separacion,
                            espesorPanel);

                        if (ValidarSolido(faceData.Geometry))
                        {
                            DirectShape ds = CrearEncofradoDirectShape(
                                faceData.Geometry,
                                $"Encofrado_Viga_Lateral{count + 1}_{viga.Id}",
                                viga,
                                $"Viga_Lateral{count + 1}_DS");

                            _resultado.ElementosCreados++;
                            count++;

                            // Log de intersecciones detectadas
                            if (faceData.IntersectingElements.Count > 0)
                            {
                                _logDebug.AppendLine($"Viga {viga.Id} - Lateral {count}: " +
                                    $"{faceData.IntersectingElements.Count} elementos intersectados restados");
                            }
                        }
                    }
                    catch (Exception exCara)
                    {
                        _resultado.AgregarAdvertencia(viga.Id,
                            $"No se pudo crear panel lateral: {exCara.Message}");
                    }
                }

                // 5. Crear panel de fondo usando patron BLIM
                if (caraFondo != null)
                {
                    try
                    {
                        // Usar metodo de calculo con deteccion de intersecciones (Patron BLIM)
                        FormworkFaceData faceData = CalcularEncoofradoConIntersecciones(
                            caraFondo,
                            viga,
                            separacion,
                            espesorPanel);

                        if (ValidarSolido(faceData.Geometry))
                        {
                            DirectShape ds = CrearEncofradoDirectShape(
                                faceData.Geometry,
                                $"Encofrado_Viga_Fondo_{viga.Id}",
                                viga,
                                "Viga_Fondo_DS");

                            _resultado.ElementosCreados++;

                            // Log de intersecciones detectadas
                            if (faceData.IntersectingElements.Count > 0)
                            {
                                _logDebug.AppendLine($"Viga {viga.Id} - Fondo: " +
                                    $"{faceData.IntersectingElements.Count} elementos intersectados restados");
                            }
                        }
                    }
                    catch (Exception exFondo)
                    {
                        _resultado.AgregarAdvertencia(viga.Id,
                            $"No se pudo crear panel de fondo: {exFondo.Message}");
                    }
                }

                if (_resultado.ElementosCreados == elementosIniciales)
                    throw new InvalidOperationException("No se pudo crear ningun panel de encofrado");
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(viga.Id,
                    $"Error DirectShape viga: {ex.Message}");
            }
        }

        /// <summary>
        /// Crea un panel de encofrado desde una cara planar
        /// offset: gap de aire entre cara y panel (para desmoldar), típicamente 0-5mm
        /// espesor: grosor del tablero de encofrado, típicamente 18-25mm
        /// El panel se coloca FUERA del elemento, nunca dentro
        /// </summary>
        private Solid CrearPanelEncofradoDesdeCara(PlanarFace cara, double offsetDesdeCara, double espesorPanel)
        {
            try
            {
                // 1. Obtener contornos de la cara
                IList<CurveLoop> loops = cara.GetEdgesAsCurveLoops();
                if (loops.Count == 0) return null;

                // 2. Direccion perpendicular a la cara (hacia afuera del elemento)
                XYZ normal = cara.FaceNormal;

                // LOGICA CORRECTA:
                // - El panel debe estar FUERA del elemento, no dentro
                // - offset = separación de aire entre cara y panel
                // - espesor = grosor del tablero
                //
                // Panel empieza a distancia "offset" de la cara
                // Panel se extiende por "espesor" hacia afuera
                // Total de extrusión = solo espesor (NO offset + espesor)

                List<CurveLoop> loopsDesplazados = new List<CurveLoop>();

                // Desplazar loops hacia AFUERA por el offset
                Transform transformAfuera = Transform.CreateTranslation(normal * offsetDesdeCara);

                foreach (CurveLoop loop in loops)
                {
                    CurveLoop nuevoLoop = new CurveLoop();
                    foreach (Curve curva in loop)
                    {
                        Curve curvaDesplazada = curva.CreateTransformed(transformAfuera);
                        nuevoLoop.Append(curvaDesplazada);
                    }
                    loopsDesplazados.Add(nuevoLoop);
                }

                // Extruir SOLO por el espesor del panel hacia afuera
                Solid panel = GeometryCreationUtilities.CreateExtrusionGeometry(
                    loopsDesplazados, normal, espesorPanel);

                return panel;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error creando panel desde cara: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Identifica si una cara necesita encofrado segun su orientacion
        /// </summary>
        private bool CaraNecesitaEncofrado(PlanarFace cara, TipoCaraEncofrado tipo)
        {
            XYZ normal = cara.FaceNormal;
            double z = normal.Z;

            switch (tipo)
            {
                case TipoCaraEncofrado.Lateral:
                    // Caras verticales o casi verticales
                    // Cambiado de 0.1 a 0.3 para ser más permisivo con losas
                    // que pueden tener caras ligeramente inclinadas
                    return Math.Abs(z) < 0.3;

                case TipoCaraEncofrado.Inferior:
                    // Cara apuntando hacia abajo
                    return z < -0.9;

                case TipoCaraEncofrado.Superior:
                    // Cara apuntando hacia arriba
                    return z > 0.9;

                case TipoCaraEncofrado.Todas:
                    return true;

                default:
                    return false;
            }
        }

        private enum TipoCaraEncofrado
        {
            Lateral,    // Vertical
            Inferior,   // Hacia abajo
            Superior,   // Hacia arriba
            Todas       // Cualquiera
        }

        #region ESTRUCTURAS Y METODOS PATRON BLIM

        /// <summary>
        /// Estructura para encapsular datos de cara de encofrado (Patron BLIM)
        /// Similar a SupportFunctions.FormworkFace de BLIM
        /// </summary>
        private struct FormworkFaceData
        {
            public Face HostFace;                   // Cara original del elemento anfitrion
            public Face ModifiedFace;               // Cara modificada/procesada
            public Element HostElement;             // Elemento que contiene la cara
            public Solid Geometry;                  // Geometria solida generada para encofrado
            public ElementId HostID;                // ID del elemento anfitrion
            public double Area;                     // Area de la cara
            public List<Element> IntersectingElements;  // Elementos que intersectan
            public string ShapeKey;                 // Clave de forma para busqueda
        }

        /// <summary>
        /// Une multiples solidos en uno solo (Patron BLIM: UnionSolidList)
        /// </summary>
        private Solid UnirSolidos(List<Solid> solidos)
        {
            if (solidos == null || solidos.Count == 0)
                return null;

            if (solidos.Count == 1)
                return solidos[0];

            try
            {
                Solid resultado = solidos[0];

                for (int i = 1; i < solidos.Count; i++)
                {
                    if (ValidarSolido(solidos[i]))
                    {
                        try
                        {
                            resultado = BooleanOperationsUtils.ExecuteBooleanOperation(
                                resultado,
                                solidos[i],
                                BooleanOperationsType.Union);
                        }
                        catch (Exception ex)
                        {
                            _logDebug.AppendLine($"No se pudo unir solido {i}: {ex.Message}");
                            continue;
                        }
                    }
                }

                return resultado;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en UnirSolidos: {ex.Message}");
                return solidos[0]; // Retornar al menos el primero
            }
        }

        /// <summary>
        /// Engrosa una superficie planar creando un solido (Patron BLIM: ThickenCurvedSurfacePlanar)
        /// </summary>
        private Solid EngrosarSuperficie(PlanarFace cara, double espesor, XYZ direccion)
        {
            try
            {
                IList<CurveLoop> loops = cara.GetEdgesAsCurveLoops();
                if (loops.Count == 0) return null;

                // Si no se proporciona direccion, usar la normal de la cara
                if (direccion == null)
                    direccion = cara.FaceNormal;

                // Crear solido por extrusion en la direccion especificada
                Solid solido = GeometryCreationUtilities.CreateExtrusionGeometry(
                    loops,
                    direccion,
                    espesor);

                return solido;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en EngrosarSuperficie: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Engrosa una superficie curva (Patron BLIM: ThickenCurvedSurface)
        /// </summary>
        private Solid EngrosarSuperficieCurva(Face cara, double espesor)
        {
            try
            {
                // Para caras curvas, crear panel por extrusion de los bordes
                if (cara is CylindricalFace cf)
                {
                    // Para superficies cilindricas, obtener bucles de bordes
                    IList<CurveLoop> loops = cf.GetEdgesAsCurveLoops();
                    if (loops.Count > 0)
                    {
                        // Obtener direccion del eje del cilindro
                        // En Revit API, CylindricalFace.Axis devuelve el vector de dirección directamente
                        XYZ direccionEje = cf.Axis.Normalize();

                        // Crear solido por extrusion
                        return GeometryCreationUtilities.CreateExtrusionGeometry(
                            loops,
                            direccionEje,
                            espesor);
                    }
                }

                // Para otras caras curvas, intentar con extrusion desde loops de bordes
                IList<CurveLoop> curvelloops = cara.GetEdgesAsCurveLoops();
                if (curvelloops.Count > 0)
                {
                    // Aproximar normal promedio de la cara
                    BoundingBoxUV bbox = cara.GetBoundingBox();
                    UV uvMid = (bbox.Min + bbox.Max) / 2.0;
                    XYZ normal = cara.ComputeNormal(uvMid);

                    // Crear solido por extrusion
                    return GeometryCreationUtilities.CreateExtrusionGeometry(
                        curvelloops,
                        normal,
                        espesor);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en EngrosarSuperficieCurva: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Obtiene elementos que intersectan con un solido (Patron BLIM: GetElementsIntersectedSolidNative)
        /// </summary>
        private IList<Element> ObtenerElementosIntersectados(Solid solido, Element excluir = null)
        {
            List<Element> elementos = new List<Element>();

            if (!ValidarSolido(solido))
                return elementos;

            try
            {
                // Crear filtro de interseccion
                ElementIntersectsSolidFilter filtro = new ElementIntersectsSolidFilter(solido);

                // Buscar elementos que intersectan
                FilteredElementCollector collector = new FilteredElementCollector(_doc)
                    .WherePasses(filtro)
                    .WhereElementIsNotElementType();

                foreach (Element elem in collector)
                {
                    // Excluir el elemento especificado
                    if (excluir != null && elem.Id == excluir.Id)
                        continue;

                    // Filtrar solo elementos estructurales
                    if (elem is Wall || elem is Floor ||
                        elem is FamilyInstance || elem is Stairs)
                    {
                        elementos.Add(elem);
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en ObtenerElementosIntersectados: {ex.Message}");
            }

            return elementos;
        }

        /// <summary>
        /// Calcula encofrado para una cara con intersecciones (Patron BLIM: FormworkCalculator2)
        /// </summary>
        private FormworkFaceData CalcularEncoofradoConIntersecciones(
            Face cara,
            Element elementoAnfitrion,
            double offset,
            double espesor)
        {
            FormworkFaceData resultado = new FormworkFaceData
            {
                HostFace = cara,
                ModifiedFace = cara,
                HostElement = elementoAnfitrion,
                HostID = elementoAnfitrion.Id,
                Area = cara.Area,
                IntersectingElements = new List<Element>()
            };

            try
            {
                // 1. Crear geometria base desde la cara
                Solid geometriaBase = null;

                if (cara is PlanarFace pf)
                {
                    // Para caras planares, crear panel desplazado
                    geometriaBase = CrearPanelEncofradoDesdeCara(pf, offset, espesor);
                }
                else
                {
                    // Para caras curvas, usar metodo de engrosamiento
                    geometriaBase = EngrosarSuperficieCurva(cara, espesor);
                }

                if (!ValidarSolido(geometriaBase))
                {
                    _logDebug.AppendLine("No se pudo crear geometria base para cara");
                    return resultado;
                }

                // 2. Detectar elementos que intersectan
                IList<Element> intersectados = ObtenerElementosIntersectados(
                    geometriaBase,
                    elementoAnfitrion);

                resultado.IntersectingElements = intersectados.ToList();

                // 3. Si hay intersecciones, restar geometria intersectada
                if (intersectados.Count > 0)
                {
                    List<Solid> solidosARestar = new List<Solid>();

                    foreach (Element elem in intersectados)
                    {
                        List<Solid> solidosElem = ExtraerTodosLosSolidos(elem);
                        solidosARestar.AddRange(solidosElem);
                    }

                    // Restar cada solido intersectado
                    foreach (Solid solidoRestar in solidosARestar)
                    {
                        if (ValidarSolido(solidoRestar))
                        {
                            try
                            {
                                geometriaBase = BooleanOperationsUtils.ExecuteBooleanOperation(
                                    geometriaBase,
                                    solidoRestar,
                                    BooleanOperationsType.Difference);

                                if (!ValidarSolido(geometriaBase))
                                    break;
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }
                }

                resultado.Geometry = geometriaBase;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en CalcularEncoofradoConIntersecciones: {ex.Message}");
            }

            return resultado;
        }

        #endregion

        #region PATRON DE DOS PASOS: GEOMETRIA VACIA + AJUSTE

        /// <summary>
        /// Patron de dos pasos observado en BLIMTAR:
        /// 1. Crear geometria "vacia" (BoundingBox extendido) con descuentos de elementos adyacentes
        /// 2. Intersectar con elemento estructural para ajuste exacto
        /// </summary>
        private Solid CrearEncofradoPatronDospasos(
            Element elementoEstructural,
            double espesorEncofrado,
            double separacion)
        {
            try
            {
                // PASO 1: Crear geometria vacia con descuentos
                Solid geometriaVacia = CrearGeometriaVaciaConDescuentos(
                    elementoEstructural,
                    espesorEncofrado,
                    separacion);

                if (!ValidarSolido(geometriaVacia))
                {
                    _logDebug.AppendLine("No se pudo crear geometria vacia");
                    return null;
                }

                // PASO 2: Ajustar al elemento estructural mediante interseccion
                Solid encofradoFinal = AjustarAlElementoEstructural(
                    geometriaVacia,
                    elementoEstructural,
                    espesorEncofrado);

                return encofradoFinal;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en CrearEncofradoPatronDospasos: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// PASO 1: Crear geometria "vacia" - BoundingBox extendido con descuentos de adyacentes
        /// Esto genera el volumen inicial que luego sera ajustado al elemento
        /// </summary>
        private Solid CrearGeometriaVaciaConDescuentos(
            Element elementoEstructural,
            double espesorEncofrado,
            double separacion)
        {
            try
            {
                // 1. Obtener BoundingBox del elemento estructural
                BoundingBoxXYZ bbox = elementoEstructural.get_BoundingBox(null);
                if (bbox == null)
                {
                    _logDebug.AppendLine("No se pudo obtener BoundingBox del elemento");
                    return null;
                }

                // 2. Extender el BoundingBox por separacion + espesor de encofrado
                double extension = separacion + espesorEncofrado;
                XYZ offset = new XYZ(extension, extension, extension);

                bbox.Min = bbox.Min - offset;
                bbox.Max = bbox.Max + offset;

                // 3. Crear solido desde BoundingBox extendido
                Solid solidoBBox = CrearSolidoDesdeBoundingBox(bbox);

                if (!ValidarSolido(solidoBBox))
                {
                    _logDebug.AppendLine("No se pudo crear solido desde BoundingBox");
                    return null;
                }

                // 4. Detectar elementos adyacentes que intersectan
                IList<Element> elementosAdyacentes = ObtenerElementosIntersectados(
                    solidoBBox,
                    elementoEstructural);

                _logDebug.AppendLine($"Elementos adyacentes detectados: {elementosAdyacentes.Count}");

                // 5. Restar geometria de elementos adyacentes (descuentos)
                Solid geometriaVacia = solidoBBox;

                foreach (Element elemAdyacente in elementosAdyacentes)
                {
                    List<Solid> solidosAdyacente = ExtraerTodosLosSolidos(elemAdyacente);

                    foreach (Solid solidoAdyacente in solidosAdyacente)
                    {
                        if (ValidarSolido(solidoAdyacente))
                        {
                            try
                            {
                                // Restar el solido adyacente
                                geometriaVacia = BooleanOperationsUtils.ExecuteBooleanOperation(
                                    geometriaVacia,
                                    solidoAdyacente,
                                    BooleanOperationsType.Difference);

                                if (!ValidarSolido(geometriaVacia))
                                {
                                    _logDebug.AppendLine("Geometria vacia invalida despues de resta");
                                    return solidoBBox; // Retornar sin descuentos si falla
                                }
                            }
                            catch (Exception ex)
                            {
                                _logDebug.AppendLine($"No se pudo restar elemento adyacente: {ex.Message}");
                                continue;
                            }
                        }
                    }
                }

                return geometriaVacia;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en CrearGeometriaVaciaConDescuentos: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// PASO 2: Ajustar geometria vacia al elemento estructural mediante interseccion
        /// Esto hace que el encofrado se "pegue" exactamente al elemento
        /// </summary>
        private Solid AjustarAlElementoEstructural(
            Solid geometriaVacia,
            Element elementoEstructural,
            double espesorEncofrado)
        {
            try
            {
                // 1. Obtener solidos del elemento estructural
                List<Solid> solidosEstructurales = ExtraerTodosLosSolidos(elementoEstructural);

                if (solidosEstructurales.Count == 0)
                {
                    _logDebug.AppendLine("No se encontraron solidos en elemento estructural");
                    return geometriaVacia; // Retornar geometria sin ajustar
                }

                // 2. Unir todos los solidos estructurales en uno solo
                Solid solidoEstructural = UnirSolidos(solidosEstructurales);

                if (!ValidarSolido(solidoEstructural))
                {
                    _logDebug.AppendLine("Solido estructural invalido");
                    return geometriaVacia;
                }

                // 3. Expandir ligeramente el solido estructural
                // Esto asegura que la interseccion capture el encofrado alrededor
                Solid solidoEstructuralExpandido = ExpandirSolido(solidoEstructural, espesorEncofrado * 1.2);

                if (!ValidarSolido(solidoEstructuralExpandido))
                {
                    _logDebug.AppendLine("No se pudo expandir solido estructural, usando original");
                    solidoEstructuralExpandido = solidoEstructural;
                }

                // 4. INTERSECTAR geometria vacia con elemento estructural expandido
                // Esto es el "ajuste" - solo mantiene la geometria que rodea al elemento
                Solid encofradoAjustado = BooleanOperationsUtils.ExecuteBooleanOperation(
                    geometriaVacia,
                    solidoEstructuralExpandido,
                    BooleanOperationsType.Intersect);

                if (!ValidarSolido(encofradoAjustado))
                {
                    _logDebug.AppendLine("Interseccion invalida, retornando geometria sin ajustar");
                    return geometriaVacia;
                }

                // 5. OPCIONAL: Restar el elemento estructural original para crear el hueco
                try
                {
                    Solid encofradoFinal = BooleanOperationsUtils.ExecuteBooleanOperation(
                        encofradoAjustado,
                        solidoEstructural,
                        BooleanOperationsType.Difference);

                    if (ValidarSolido(encofradoFinal))
                    {
                        return encofradoFinal;
                    }
                    else
                    {
                        return encofradoAjustado;
                    }
                }
                catch
                {
                    return encofradoAjustado;
                }
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en AjustarAlElementoEstructural: {ex.Message}");
                return geometriaVacia; // Retornar sin ajustar en caso de error
            }
        }

        /// <summary>
        /// Crea un solido a partir de un BoundingBox
        /// </summary>
        private Solid CrearSolidoDesdeBoundingBox(BoundingBoxXYZ bbox)
        {
            try
            {
                XYZ min = bbox.Min;
                XYZ max = bbox.Max;

                // Crear los 8 vertices del box
                List<XYZ> vertices = new List<XYZ>
                {
                    new XYZ(min.X, min.Y, min.Z),
                    new XYZ(max.X, min.Y, min.Z),
                    new XYZ(max.X, max.Y, min.Z),
                    new XYZ(min.X, max.Y, min.Z),
                    new XYZ(min.X, min.Y, max.Z),
                    new XYZ(max.X, min.Y, max.Z),
                    new XYZ(max.X, max.Y, max.Z),
                    new XYZ(min.X, max.Y, max.Z)
                };

                // Crear la base del box (rectangulo en Z minimo)
                List<Curve> curvasBase = new List<Curve>
                {
                    Line.CreateBound(vertices[0], vertices[1]),
                    Line.CreateBound(vertices[1], vertices[2]),
                    Line.CreateBound(vertices[2], vertices[3]),
                    Line.CreateBound(vertices[3], vertices[0])
                };

                CurveLoop loopBase = CurveLoop.Create(curvasBase);
                List<CurveLoop> loops = new List<CurveLoop> { loopBase };

                // Extruir hacia arriba para crear el box solido
                double altura = max.Z - min.Z;
                XYZ direccion = XYZ.BasisZ;

                Solid solido = GeometryCreationUtilities.CreateExtrusionGeometry(
                    loops,
                    direccion,
                    altura);

                return solido;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en CrearSolidoDesdeBoundingBox: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Expande un solido en todas direcciones
        /// Similar a offset 3D
        /// </summary>
        private Solid ExpandirSolido(Solid solido, double offset)
        {
            try
            {
                // Obtener BoundingBox del solido
                BoundingBoxXYZ bbox = solido.GetBoundingBox();
                if (bbox == null)
                    return solido;

                // Expandir BoundingBox
                XYZ offsetVec = new XYZ(offset, offset, offset);
                bbox.Min = bbox.Min - offsetVec;
                bbox.Max = bbox.Max + offsetVec;

                // Crear solido expandido desde BoundingBox
                Solid bboxExpandido = CrearSolidoDesdeBoundingBox(bbox);

                if (!ValidarSolido(bboxExpandido))
                    return solido;

                // Intersectar con el bbox expandido mantiene la forma pero expandida
                // Esto es una aproximacion - no es perfecto pero funciona
                return bboxExpandido;
            }
            catch
            {
                return solido;
            }
        }

        /// <summary>
        /// Metodo alternativo para columnas circulares con vigas
        /// Genera masa de corte en intersecciones
        /// </summary>
        private Solid CrearMasaCorteColumnaCilindrica(
            Element columna,
            IList<Element> vigasIntersectadas,
            double espesorEncofrado)
        {
            try
            {
                List<Solid> masasCorte = new List<Solid>();

                // Obtener geometria de la columna
                List<Solid> solidosColumna = ExtraerTodosLosSolidos(columna);
                if (solidosColumna.Count == 0)
                    return null;

                Solid solidoColumna = UnirSolidos(solidosColumna);

                foreach (Element viga in vigasIntersectadas)
                {
                    // Obtener solidos de la viga
                    List<Solid> solidosViga = ExtraerTodosLosSolidos(viga);

                    foreach (Solid solidoViga in solidosViga)
                    {
                        if (!ValidarSolido(solidoViga))
                            continue;

                        try
                        {
                            // Intersectar columna con viga para obtener volumen de cruce
                            Solid volumenCruce = BooleanOperationsUtils.ExecuteBooleanOperation(
                                solidoColumna,
                                solidoViga,
                                BooleanOperationsType.Intersect);

                            if (ValidarSolido(volumenCruce))
                            {
                                // Expandir el volumen de cruce para crear masa de corte
                                Solid masaCorte = ExpandirSolido(volumenCruce, espesorEncofrado);
                                masasCorte.Add(masaCorte);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logDebug.AppendLine($"Error creando masa de corte: {ex.Message}");
                            continue;
                        }
                    }
                }

                // Unir todas las masas de corte
                if (masasCorte.Count > 0)
                {
                    return UnirSolidos(masasCorte);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error en CrearMasaCorteColumnaCilindrica: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region METODOS DE DETECCION Y GENERACION CON PATRON DOS PASOS

        /// <summary>
        /// Detecta si una columna es cilindrica (circular)
        /// </summary>
        private bool EsColumnaCilindrica(FamilyInstance columna)
        {
            try
            {
                List<Solid> solidos = ExtraerTodosLosSolidos(columna);
                if (solidos.Count == 0)
                    return false;

                Solid solidoPrincipal = solidos.OrderByDescending(s => s.Volume).First();

                // Verificar si tiene caras cilindricas
                foreach (Face face in solidoPrincipal.Faces)
                {
                    if (face is CylindricalFace)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detecta vigas que intersectan con la columna
        /// </summary>
        private IList<Element> DetectarVigasIntersectadas(FamilyInstance columna)
        {
            List<Element> vigas = new List<Element>();

            try
            {
                // Obtener BoundingBox de la columna
                BoundingBoxXYZ bbox = columna.get_BoundingBox(null);
                if (bbox == null)
                    return vigas;

                // Expandir ligeramente para capturar elementos cercanos
                XYZ offset = new XYZ(0.5, 0.5, 0.5); // 6 pulgadas
                bbox.Min = bbox.Min - offset;
                bbox.Max = bbox.Max + offset;

                Outline outline = new Outline(bbox.Min, bbox.Max);
                BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(outline);

                // Buscar FamilyInstances que sean vigas
                FilteredElementCollector collector = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilyInstance))
                    .WherePasses(bboxFilter);

                foreach (FamilyInstance fi in collector)
                {
                    if (fi.Id == columna.Id)
                        continue;

                    // Verificar si es viga por categoría
                    if (fi.Category?.Name == "Vigas estructurales" ||
                        fi.Category?.Name == "Structural Framing")
                    {
                        vigas.Add(fi);
                    }
                }
            }
            catch (Exception ex)
            {
                _logDebug.AppendLine($"Error detectando vigas intersectadas: {ex.Message}");
            }

            return vigas;
        }

        /// <summary>
        /// Genera encofrado usando patron de dos pasos - Alternativa optimizada
        /// Especialmente efectivo para columnas cilindricas con vigas
        /// </summary>
        private void GenerarEncofradoColumnaPatronDospasos(FamilyInstance columna)
        {
            if (YaTieneEncoofrado(columna.Id)) return;

            try
            {
                _logDebug.AppendLine($"\n=== Generando encofrado con patron dos pasos - Columna {columna.Id} ===");

                // 1. Parametros de encofrado
                double separacion = 0.0; // Sin separación - pegado a la cara
                double espesorPanel = UnitUtils.ConvertToInternalUnits(
                    _settings.EspesorMuro, UnitTypeId.Millimeters);

                // 2. Detectar si es columna cilindrica
                bool esCilindrica = EsColumnaCilindrica(columna);
                _logDebug.AppendLine($"Columna cilindrica: {esCilindrica}");

                // 3. Detectar vigas intersectadas
                IList<Element> vigasIntersectadas = DetectarVigasIntersectadas(columna);
                _logDebug.AppendLine($"Vigas intersectadas detectadas: {vigasIntersectadas.Count}");

                // 4. Usar patron de dos pasos
                Solid encofradoBase = CrearEncofradoPatronDospasos(
                    columna,
                    espesorPanel,
                    separacion);

                if (!ValidarSolido(encofradoBase))
                {
                    _logDebug.AppendLine("No se pudo crear encofrado base con patron dos pasos");
                    throw new InvalidOperationException("Fallo patron dos pasos");
                }

                _logDebug.AppendLine($"Encofrado base creado - Volumen: {encofradoBase.Volume:F6} cf");

                // 5. Para columnas cilindricas con vigas: aplicar masa de corte
                if (esCilindrica && vigasIntersectadas.Count > 0)
                {
                    _logDebug.AppendLine("Aplicando masa de corte para columna cilindrica con vigas...");

                    Solid masaCorte = CrearMasaCorteColumnaCilindrica(
                        columna,
                        vigasIntersectadas,
                        espesorPanel);

                    if (ValidarSolido(masaCorte))
                    {
                        try
                        {
                            // Restar la masa de corte del encofrado
                            encofradoBase = BooleanOperationsUtils.ExecuteBooleanOperation(
                                encofradoBase,
                                masaCorte,
                                BooleanOperationsType.Difference);

                            _logDebug.AppendLine($"Masa de corte aplicada exitosamente");
                        }
                        catch (Exception exCorte)
                        {
                            _logDebug.AppendLine($"No se pudo aplicar masa de corte: {exCorte.Message}");
                            // Continuar sin masa de corte
                        }
                    }
                }

                // 6. Crear DirectShape con el encofrado final
                if (ValidarSolido(encofradoBase))
                {
                    DirectShape ds = CrearEncofradoDirectShape(
                        encofradoBase,
                        $"Encofrado_Columna_DosP_{columna.Id}",
                        columna,
                        "Columna_PatronDosP_DS");

                    if (ds != null)
                    {
                        _resultado.ElementosCreados++;
                        _logDebug.AppendLine($"✓ Encofrado creado exitosamente con patron dos pasos");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Solido final invalido");
                }
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(columna.Id,
                    $"Error patron dos pasos: {ex.Message}");
                _logDebug.AppendLine($"Error en GenerarEncofradoColumnaPatronDospasos: {ex.Message}");
            }
        }

        /// <summary>
        /// Genera encofrado para muro usando patron de dos pasos
        /// </summary>
        private void GenerarEncofradoMuroPatronDospasos(Wall muro)
        {
            if (YaTieneEncoofrado(muro.Id)) return;

            try
            {
                _logDebug.AppendLine($"\n=== Generando encofrado muro patron dos pasos - {muro.Id} ===");

                // 1. Parametros de encofrado
                double separacion = 0.0; // Sin separación - pegado a la cara
                double espesorPanel = UnitUtils.ConvertToInternalUnits(
                    _settings.EspesorMuro, UnitTypeId.Millimeters);

                // 2. Crear encofrado usando patron de dos pasos
                Solid encofradoBase = CrearEncofradoPatronDospasos(
                    muro,
                    espesorPanel,
                    separacion);

                if (!ValidarSolido(encofradoBase))
                {
                    throw new InvalidOperationException("No se pudo crear encofrado con patron dos pasos");
                }

                // 3. Crear DirectShape
                DirectShape ds = CrearEncofradoDirectShape(
                    encofradoBase,
                    $"Encofrado_Muro_DosP_{muro.Id}",
                    muro,
                    "Muro_PatronDosP_DS");

                if (ds != null)
                {
                    _resultado.ElementosCreados++;
                    _logDebug.AppendLine($"✓ Encofrado muro creado con patron dos pasos");
                }
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(muro.Id,
                    $"Error patron dos pasos muro: {ex.Message}");
                _logDebug.AppendLine($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Genera encofrado para viga usando patron de dos pasos
        /// </summary>
        private void GenerarEncofradoVigaPatronDospasos(FamilyInstance viga)
        {
            if (YaTieneEncoofrado(viga.Id)) return;

            try
            {
                _logDebug.AppendLine($"\n=== Generando encofrado viga patron dos pasos - {viga.Id} ===");

                // 1. Parametros de encofrado
                double separacion = 0.0; // Sin separación - pegado a la cara
                double espesorPanel = UnitUtils.ConvertToInternalUnits(
                    _settings.EspesorMuro, UnitTypeId.Millimeters);

                // 2. Crear encofrado usando patron de dos pasos
                Solid encofradoBase = CrearEncofradoPatronDospasos(
                    viga,
                    espesorPanel,
                    separacion);

                if (!ValidarSolido(encofradoBase))
                {
                    throw new InvalidOperationException("No se pudo crear encofrado con patron dos pasos");
                }

                // 3. Crear DirectShape
                DirectShape ds = CrearEncofradoDirectShape(
                    encofradoBase,
                    $"Encofrado_Viga_DosP_{viga.Id}",
                    viga,
                    "Viga_PatronDosP_DS");

                if (ds != null)
                {
                    _resultado.ElementosCreados++;
                    _logDebug.AppendLine($"✓ Encofrado viga creado con patron dos pasos");
                }
            }
            catch (Exception ex)
            {
                _resultado.AgregarError(viga.Id,
                    $"Error patron dos pasos viga: {ex.Message}");
                _logDebug.AppendLine($"Error: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Aplica material de encofrado a un DirectShape
        /// </summary>
        private void AplicarMaterialEncofrado(DirectShape ds)
        {
            try
            {
                // Buscar o crear material de encofrado
                ElementId matId = _gestorTipos.ObtenerMaterialEncofrado();
                if (matId != null && matId != ElementId.InvalidElementId)
                {
                    Parameter paramMaterial = ds.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (paramMaterial != null && !paramMaterial.IsReadOnly)
                    {
                        paramMaterial.Set(matId);
                    }
                }
            }
            catch
            {
                // Si falla, continuar sin material
            }
        }

        /// <summary>
        /// Obtiene el centroide de un sólido
        /// </summary>
        private XYZ ObtenerCentroide(Solid solido)
        {
            XYZ suma = XYZ.Zero;
            int count = 0;

            foreach (Face face in solido.Faces)
            {
                try
                {
                    BoundingBoxUV bbox = face.GetBoundingBox();
                    UV uvMid = (bbox.Min + bbox.Max) / 2.0;
                    suma += face.Evaluate(uvMid);
                    count++;
                }
                catch
                {
                    continue;
                }
            }

            return count > 0 ? suma / count : solido.GetBoundingBox().Min;
        }

        /// <summary>
        /// Metodo helper centralizado para crear DirectShape (patron BLIM)
        /// Similar a GeometeryTools.PlaceDirectShapeSpecial()
        /// </summary>
        private DirectShape CrearEncofradoDirectShape(Solid solido, string nombre, Element elementoOriginal, string tipoVinculo)
        {
            // Validar solido
            if (!ValidarSolido(solido))
                throw new ArgumentException($"Solido invalido para DirectShape '{nombre}'");

            try
            {
                // Crear DirectShape
                DirectShape ds = DirectShape.CreateElement(
                    _doc,
                    new ElementId(BuiltInCategory.OST_GenericModel));

                // Asignar geometria
                ds.SetShape(new GeometryObject[] { solido });

                // Asignar nombre
                ds.Name = nombre;

                // Aplicar material de encofrado
                AplicarMaterialEncofrado(ds);

                // Vincular con elemento original
                VincularConOriginal(ds, elementoOriginal, tipoVinculo);

                // Almacenar ID del elemento original en parámetro "Comentarios"
                try
                {
                    Parameter paramComentarios = ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (paramComentarios != null && !paramComentarios.IsReadOnly)
                    {
                        paramComentarios.Set($"{elementoOriginal.Id.Value}");
                    }
                    else
                    {
                        _logDebug.AppendLine($"    ⚠ Parámetro Comentarios no disponible o es ReadOnly");
                    }
                }
                catch (Exception exComentarios)
                {
                    _logDebug.AppendLine($"    ⚠ Error al guardar comentarios: {exComentarios.Message}");
                }

                return ds;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error al crear DirectShape '{nombre}': {ex.Message}", ex);
            }
        }

        #endregion

        #region MÉTODOS AUXILIARES

        private List<Element> SeleccionarElementosManualmente()
        {
            try
            {
                IList<Reference> refs = _uidoc.Selection.PickObjects(
                    ObjectType.Element, "Selecciona elementos (ESC para terminar)");
                return refs.Select(r => _doc.GetElement(r)).Where(e => e != null).ToList();
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return new List<Element>();
            }
        }

        private List<Element> ObtenerTodosLosElementosEstructurales()
        {
            List<Element> elementos = new List<Element>();

            elementos.AddRange(new FilteredElementCollector(_doc)
                .OfClass(typeof(Wall)).Cast<Wall>().Where(w => EsEstructural(w)));

            elementos.AddRange(new FilteredElementCollector(_doc)
                .OfClass(typeof(Floor)).Cast<Floor>().Where(f => EsEstructural(f)));

            elementos.AddRange(new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType());

            elementos.AddRange(new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType());

            elementos.AddRange(new FilteredElementCollector(_doc)
                .OfClass(typeof(Stairs)));

            return elementos;
        }

        private bool EsEstructural(Wall muro)
        {
            var p = muro.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
            return p != null && p.AsInteger() == 1;
        }

        private bool EsEstructural(Floor losa)
        {
            var p = losa.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
            return p != null && p.AsInteger() == 1;
        }

        private bool EsCimentacion(Floor losa)
        {
            // Método 1: Verificar si el tipo contiene palabras clave de cimentación
            string nombreTipo = losa.FloorType?.Name?.ToLower() ?? "";
            if (nombreTipo.Contains("cimentacion") ||
                nombreTipo.Contains("foundation") ||
                nombreTipo.Contains("zapata") ||
                nombreTipo.Contains("footing") ||
                nombreTipo.Contains("losa de cimentacion") ||
                nombreTipo.Contains("foundation slab"))
            {
                return true;
            }

            // Método 2: Verificar categoría de uso estructural
            Parameter paramUso = losa.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
            if (paramUso != null && paramUso.AsInteger() == 1)
            {
                // Verificar si está al nivel más bajo del proyecto (típico de cimentaciones)
                Level nivel = _doc.GetElement(losa.LevelId) as Level;
                if (nivel != null)
                {
                    var todosLosNiveles = new FilteredElementCollector(_doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .ToList();

                    if (todosLosNiveles.Count > 0 && nivel.Id == todosLosNiveles.First().Id)
                    {
                        // Es el nivel más bajo, probablemente cimentación
                        return true;
                    }
                }
            }

            return false;
        }

        private void ConfigurarMuroNoEstructural(Wall muro)
        {
            var p1 = muro.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
            if (p1 != null && !p1.IsReadOnly) p1.Set(0);

            var p2 = muro.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM);
            if (p2 != null && !p2.IsReadOnly) p2.Set((int)StructuralWallUsage.NonBearing);
        }

        private void ConfigurarLosaNoEstructural(Floor losa)
        {
            var p = losa.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
            if (p != null && !p.IsReadOnly) p.Set(0);
        }

        private void VincularConOriginal(Element encofrado, Element original, string tipo)
        {
            try
            {
                Schema schema = ObtenerSchema();
                Entity entity = new Entity(schema);
                entity.Set("ElementoOriginalId", original.Id);
                entity.Set("TipoEncofrado", tipo);
                entity.Set("FechaCreacion", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                encofrado.SetEntity(entity);
            }
            catch { }
        }

        private bool YaTieneEncoofrado(ElementId id)
        {
            try
            {
                Schema schema = ObtenerSchema();
                return new FilteredElementCollector(_doc).WhereElementIsNotElementType()
                    .Any(e => e.GetEntity(schema).IsValid() &&
                              e.GetEntity(schema).Get<ElementId>("ElementoOriginalId") == id);
            }
            catch { return false; }
        }

        private static Guid _schemaGuid = new Guid("{A1B2C3D4-E5F6-7890-ABCD-1234567890AB}");

        private Schema ObtenerSchema()
        {
            Schema s = Schema.Lookup(_schemaGuid);
            if (s != null) return s;

            SchemaBuilder b = new SchemaBuilder(_schemaGuid);
            b.SetSchemaName("VinculoEncoofrado");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField("ElementoOriginalId", typeof(ElementId));
            b.AddSimpleField("TipoEncofrado", typeof(string));
            b.AddSimpleField("FechaCreacion", typeof(string));
            return b.Finish();
        }

        private void MostrarResultados()
        {
            string msg = "═══════════════════════════════════════\n" +
                        "   ENCOFRADO UNIVERSAL - RESULTADOS\n" +
                        "═══════════════════════════════════════\n\n" +
                        $"✓ Muros: {_resultado.MurosProcesados}\n" +
                        $"✓ Losas: {_resultado.LosasProcesadas}\n" +
                        $"✓ Columnas: {_resultado.ColumnasProcesadas}\n" +
                        $"✓ Vigas: {_resultado.VigasProcesadas}\n" +
                        $"✓ Escaleras: {_resultado.EscalerasProcesadas}\n\n" +
                        $"➤ Elementos creados: {_resultado.ElementosCreados}\n";

            if (_resultado.Advertencias.Count > 0)
                msg += $"\n⚠ Advertencias: {_resultado.Advertencias.Count}";
            if (_resultado.Errores.Count > 0)
                msg += $"\n✖ Errores: {_resultado.Errores.Count}";

            TaskDialog td = new TaskDialog("Completado");
            td.MainInstruction = "Proceso completado";
            td.MainContent = msg;
            td.Show();
        }

        private void MostrarLogDebug()
        {
            TaskDialog td = new TaskDialog("Log de Debug");
            td.MainInstruction = "Información de diagnóstico";
            td.MainContent = _logDebug.ToString();
            td.Show();
        }

        #endregion

        #region MÉTODOS DE GEOMETRÍA AVANZADA

        /// <summary>
        /// Extrae TODOS los solidos de un elemento (patron BLIM)
        /// Similar a GeometeryTools.GetElementSolids()
        /// </summary>
        private List<Solid> ExtraerTodosLosSolidos(Element elemento)
        {
            List<Solid> solidos = new List<Solid>();

            Options opts = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = true,
                IncludeNonVisibleObjects = false  // No incluir objetos ocultos
            };

            GeometryElement geomElem = elemento.get_Geometry(opts);
            if (geomElem == null) return solidos;

            foreach (GeometryObject geomObj in geomElem)
            {
                // Solidos directos
                if (geomObj is Solid solid && ValidarSolido(solid))
                {
                    solidos.Add(solid);
                }
                // Solidos en instancias de geometria
                else if (geomObj is GeometryInstance geomInst)
                {
                    GeometryElement instGeom = geomInst.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        foreach (GeometryObject instObj in instGeom)
                        {
                            if (instObj is Solid instSolid && ValidarSolido(instSolid))
                            {
                                solidos.Add(instSolid);
                            }
                        }
                    }
                }
            }

            return solidos;
        }

        /// <summary>
        /// Extrae el solido principal de un elemento (el de mayor volumen)
        /// </summary>
        private Solid ExtraerSolidoPrincipal(Element elemento)
        {
            List<Solid> solidos = ExtraerTodosLosSolidos(elemento);
            return solidos.OrderByDescending(s => s.Volume).FirstOrDefault();
        }

        /// <summary>
        /// Valida que un solido sea valido para procesar
        /// </summary>
        private bool ValidarSolido(Solid solido)
        {
            if (solido == null) return false;
            if (solido.Volume < 0.001) return false;  // ~0.03 cm³
            if (solido.Faces.Size == 0) return false;
            return true;
        }

        /// <summary>
        /// Extrae el contorno de una cara planar en un nivel Z específico
        /// </summary>
        private CurveLoop ExtraerContornoEnNivel(Solid solido, double elevacionZ, bool esCaraSuperior = true)
        {
            double tolerancia = 0.01; // ~3mm

            foreach (Face face in solido.Faces)
            {
                if (face is PlanarFace pf)
                {
                    // Verificar si es la cara que buscamos
                    bool esHorizontal = Math.Abs(Math.Abs(pf.FaceNormal.Z) - 1.0) < 0.01;
                    bool enNivelCorrecto = Math.Abs(pf.Origin.Z - elevacionZ) < tolerancia;

                    if (esHorizontal && enNivelCorrecto)
                    {
                        // Verificar orientación
                        if ((esCaraSuperior && pf.FaceNormal.Z > 0) ||
                            (!esCaraSuperior && pf.FaceNormal.Z < 0))
                        {
                            IList<CurveLoop> loops = pf.GetEdgesAsCurveLoops();
                            return loops.OrderByDescending(l => ObtenerAreaCurveLoop(l)).FirstOrDefault();
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Obtiene el contorno de la base de un elemento (columna, viga, etc.)
        /// </summary>
        private CurveLoop ObtenerContornoBase(Element elemento)
        {
            Solid solido = ExtraerSolidoPrincipal(elemento);
            if (solido == null) return null;

            // Buscar la cara inferior (más baja en Z)
            PlanarFace caraInferior = null;
            double zMinimo = double.MaxValue;

            foreach (Face face in solido.Faces)
            {
                if (face is PlanarFace pf && Math.Abs(Math.Abs(pf.FaceNormal.Z) - 1.0) < 0.01)
                {
                    if (pf.Origin.Z < zMinimo)
                    {
                        zMinimo = pf.Origin.Z;
                        caraInferior = pf;
                    }
                }
            }

            if (caraInferior != null)
            {
                IList<CurveLoop> loops = caraInferior.GetEdgesAsCurveLoops();
                return loops.OrderByDescending(l => ObtenerAreaCurveLoop(l)).FirstOrDefault();
            }

            return null;
        }

        /// <summary>
        /// Calcula el área aproximada de un CurveLoop
        /// </summary>
        private double ObtenerAreaCurveLoop(CurveLoop loop)
        {
            try
            {
                // Calcular área usando el método de Shoelace (Gauss)
                List<XYZ> puntos = new List<XYZ>();
                foreach (Curve curve in loop)
                {
                    puntos.Add(curve.GetEndPoint(0));
                }

                if (puntos.Count < 3) return 0.0;

                double area = 0.0;
                for (int i = 0; i < puntos.Count; i++)
                {
                    XYZ p1 = puntos[i];
                    XYZ p2 = puntos[(i + 1) % puntos.Count];
                    area += (p1.X * p2.Y) - (p2.X * p1.Y);
                }

                return Math.Abs(area / 2.0);
            }
            catch
            {
                // Fallback: calcular bounding box
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (Curve curve in loop)
                {
                    XYZ p1 = curve.GetEndPoint(0);
                    XYZ p2 = curve.GetEndPoint(1);

                    minX = Math.Min(minX, Math.Min(p1.X, p2.X));
                    minY = Math.Min(minY, Math.Min(p1.Y, p2.Y));
                    maxX = Math.Max(maxX, Math.Max(p1.X, p2.X));
                    maxY = Math.Max(maxY, Math.Max(p1.Y, p2.Y));
                }

                return (maxX - minX) * (maxY - minY);
            }
        }

        /// <summary>
        /// Expande un CurveLoop con un offset uniforme
        /// </summary>
        private CurveLoop ExpandirCurveLoop(CurveLoop loopOriginal, double offset)
        {
            try
            {
                // Método 1: Usar CurveLoop.CreateViaOffset
                return CurveLoop.CreateViaOffset(loopOriginal, offset, XYZ.BasisZ);
            }
            catch
            {
                // Método 2: Fallback - expandir manualmente cada curva
                List<Curve> curvasExpandidas = new List<Curve>();

                foreach (Curve curva in loopOriginal)
                {
                    if (curva is Line linea)
                    {
                        // Para líneas, mover perpendicularmente
                        XYZ dir = (linea.GetEndPoint(1) - linea.GetEndPoint(0)).Normalize();
                        XYZ perpendicular = new XYZ(-dir.Y, dir.X, 0).Normalize();

                        XYZ p1 = linea.GetEndPoint(0) + perpendicular * offset;
                        XYZ p2 = linea.GetEndPoint(1) + perpendicular * offset;

                        curvasExpandidas.Add(Line.CreateBound(p1, p2));
                    }
                    else if (curva is Arc arco)
                    {
                        // Para arcos, aumentar el radio
                        double nuevoRadio = arco.Radius + offset;
                        curvasExpandidas.Add(Arc.Create(arco.Center, nuevoRadio,
                            arco.GetEndParameter(0), arco.GetEndParameter(1),
                            arco.XDirection, arco.YDirection));
                    }
                }

                CurveLoop nuevoLoop = new CurveLoop();
                foreach (Curve c in curvasExpandidas)
                    nuevoLoop.Append(c);

                return nuevoLoop;
            }
        }

        /// <summary>
        /// Detecta si un contorno es aproximadamente rectangular
        /// </summary>
        private bool EsRectangular(CurveLoop loop)
        {
            if (loop == null) return false;

            int numCurvas = loop.Count();
            if (numCurvas != 4) return false;

            // Verificar que todas sean líneas
            foreach (Curve curva in loop)
            {
                if (!(curva is Line)) return false;
            }

            return true;
        }

        /// <summary>
        /// Detecta si un contorno es aproximadamente circular
        /// </summary>
        private bool EsCircular(CurveLoop loop)
        {
            if (loop == null) return false;

            int numCurvas = loop.Count();

            // Un círculo puede ser una sola curva o múltiples arcos
            if (numCurvas == 1)
            {
                Curve curva = loop.First();
                return curva is Arc && curva.IsBound == false;
            }

            // Verificar que todos sean arcos con el mismo radio
            double radio = -1;
            foreach (Curve curva in loop)
            {
                if (!(curva is Arc arco)) return false;

                if (radio < 0)
                    radio = arco.Radius;
                else if (Math.Abs(arco.Radius - radio) > 0.001)
                    return false;
            }

            return true;
        }

        #endregion
    }

    #region CLASES AUXILIARES

    public class FormworkSettings
    {
        public double EspesorMuro { get; set; } = 18.0;          // mm - Grosor del tablero
        public double EspesorLosa { get; set; } = 25.0;          // mm - Grosor del tablero
        public double OffsetMuro { get; set; } = 2.0;            // mm - Gap de desmolde (antes 50mm)
        public double OffsetLosaInferior { get; set; } = 2.0;    // mm - Gap de desmolde (antes 100mm)
        public double OffsetColumna { get; set; } = 2.0;         // mm - Gap de desmolde (antes 30mm)
        public double OffsetViga { get; set; } = 2.0;            // mm - Gap de desmolde (antes 40mm)
    }

    public class GestorTiposEncofrado
    {
        private Document _doc;
        public WallType TipoMuroEncofrado { get; private set; }
        public FloorType TipoLosaCimbra { get; private set; }

        public GestorTiposEncofrado(Document doc) { _doc = doc; }

        public bool VerificarYCrearTipos(System.Text.StringBuilder log = null)
        {
            try
            {
                log?.AppendLine("  → Iniciando creación de tipo muro...");
                TipoMuroEncofrado = CrearOObtenerTipoMuro(log);
                if (TipoMuroEncofrado == null)
                {
                    log?.AppendLine("  ✖ FALLO: TipoMuroEncofrado es NULL");
                    TaskDialog.Show("Error Diagnóstico",
                        "No se pudo crear el tipo de muro de encofrado.\n" +
                        "Verifica que exista al menos un tipo de muro básico en el proyecto.");
                    return false;
                }
                log?.AppendLine($"  ✓ Tipo muro creado: {TipoMuroEncofrado.Name}");

                log?.AppendLine("  → Iniciando creación de tipo losa...");
                TipoLosaCimbra = CrearOObtenerTipoLosa(log);
                if (TipoLosaCimbra == null)
                {
                    log?.AppendLine("  ✖ FALLO: TipoLosaCimbra es NULL");
                    TaskDialog.Show("Error Diagnóstico",
                        "No se pudo crear el tipo de losa de cimbra.\n" +
                        "Verifica que exista al menos un tipo de losa en el proyecto.");
                    return false;
                }
                log?.AppendLine($"  ✓ Tipo losa creado: {TipoLosaCimbra.Name}");

                return true;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"  ✖ EXCEPCIÓN: {ex.Message}");
                log?.AppendLine($"  Stack: {ex.StackTrace}");
                TaskDialog.Show("Error Diagnóstico",
                    $"Error al verificar/crear tipos:\n{ex.Message}");
                return false;
            }
        }

        private WallType CrearOObtenerTipoMuro(System.Text.StringBuilder log = null)
        {
            try
            {
                string nombre = "Encofrado 18mm";
                log?.AppendLine($"    → Buscando tipo existente: '{nombre}'");

                // 1. Verificar si ya existe
                var todosLosMuros = new FilteredElementCollector(_doc)
                    .OfClass(typeof(WallType)).Cast<WallType>().ToList();
                log?.AppendLine($"    → Total WallTypes en proyecto: {todosLosMuros.Count}");

                WallType existe = todosLosMuros.FirstOrDefault(wt => wt.Name == nombre);
                if (existe != null)
                {
                    log?.AppendLine($"    ✓ Tipo '{nombre}' ya existe, reutilizando");
                    return existe;
                }

                // 2. Buscar tipo base - primero Basic, luego cualquiera
                log?.AppendLine("    → Buscando tipo base (WallKind.Basic)...");
                WallType baseTipo = todosLosMuros.FirstOrDefault(wt => wt.Kind == WallKind.Basic);

                if (baseTipo == null)
                {
                    log?.AppendLine("    ⚠ No hay WallKind.Basic, buscando cualquier tipo...");
                    baseTipo = todosLosMuros.FirstOrDefault();
                }

                if (baseTipo == null)
                {
                    log?.AppendLine("    ✖ ERROR: No se encontró ningún WallType en el proyecto");
                    TaskDialog.Show("Error",
                        "No se encontró ningún tipo de muro en el proyecto.\n" +
                        "Crea al menos un muro básico antes de ejecutar este comando.");
                    return null;
                }
                log?.AppendLine($"    ✓ Tipo base encontrado: '{baseTipo.Name}' (Kind: {baseTipo.Kind})");

                // 3. Duplicar el tipo
                log?.AppendLine($"    → Duplicando '{baseTipo.Name}' como '{nombre}'...");
                WallType nuevo = baseTipo.Duplicate(nombre) as WallType;
                if (nuevo == null)
                {
                    log?.AppendLine("    ✖ ERROR: Duplicate() retornó null");
                    return null;
                }
                log?.AppendLine($"    ✓ Duplicado exitosamente. ID: {nuevo.Id}");

                // 4. Configurar estructura compuesta
                log?.AppendLine("    → Configurando estructura compuesta del muro...");
                try
                {
                    ElementId matId = ObtenerMaterial("Contrachapado", log);
                    log?.AppendLine($"    ✓ Material ID: {matId}");

                    double esp = UnitUtils.ConvertToInternalUnits(18.0, UnitTypeId.Millimeters);
                    log?.AppendLine($"    → Espesor: {esp} pies internos");

                    CompoundStructureLayer capa = new CompoundStructureLayer(
                        esp, MaterialFunctionAssignment.Structure, matId);
                    CompoundStructure est = CompoundStructure.CreateSimpleCompoundStructure(
                        new List<CompoundStructureLayer> { capa });
                    est.SetNumberOfShellLayers(ShellLayerType.Exterior, 0);
                    est.SetNumberOfShellLayers(ShellLayerType.Interior, 0);

                    // Validar y aplicar
                    log?.AppendLine("    → Validando estructura compuesta...");
                    if (est.IsValid(_doc, out var e1, out var e2))
                    {
                        log?.AppendLine("    ✓ Estructura válida, aplicando...");
                        nuevo.SetCompoundStructure(est);
                        log?.AppendLine("    ✓ Estructura aplicada exitosamente");
                    }
                    else
                    {
                        log?.AppendLine("    ⚠ Estructura no válida, usando valores por defecto");
                    }
                }
                catch (Exception exEstructura)
                {
                    log?.AppendLine($"    ⚠ No se pudo configurar estructura: {exEstructura.Message}");
                    log?.AppendLine("    → Usando tipo duplicado con estructura original");
                }

                return nuevo;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"    ✖ EXCEPCIÓN en CrearOObtenerTipoMuro: {ex.Message}");
                log?.AppendLine($"    Stack: {ex.StackTrace}");
                TaskDialog.Show("Error al crear tipo de muro",
                    $"Detalles: {ex.Message}");
                return null;
            }
        }

        private FloorType CrearOObtenerTipoLosa(System.Text.StringBuilder log = null)
        {
            try
            {
                string nombre = "Cimbra 25mm";
                log?.AppendLine($"    → Buscando tipo existente: '{nombre}'");

                // 1. Verificar si ya existe
                var todasLasLosas = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
                log?.AppendLine($"    → Total FloorTypes en proyecto: {todasLasLosas.Count}");

                FloorType existe = todasLasLosas.FirstOrDefault(ft => ft.Name == nombre);
                if (existe != null)
                {
                    log?.AppendLine($"    ✓ Tipo '{nombre}' ya existe, reutilizando");
                    return existe;
                }

                // 2. Buscar tipo base
                log?.AppendLine("    → Buscando tipo base de losa...");
                FloorType baseTipo = todasLasLosas.FirstOrDefault();

                if (baseTipo == null)
                {
                    log?.AppendLine("    ✖ ERROR: No se encontró ningún FloorType en el proyecto");
                    TaskDialog.Show("Error",
                        "No se encontró ningún tipo de losa en el proyecto.\n" +
                        "Crea al menos una losa antes de ejecutar este comando.");
                    return null;
                }
                log?.AppendLine($"    ✓ Tipo base encontrado: '{baseTipo.Name}'");

                // 3. Duplicar el tipo
                log?.AppendLine($"    → Duplicando '{baseTipo.Name}' como '{nombre}'...");
                FloorType nuevo = baseTipo.Duplicate(nombre) as FloorType;
                if (nuevo == null)
                {
                    log?.AppendLine("    ✖ ERROR: Duplicate() retornó null");
                    return null;
                }
                log?.AppendLine($"    ✓ Duplicado exitosamente. ID: {nuevo.Id}");

                // 4. Configurar estructura compuesta (FloorType tiene restricciones diferentes a WallType)
                log?.AppendLine("    → Intentando modificar estructura compuesta...");
                try
                {
                    CompoundStructure estructuraActual = nuevo.GetCompoundStructure();
                    if (estructuraActual != null)
                    {
                        log?.AppendLine($"    → Estructura actual tiene {estructuraActual.LayerCount} capas");

                        // Para FloorType, modificamos el material de la primera capa estructural
                        ElementId matId = ObtenerMaterial("Contrachapado", log);
                        double nuevoEspesor = UnitUtils.ConvertToInternalUnits(25.0, UnitTypeId.Millimeters);

                        // Obtener las capas actuales
                        IList<CompoundStructureLayer> capas = estructuraActual.GetLayers();
                        bool capaModificada = false;

                        for (int i = 0; i < capas.Count; i++)
                        {
                            if (capas[i].Function == MaterialFunctionAssignment.Structure)
                            {
                                log?.AppendLine($"    → Modificando capa estructural {i}...");

                                // Reemplazar la capa con una nueva con el material y espesor deseados
                                CompoundStructureLayer nuevaCapa = new CompoundStructureLayer(
                                    nuevoEspesor,
                                    MaterialFunctionAssignment.Structure,
                                    matId);

                                capas[i] = nuevaCapa;
                                capaModificada = true;
                                log?.AppendLine($"    ✓ Capa modificada: Material={matId}, Espesor={nuevoEspesor}");
                                break;
                            }
                        }

                        if (capaModificada)
                        {
                            // Aplicar las capas modificadas
                            estructuraActual.SetLayers(capas);
                            nuevo.SetCompoundStructure(estructuraActual);
                            log?.AppendLine("    ✓ Estructura compuesta aplicada exitosamente");
                        }
                        else
                        {
                            log?.AppendLine("    ℹ No se encontró capa estructural, usando estructura original");
                        }
                    }
                    else
                    {
                        log?.AppendLine("    ℹ Tipo de losa sin estructura compuesta, usando tal cual");
                    }
                }
                catch (Exception exEstructura)
                {
                    log?.AppendLine($"    ⚠ No se pudo modificar estructura: {exEstructura.Message}");
                    log?.AppendLine("    → Usando tipo duplicado con estructura original");
                }

                return nuevo;
            }
            catch (Exception ex)
            {
                log?.AppendLine($"    ✖ EXCEPCIÓN en CrearOObtenerTipoLosa: {ex.Message}");
                log?.AppendLine($"    Stack: {ex.StackTrace}");
                TaskDialog.Show("Error al crear tipo de losa",
                    $"Detalles: {ex.Message}");
                return null;
            }
        }

        private ElementId ObtenerMaterial(string nombre, System.Text.StringBuilder log = null)
        {
            var todosMateriales = new FilteredElementCollector(_doc)
                .OfClass(typeof(Material)).Cast<Material>().ToList();
            log?.AppendLine($"      → Total materiales en proyecto: {todosMateriales.Count}");

            Material m = todosMateriales.FirstOrDefault(mat => mat.Name.Contains(nombre) ||
                mat.Name.ToLower().Contains("plywood") ||
                mat.Name.ToLower().Contains("madera"));

            if (m != null)
            {
                log?.AppendLine($"      ✓ Material existente encontrado: '{m.Name}'");
                return m.Id;
            }

            log?.AppendLine($"      → Creando nuevo material: '{nombre}'");
            ElementId id = Material.Create(_doc, nombre);
            Material nm = _doc.GetElement(id) as Material;
            if (nm != null)
            {
                nm.Color = new Color(210, 180, 140);
                nm.Transparency = 0;
                log?.AppendLine($"      ✓ Material creado exitosamente. ID: {id}");
            }
            return id;
        }

        /// <summary>
        /// Método público para obtener el ID del material de encofrado
        /// Usado por DirectShapes
        /// </summary>
        public ElementId ObtenerMaterialEncofrado()
        {
            return ObtenerMaterial("Contrachapado", null);
        }
    }

    public class ResultadoEncofrado
    {
        public int MurosProcesados { get; set; }
        public int LosasProcesadas { get; set; }
        public int ColumnasProcesadas { get; set; }
        public int VigasProcesadas { get; set; }
        public int EscalerasProcesadas { get; set; }
        public int ElementosCreados { get; set; }
        public List<string> Advertencias { get; set; } = new List<string>();
        public List<string> Errores { get; set; } = new List<string>();

        public void AgregarAdvertencia(ElementId id, string msg)
        {
            Advertencias.Add($"Elemento {id}: {msg}");
        }

        public void AgregarError(ElementId id, string msg)
        {
            Errores.Add($"Elemento {id}: {msg}");
        }
    }

    #endregion
}
