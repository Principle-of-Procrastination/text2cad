using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Text2Cad.Addin.Infrastructure;

namespace Text2Cad.Addin.Commands
{
    internal sealed class PrimitiveCreationResult
    {
        public PrimitiveCreationResult(string documentTitle, string shapeDescription)
        {
            DocumentTitle = documentTitle;
            ShapeDescription = shapeDescription;
        }

        public string DocumentTitle { get; }

        public string ShapeDescription { get; }
    }

    internal sealed class CreatePrimitiveCommand
    {
        private readonly ISldWorks _solidWorks;

        public CreatePrimitiveCommand(ISldWorks solidWorks)
        {
            _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
        }

        public PrimitiveCreationResult Execute(PrimitiveKind primitiveKind)
        {
            PrimitiveDefinition definition = PrimitiveCatalog.Get(primitiveKind);
            IModelDoc2? document = null;
            SketchManager? sketchManager = null;
            FeatureManager? featureManager = null;
            Feature? sketchPlane = null;
            Feature? sketchFeature = null;
            Feature? extrusionFeature = null;
            object? profileEntities = null;
            string? documentTitle = null;
            string stage = "读取默认零件模板";
            bool operationCompleted = false;

            try
            {
                string templatePath = GetDefaultPartTemplatePath();
                AddinLog.Info($"Creating {definition.DisplayName} with template '{templatePath}'.");

                stage = "新建零件文档";
                document = _solidWorks.NewDocument(templatePath, 0, 0, 0) as IModelDoc2
                    ?? throw new InvalidOperationException("SOLIDWORKS 未能创建新的零件文档。");
                documentTitle = document.GetTitle();

                if ((swDocumentTypes_e)document.GetType() != swDocumentTypes_e.swDocPART)
                {
                    throw new InvalidOperationException("默认模板没有创建出零件文档，请检查 SOLIDWORKS 的默认零件模板设置。");
                }

                EnsureTargetDocumentIsActive(document);

                AddinLog.Info($"New part document '{documentTitle}' created for {definition.DisplayName}.");

                sketchManager = document.SketchManager
                    ?? throw new InvalidOperationException("无法访问 SOLIDWORKS 草图管理器。");

                bool reuseActiveSketch = HasActiveSketch(sketchManager);
                if (!reuseActiveSketch)
                {
                    stage = "选择草图基准面";
                    document.ClearSelection2(true);
                    sketchPlane = FindFirstFeatureByType(document, "RefPlane")
                        ?? throw new InvalidOperationException("新零件中没有找到可用的基准面。");

                    if (!sketchPlane.Select2(false, 0))
                    {
                        throw new InvalidOperationException("无法选择新零件的基准面。");
                    }
                }

                stage = $"创建 {definition.DisplayName}草图";
                CreateProfileSketch(sketchManager, !reuseActiveSketch, definition, out profileEntities);

                sketchFeature = FindLastFeatureByType(document, "ProfileFeature")
                    ?? throw new InvalidOperationException("轮廓已经绘制，但没有找到对应的二维草图特征。");
                TryRenameFeature(sketchFeature, definition.SketchFeatureName);

                stage = $"拉伸 {definition.DisplayName}";
                EnsureTargetDocumentIsActive(document);
                document.ClearSelection2(true);
                if (!sketchFeature.Select2(false, 0))
                {
                    throw new InvalidOperationException("无法选择刚创建的二维草图。");
                }

                featureManager = document.FeatureManager
                    ?? throw new InvalidOperationException("无法访问 SOLIDWORKS 特征管理器。");

                extrusionFeature = featureManager.FeatureExtrusion3(
                    true,
                    false,
                    false,
                    (int)swEndConditions_e.swEndCondBlind,
                    (int)swEndConditions_e.swEndCondBlind,
                    definition.ExtrusionDepthMeters,
                    0,
                    false,
                    false,
                    false,
                    false,
                    0,
                    0,
                    false,
                    false,
                    false,
                    false,
                    true,
                    false,
                    true,
                    (int)swStartConditions_e.swStartSketchPlane,
                    0,
                    false);

                if (extrusionFeature == null)
                {
                    throw new InvalidOperationException("SOLIDWORKS 没有创建拉伸凸台；请查看 Add-in 日志获取详细信息。");
                }

                TryRenameFeature(extrusionFeature, definition.ExtrusionFeatureName);

                stage = "重建并显示结果";
                document.ClearSelection2(true);
                if (!document.EditRebuild3())
                {
                    throw new InvalidOperationException($"{definition.DisplayName}已生成，但 SOLIDWORKS 重建模型失败。");
                }

                TryShowResult(document);
                operationCompleted = true;

                string resultTitle = string.IsNullOrWhiteSpace(documentTitle) ? "新零件" : documentTitle;
                AddinLog.Info($"{definition.DisplayName} created successfully in '{resultTitle}'.");
                return new PrimitiveCreationResult(resultTitle, definition.DisplayName);
            }
            catch (Exception exception)
            {
                AddinLog.Error($"{definition.DisplayName} creation failed during stage '{stage}'.", exception);

                if (!operationCompleted &&
                    documentTitle is string failedDocumentTitle &&
                    !string.IsNullOrWhiteSpace(failedDocumentTitle))
                {
                    TryCloseFailedDocument(failedDocumentTitle);
                }

                throw new InvalidOperationException($"{stage}失败：{exception.Message}", exception);
            }
            finally
            {
                ReleaseComObjects(profileEntities);
                ReleaseComObject(extrusionFeature);
                ReleaseComObject(sketchFeature);
                ReleaseComObject(sketchPlane);
                ReleaseComObject(featureManager);
                ReleaseComObject(sketchManager);
                ReleaseComObject(document);
            }
        }

