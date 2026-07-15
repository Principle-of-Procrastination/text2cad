using System;

namespace Text2Cad.Addin.Commands
{
    internal enum PrimitiveKind
    {
        TestPlate,
        Cube,
        Cylinder
    }

    internal enum PrimitiveProfileKind
    {
        Rectangle,
        Circle
    }

    internal sealed class PrimitiveDefinition
    {
        public PrimitiveDefinition(
            PrimitiveKind kind,
            string displayName,
            PrimitiveProfileKind profile,
            double halfWidthMeters,
            double halfHeightMeters,
            double radiusMeters,
            double extrusionDepthMeters,
            string sketchFeatureName,
            string extrusionFeatureName)
        {
            Kind = kind;
            DisplayName = displayName;
            Profile = profile;
            HalfWidthMeters = halfWidthMeters;
            HalfHeightMeters = halfHeightMeters;
            RadiusMeters = radiusMeters;
            ExtrusionDepthMeters = extrusionDepthMeters;
            SketchFeatureName = sketchFeatureName;
            ExtrusionFeatureName = extrusionFeatureName;
        }

        public PrimitiveKind Kind { get; }

        public string DisplayName { get; }

        public PrimitiveProfileKind Profile { get; }

        public double HalfWidthMeters { get; }

        public double HalfHeightMeters { get; }

        public double RadiusMeters { get; }

        public double ExtrusionDepthMeters { get; }

        public string SketchFeatureName { get; }

        public string ExtrusionFeatureName { get; }
    }

    internal static class PrimitiveCatalog
    {
        public static PrimitiveDefinition Get(PrimitiveKind primitiveKind)
        {
            switch (primitiveKind)
            {
                case PrimitiveKind.TestPlate:
                    return new PrimitiveDefinition(
                        PrimitiveKind.TestPlate,
                        "100 × 60 × 10 mm 测试板",
                        PrimitiveProfileKind.Rectangle,
                        0.050,
                        0.030,
                        0,
                        0.010,
                        "Text2CAD_TestPlate_Sketch",
                        "Text2CAD_TestPlate_100x60x10");
                case PrimitiveKind.Cube:
                    return new PrimitiveDefinition(
                        PrimitiveKind.Cube,
                        "40 × 40 × 40 mm 方块",
                        PrimitiveProfileKind.Rectangle,
                        0.020,
                        0.020,
                        0,
                        0.040,
                        "Text2CAD_Cube_Sketch",
                        "Text2CAD_Cube_40x40x40");
                case PrimitiveKind.Cylinder:
                    return new PrimitiveDefinition(
                        PrimitiveKind.Cylinder,
                        "Ø40 × 60 mm 圆柱",
                        PrimitiveProfileKind.Circle,
                        0,
                        0,
                        0.020,
                        0.060,
                        "Text2CAD_Cylinder_Sketch",
                        "Text2CAD_Cylinder_D40x60");
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(primitiveKind),
                        primitiveKind,
                        "未知的基础几何类型。");
            }
        }
    }
}
