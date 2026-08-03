using System;
using System.IO;
using UnityEditor.PackageManager;

namespace VirtualLab.Unity.Authoring.Recipes
{
    /// <summary>
    /// 显式定位并加载包内平台通用配方，不扫描运行时类型。
    /// </summary>
    public sealed class CoreRecipePackageProvider : IRecipePackageProvider
    {
        private const string Id = "平台通用";

        public string PackageId => Id;

        public RecipePackage Load()
        {
            var packageInfo = PackageInfo.FindForAssembly(
                typeof(CoreRecipePackageProvider).Assembly);
            if (packageInfo == null)
            {
                throw new InvalidOperationException(
                    "无法定位平台通用配方所在的 Unity 包。");
            }

            return new RecipePackageCsvLoader().Load(
                Id,
                RecipeLayer.Platform,
                Path.Combine(
                    packageInfo.resolvedPath,
                    "Editor",
                    "Authoring",
                    "Recipes",
                    "平台通用"));
        }
    }
}
