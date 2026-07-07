using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwCopilot.Routing;

namespace SwCopilot.Execution
{
    /// <summary>
    /// Deterministic, defensive executor (spec section 9). Turns a typed command
    /// into native SolidWorks feature operations and returns a structured result.
    /// All SolidWorks calls happen on the caller's (UI) thread — SW COM is STA.
    /// </summary>
    public sealed class Executor
    {
        private readonly ISldWorks _app;

        /// <summary>Create a fresh part when none is active (smoother demo).</summary>
        public bool AutoCreatePartIfNone = true;

        public Executor(ISldWorks app)
        {
            _app = app;
        }

        public ExecutionResult Run(CommandBase cmd)
        {
            try
            {
                if (_app == null)
                    return ExecutionResult.Fail("SolidWorks 未连接。", "ISldWorks is null");

                switch (cmd.Kind)
                {
                    case CommandKind.CreateMountingPlate:
                        return CreateMountingPlate((CreateMountingPlateCommand)cmd);
                    case CommandKind.UpdateThickness:
                        return UpdateThickness((UpdateThicknessCommand)cmd);
                    case CommandKind.UpdateHoleDiameter:
                        return UpdateHoleDiameter((UpdateHoleDiameterCommand)cmd);
                    default:
                        return ExecutionResult.Fail("不支持的命令。");
                }
            }
            catch (Exception ex)
            {
                // The executor must never crash SolidWorks; surface everything as a result.
                return ExecutionResult.Fail("执行时发生异常。", ex.Message);
            }
        }

        // ---------- Flow A ----------

        private ExecutionResult CreateMountingPlate(CreateMountingPlateCommand c)
        {
            string err;
            ModelDoc2 doc = EnsurePart(out err);
            if (doc == null) return ExecutionResult.Fail(err);

            double w = c.WidthMm * SwApiHelpers.MmToM;
            double h = c.HeightMm * SwApiHelpers.MmToM;
            double t = c.ThicknessMm * SwApiHelpers.MmToM;
            double r = (c.HoleDiameterMm * SwApiHelpers.MmToM) / 2.0;
            double dia = c.HoleDiameterMm * SwApiHelpers.MmToM;
            double off = c.HoleOffsetMm * SwApiHelpers.MmToM;

            var result = new ExecutionResult { Success = true };
            doc.ClearSelection2(true);

            // --- Boss: base plate ---
            if (!SwApiHelpers.SelectFrontPlane(doc))
                return ExecutionResult.Fail("无法选择基准面。", "SelectFrontPlane failed");

            SketchManager sk = doc.SketchManager;
            sk.InsertSketch(true);
            object rectObj = sk.CreateCenterRectangle(0, 0, 0, w / 2.0, h / 2.0, 0);

            // Best-effort width/height dimensions (non-fatal if they fail).
            object[] segs = rectObj as object[];
            if (segs != null)
            {
                SketchSegment segW = segs.Length > 0 ? segs[0] as SketchSegment : null;
                SketchSegment segH = segs.Length > 1 ? segs[1] as SketchSegment : null;
                SwApiHelpers.AddNamedDimension(doc, segW, 0, -(h / 2.0 + off), "AI_width", w);
                SwApiHelpers.AddNamedDimension(doc, segH, -(w / 2.0 + off), 0, "AI_height", h);
            }
            sk.InsertSketch(true); // close sketch

            if (!SwApiHelpers.SelectLastSketch(doc))
                return ExecutionResult.Fail("无法选择草图进行拉伸。", "SelectLastSketch failed");

            FeatureManager fm = doc.FeatureManager;
            Feature boss = fm.FeatureExtrusion3(
                true, false, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                t, 0,
                false, false, false, false,
                0, 0,
                false, false, false, false,
                true,   // Merge
                false,  // UseFeatScope
                true,   // UseAutoSelect
                (int)swStartConditions_e.swStartSketchPlane, 0, false);

            if (boss == null)
                return ExecutionResult.Fail("拉伸失败。", "FeatureExtrusion3 returned null");
            try { boss.Name = "AI_BasePlate"; } catch { }
            SwApiHelpers.RenameFeatureFirstDimension(boss, "AI_thickness"); // extrude depth
            result.CreatedFeatures.Add("AI_BasePlate");

            // --- Cut: four through holes (sketch on the same Front plane) ---
            doc.ClearSelection2(true);
            if (!SwApiHelpers.SelectFrontPlane(doc))
                return ExecutionResult.Fail("无法选择基准面(孔)。", "SelectFrontPlane (holes) failed");

            sk.InsertSketch(true);
            double hx = w / 2.0 - off;
            double hy = h / 2.0 - off;
            double[,] centers = { { hx, hy }, { -hx, hy }, { -hx, -hy }, { hx, -hy } };
            for (int i = 0; i < 4; i++)
            {
                double cx = centers[i, 0];
                double cy = centers[i, 1];
                SketchSegment circ = sk.CreateCircleByRadius(cx, cy, 0, r);
                string dimName = i == 0 ? "AI_hole_diameter" : "AI_hole_diameter_" + (i + 1);
                SwApiHelpers.AddNamedDimension(doc, circ, cx + off / 2.0, cy + off / 2.0, dimName, dia);
            }
            sk.InsertSketch(true); // close sketch

            if (!SwApiHelpers.SelectLastSketch(doc))
                return ExecutionResult.Fail("无法选择草图进行切除。", "SelectLastSketch (holes) failed");

            // Through-all in BOTH directions so cut direction can't fail (spec section 10, lesson 2).
            Feature cut = fm.FeatureCut4(
                false, false, false,
                (int)swEndConditions_e.swEndCondThroughAll, (int)swEndConditions_e.swEndCondThroughAll,
                0, 0,
                false, false, false, false,
                0, 0,
                false, false, false, false,
                false,  // NormalCut
                true,   // UseFeatScope
                true,   // UseAutoSelect
                false, false, false,  // assembly scope
                (int)swStartConditions_e.swStartSketchPlane, 0, false, true);

            if (cut == null)
            {
                SwApiHelpers.Rebuild(doc);
                SwApiHelpers.ZoomToFit(doc);
                result.Message = "已创建安装板 (AI_BasePlate),但通孔创建失败,请在真实环境查看报错。";
                return result;
            }
            try { cut.Name = "AI_4x_M6_ThroughHoles"; } catch { }
            result.CreatedFeatures.Add("AI_4x_M6_ThroughHoles");

            SwApiHelpers.Rebuild(doc);
            SwApiHelpers.ZoomToFit(doc);
            result.Message = "已创建原生特征:" + string.Join(", ", result.CreatedFeatures.ToArray());
            return result;
        }

