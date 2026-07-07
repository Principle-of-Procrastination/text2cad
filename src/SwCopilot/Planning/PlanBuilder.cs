using System.Collections.Generic;
using System.Globalization;
using SwCopilot.Routing;

namespace SwCopilot.Planning
{
    /// <summary>
    /// Turns a matched command object into a human-readable execution plan
    /// (spec section 5 "Plan Builder"). Shown to the user before anything runs.
    /// </summary>
    public static class PlanBuilder
    {
        public static string[] BuildSteps(CommandBase cmd)
        {
            var steps = new List<string>();
            switch (cmd.Kind)
            {
                case CommandKind.CreateMountingPlate:
                    {
                        var c = (CreateMountingPlateCommand)cmd;
                        steps.Add("创建中心矩形草图 (" + Fmt(c.WidthMm) + " x " + Fmt(c.HeightMm) + " mm)");
                        steps.Add("拉伸 " + Fmt(c.ThicknessMm) + " mm");
                        steps.Add("创建 4 个 Ø" + Fmt(c.HoleDiameterMm) + " 通孔 (距边 " + Fmt(c.HoleOffsetMm) + " mm)");
                        steps.Add("命名特征和关键尺寸 (AI_BasePlate / AI_4x_M6_ThroughHoles)");
                        break;
                    }
                case CommandKind.UpdateThickness:
                    {
                        var c = (UpdateThicknessCommand)cmd;
                        steps.Add("找到 AI_thickness 尺寸");
                        steps.Add("将厚度改为 " + Fmt(c.ValueMm) + " mm");
                        steps.Add("重建模型");
                        break;
                    }
                case CommandKind.UpdateHoleDiameter:
                    {
                        var c = (UpdateHoleDiameterCommand)cmd;
                        steps.Add("找到 AI_hole_diameter 尺寸");
                        steps.Add("将孔径改为 " + Fmt(c.ValueMm) + " mm");
                        steps.Add("重建模型");
                        break;
                    }
            }
            return steps.ToArray();
        }

        /// <summary>Message shown when nothing matched (spec section 6).</summary>
        public static string UnsupportedMessage()
        {
            return "当前 MVP 只支持：\n1. 创建安装板\n2. 修改厚度\n3. 修改孔径";
        }

        private static string Fmt(double mm)
        {
            return mm.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
