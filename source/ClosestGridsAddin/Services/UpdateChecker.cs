using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json;
using FireSharp.Config;
using FireSharp.Interfaces;
using FireSharp.Response;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Servicio para verificar y descargar actualizaciones del add-in
    /// </summary>
    public class UpdateChecker
    {
        private readonly IFirebaseClient _client;
        private static readonly string UPDATE_FOLDER = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClosestGridsAddin",
            "Updates"
        );

        public UpdateChecker()
        {
            IFirebaseConfig config = new FirebaseConfig
            {
                AuthSecret = "I2yypO4zT4LNHCG9NrBwI9VebMdOn9f4PiZwjlTY",
                BasePath = "https://bims-8d507-default-rtdb.firebaseio.com/"
            };

            _client = new FireSharp.FirebaseClient(config);
        }

        /// <summary>
        /// Verifica si hay una actualización disponible
        /// </summary>
        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                // Obtener versión actual
                var currentVersion = GetCurrentVersion();

                // Obtener información de actualización desde Firebase
                var response = await _client.GetAsync("updates/latest");

                if (response == null || response.Body == "null" || string.IsNullOrEmpty(response.Body))
                {
                    return new UpdateInfo
                    {
                        IsAvailable = false,
                        Message = "No hay información de actualizaciones disponible."
                    };
                }

                var updateData = JsonConvert.DeserializeObject<UpdateData>(response.Body);

                if (updateData == null)
                {
                    return new UpdateInfo { IsAvailable = false };
                }

                // Comparar versiones
                Version remoteVersion = new Version(updateData.Version);
                Version localVersion = new Version(currentVersion);

                bool isUpdateAvailable = remoteVersion > localVersion;

                return new UpdateInfo
                {
                    IsAvailable = isUpdateAvailable,
                    CurrentVersion = currentVersion,
                    LatestVersion = updateData.Version,
                    DownloadUrl = updateData.DownloadUrl,
                    ReleaseNotes = updateData.ReleaseNotes,
                    IsMandatory = updateData.IsMandatory,
                    ReleaseDate = updateData.ReleaseDate,
                    Message = isUpdateAvailable
                        ? $"Nueva versión {updateData.Version} disponible"
                        : "Estás usando la versión más reciente"
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al verificar actualizaciones: {ex.Message}");
                return new UpdateInfo
                {
                    IsAvailable = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Descarga la actualización en segundo plano
        /// </summary>
        public async Task<bool> DownloadUpdateAsync(string downloadUrl, IProgress<int> progress = null)
        {
            try
            {
                // Crear carpeta de updates si no existe
                if (!Directory.Exists(UPDATE_FOLDER))
                {
                    Directory.CreateDirectory(UPDATE_FOLDER);
                }

                string tempFilePath = Path.Combine(UPDATE_FOLDER, "ClosestGridsAddinVANILLA_update.dll");

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);

                    using (HttpResponseMessage response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1 && progress != null;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0L;

                            int bytesRead;
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                if (canReportProgress)
                                {
                                    var progressPercentage = (int)((totalRead * 100) / totalBytes);
                                    progress.Report(progressPercentage);
                                }
                            }
                        }
                    }
                }

                // Crear script de actualización
                CreateUpdateScript(tempFilePath);

                System.Diagnostics.Debug.WriteLine($"Actualización descargada: {tempFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al descargar actualización: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Crea un script .bat para actualizar el DLL cuando Revit se cierre
        /// </summary>
        private void CreateUpdateScript(string downloadedFilePath)
        {
            try
            {
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);
                string targetDllPath = Path.Combine(assemblyDir, "ClosestGridsAddinVANILLA.dll");
                string backupPath = Path.Combine(assemblyDir, "ClosestGridsAddinVANILLA_backup.dll");

                string scriptPath = Path.Combine(UPDATE_FOLDER, "apply_update.bat");

                string script = $@"@echo off
echo ================================================
echo   BIMS VANILLA - Aplicando Actualizacion
echo ================================================
echo.
echo Esperando que Revit se cierre...
timeout /t 5 /nobreak

:RETRY
tasklist /FI ""IMAGENAME eq Revit.exe"" 2>NUL | find /I /N ""Revit.exe"">NUL
if ""%ERRORLEVEL%""==""0"" (
    echo Revit aun esta en ejecucion. Esperando...
    timeout /t 3 /nobreak
    goto RETRY
)

echo.
echo Revit cerrado. Aplicando actualizacion...
echo.

REM Crear backup del DLL actual
if exist ""{targetDllPath}"" (
    echo Creando backup del archivo actual...
    copy /Y ""{targetDllPath}"" ""{backupPath}""
)

REM Copiar nueva version
echo Copiando nueva version...
copy /Y ""{downloadedFilePath}"" ""{targetDllPath}""

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ================================================
    echo   Actualizacion completada exitosamente
    echo ================================================
    echo.
    echo La proxima vez que abra Revit, se cargara
    echo la nueva version del plugin.
    echo.

    REM Limpiar archivos temporales
    del ""{downloadedFilePath}""

    timeout /t 10
    exit
) else (
    echo.
    echo ================================================
    echo   ERROR: No se pudo aplicar la actualizacion
    echo ================================================
    echo.
    echo Restaurando backup...
    copy /Y ""{backupPath}"" ""{targetDllPath}""

    pause
    exit /b 1
)
";

                File.WriteAllText(scriptPath, script);

                // Guardar ruta del script para ejecutarlo después
                string scriptFlagPath = Path.Combine(UPDATE_FOLDER, "update_pending.txt");
                File.WriteAllText(scriptFlagPath, scriptPath);

                System.Diagnostics.Debug.WriteLine($"Script de actualización creado: {scriptPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al crear script: {ex.Message}");
            }
        }

        /// <summary>
        /// Ejecuta el script de actualización cuando Revit se está cerrando
        /// </summary>
        public static void ApplyPendingUpdate()
        {
            try
            {
                string scriptFlagPath = Path.Combine(UPDATE_FOLDER, "update_pending.txt");

                if (File.Exists(scriptFlagPath))
                {
                    string scriptPath = File.ReadAllText(scriptFlagPath);

                    if (File.Exists(scriptPath))
                    {
                        // Ejecutar script en segundo plano
                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = scriptPath,
                            UseShellExecute = true,
                            CreateNoWindow = false,
                            WindowStyle = ProcessWindowStyle.Normal
                        };

                        Process.Start(startInfo);

                        // Eliminar flag
                        File.Delete(scriptFlagPath);

                        System.Diagnostics.Debug.WriteLine("Script de actualización iniciado");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al aplicar actualización: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene la versión actual del ensamblado
        /// </summary>
        public static string GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>
    /// Información sobre una actualización disponible
    /// </summary>
    public class UpdateInfo
    {
        public bool IsAvailable { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public bool IsMandatory { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Datos de actualización desde Firebase
    /// </summary>
    public class UpdateData
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; }

        [JsonProperty("releaseNotes")]
        public string ReleaseNotes { get; set; }

        [JsonProperty("isMandatory")]
        public bool IsMandatory { get; set; }

        [JsonProperty("releaseDate")]
        public DateTime? ReleaseDate { get; set; }
    }
}
