using System;
using System.IO;
using System.Linq;
using VirtualLab.Chemistry.Authoring;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Drafts;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    /// <summary>
    /// 测试与生产构建器使用相同的十表编译入口，避免测试继续维护已删除的旧协议。
    /// </summary>
    internal static class OxygenCourseTestCompiler
    {
        public static CourseBlueprintCompilationResult Compile(string directory)
        {
            var source = CourseBlueprintSource.FromDirectory(
                Path.GetFullPath(directory));
            var read = new CourseAuthoringDraftReader().Read(source);
            var platform = new CoreRecipePackageProvider();
            var chemistry = new ChemistryRecipePackageProvider();
            var authoringCatalog = CourseAuthoringCatalog.Create(new[]
            {
                new ChemistryCourseAuthoringProvider()
            });
            var recipes = RecipeCatalog.Create(
                platform,
                new IRecipePackageProvider[] { chemistry });
            var expansion = read.Draft == null
                ? null
                : new CourseDraftExpander().Expand(
                    read.Draft,
                    authoringCatalog,
                    recipes);

            return new CourseBlueprintCompiler().Compile(
                expansion?.Blueprint,
                platform,
                new IRecipePackageProvider[] { chemistry },
                read.Diagnostics.Concat(
                    expansion?.Diagnostics
                    ?? Array.Empty<CourseCompilationDiagnostic>()));
        }
    }
}
