using Autodesk.Revit.UI;
using ClosestGridsAddinVANILLA.ACERO;
using ClosestGridsAddinVANILLA.Commands;
using ClosestGridsAddinVANILLA.DWG_IMPORT;
using ClosestGridsAddinVANILLA.ENCOFRADO;
using ClosestGridsAddinVANILLA;
using ClosestGridsAddinVANILLA.SANITARIAS;
using ClosestGridsAddinVANILLA.ParameterTransfer;
using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace ClosestGridsAddinVANILLA;

public class Application : IExternalApplication
{
    /// <summary>
    /// Carga un icono PNG desde la carpeta Resources
    /// </summary>
    private static BitmapImage LoadIconFromFile(string fileName)
    {
        try
        {
            string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string iconPath = Path.Combine(assemblyPath, "Resources", fileName);

            if (File.Exists(iconPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            else
            {
                // Fallback: crear icono de color si no existe el archivo
                return CreateColorIcon(128, 128, 128);
            }
        }
        catch
        {
            // En caso de error, devolver icono gris
            return CreateColorIcon(128, 128, 128);
        }
    }

    /// <summary>
    /// Crea un BitmapSource de color sólido (fallback)
    /// </summary>
    private static BitmapImage CreateColorIcon(byte r, byte g, byte b, int size = 32)
    {
        int stride = size * 4;
        byte[] pixels = new byte[size * stride];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;       // Blue
            pixels[i + 1] = g;   // Green
            pixels[i + 2] = r;   // Red
            pixels[i + 3] = 255; // Alpha
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        // Convertir BitmapSource a BitmapImage para compatibilidad
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new MemoryStream())
        {
            encoder.Save(stream);
            stream.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = stream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }
    }

    public Result OnStartup(UIControlledApplication application)
    {
        // IMPORTANTE: NO validar licencia aquí para permitir que el add-in se cargue
        // La validación se realizará cuando el usuario ejecute un comando

        // Verificar actualizaciones en segundo plano
        CheckForUpdatesAsync();

        // Crear la pestaña personalizada "BIMS VANILLA"
        string tabName = "BIMS VANILLA";

        try
        {
            application.CreateRibbonTab(tabName);
        }
        catch
        {
            // La pestaña ya existe, continuar
        }

        // ===================================
        // PANEL 1: HERRAMIENTAS
        // ===================================
        var panelHerramientas = application.CreateRibbonPanel(tabName, "Herramientas");

        

        var btnEjesCercanos = new PushButtonData(
            "AsignarRejillasButton",
            "Asignar\nRejillas",
            typeof(Application).Assembly.Location,
            typeof(AsignarRejillasATodosCommand).FullName);
        btnEjesCercanos.ToolTip = "Asigna rejillas automáticamente a todos los elementos del proyecto";
        btnEjesCercanos.LongDescription = "Procesa automáticamente todos los elementos del proyecto y les asigna las rejillas más cercanas a sus parámetros correspondientes.\n\nCategorías procesadas:\n• Columnas, Muros, Vigas, Suelos\n• Puertas, Ventanas, Escaleras\n• Mobiliario, Aparatos MEP\n• Y 16 categorías más\n\nResultados:\n• Asignación automática sin intervención\n• Informe detallado por categoría\n• Parámetros: UBICACIÓN POR REJILLAS, REJILLA INICIAL, REJILLA FINAL";
        btnEjesCercanos.LargeImage = LoadIconFromFile("btnEjesCercanos.png");
        panelHerramientas.AddItem(btnEjesCercanos);

        var btnTransferParametros = new PushButtonData(
            "TransferParametrosButton",
            "Transferir\nParámetros",
            typeof(Application).Assembly.Location,
            typeof(TransferParametrosCommand).FullName);
        btnTransferParametros.ToolTip = "Transfiere valores entre parámetros";
        btnTransferParametros.LongDescription = "Transfiere valores de un parámetro a otro en elementos seleccionados o en todo el modelo.";
        btnTransferParametros.LargeImage = LoadIconFromFile("btnTransferirParametros.png");
        panelHerramientas.AddItem(btnTransferParametros);

        var btnTransferirId = new PushButtonData(
            "TransferirIdButton",
            "Transferir\nID",
            typeof(Application).Assembly.Location,
            typeof(TransferirIdElementoCommand).FullName);
        btnTransferirId.ToolTip = "Transfiere el ID del elemento al parámetro OIP_ID_BIM";
        btnTransferirId.LongDescription = "Asigna el ID único de cada elemento seleccionado al parámetro 'OIP_ID_BIM'.\n\nÚtil para:\n• Trazabilidad de elementos\n• Vinculación con sistemas externos\n• Identificación única de componentes";
        btnTransferirId.LargeImage = LoadIconFromFile("btnTransferirId.png");
        panelHerramientas.AddItem(btnTransferirId);

        var btnMarcaAnfitrion = new PushButtonData(
            "MarcaAnfitrionButton",
            "Marca\nAnfitrión",
            typeof(Application).Assembly.Location,
            typeof(TransferirMarcaAnfitrionCommand).FullName);
        btnMarcaAnfitrion.ToolTip = "Transfiere la marca del elemento anfitrión";
        btnMarcaAnfitrion.LongDescription = "Lee el parámetro 'Host_ID', busca ese elemento y transfiere su marca al parámetro 'Host_Name'.\n\nProceso:\n1. Lee Host_ID del elemento\n2. Busca el elemento anfitrión\n3. Obtiene su marca\n4. La escribe en Host_Name";
        btnMarcaAnfitrion.LargeImage = LoadIconFromFile("btnMarcaAnfitrion.png");
        panelHerramientas.AddItem(btnMarcaAnfitrion);

        var btnAsignarHost = new PushButtonData(
            "AsignarHostButton",
            "Asignar\nHost ID",
            typeof(Application).Assembly.Location,
            typeof(AsignarHostIdCommand).FullName);
        btnAsignarHost.ToolTip = "Asigna el ID del elemento estructural que más cubre";
        btnAsignarHost.LongDescription = "Encuentra el elemento estructural que más área cubre de un muro/suelo y asigna su ID al parámetro 'Host_ID'.\n\nElementos estructurales considerados:\n• Vigas, Columnas, Losas\n• Cimentaciones, Muros estructurales\n\nSe asigna a: Muros arquitectónicos y suelos";
        btnAsignarHost.LargeImage = LoadIconFromFile("btnAsignarHost.png");
        panelHerramientas.AddItem(btnAsignarHost);

        var btnAmbiente = new PushButtonData(
            "AmbienteButton",
            "Asignar\nAmbiente",
            typeof(Application).Assembly.Location,
            typeof(AsignarAmbienteCommand).FullName);
        btnAmbiente.ToolTip = "Asigna el nombre de una habitación a sus elementos límite";
        btnAmbiente.LongDescription = "Asigna el nombre y número de una habitación seleccionada a todos los muros y suelos que forman sus límites en el parámetro 'Ambiente'.\n\nUso:\n1. Seleccione una habitación (Room)\n2. Ejecute el comando\n3. Se asigna automáticamente a muros y suelos del perímetro";
        btnAmbiente.LargeImage = LoadIconFromFile("btnAmbiente.png");
        panelHerramientas.AddItem(btnAmbiente);

        var btnDWGExtractor = new PushButtonData(
            "DWGExtractorButton",
            "Importar\nDWG",
            typeof(Application).Assembly.Location,
            typeof(DWGBlockExtractorCommand).FullName);
        btnDWGExtractor.ToolTip = "Extrae bloques de archivos DWG importados";
        btnDWGExtractor.LongDescription = "Convierte bloques de archivos DWG de SolidWorks a DirectShapes en Revit. Soporta geometría compleja, superficies sin volumen, y crea meshes individuales cuando es necesario.";
        btnDWGExtractor.LargeImage = LoadIconFromFile("btnImportarDWG.png");
        panelHerramientas.AddItem(btnDWGExtractor);

        var btnCalcularVolumenes = new PushButtonData(
            "CalcularVolumenesButton",
            "Calcular\nVolúmenes",
            typeof(Application).Assembly.Location,
            typeof(CalcularVolumenesDirectShapesCommand).FullName);
        btnCalcularVolumenes.ToolTip = "Calcula volúmenes de DirectShapes y asigna IDs por grupo";
        btnCalcularVolumenes.LongDescription = "Analiza todos los DirectShapes del proyecto, calcula sus volúmenes, los agrupa por volumen similar (tolerancia: 0.0001 m³), y asigna:\n• Parámetro 'Comentarios': Volumen en m³\n• Parámetro 'Mark': ID único del grupo de volumen";
        btnCalcularVolumenes.LargeImage = LoadIconFromFile("btnCalcularVolumenes.png");
        panelHerramientas.AddItem(btnCalcularVolumenes);

        // ===================================
        // PANEL 2: ENCOFRADO
        // ===================================
        var panelEncofrado = application.CreateRibbonPanel(tabName, "Encofrado");

        var btnFormwBims = new PushButtonData(
            "FormwBimsButton",
            "FORMWBIMS",
            typeof(Application).Assembly.Location,
            typeof(FormwBimsCommand).FullName);
        btnFormwBims.ToolTip = "Encofrado Inteligente con Selección Guiada";
        btnFormwBims.LongDescription = "Sistema avanzado de encofrado con workflow guiado:\n• Seleccione categorías específicas a procesar\n• Filtro inteligente de elementos\n• Descuentos automáticos por contactos\n• Estadísticas detalladas por categoría\n• Ideal para proyectos grandes";
        btnFormwBims.LargeImage = LoadIconFromFile("btnFormwBims.png");
        panelEncofrado.AddItem(btnFormwBims);

        var btnEncofradoAutomatico = new PushButtonData(
            "EncofradoAutomaticoButton",
            "Encofrado\nAutomatizado",
            typeof(Application).Assembly.Location,
            typeof(EncofradoAutomaticoCommand).FullName);
        btnEncofradoAutomatico.ToolTip = "Sistema Integrado de Encofrado Automatizado";
        btnEncofradoAutomatico.LongDescription = "Sistema completamente automatizado de encofrado:\n\n" +
            "✅ CARACTERÍSTICAS:\n" +
            "• Clasificación inteligente por tipo de elemento\n" +
            "• Extrusión siempre hacia afuera del elemento\n" +
            "• Recortes automáticos por elementos adyacentes\n" +
            "• Conversión directa a Wall/Floor nativos\n" +
            "• Curvas recortadas preservadas\n\n" +
            "📋 REGLAS AUTOMÁTICAS:\n" +
            "• Columnas → caras verticales → Muros\n" +
            "• Vigas → laterales=Muros, inferior=Suelo\n" +
            "• Muros → laterales → Muros\n" +
            "• Losas → inferior → Suelo\n" +
            "• Escaleras → verticales=Muros, inclinadas=Suelos\n\n" +
            "⚡ FLUJO:\n" +
            "1. Seleccione tipos de muro y suelo\n" +
            "2. Seleccione elementos estructurales\n" +
            "3. Sistema crea Wall/Floor nativos automáticamente";
        btnEncofradoAutomatico.LargeImage = LoadIconFromFile("btnEncofradoAutomatico.png");
        panelEncofrado.AddItem(btnEncofradoAutomatico);

        var btnFormwBimsAutoConvert = new PushButtonData(
            "FormwBimsAutoConvertButton",
            "FORMWBIMS\nAuto-Convert",
            typeof(Application).Assembly.Location,
            typeof(FormwBimsAutoConvertCommand).FullName);
        btnFormwBimsAutoConvert.ToolTip = "Encofrado Inteligente + Auto-Conversión a Wall/Floor";
        btnFormwBimsAutoConvert.LongDescription = "Proceso combinado automático (2 en 1):\n\n1️⃣ FORMWBIMS: Crea encofrados DirectShapes\n   • Selección guiada por categorías\n   • Descuentos automáticos\n   • Estadísticas detalladas\n\n2️⃣ AUTO-CONVERSIÓN: Convierte a Wall/Floor\n   • 5 métodos avanzados de conversión\n   • Orientación automática\n   • Parámetros nativos de Revit\n\n⚡ Ideal para flujo de trabajo rápido sin pasos manuales";
        btnFormwBimsAutoConvert.LargeImage = LoadIconFromFile("btnEncofradoMultiple.png");
        panelEncofrado.AddItem(btnFormwBimsAutoConvert);

        var btnConvertirEncofrado = new PushButtonData(
            "ConvertirEncofradoButton",
            "Convertir a\nWall/Floor",
            typeof(Application).Assembly.Location,
            typeof(ConvertGenericToWallOrFloorCommand).FullName);
        btnConvertirEncofrado.ToolTip = "Convierte encofrados (DirectShapes) a Walls o Floors nativos";
        btnConvertirEncofrado.LongDescription = "Convierte los DirectShapes de encofrado a elementos nativos de Revit (Walls o Floors) para obtener:\n• Parámetros nativos de área y volumen\n• Mejor integración con schedules\n• Cálculos automáticos de Revit\n\nWorkflow: FORMWBIMS → Convertir → Verificar parámetros";
        btnConvertirEncofrado.LargeImage = LoadIconFromFile("btnConvertir.png");
        panelEncofrado.AddItem(btnConvertirEncofrado);

        var btnSplitDirectShape = new PushButtonData(
            "SplitDirectShapeButton",
            "Dividir\nDirectShape",
            typeof(Application).Assembly.Location,
            typeof(SplitDirectShapeCommand).FullName);
        btnSplitDirectShape.ToolTip = "Divide un DirectShape en todas sus piezas individuales";
        btnSplitDirectShape.LongDescription = "Separa un DirectShape compuesto en elementos individuales:\n\n• Selecciona un DirectShape\n• Extrae todos los sólidos que contiene\n• Crea DirectShapes separados para cada pieza\n• Conserva o elimina el original\n\n✅ Útil para DirectShapes complejos con múltiples geometrías";
        btnSplitDirectShape.LargeImage = LoadIconFromFile("btnSplit.png");
        panelEncofrado.AddItem(btnSplitDirectShape);

        // ===================================
        // PANEL 3: ACEROS DE REFUERZO
        // ===================================
        var panelAceros = application.CreateRibbonPanel(tabName, "Aceros de Refuerzo");
        
        var btnAceroColumnas = new PushButtonData(
            "AceroColumnasButton",
            "Acero\nColumnas",
            typeof(Application).Assembly.Location,
            typeof(ACEROCOLUMNAS).FullName);
        btnAceroColumnas.ToolTip = "Acero de refuerzo para columnas";
        btnAceroColumnas.LongDescription = "Calcula y coloca refuerzo longitudinal y transversal en columnas estructurales.";
        btnAceroColumnas.AvailabilityClassName = typeof(PlaceholderAvailability).FullName;
        btnAceroColumnas.LargeImage = LoadIconFromFile("btnAceroColumnas.png");
        panelAceros.AddItem(btnAceroColumnas);

        var btnAceroVigas = new PushButtonData(
            "AceroVigasButton",
            "Acero\nVigas",
            typeof(Application).Assembly.Location,
            typeof(ACEROVIGAS).FullName);
        btnAceroVigas.ToolTip = "Acero de refuerzo para vigas";
        btnAceroVigas.LongDescription = "Calcula y coloca refuerzo longitudinal y estribos en vigas estructurales.";
        btnAceroVigas.AvailabilityClassName = typeof(PlaceholderAvailability).FullName;
        btnAceroVigas.LargeImage = LoadIconFromFile("btnAceroVigas.png");
        panelAceros.AddItem(btnAceroVigas);

        var btnAceroMuros = new PushButtonData(
            "AceroMurosButton",
            "Acero\nMuros",
            typeof(Application).Assembly.Location,
            typeof(ACEROMUROS).FullName);
        btnAceroMuros.ToolTip = "Acero de refuerzo para muros";
        btnAceroMuros.LongDescription = "Calcula y coloca refuerzo en muros estructurales con aberturas.";
        btnAceroMuros.AvailabilityClassName = typeof(PlaceholderAvailability).FullName;
        btnAceroMuros.LargeImage = LoadIconFromFile("btnAceroMuros.png");
        panelAceros.AddItem(btnAceroMuros);

        var btnAceroLosas = new PushButtonData(
            "AceroLosasButton",
            "Acero\nLosas",
            typeof(Application).Assembly.Location,
            typeof(ACEROLOSASYCIMIENTOS).FullName);
        btnAceroLosas.ToolTip = "Acero de refuerzo para losas";
        btnAceroLosas.LongDescription = "Calcula y coloca refuerzo en losas y cimentaciones.";
        btnAceroLosas.AvailabilityClassName = typeof(PlaceholderAvailability).FullName;
        btnAceroLosas.LargeImage = LoadIconFromFile("btnAceroLosas.png");
        panelAceros.AddItem(btnAceroLosas);


        // ===================================
        // PANEL 4: INSTALACIONES SANITARIAS
        // ===================================
        var panelSanitarias = application.CreateRibbonPanel(tabName, "IISS");

        var btnRegistros = new PushButtonData(
            "Sanitarias",
            "Instalaciones\nSanitarias",
            typeof(Application).Assembly.Location,
            typeof(DimRegistrosSAnitarios).FullName);
        btnRegistros.ToolTip = "Registros Sanitarios";
        btnRegistros.LongDescription = "Dimensiona los registros sanitarios";
        btnRegistros.AvailabilityClassName = typeof(PlaceholderAvailability).FullName;
        btnRegistros.LargeImage = LoadIconFromFile("btnSanitarias.png");
        panelSanitarias.AddItem(btnRegistros);

        var btnCalcularLongitud = new PushButtonData(
            "CalcularLongitudTuberias",
            "Calcular\nLongitud",
            typeof(Application).Assembly.Location,
            typeof(CalcularLongitudTuberiasCommand).FullName);
        btnCalcularLongitud.ToolTip = "Calcula la longitud total de tuberías incluyendo distancias a aparatos";
        btnCalcularLongitud.LongDescription = "Calcula la longitud total de las tuberías seleccionadas, sumando:\n• Longitud de la tubería\n• Distancias a aparatos sanitarios cercanos\n\nResultado se escribe en el parámetro 'Comentarios' en metros.";
        btnCalcularLongitud.LargeImage = LoadIconFromFile("btnCalcularLongitud.png");
        panelSanitarias.AddItem(btnCalcularLongitud);

        var btnCotaTapa = new PushButtonData(
            "AsignarCotaTapa",
            "Asignar\nCota Tapa",
            typeof(Application).Assembly.Location,
            typeof(AsignarCotaTapaCommand).FullName);
        btnCotaTapa.ToolTip = "Asigna la cota superior (Z máxima) a aparatos sanitarios";
        btnCotaTapa.LongDescription = "Asigna la coordenada Z máxima del BoundingBox al parámetro 'OIP_COTA_TAPA' de los aparatos sanitarios seleccionados.\n\nEl valor se convierte automáticamente de pies a metros.";
        btnCotaTapa.LargeImage = LoadIconFromFile("btnCotaTapa.png");
        panelSanitarias.AddItem(btnCotaTapa);

        return Result.Succeeded;
    }

    /// <summary>
    /// Verifica actualizaciones en segundo plano y notifica al usuario
    /// </summary>
    private async void CheckForUpdatesAsync()
    {
        try
        {
            var updateChecker = new Services.UpdateChecker();
            var updateInfo = await updateChecker.CheckForUpdatesAsync();

            if (updateInfo.IsAvailable)
            {
                System.Diagnostics.Debug.WriteLine($"🔔 Actualización disponible: {updateInfo.LatestVersion}");

                // Notificar al usuario (usando TaskDialog sincronizado en el thread de UI)
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowUpdateNotification(updateInfo, updateChecker);
                }));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"✅ Versión actual ({updateInfo.CurrentVersion}) está actualizada");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al verificar actualizaciones: {ex.Message}");
        }
    }

    /// <summary>
    /// Muestra notificación de actualización disponible
    /// </summary>
    private void ShowUpdateNotification(Services.UpdateInfo updateInfo, Services.UpdateChecker updateChecker)
    {
        try
        {
            string message = $"🎉 Nueva versión disponible\n\n" +
                           $"Versión actual: {updateInfo.CurrentVersion}\n" +
                           $"Nueva versión: {updateInfo.LatestVersion}\n\n";

            if (!string.IsNullOrEmpty(updateInfo.ReleaseNotes))
            {
                message += $"Novedades:\n{updateInfo.ReleaseNotes}\n\n";
            }

            if (updateInfo.IsMandatory)
            {
                message += "⚠️ Esta actualización es obligatoria.\n\n";
            }

            message += "¿Desea descargar e instalar la actualización ahora?\n\n" +
                      "La actualización se aplicará cuando cierre Revit.";

            var result = TaskDialog.Show(
                "Actualización Disponible",
                message,
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                TaskDialogResult.Yes);

            if (result == TaskDialogResult.Yes)
            {
                DownloadUpdateAsync(updateInfo, updateChecker);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al mostrar notificación: {ex.Message}");
        }
    }

    /// <summary>
    /// Descarga la actualización con progress
    /// </summary>
    private async void DownloadUpdateAsync(Services.UpdateInfo updateInfo, Services.UpdateChecker updateChecker)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Descargando actualización...");

            var progress = new Progress<int>(percent =>
            {
                System.Diagnostics.Debug.WriteLine($"Descarga: {percent}%");
            });

            bool success = await updateChecker.DownloadUpdateAsync(updateInfo.DownloadUrl, progress);

            if (success)
            {
                TaskDialog.Show(
                    "Descarga Completada",
                    "✅ La actualización se descargó correctamente.\n\n" +
                    "Se instalará automáticamente cuando cierre Revit.\n\n" +
                    "Guarde su trabajo y cierre Revit para completar la actualización.",
                    TaskDialogCommonButtons.Ok);
            }
            else
            {
                TaskDialog.Show(
                    "Error de Descarga",
                    "❌ No se pudo descargar la actualización.\n\n" +
                    "Verifique su conexión a internet e intente nuevamente.",
                    TaskDialogCommonButtons.Ok);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error al descargar: {ex.Message}");
            TaskDialog.Show("Error", $"Error al descargar actualización:\n{ex.Message}");
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        // Aplicar actualización pendiente si existe
        Services.UpdateChecker.ApplyPendingUpdate();

        // Limpiar recursos si es necesario
        return Result.Succeeded;
    }
}
