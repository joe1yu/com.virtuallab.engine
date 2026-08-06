using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Catalogs
{
    public sealed class CourseAuthoringProviderDiscoveryResult
    {
        internal CourseAuthoringProviderDiscoveryResult(
            IEnumerable<ICourseAuthoringCatalogProvider> providers,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Providers = providers.ToArray();
            Diagnostics = diagnostics.ToArray();
        }

        public IReadOnlyList<ICourseAuthoringCatalogProvider> Providers { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
        public bool IsSuccess => Diagnostics.Count == 0;
    }

    /// <summary>
    /// 从当前项目已安装的 Editor 程序集中发现创作目录提供者。
    /// 通用工作台不引用 Chemistry 或其它学科的具体类型。
    /// </summary>
    public static class CourseAuthoringCatalogProviderDiscovery
    {
        public static CourseAuthoringProviderDiscoveryResult Discover()
        {
            var providers = new List<ICourseAuthoringCatalogProvider>();
            var diagnostics = new List<CourseCompilationDiagnostic>();
            foreach (var type in TypeCache
                         .GetTypesDerivedFrom<ICourseAuthoringCatalogProvider>()
                         .Where(value => !value.IsAbstract
                                         && value.GetConstructor(Type.EmptyTypes)
                                         != null)
                         .OrderBy(value => value.FullName, StringComparer.Ordinal))
            {
                try
                {
                    providers.Add((ICourseAuthoringCatalogProvider)
                        Activator.CreateInstance(type));
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new CourseCompilationDiagnostic(
                        "authoring.provider.create-failed",
                        "模块创作目录",
                        1,
                        1,
                        string.Empty,
                        type.FullName ?? string.Empty,
                        $"无法创建创作目录提供者“{type.FullName}”：{exception.Message}",
                        "确保提供者具有可用的无参构造函数，且构造过程不依赖场景状态。"));
                }
            }

            return new CourseAuthoringProviderDiscoveryResult(
                providers.OrderBy(value => value.PackageId, StringComparer.Ordinal),
                diagnostics.OrderBy(value => value.ConfigurationId,
                    StringComparer.Ordinal));
        }
    }
}
