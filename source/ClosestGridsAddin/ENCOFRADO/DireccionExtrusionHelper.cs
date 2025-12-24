using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClosestGridsAddinVANILLA.ENCOFRADO
{
    /// <summary>
    /// Calcula la dirección de extrusión correcta para que el encofrado
    /// siempre apunte HACIA AFUERA del elemento estructural
    /// </summary>
    public static class DireccionExtrusionHelper
    {
        /// <summary>
        /// Calcula la dirección de extrusión HACIA AFUERA del elemento
        /// </summary>
        /// <param name="cara">Cara planar del elemento</param>
        /// <param name="solidoElemento">Sólido del elemento estructural</param>
        /// <returns>Vector normal apuntando hacia afuera</returns>
        public static XYZ ObtenerDireccionHaciaAfuera(
            PlanarFace cara,
            Solid solidoElemento)
        {
            try
            {
                XYZ normalCara = cara.FaceNormal;

                // Calcular centroide del sólido del elemento
                XYZ centroElemento = ObtenerCentroideSolido(solidoElemento);

                // Calcular centroide de la cara
                XYZ centroCara = ObtenerCentroideCara(cara);

                // Vector desde centro del elemento hacia centro de la cara
                XYZ vectorHaciaAfuera = (centroCara - centroElemento).Normalize();

                // Verificar que la normal apunte hacia afuera
                double producto = normalCara.DotProduct(vectorHaciaAfuera);

                if (producto < 0)
                {
                    // La normal apunta hacia adentro, invertirla
                    normalCara = -normalCara;
                }

                return normalCara;
            }
            catch
            {
                // Si falla el cálculo, devolver la normal original
                return cara.FaceNormal;
            }
        }

        /// <summary>
        /// Calcula el centroide de un sólido
        /// </summary>
        private static XYZ ObtenerCentroideSolido(Solid solido)
        {
            try
            {
                // Intentar usar el método de Revit (más preciso)
                XYZ centroide = solido.ComputeCentroid();

                if (centroide != null)
                {
                    return centroide;
                }
            }
            catch { }

            try
            {
                // Fallback: usar centro del BoundingBox
                BoundingBoxXYZ bbox = solido.GetBoundingBox();
                if (bbox != null)
                {
                    return (bbox.Min + bbox.Max) / 2.0;
                }
            }
            catch { }

            // Último fallback: origen
            return XYZ.Zero;
        }

        /// <summary>
        /// Calcula el centroide de una cara planar
        /// </summary>
        public static XYZ ObtenerCentroideCara(PlanarFace cara)
        {
            try
            {
                var curveLoops = cara.GetEdgesAsCurveLoops();
                if (curveLoops == null || curveLoops.Count == 0)
                    return XYZ.Zero;

                List<XYZ> puntos = new List<XYZ>();

                // Usar solo el primer loop (contorno exterior)
                foreach (Curve curve in curveLoops[0])
                {
                    puntos.Add(curve.GetEndPoint(0));
                    puntos.Add(curve.GetEndPoint(1));
                }

                if (puntos.Count == 0) return XYZ.Zero;

                // Calcular promedio de puntos
                XYZ suma = puntos.Aggregate(XYZ.Zero, (acc, p) => acc + p);
                return suma / puntos.Count;
            }
            catch
            {
                return XYZ.Zero;
            }
        }

        /// <summary>
        /// Verifica si una normal apunta hacia afuera comparándola con el centro del elemento
        /// </summary>
        /// <param name="normal">Normal a verificar</param>
        /// <param name="puntoCara">Punto en la cara</param>
        /// <param name="centroElemento">Centro del elemento</param>
        /// <returns>True si apunta hacia afuera, False si apunta hacia adentro</returns>
        public static bool ApuntaHaciaAfuera(XYZ normal, XYZ puntoCara, XYZ centroElemento)
        {
            // Vector desde centro del elemento hacia el punto en la cara
            XYZ vectorHaciaAfuera = (puntoCara - centroElemento).Normalize();

            // Si el producto punto es positivo, apunta hacia afuera
            double producto = normal.DotProduct(vectorHaciaAfuera);

            return producto > 0;
        }

        /// <summary>
        /// Invierte una normal si es necesario para que apunte hacia afuera
        /// </summary>
        public static XYZ CorregirDireccion(XYZ normal, XYZ puntoCara, XYZ centroElemento)
        {
            if (ApuntaHaciaAfuera(normal, puntoCara, centroElemento))
            {
                return normal;
            }
            else
            {
                return -normal;
            }
        }
    }
}