        private string GetDefaultPartTemplatePath()
        {
            string configuredTemplatePath = _solidWorks.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);

            if (!string.IsNullOrWhiteSpace(configuredTemplatePath) &&
                File.Exists(configuredTemplatePath))
            {
                return configuredTemplatePath;
            }

            string resolvedTemplatePath = _solidWorks.GetDocumentTemplate(
                (int)swDocumentTypes_e.swDocPART,
                string.Empty,
                0,
                0,
                0);

            if (!string.IsNullOrWhiteSpace(resolvedTemplatePath) &&
                File.Exists(resolvedTemplatePath))
            {
                AddinLog.Info(
                    $"No explicit default part template was configured; SOLIDWORKS resolved '{resolvedTemplatePath}'.");
                return resolvedTemplatePath;
            }

            string? installedTemplatePath = FindInstalledPartTemplate();
            if (installedTemplatePath != null)
            {
                AddinLog.Info(
                    $"SOLIDWORKS template preferences were empty; using installed template '{installedTemplatePath}'.");
                return installedTemplatePath;
            }

            string configuredPathDetail = string.IsNullOrWhiteSpace(configuredTemplatePath)
                ? "未配置显式默认模板"
                : $"配置路径不存在：{configuredTemplatePath}";
            throw new FileNotFoundException(
                $"SOLIDWORKS 未能解析可用的零件模板（{configuredPathDetail}）。请在“系统选项 → 默认模板”中配置零件模板。",
                configuredTemplatePath);
        }

        private string? FindInstalledPartTemplate()
        {
            int productYear = 2026;
            string revision = _solidWorks.RevisionNumber();
            string[] revisionParts = revision.Split('.');
            if (revisionParts.Length > 0 &&
                int.TryParse(revisionParts[0], out int revisionMajor) &&
                revisionMajor >= 8)
            {
                productYear = revisionMajor + 1992;
            }

            string templateDirectory = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                "SolidWorks",
                $"SOLIDWORKS {productYear}",
                "templates");

            if (!Directory.Exists(templateDirectory))
            {
                return null;
            }

            string[] preferredNames =
            {
                "gb_part.prtdot",
                "Part.prtdot",
                "part.prtdot"
            };

            foreach (string preferredName in preferredNames)
            {
                string preferredPath = Path.Combine(templateDirectory, preferredName);
                if (File.Exists(preferredPath))
                {
                    return preferredPath;
                }
            }

            string[] availableTemplates = Directory.GetFiles(templateDirectory, "*.prtdot");
            if (availableTemplates.Length == 0)
            {
                return null;
            }

