using System;
using System.Collections.Generic;
using System.IO;
using VirtualLab.Application.Courses;
using UnityEditor.Compilation;

namespace VirtualLab.Unity.Authoring.Recipes
{
    /// <summary>
    /// 显式定位并加载包内平台通用配方，不扫描运行时类型。
    /// </summary>
    public sealed class CoreRecipePackageProvider : IRecipePackageProvider
    {
        private const string Id = "平台通用";

        public string PackageId => Id;

        public IReadOnlyList<string> RequiredRuntimeModuleIds { get; } =
            new[] { CourseModuleIds.Core };

        public RecipePackage Load()
        {
            // 从程序集定义所在目录推导配方位置，使 Sample 可导入到任意 Assets 路径。
            var assemblyName = typeof(CoreRecipePackageProvider)
                .Assembly
                .GetName()
                .Name;
            var assemblyDefinitionPath = CompilationPipeline
                .GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            if (string.IsNullOrWhiteSpace(assemblyDefinitionPath))
            {
                throw new InvalidOperationException(
                    "无法定位平台通用配方所在的课程创作程序集。");
            }

            var authoringDirectory = Path.GetDirectoryName(
                assemblyDefinitionPath);

            return new RecipePackageCsvLoader().Load(
                Id,
                RecipeLayer.Platform,
                Path.Combine(
                    authoringDirectory,
                    "Recipes",
                    "平台通用"));
        }
    }
}
