using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Generation;
using VirtualLab.Unity.Authoring.Recipes;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public sealed class CourseWorkbenchCompilation
    {
        internal CourseWorkbenchCompilation(
            CourseBlueprintCompilationResult compilation,
            CourseFeatureCatalog featureCatalog,
            IEnumerable<string> messages,
            IEnumerable<string> availableDisciplinePackageIds)
        {
            Compilation = compilation;
            FeatureCatalog = featureCatalog;
            Messages = (messages ?? Array.Empty<string>()).ToArray();
            AvailableDisciplinePackageIds = (
                    availableDisciplinePackageIds ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        public CourseBlueprintCompilationResult Compilation { get; }
        public CourseFeatureCatalog FeatureCatalog { get; }
        public IReadOnlyList<string> Messages { get; }
        public IReadOnlyList<string> AvailableDisciplinePackageIds { get; }
    }

    /// <summary>
    /// 工作台的编译应用服务。提供者只在编辑期发现，并按类型名和配方包 ID
    /// 稳定排序；重复 ID 直接失败，避免因 TypeCache 返回顺序改变编译结果。
    /// </summary>
    public sealed class CourseWorkbenchCompiler
    {
        public CourseWorkbenchCompilation Compile(CourseBlueprintSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var messages = new List<string>();
            var read = new CourseBlueprintReader().Read(source);
            var selectedPackageIds = read.Blueprint?.Course
                ?.DisciplinePackageIds
                ?? Array.Empty<string>();
            var providers = DiscoverDisciplineProviders(messages);
            var selectedProviders = providers
                .Where(value => selectedPackageIds.Contains(
                    value.PackageId,
                    StringComparer.Ordinal))
                .ToArray();

            var platform = new CoreRecipePackageProvider();
            var compilation = new CourseBlueprintCompiler().Compile(
                source,
                platform,
                selectedProviders);
            var catalog = compilation.Catalog
                          ?? RecipeCatalog.Create(platform, selectedProviders);
            return new CourseWorkbenchCompilation(
                compilation,
                CourseFeatureCatalog.Create(catalog),
                messages,
                providers.Select(value => value.PackageId));
        }

        /// <summary>
        /// 返回当前项目中实际可用的学科类型。新建课程只允许从这里选择，
        /// 避免把序号或随意文本误写成学科配方包 ID。
        /// </summary>
        public static IReadOnlyList<string> FindAvailableDisciplinePackageIds()
        {
            var messages = new List<string>();
            return DiscoverDisciplineProviders(messages)
                .Select(value => value.PackageId)
                .ToArray();
        }

        private static IReadOnlyList<IRecipePackageProvider>
            DiscoverDisciplineProviders(ICollection<string> messages)
        {
            var providers = new List<IRecipePackageProvider>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<
                         IRecipePackageProvider>()
                     .OrderBy(value => value.FullName, StringComparer.Ordinal))
            {
                if (type.IsAbstract
                    || type.IsInterface
                    || (!type.IsPublic && !type.IsNestedPublic)
                    || type == typeof(CoreRecipePackageProvider))
                {
                    continue;
                }

                try
                {
                    var provider = Activator.CreateInstance(type)
                        as IRecipePackageProvider;
                    if (provider != null
                        && !string.IsNullOrWhiteSpace(provider.PackageId))
                    {
                        providers.Add(provider);
                    }
                }
                catch (Exception exception)
                {
                    messages.Add(
                        $"无法创建配方提供者 {type.FullName}：{exception.Message}");
                }
            }

            var duplicate = providers
                .GroupBy(value => value.PackageId, StringComparer.Ordinal)
                .FirstOrDefault(value => value.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidOperationException(
                    $"学科配方包 ID“{duplicate.Key}”存在多个提供者："
                    + string.Join(
                        "、",
                        duplicate.Select(value => value.GetType().FullName))
                    + "。为保证编译结果确定，工作台已停止选择提供者。");
            }

            return providers
                .OrderBy(value => value.PackageId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public sealed class CourseWorkbenchBuildResult
    {
        internal CourseWorkbenchBuildResult(
            string assetPath,
            CompiledCourseAsset asset,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            AssetPath = assetPath ?? string.Empty;
            Asset = asset;
            Diagnostics = (diagnostics
                           ?? Array.Empty<CourseCompilationDiagnostic>())
                .ToArray();
        }

        public string AssetPath { get; }
        public CompiledCourseAsset Asset { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
        public bool IsSuccess => Asset != null && Diagnostics.Count == 0;
    }

    /// <summary>
    /// 课程资产生成服务。只复用课程 ID 一致的既有资产，绝不因为目录内刚好只有
    /// 一个资产就覆盖它。
    /// </summary>
    public sealed class CourseWorkbenchBuildService
    {
        public CourseWorkbenchBuildResult Build(
            string courseRootAssetPath,
            CourseBlueprintCompilationResult compilation)
        {
            if (string.IsNullOrWhiteSpace(courseRootAssetPath))
            {
                throw new ArgumentException("课程根目录不能为空。", nameof(courseRootAssetPath));
            }

            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (!compilation.IsSuccess)
            {
                return new CourseWorkbenchBuildResult(
                    string.Empty,
                    null,
                    compilation.Diagnostics);
            }

            var generatedAssetPath = courseRootAssetPath + "/Generated";
            Directory.CreateDirectory(Path.GetFullPath(generatedAssetPath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var assetPath = ResolveCourseAssetPath(
                generatedAssetPath,
                compilation.Domain.CourseId);
            if (!new CourseAssetGenerator().TryGenerateFromBlueprint(
                    assetPath,
                    generatedAssetPath,
                    compilation,
                    out var diagnostics))
            {
                return new CourseWorkbenchBuildResult(
                    assetPath,
                    null,
                    diagnostics);
            }

            AssetDatabase.SaveAssets();
            return new CourseWorkbenchBuildResult(
                assetPath,
                AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(assetPath),
                Array.Empty<CourseCompilationDiagnostic>());
        }

        private static string ResolveCourseAssetPath(
            string generatedAssetPath,
            string courseId)
        {
            var existing = AssetDatabase.FindAssets(
                    "t:CompiledCourseAsset",
                    new[] { generatedAssetPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var matching = existing.Where(path =>
                    string.Equals(
                        AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(path)
                            ?.CourseId,
                        courseId,
                        StringComparison.Ordinal))
                .ToArray();
            if (matching.Length > 1)
            {
                throw new InvalidOperationException(
                    $"生成目录中存在多个课程 ID 为“{courseId}”的课程资产，请只保留一个。");
            }

            if (matching.Length == 1)
            {
                return matching[0];
            }

            var fileName = courseId;
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            var deterministicPath =
                generatedAssetPath + "/" + fileName + "课程.asset";
            var occupied = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                deterministicPath);
            if (occupied != null
                && !string.Equals(
                    occupied.CourseId,
                    courseId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"目标资产“{deterministicPath}”属于课程“{occupied.CourseId}”，工作台不会覆盖它。");
            }

            return deterministicPath;
        }
    }
}
