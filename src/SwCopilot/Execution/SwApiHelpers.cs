using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SwCopilot.Execution
{
    /// <summary>
    /// Thin, defensive wrappers over the raw SolidWorks API. Every method is
    /// written to fail soft (return false / null, swallow COM exceptions) so the
    /// Executor can decide how to report. Handles the fragile bits called out in
    /// spec section 10: localized plane names and sketch selection.
    /// </summary>
    internal static class SwApiHelpers
    {
        public const double MmToM = 0.001;

        // ---------- document ----------

        public static ModelDoc2 GetActiveDoc(ISldWorks app)
        {
            return app == null ? null : app.ActiveDoc as ModelDoc2;
        }

        /// <summary>
        /// IModelDoc2.GetType() returns the document type (swDocumentTypes_e); on the
        /// interface type it shadows object.GetType(). This is the standard SW idiom.
        /// </summary>
        public static int GetDocType(ModelDoc2 doc)
        {
            return doc.GetType();
        }

        public static bool IsPart(ModelDoc2 doc)
        {
            return doc != null && GetDocType(doc) == (int)swDocumentTypes_e.swDocPART;
        }

        public static string DescribeActiveDoc(ISldWorks app)
        {
            ModelDoc2 doc = GetActiveDoc(app);
            if (doc == null) return "无打开的文档 (No document open)";
            int t = GetDocType(doc);
            if (t == (int)swDocumentTypes_e.swDocPART) return "零件已激活 (Part active)";
            if (t == (int)swDocumentTypes_e.swDocASSEMBLY) return "装配体已激活 (Assembly active)";
            if (t == (int)swDocumentTypes_e.swDocDRAWING) return "工程图已激活 (Drawing active)";
            return "不支持的文档类型 (Unsupported)";
        }

        public static ModelDoc2 CreateNewPart(ISldWorks app)
        {
            string tpl = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            if (string.IsNullOrEmpty(tpl)) return null;
            return app.NewDocument(tpl, 0, 0, 0) as ModelDoc2;
        }

        // ---------- selection ----------

        /// <summary>
        /// Select the Front reference plane without trusting the English name
        /// (spec section 10, lesson 1). Tries common localized names, then falls
        /// back to the first RefPlane found by traversing the feature tree.
        /// </summary>
        public static bool SelectFrontPlane(ModelDoc2 doc)
        {
            ModelDocExtension ext = doc.Extension;
            string[] names =
            {
                "Front Plane", "Front",
                "前视基准面", "前视", "正视基准面",   // Simplified Chinese variants
                "Plan de face", "Vorderansicht", "Alzado", "Fronte"
            };
            foreach (string n in names)
            {
                try { if (ext.SelectByID2(n, "PLANE", 0, 0, 0, false, 0, null, 0)) return true; }
                catch { }
            }

            Feature f = doc.FirstFeature() as Feature;
            while (f != null)
            {
                try
                {
                    if (f.GetTypeName2() == "RefPlane")
                        return f.Select2(false, 0);
                }
                catch { }
                f = f.GetNextFeature() as Feature;
            }
            return false;
        }

        /// <summary>Select the most-recently-created (still top-level) sketch.</summary>
        public static bool SelectLastSketch(ModelDoc2 doc)
        {
            Feature last = null;
            Feature f = doc.FirstFeature() as Feature;
            while (f != null)
            {
                try { if (f.GetTypeName2() == "ProfileFeature") last = f; }
                catch { }
                f = f.GetNextFeature() as Feature;
            }
            return last != null && last.Select2(false, 0);
        }

        // ---------- dimensions ----------

        /// <summary>
        /// While a sketch is open, add a driving dimension to <paramref name="seg"/>,
        /// name it, and set its value. Entirely best-effort: any failure is swallowed
        /// so geometry creation still succeeds.
        /// </summary>
        public static void AddNamedDimension(ModelDoc2 doc, SketchSegment seg,
            double labelX, double labelY, string name, double valueMeters)
        {
            if (seg == null) return;
            try
            {
                doc.ClearSelection2(true);
                seg.Select4(false, null);
                object ddObj = doc.AddDimension2(labelX, labelY, 0);
                doc.ClearSelection2(true);

                DisplayDimension dd = ddObj as DisplayDimension;
                if (dd == null) return;
                Dimension d = dd.GetDimension2(0) as Dimension;
                if (d == null) return;
                try { d.Name = name; } catch { }
                try { d.SetSystemValue3(valueMeters, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null); } catch { }
            }
            catch { }
        }

        /// <summary>Rename the first display dimension of a feature (e.g. extrude depth).</summary>
        public static void RenameFeatureFirstDimension(Feature feat, string name)
        {
            Dimension d = GetFirstDimensionOfFeature(feat);
            if (d != null) { try { d.Name = name; } catch { } }
        }

        /// <summary>All dimensions whose (short) name starts with the prefix.</summary>
        public static List<Dimension> FindDimensionsByPrefix(ModelDoc2 doc, string prefix)
        {
            var result = new List<Dimension>();
            Feature f = doc.FirstFeature() as Feature;
            while (f != null)
            {
                try
                {
                    object ddObj = f.GetFirstDisplayDimension();
                    while (ddObj != null)
                    {
                        DisplayDimension dd = ddObj as DisplayDimension;
                        if (dd != null)
                        {
                            Dimension d = dd.GetDimension2(0) as Dimension;
                            if (d != null)
                            {
                                string nm = null;
                                try { nm = d.Name; } catch { }
                                if (!string.IsNullOrEmpty(nm) && nm.StartsWith(prefix))
                                    result.Add(d);
                            }
                        }
                        ddObj = f.GetNextDisplayDimension(ddObj);
                    }
                }
                catch { }
                f = f.GetNextFeature() as Feature;
            }
            return result;
        }

        public static Feature FindFeatureByName(ModelDoc2 doc, string name)
        {
            Feature f = doc.FirstFeature() as Feature;
            while (f != null)
            {
                try { if (f.Name == name) return f; } catch { }
                f = f.GetNextFeature() as Feature;
            }
            return null;
        }

        public static Dimension GetFirstDimensionOfFeature(Feature feat)
        {
            if (feat == null) return null;
            try
            {
                DisplayDimension dd = feat.GetFirstDisplayDimension() as DisplayDimension;
                return dd == null ? null : dd.GetDimension2(0) as Dimension;
            }
            catch { return null; }
        }

        public static bool SetDimensionMeters(Dimension d, double meters)
        {
            if (d == null) return false;
            try
            {
                d.SetSystemValue3(meters, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                return true;
            }
            catch { return false; }
        }

        // ---------- rebuild / view ----------

        public static void Rebuild(ModelDoc2 doc)
        {
            try { doc.EditRebuild3(); } catch { }
        }

        public static void ZoomToFit(ModelDoc2 doc)
        {
            try { doc.ViewZoomtofit2(); } catch { }
        }
    }
}
