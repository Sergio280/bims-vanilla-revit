using Autodesk.Revit.DB;
using System;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Define reglas de encofrado específicas para cada tipo de elemento estructural
    /// </summary>
    public static class ReglasEncofrado
    {
        /// <summary>
        /// Determina si una cara debe encofrarse y qué tipo de elemento crear
        /// </summary>
        public static bool DebeEncofrarCara(
            Element elemento,
            PlanarFace cara,
            out TipoElementoEncofrado tipoEncofrado)
        {
            try
            {
                BuiltInCategory categoria = GetBuiltInCategory(elemento);
                XYZ normal = cara.FaceNormal;

                switch (categoria)
                {
                    case BuiltInCategory.OST_StructuralColumns:
                        return ReglaColumna(cara, normal, out tipoEncofrado);

                    case BuiltInCategory.OST_StructuralFraming:
                        return ReglaViga(cara, normal, out tipoEncofrado);

                    case BuiltInCategory.OST_Walls:
                        return ReglaMuro(cara, normal, out tipoEncofrado);

                    case BuiltInCategory.OST_Floors:
                        return ReglaLosa(cara, normal, out tipoEncofrado);

                    case BuiltInCategory.OST_Stairs:
                        return ReglaEscalera(cara, normal, out tipoEncofrado);

                    case BuiltInCategory.OST_StructuralFoundation:
                        return ReglaCimentacion(cara, normal, out tipoEncofrado);

                    default:
                        tipoEncofrado = TipoElementoEncofrado.NoDefinido;
                        return false;
                }
            }
            catch
            {
                tipoEncofrado = TipoElementoEncofrado.NoDefinido;
                return false;
            }
        }

        /// <summary>
        /// Regla para columnas: Solo encofrar caras verticales como muros
        /// </summary>
        private static bool ReglaColumna(PlanarFace cara, XYZ normal,
            out TipoElementoEncofrado tipo)
        {
            // Solo caras verticales
            bool esVertical = Math.Abs(normal.Z) < 0.3;
            tipo = esVertical ? TipoElementoEncofrado.Muro : TipoElementoEncofrado.NoDefinido;
            return esVertical;
        }

        /// <summary>
        /// Regla para vigas: Caras laterales como muros, cara inferior como suelo
        /// </summary>
        private static bool ReglaViga(PlanarFace cara, XYZ normal,
            out TipoElementoEncofrado tipo)
        {
            double nz = normal.Z;

            // Cara inferior → Suelo
            if (nz < -0.7)
            {
                tipo = TipoElementoEncofrado.Suelo;
                return true;
            }

            // Caras laterales verticales → Muro
            if (Math.Abs(nz) < 0.3)
            {
                tipo = TipoElementoEncofrado.Muro;
                return true;
            }

            // Cara superior → NO encofrar
            tipo = TipoElementoEncofrado.NoDefinido;
            return false;
        }

        /// <summary>
        /// Regla para muros: Solo encofrar caras laterales verticales como muros
        /// </summary>
        private static bool ReglaMuro(PlanarFace cara, XYZ normal,
            out TipoElementoEncofrado tipo)
        {
            // Solo caras laterales verticales (no superior/inferior)
            bool esVertical = Math.Abs(normal.Z) < 0.3;
            tipo = esVertical ? TipoElementoEncofrado.Muro : TipoElementoEncofrado.NoDefinido;
            return esVertical;
        }

        /// <summary>
        /// Regla para losas: Solo encofrar cara inferior como suelo
        /// </summary>
        private static bool ReglaLosa(PlanarFace cara, XYZ normal,
            out TipoElementoEncofrado tipo)
        {
            // Solo cara inferior (normal apunta hacia abajo)
            bool esInferior = normal.Z < -0.7;
            tipo = esInferior ? TipoElementoEncofrado.Suelo : TipoElementoEncofrado.NoDefinido;
            return esInferior;
        }

        /// <summary>
        /// Regla para escaleras: Verticales como muros, inclinadas/horizontales como suelos
        /// </summary>
        private static bool ReglaEscalera(PlanarFace cara, XYZ normal,
            out TipoElementoEncofrado tipo)
        {
            double nz = Math.Abs(normal.Z);

            // Cara vertical → Muro
            if (nz < 0.3)
            {
                tipo = TipoElementoEncofrado.Muro;
                return true;
            }

            // Cara inclinada o horizontal inferior → Suelo
            // (No encofrar cara superior: normal.Z > 0.7)
            if (normal.Z < 0.7)
            {
                tipo = TipoElementoEncofrado.Suelo;
                return true;
            }

            tipo = TipoElementoEncofrado.NoDefinido;
            return false;
        }

        /// <summary>
        /// Regla para cimentaciones: Verticales como muros, horizontales inferiores como suelos
        /// </summary>
        private static bool ReglaCimentacion(PlanarFace cara, XYZ normal,
            out TipoElementoEncofrado tipo)
        {
            // Vertical → Muro
            if (Math.Abs(normal.Z) < 0.3)
            {
                tipo = TipoElementoEncofrado.Muro;
                return true;
            }

            // Horizontal inferior → Suelo
            if (normal.Z < -0.7)
            {
                tipo = TipoElementoEncofrado.Suelo;
                return true;
            }

            tipo = TipoElementoEncofrado.NoDefinido;
            return false;
        }

        /// <summary>
        /// Obtiene la categoría built-in de un elemento
        /// </summary>
        private static BuiltInCategory GetBuiltInCategory(Element elemento)
        {
            try
            {
                if (elemento.Category != null && elemento.Category.Id != null)
                {
                    return (BuiltInCategory)elemento.Category.Id.Value;
                }
            }
            catch { }

            return BuiltInCategory.INVALID;
        }

        /// <summary>
        /// Obtiene el nombre descriptivo del tipo de elemento para reporting
        /// </summary>
        public static string ObtenerNombreTipoElemento(Element elemento)
        {
            BuiltInCategory categoria = GetBuiltInCategory(elemento);

            switch (categoria)
            {
                case BuiltInCategory.OST_StructuralColumns:
                    return "Columna";
                case BuiltInCategory.OST_StructuralFraming:
                    return "Viga";
                case BuiltInCategory.OST_Walls:
                    return "Muro";
                case BuiltInCategory.OST_Floors:
                    return "Losa";
                case BuiltInCategory.OST_Stairs:
                    return "Escalera";
                case BuiltInCategory.OST_StructuralFoundation:
                    return "Cimentación";
                default:
                    return "Desconocido";
            }
        }
    }

    /// <summary>
    /// Tipo de elemento de encofrado a crear
    /// </summary>
    public enum TipoElementoEncofrado
    {
        /// <summary>
        /// No se debe encofrar esta cara
        /// </summary>
        NoDefinido,

        /// <summary>
        /// Crear un muro (Wall) para esta cara
        /// </summary>
        Muro,

        /// <summary>
        /// Crear un suelo (Floor) para esta cara
        /// </summary>
        Suelo
    }
}
