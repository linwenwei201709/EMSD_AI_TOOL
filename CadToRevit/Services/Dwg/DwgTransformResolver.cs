using Autodesk.Revit.DB;
using System;

namespace CadToRevit.Services.Dwg
{
    public static class DwgTransformResolver
    {
        public static Transform GetCadToRevitTransform(ImportInstance import, Action<string> log = null)
        {
            // Use import total transform so DWG text points match runDataset geometry space.
            if (import == null)
            {
                log?.Invoke("[RoomText] TransformResolver: import is null, fallback Identity.");
                return Transform.Identity;
            }

            try
            {
                Transform tf = import.GetTotalTransform();
                if (tf == null)
                {
                    log?.Invoke("[RoomText] TransformResolver: GetTotalTransform returned null, fallback Identity.");
                    return Transform.Identity;
                }

                double sx = tf.BasisX != null ? tf.BasisX.GetLength() : 0.0;
                double sy = tf.BasisY != null ? tf.BasisY.GetLength() : 0.0;
                double sz = tf.BasisZ != null ? tf.BasisZ.GetLength() : 0.0;
                XYZ origin = tf.Origin ?? XYZ.Zero;
                log?.Invoke(
                    "[RoomText] TransformResolver: Origin=(" +
                    origin.X.ToString("F3") + "," + origin.Y.ToString("F3") + "," + origin.Z.ToString("F3") +
                    "), ScaleApprox=(" +
                    sx.ToString("F6") + "," + sy.ToString("F6") + "," + sz.ToString("F6") + ").");
                return tf;
            }
            catch (Exception ex)
            {
                log?.Invoke("[RoomText] TransformResolver failed: " + ex.Message + ", fallback Identity.");
                return Transform.Identity;
            }
        }
    }
}
