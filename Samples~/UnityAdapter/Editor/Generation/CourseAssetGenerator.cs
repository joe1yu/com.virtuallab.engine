using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.Unity.Authoring.Generation
{
    public sealed class CourseCompilationResultForGeneration
    {
        public CourseCompilationResultForGeneration(
            CompiledCourseDefinition definition)
        {
            Definition = definition
                ?? throw new ArgumentNullException(nameof(definition));
            Diagnostics = Array.Empty<CourseCompilationDiagnostic>();
        }

        public CourseCompilationResultForGeneration(
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Diagnostics = diagnostics?.ToArray()
                ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public CompiledCourseDefinition Definition { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
        public bool IsSuccess =>
            Definition != null && Diagnostics.Count == 0;
    }

    public sealed class CourseAssetGenerator
    {
        public CompiledCourseAsset GenerateOrUpdate(
            string assetPath,
            CompiledCourseDefinition definition,
            string presentationJson)
        {
            return GenerateOrUpdate(
                assetPath,
                definition,
                presentationJson,
                Array.Empty<CourseTextArtifactBinding>());
        }

        public CompiledCourseAsset GenerateOrUpdate(
            string assetPath,
            CompiledCourseDefinition definition,
            string presentationJson,
            IEnumerable<CourseTextArtifactBinding> textArtifacts)
        {
            ValidatePath(assetPath);
            var domainJson = CourseAssetDecoder.EncodeDomain(definition);

            var asset = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                assetPath);
            var created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<CompiledCourseAsset>();
            }

            // 所有输入先完成校验和序列化，避免失败时污染上一次可用资产。
            asset.SetData(
                definition.CourseId,
                domainJson,
                presentationJson,
                textArtifacts);
            if (created)
            {
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        public bool TryGenerateOrUpdate(
            string assetPath,
            CourseCompilationResultForGeneration compilation,
            string presentationJson)
        {
            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (!compilation.IsSuccess)
            {
                return false;
            }

            GenerateOrUpdate(
                assetPath,
                compilation.Definition,
                presentationJson);
            return true;
        }

        /// <summary>
        /// 使用已经通过全部编译阶段的内存结果生成资产。该入口不会重新读取蓝图，
        /// 从而保证校验内容、运行时资产和学科产物来自同一份快照。
        /// </summary>
        public bool TryGenerateFromBlueprint(
            string assetPath,
            string generatedDirectory,
            CourseBlueprintCompilationResult compilation,
            out IReadOnlyList<CourseCompilationDiagnostic> diagnostics)
        {
            ValidatePath(assetPath);
            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (!compilation.IsSuccess)
            {
                diagnostics = compilation.Diagnostics;
                return false;
            }

            if (!TryValidateResources(
                    compilation.Domain,
                    generatedDirectory,
                    out diagnostics))
            {
                return false;
            }

            if (!TryValidateArtifacts(
                    generatedDirectory,
                    compilation.GeneratedArtifacts,
                    out var generatedPath,
                    out var artifacts,
                    out diagnostics))
            {
                return false;
            }

            var presentationJson = CourseAssetDecoder.EncodePresentation(
                compilation.Presentation);
            var previousAsset = AssetDatabase.LoadAssetAtPath<
                CompiledCourseAsset>(assetPath);
            // 旧字段资产不参与兼容读取，下一次生成直接用当前载荷覆盖；
            // 仍复用原 ScriptableObject，保持生成资产身份稳定。
            var previousAssetState = previousAsset?.HasCompiledPayload == true
                ? new AssetState(previousAsset)
                : null;
            var deleteAssetWhenRollback = previousAsset == null;
            var previousFiles = artifacts.ToDictionary(
                value => value.FileName,
                value =>
                {
                    var path = Path.Combine(generatedPath, value.FileName);
                    return File.Exists(path) ? File.ReadAllBytes(path) : null;
                },
                StringComparer.Ordinal);
            var stagingPath = generatedPath + ".tmp-"
                + Guid.NewGuid().ToString("N");
            var stagingMetaPath = stagingPath + ".meta";

            try
            {
                Directory.CreateDirectory(stagingPath);
                foreach (var artifact in artifacts)
                {
                    File.WriteAllBytes(
                        Path.Combine(stagingPath, artifact.FileName),
                        artifact.Content);
                }

                GenerateOrUpdate(
                    assetPath,
                    compilation.Domain,
                    presentationJson,
                    artifacts.Select(value =>
                        new CourseTextArtifactBinding(
                            value.ArtifactId,
                            value.TextContent)));

                Directory.CreateDirectory(generatedPath);
                foreach (var artifact in artifacts)
                {
                    File.Copy(
                        Path.Combine(stagingPath, artifact.FileName),
                        Path.Combine(generatedPath, artifact.FileName),
                        true);
                }

                AssetDatabase.Refresh();
                diagnostics = Array.Empty<CourseCompilationDiagnostic>();
                return true;
            }
            catch (Exception exception)
            {
                var rollbackError = TryRollback(
                    assetPath,
                    previousAssetState,
                    deleteAssetWhenRollback,
                    generatedPath,
                    previousFiles);
                diagnostics = new[]
                {
                    new CourseCompilationDiagnostic(
                        "course.asset.atomic-write-failed",
                        "课程资产生成",
                        1,
                        1,
                        string.Empty,
                        compilation.Domain.CourseId,
                        $"课程资产或学科产物写入失败：{exception.Message}"
                        + rollbackError,
                        "检查生成目录权限和文件占用后重试。")
                };
                return false;
            }
            finally
            {
                if (Directory.Exists(stagingPath))
                {
                    try
                    {
                        Directory.Delete(stagingPath, true);
                    }
                    catch
                    {
                        // 临时目录清理失败不能反转已经明确的生成结果。
                    }
                }

                if (File.Exists(stagingMetaPath))
                {
                    try
                    {
                        File.Delete(stagingMetaPath);
                    }
                    catch
                    {
                        // 临时元文件清理失败不应反转已经成功生成的课程资产。
                    }
                }
            }
        }

        private static bool TryValidateResources(
            CompiledCourseDefinition domain,
            string generatedDirectory,
            out IReadOnlyList<CourseCompilationDiagnostic> diagnostics)
        {
            var errors = new List<CourseCompilationDiagnostic>();
            var hasExperimentPrefab = false;
            var prefabContracts = domain.PrefabContracts.ToDictionary(
                value => value.EntityId,
                StringComparer.Ordinal);
            var courseRoot = Path.GetDirectoryName(
                generatedDirectory.TrimEnd('/', '\\'));
            foreach (var resource in domain.Resources
                         .OrderBy(value => value.ResourceId,
                             StringComparer.Ordinal))
            {
                var resolvedAssetPath = CourseAssetPath.Resolve(
                    resource.AssetPath,
                    courseRoot);
                var loaded = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    resolvedAssetPath);
                if (loaded == null || !EditorUtility.IsPersistent(loaded))
                {
                    errors.Add(ResourceDiagnostic(
                        "course.asset.missing",
                        resource,
                        "资源路径没有指向已保存的工程资产。",
                        "修正资源路径并确认资产已导入。"));
                    continue;
                }

                if (!string.Equals(
                        resource.ResourceId,
                        domain.ExperimentPrefabResourceId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var prefab = loaded as GameObject;
                if (prefab == null
                    || PrefabUtility.GetPrefabAssetType(prefab)
                    == PrefabAssetType.NotAPrefab)
                {
                    errors.Add(ResourceDiagnostic(
                        "course.asset.type-mismatch",
                        resource,
                        $"实验资源必须是 Prefab，当前资产为“"
                        + loaded.GetType().Name + "”。",
                        "将实验预制体路径修正为持久化 Prefab 资产。"));
                    continue;
                }

                hasExperimentPrefab = true;
                ValidateExperimentPrefab(
                    domain,
                    prefab,
                    prefabContracts,
                    errors);
            }

            if (!hasExperimentPrefab)
            {
                errors.Add(new CourseCompilationDiagnostic(
                    "course.experiment-prefab.missing",
                    "课程.csv",
                    2,
                    1,
                    string.Empty,
                    domain.ExperimentPrefabResourceId ?? string.Empty,
                    "课程实验资源没有解析为 Prefab。",
                    "将实验预制体路径修正为持久化 Prefab 资产。"));
            }

            diagnostics = errors;
            return errors.Count == 0;
        }

        private static void ValidateExperimentPrefab(
            CompiledCourseDefinition domain,
            GameObject prefab,
            IReadOnlyDictionary<string, CoursePrefabContractDefinition>
                contracts,
            ICollection<CourseCompilationDiagnostic> errors)
        {
            var views = prefab.GetComponentsInChildren<CourseEntityView>(true);
            var viewsById = views
                .Where(value => !string.IsNullOrWhiteSpace(value.EntityId))
                .GroupBy(value => value.EntityId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.ToArray(),
                    StringComparer.Ordinal);
            if (views.Any(value => string.IsNullOrWhiteSpace(value.EntityId)))
            {
                errors.Add(ExperimentPrefabDiagnostic(
                    "course.experiment-prefab.entity-id-missing",
                    domain.CourseId,
                    "实验预制体包含未填写实体 ID 的 CourseEntityView。",
                    "为每个操作对象节点填写唯一且与实验对象.csv 一致的实体 ID。"));
            }

            var expected = new HashSet<string>(
                domain.Entities.Select(value => value.EntityId),
                StringComparer.Ordinal);
            foreach (var pair in viewsById.Where(value =>
                         value.Value.Length > 1))
            {
                errors.Add(ExperimentPrefabDiagnostic(
                    "course.experiment-prefab.entity-id-duplicate",
                    pair.Key,
                    $"实验预制体包含 {pair.Value.Length} 个实体视图“{pair.Key}”。",
                    "每个领域实体在实验预制体中只保留一个 CourseEntityView。"));
            }

            foreach (var unknown in viewsById.Keys.Where(value =>
                         !expected.Contains(value)))
            {
                errors.Add(ExperimentPrefabDiagnostic(
                    "course.experiment-prefab.entity-unknown",
                    unknown,
                    $"实验预制体中的实体视图“{unknown}”未在实验对象.csv 中声明。",
                    "删除该实体视图，或在实验对象.csv 中声明同 ID 实体。"));
            }

            foreach (var entityId in expected.Where(value =>
                         !viewsById.ContainsKey(value)))
            {
                errors.Add(ExperimentPrefabDiagnostic(
                    "course.experiment-prefab.entity-missing",
                    entityId,
                    $"实验预制体缺少实体视图“{entityId}”。",
                    "在实验预制体中添加带同 ID CourseEntityView 的操作对象。"));
            }

            foreach (var pair in contracts)
            {
                if (!viewsById.TryGetValue(pair.Key, out var matching)
                    || matching.Length != 1)
                {
                    continue;
                }

                foreach (var diagnostic in PrefabContractValidator.Validate(
                             matching[0],
                             pair.Value))
                {
                    errors.Add(ExperimentPrefabDiagnostic(
                        diagnostic.Code,
                        pair.Key,
                        diagnostic.Message,
                        "修正实体节点的通用组件、语义锚点或表现插槽。"));
                }
            }
        }

        private static CourseCompilationDiagnostic ExperimentPrefabDiagnostic(
            string code,
            string entityId,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                "实验预制体",
                1,
                1,
                string.Empty,
                entityId,
                reason,
                suggestion);

        private static bool TryValidateArtifacts(
            string generatedDirectory,
            IEnumerable<NormalizedItem<GeneratedCourseArtifact>> source,
            out string generatedPath,
            out IReadOnlyList<ArtifactWrite> artifacts,
            out IReadOnlyList<CourseCompilationDiagnostic> diagnostics)
        {
            generatedPath = string.IsNullOrWhiteSpace(generatedDirectory)
                ? string.Empty
                : Path.GetFullPath(generatedDirectory);
            var errors = new List<CourseCompilationDiagnostic>();
            var result = new List<ArtifactWrite>();
            if (string.IsNullOrWhiteSpace(generatedDirectory)
                || string.Equals(
                    generatedPath,
                    Path.GetPathRoot(generatedPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(GenerationDiagnostic(
                    "course.artifact.directory-invalid",
                    generatedDirectory,
                    "学科产物生成目录不能为空或磁盘根目录。",
                    "指定课程自己的 Generated 目录。"));
            }

            foreach (var item in source
                         ?? Array.Empty<
                             NormalizedItem<GeneratedCourseArtifact>>())
            {
                var artifactId = item.Definition.ArtifactId?.Trim();
                if (string.IsNullOrWhiteSpace(artifactId))
                {
                    errors.Add(GenerationDiagnostic(
                        "course.artifact.id-missing",
                        item.Identity?.ToString(),
                        "学科产物缺少稳定的产物 ID。",
                        "由学科扩展包为产物提供唯一且稳定的 ID。"));
                    continue;
                }

                var fileName = item.Definition.SuggestedFileName;
                if (string.IsNullOrWhiteSpace(fileName)
                    || !string.Equals(
                        fileName,
                        Path.GetFileName(fileName),
                        StringComparison.Ordinal)
                    || fileName.IndexOfAny(
                        Path.GetInvalidFileNameChars()) >= 0)
                {
                    errors.Add(GenerationDiagnostic(
                        "course.artifact.filename-invalid",
                        item.Definition.ArtifactId,
                        $"学科产物文件名“{fileName}”不是安全的单文件名。",
                        "只填写文件名和扩展名，不要包含目录。"));
                    continue;
                }

                try
                {
                    result.Add(new ArtifactWrite(
                        artifactId,
                        fileName,
                        item.Definition.Content));
                }
                catch (DecoderFallbackException)
                {
                    errors.Add(GenerationDiagnostic(
                        "course.artifact.encoding-invalid",
                        artifactId,
                        $"学科产物“{artifactId}”不是有效的 UTF-8 文本。",
                        "课程资产只内嵌文本产物；请把二进制资源改为普通 Unity 资源引用。"));
                }
            }

            foreach (var duplicate in result
                         .GroupBy(value => value.ArtifactId,
                             StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                errors.Add(GenerationDiagnostic(
                    "course.artifact.id-duplicate",
                    duplicate.Key,
                    $"学科产物 ID“{duplicate.Key}”重复。",
                    "为每份学科产物设置唯一且稳定的产物 ID。"));
            }

            foreach (var duplicate in result
                         .GroupBy(value => value.FileName,
                             StringComparer.OrdinalIgnoreCase)
                         .Where(value => value.Count() > 1))
            {
                errors.Add(GenerationDiagnostic(
                    "course.artifact.filename-duplicate",
                    duplicate.Key,
                    $"多个学科产物使用同一文件名“{duplicate.Key}”。",
                    "为每份学科产物设置唯一文件名。"));
            }

            artifacts = result;
            diagnostics = errors;
            return errors.Count == 0;
        }

        private static void RestoreAsset(
            string assetPath,
            AssetState previous,
            bool deleteAssetWhenMissing)
        {
            if (previous == null)
            {
                if (deleteAssetWhenMissing)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }

                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                assetPath);
            asset.SetData(
                previous.CourseId,
                previous.DomainJson,
                previous.PresentationJson,
                previous.TextArtifacts);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static void RestoreArtifactFiles(
            string generatedPath,
            IReadOnlyDictionary<string, byte[]> previousFiles)
        {
            foreach (var pair in previousFiles)
            {
                var path = Path.Combine(generatedPath, pair.Key);
                if (pair.Value == null)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                else
                {
                    Directory.CreateDirectory(generatedPath);
                    File.WriteAllBytes(path, pair.Value);
                }
            }
        }

        private static string TryRollback(
            string assetPath,
            AssetState previousAsset,
            bool deleteAssetWhenMissing,
            string generatedPath,
            IReadOnlyDictionary<string, byte[]> previousFiles)
        {
            try
            {
                RestoreAsset(
                    assetPath,
                    previousAsset,
                    deleteAssetWhenMissing);
                RestoreArtifactFiles(generatedPath, previousFiles);
                AssetDatabase.Refresh();
                return string.Empty;
            }
            catch (Exception exception)
            {
                return $"；回滚也失败：{exception.Message}";
            }
        }

        private static CourseCompilationDiagnostic GenerationDiagnostic(
            string code,
            string configurationId,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                "课程资产生成",
                1,
                1,
                string.Empty,
                configurationId ?? string.Empty,
                reason,
                suggestion);

        private sealed class ArtifactWrite
        {
            public ArtifactWrite(
                string artifactId,
                string fileName,
                byte[] content)
            {
                ArtifactId = artifactId;
                FileName = fileName;
                Content = content;
                TextContent = new UTF8Encoding(false, true)
                    .GetString(content);
            }

            public string ArtifactId { get; }
            public string FileName { get; }
            public byte[] Content { get; }
            public string TextContent { get; }
        }

        private sealed class AssetState
        {
            public AssetState(CompiledCourseAsset asset)
            {
                CourseId = asset.CourseId;
                DomainJson = asset.DomainJson;
                PresentationJson = asset.PresentationJson;
                TextArtifacts = asset.TextArtifacts.ToArray();
            }

            public string CourseId { get; }
            public string DomainJson { get; }
            public string PresentationJson { get; }
            public CourseTextArtifactBinding[] TextArtifacts { get; }
        }

        private static CourseCompilationDiagnostic ResourceDiagnostic(
            string code,
            CourseResourceDefinition resource,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                "资源.csv",
                1,
                1,
                string.Empty,
                resource.ResourceId,
                reason,
                suggestion);

        private static void ValidatePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".asset", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "课程资产路径必须位于 Assets 下且以 .asset 结尾。",
                    nameof(assetPath));
            }
        }
    }
}
