using System;

namespace ClosestGridsAddinVANILLA.Services
{
    /// <summary>
    /// Gestiona la sesión activa en memoria durante la ejecución de Revit
    /// </summary>
    public static class SessionCache
    {
        private static SessionData _cachedSession = null;
        private static DateTime _lastValidation = DateTime.MinValue;
        private static readonly TimeSpan VALIDATION_CACHE_DURATION = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Establece la sesión activa en memoria
        /// </summary>
        public static void SetSession(SessionData session)
        {
            _cachedSession = session;
            _lastValidation = DateTime.Now;
        }

        /// <summary>
        /// Obtiene la sesión activa en memoria
        /// </summary>
        public static SessionData GetSession()
        {
            return _cachedSession;
        }

        /// <summary>
        /// Verifica si hay una sesión válida en memoria
        /// </summary>
        public static bool HasValidSession()
        {
            return _cachedSession != null && 
                   !string.IsNullOrEmpty(_cachedSession.UserId);
        }

        /// <summary>
        /// Verifica si necesita revalidar con Firebase
        /// La revalidación se hace cada 5 minutos para no sobrecargar Firebase
        /// </summary>
        public static bool NeedsRevalidation()
        {
            if (!HasValidSession())
                return true;

            return (DateTime.Now - _lastValidation) > VALIDATION_CACHE_DURATION;
        }

        /// <summary>
        /// Actualiza el timestamp de última validación
        /// </summary>
        public static void UpdateLastValidation()
        {
            _lastValidation = DateTime.Now;
        }

        /// <summary>
        /// Limpia la sesión en memoria
        /// </summary>
        public static void ClearSession()
        {
            _cachedSession = null;
            _lastValidation = DateTime.MinValue;
        }

        /// <summary>
        /// Obtiene información de la sesión para logging
        /// </summary>
        public static string GetSessionInfo()
        {
            if (!HasValidSession())
                return "No hay sesión activa";

            var timeSinceValidation = DateTime.Now - _lastValidation;
            return $"Usuario: {_cachedSession.Email}, Última validación: {timeSinceValidation.TotalMinutes:F1} minutos atrás";
        }
    }
}