            Array.Sort(availableTemplates, StringComparer.OrdinalIgnoreCase);
            return availableTemplates[0];
        }

        private void EnsureTargetDocumentIsActive(IModelDoc2 targetDocument)
        {
            IModelDoc2? activeDocument = null;

            try
            {
                activeDocument = _solidWorks.IActiveDoc2;
                if (activeDocument == null || !AreSameComObject(activeDocument, targetDocument))
                {
                    throw new InvalidOperationException(
                        "SOLIDWORKS 当前活动文档不是本次新建的零件，已中止操作以保护其他模型。");
                }
            }
            finally
            {
                ReleaseComObject(activeDocument);
            }
        }

        private static bool AreSameComObject(object first, object second)
        {
            IntPtr firstIdentity = IntPtr.Zero;
            IntPtr secondIdentity = IntPtr.Zero;

            try
            {
                firstIdentity = Marshal.GetIUnknownForObject(first);
                secondIdentity = Marshal.GetIUnknownForObject(second);
                return firstIdentity == secondIdentity;
            }
            finally
            {
                if (secondIdentity != IntPtr.Zero)
                {
                    Marshal.Release(secondIdentity);
                }

                if (firstIdentity != IntPtr.Zero)
                {
                    Marshal.Release(firstIdentity);
                }
            }
        }

        private static bool HasActiveSketch(SketchManager sketchManager)
        {
            Sketch? activeSketch = null;

            try
            {
                activeSketch = sketchManager.ActiveSketch;
                return activeSketch != null;
            }
            finally
            {
                ReleaseComObject(activeSketch);
            }
        }

        private static void CreateProfileSketch(
            SketchManager sketchManager,
            bool enterSketch,
            PrimitiveDefinition definition,
            out object? profileEntities)
        {
            profileEntities = null;
            bool shouldExitSketch = false;
            Sketch? activeSketch = null;

            try
            {
                if (enterSketch)
                {
                    sketchManager.InsertSketch(true);
                }

                activeSketch = sketchManager.ActiveSketch;
                if (activeSketch == null)
                {
                    throw new InvalidOperationException("SOLIDWORKS 没有进入二维草图编辑状态。");
                }
                shouldExitSketch = true;

                switch (definition.Profile)
                {
                    case PrimitiveProfileKind.Rectangle:
                        profileEntities = sketchManager.CreateCornerRectangle(
                            -definition.HalfWidthMeters,
                            -definition.HalfHeightMeters,
                            0,
                            definition.HalfWidthMeters,
                            definition.HalfHeightMeters,
                            0);
                        break;
                    case PrimitiveProfileKind.Circle:
                        profileEntities = sketchManager.CreateCircleByRadius(
                            0,
                            0,
                            0,
                            definition.RadiusMeters);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(definition.Profile),
                            definition.Profile,
                            "未知的草图轮廓类型。");
                }

                if (profileEntities == null)
                {
                    throw new InvalidOperationException("SOLIDWORKS 没有创建草图轮廓。");
                }
            }
            finally
            {
                ReleaseComObject(activeSketch);

                if (shouldExitSketch)
                {
                    sketchManager.InsertSketch(true);
                }
            }
        }

        private static Feature? FindFirstFeatureByType(IModelDoc2 document, string featureType)
        {
            Feature? current = document.FirstFeature() as Feature;

            while (current != null)
            {
                Feature? next = current.GetNextFeature() as Feature;
                if (string.Equals(current.GetTypeName2(), featureType, StringComparison.Ordinal))
                {
                    ReleaseComObject(next);
                    return current;
                }

                ReleaseComObject(current);
                current = next;
            }

            return null;
        }

        private static Feature? FindLastFeatureByType(IModelDoc2 document, string featureType)
        {
            Feature? current = document.FirstFeature() as Feature;
            Feature? match = null;

            while (current != null)
            {
                Feature? next = current.GetNextFeature() as Feature;
                if (string.Equals(current.GetTypeName2(), featureType, StringComparison.Ordinal))
                {
                    ReleaseComObject(match);
                    match = current;
                }
                else
                {
                    ReleaseComObject(current);
                }

                current = next;
            }

            return match;
        }

        private static void TryRenameFeature(Feature feature, string name)
        {
            try
            {
                feature.Name = name;
            }
            catch (COMException exception)
            {
                AddinLog.Error($"Feature rename to '{name}' failed; geometry creation will continue.", exception);
            }
        }

        private static void TryShowResult(IModelDoc2 document)
        {
            try
            {
                document.ShowNamedView2(string.Empty, (int)swStandardViews_e.swIsometricView);
                document.ViewZoomtofit2();
            }
            catch (COMException exception)
            {
                AddinLog.Error("Geometry was created, but the result view could not be adjusted.", exception);
            }
        }

        private void TryCloseFailedDocument(string documentTitle)
        {
            try
            {
                _solidWorks.CloseDoc(documentTitle);
                AddinLog.Info($"Closed incomplete generated document '{documentTitle}'.");
            }
            catch (Exception cleanupException)
            {
                AddinLog.Error($"Could not close incomplete generated document '{documentTitle}'.", cleanupException);
            }
        }

        private static void ReleaseComObjects(object? value)
        {
            if (value is IEnumerable items)
            {
                foreach (object? item in items)
                {
                    ReleaseComObject(item);
                }
            }

            ReleaseComObject(value);
        }

        private static void ReleaseComObject(object? value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.ReleaseComObject(value);
            }
        }
    }
}
