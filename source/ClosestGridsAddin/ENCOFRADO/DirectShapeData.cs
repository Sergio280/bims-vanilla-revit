using Autodesk.Revit.DB;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Almacena los datos extraídos de un DirectShape para crear Wall/Floor después
    /// Esto permite eliminar el DirectShape antes de crear el elemento nativo
    /// </summary>
    public class DirectShapeData
    {
        /// <summary>
        /// ID del DirectShape original (para referencia en logs)
        /// </summary>
        public ElementId DirectShapeId { get; set; }

        /// <summary>
        /// Curva base para crear muros (la curva más larga del contorno)
        /// </summary>
        public Curve CurvaBase { get; set; }

        /// <summary>
        /// Contorno completo para crear suelos
        /// </summary>
        public CurveLoop ContornoCompleto { get; set; }

        /// <summary>
        /// Altura del muro (extraída del BoundingBox)
        /// </summary>
        public double Altura { get; set; }

        /// <summary>
        /// Normal de la cara principal
        /// </summary>
        public XYZ Normal { get; set; }

        /// <summary>
        /// True si es vertical (muro), False si es horizontal (suelo)
        /// </summary>
        public bool EsVertical { get; set; }

        /// <summary>
        /// Nivel base donde se creará el elemento
        /// </summary>
        public Level NivelBase { get; set; }

        /// <summary>
        /// Comentario del DirectShape original (para copiar al nuevo elemento)
        /// </summary>
        public string Comentario { get; set; }

        /// <summary>
        /// Área de la cara principal (para logging)
        /// </summary>
        public double Area { get; set; }
    }
}