        // ---------- Flow B ----------

        private ExecutionResult UpdateThickness(UpdateThicknessCommand c)
        {
            string err;
            ModelDoc2 doc = EnsureExistingPart(out err);
            if (doc == null) return ExecutionResult.Fail(err);

            double meters = c.ValueMm * SwApiHelpers.MmToM;

            var dims = SwApiHelpers.FindDimensionsByPrefix(doc, "AI_thickness");
            Dimension target = dims.Count > 0 ? dims[0] : null;
            if (target == null) // fallback: base-plate feature's first (depth) dimension
                target = SwApiHelpers.GetFirstDimensionOfFeature(SwApiHelpers.FindFeatureByName(doc, "AI_BasePlate"));
            if (target == null)
                return ExecutionResult.Fail("找不到 AI_thickness 尺寸。请先创建安装板。");

            if (!SwApiHelpers.SetDimensionMeters(target, meters))
                return ExecutionResult.Fail("设置厚度失败。");
            SwApiHelpers.Rebuild(doc);

            var result = ExecutionResult.Ok("厚度已改为 " + c.ValueMm + " mm 并重建。");
            result.ModifiedDimensions.Add("AI_thickness");
            return result;
        }

        // ---------- Flow C ----------

        private ExecutionResult UpdateHoleDiameter(UpdateHoleDiameterCommand c)
        {
            string err;
            ModelDoc2 doc = EnsureExistingPart(out err);
            if (doc == null) return ExecutionResult.Fail(err);

            double meters = c.ValueMm * SwApiHelpers.MmToM;

            var dims = SwApiHelpers.FindDimensionsByPrefix(doc, "AI_hole_diameter");
            if (dims.Count == 0)
                return ExecutionResult.Fail("找不到 AI_hole_diameter 尺寸。请先创建安装板。");

            int ok = 0;
            foreach (Dimension d in dims)
                if (SwApiHelpers.SetDimensionMeters(d, meters)) ok++;
            SwApiHelpers.Rebuild(doc);

            if (ok == 0) return ExecutionResult.Fail("设置孔径失败。");
            var result = ExecutionResult.Ok("孔径已改为 " + c.ValueMm + " mm(" + ok + " 处)并重建。");
            result.ModifiedDimensions.Add("AI_hole_diameter");
            return result;
        }

        // ---------- guards ----------

        private ModelDoc2 EnsurePart(out string error)
        {
            error = null;
            ModelDoc2 doc = SwApiHelpers.GetActiveDoc(_app);
            if (doc == null)
            {
                if (!AutoCreatePartIfNone) { error = "没有打开的文档。请先新建或打开一个零件。"; return null; }
                doc = SwApiHelpers.CreateNewPart(_app);
                if (doc == null) { error = "无法自动新建零件(缺少默认零件模板)。请先手动新建一个零件。"; return null; }
                return doc;
            }
            if (!SwApiHelpers.IsPart(doc)) { error = "当前文档不是零件。MVP 仅支持零件文档。"; return null; }
            return doc;
        }

        private ModelDoc2 EnsureExistingPart(out string error)
        {
            error = null;
            ModelDoc2 doc = SwApiHelpers.GetActiveDoc(_app);
            if (doc == null) { error = "没有打开的零件。请先创建安装板。"; return null; }
            if (!SwApiHelpers.IsPart(doc)) { error = "当前文档不是零件。"; return null; }
            return doc;
        }
    }
}
