using System.Globalization;

namespace SwCopilot.Routing
{
    /// <summary>The set of hard-coded commands the MVP understands.</summary>
    public enum CommandKind
    {
        CreateMountingPlate,
        UpdateThickness,
        UpdateHoleDiameter,
        Unsupported
    }

    /// <summary>Base type for the typed command objects the router emits.</summary>
    public abstract class CommandBase
    {
        public abstract CommandKind Kind { get; }

        /// <summary>Raw text the user typed, kept for the transcript.</summary>
        public string RawInput { get; set; }

        protected static string Fmt(double mm)
        {
            return mm.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Create a rectangular mounting plate with four through holes.</summary>
    public sealed class CreateMountingPlateCommand : CommandBase
    {
        public double WidthMm = 100;
        public double HeightMm = 60;
        public double ThicknessMm = 8;
        public double HoleDiameterMm = 6;
        public double HoleOffsetMm = 10; // inset of each hole centre from the plate edges

        public override CommandKind Kind { get { return CommandKind.CreateMountingPlate; } }

        public string Summary
        {
            get
            {
                return "创建安装板 " + Fmt(WidthMm) + "x" + Fmt(HeightMm) + "x" + Fmt(ThicknessMm)
                     + " mm，4 个 Ø" + Fmt(HoleDiameterMm) + " 通孔";
            }
        }
    }

    /// <summary>Change the plate thickness (drives AI_thickness).</summary>
    public sealed class UpdateThicknessCommand : CommandBase
    {
        public double ValueMm;
        public override CommandKind Kind { get { return CommandKind.UpdateThickness; } }
        public string Summary { get { return "把厚度改成 " + Fmt(ValueMm) + " mm"; } }
    }

    /// <summary>Change the hole diameter (drives AI_hole_diameter).</summary>
    public sealed class UpdateHoleDiameterCommand : CommandBase
    {
        public double ValueMm;
        public override CommandKind Kind { get { return CommandKind.UpdateHoleDiameter; } }
        public string Summary { get { return "把孔径改成 " + Fmt(ValueMm) + " mm"; } }
    }

    /// <summary>Nothing matched.</summary>
    public sealed class UnsupportedCommand : CommandBase
    {
        public override CommandKind Kind { get { return CommandKind.Unsupported; } }
    }
}
