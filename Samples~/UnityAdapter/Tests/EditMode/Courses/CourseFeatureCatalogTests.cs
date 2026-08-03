using System;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Recipes;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseFeatureCatalogTests
    {
        [Test]
        public void DerivesFeaturesAndCompatibilityParametersFromRecipes()
        {
            var recipes = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            var catalog = CourseFeatureCatalog.Create(recipes);

            Assert.That(catalog.TryGet("可抓取", out var grabbable), Is.True);
            CollectionAssert.Contains(grabbable.RecipeIds, "通用.抓取");

            Assert.That(catalog.TryGet("可连接", out var connector), Is.True);
            Assert.That(
                connector.Parameters.Any(value =>
                    value.ColumnName == "参数.端口.出口.兼容组"),
                Is.True);

            Assert.That(catalog.TryGet("容器", out var container), Is.True);
            Assert.That(container.IsCapabilityOnly, Is.True);
            Assert.That(container.RecipeIds, Is.Empty);
            Assert.That(catalog.TryGet("尚未注册的能力", out _), Is.False);
        }

        [Test]
        public void PairSourceFeatureIncludesInheritedRecipeParameters()
        {
            var platform = new StubProvider(new RecipePackage(
                "平台通用",
                RecipeLayer.Platform,
                recipes: new[]
                {
                    new RecipeDefinition(
                        "通用.过程",
                        RecipeMatchKind.Process,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        RecipeSafetyLevel.CourseMayTighten,
                        Array.Empty<string>())
                },
                parameters: new[]
                {
                    new RecipeParameterContract(
                        "通用.过程",
                        "目标实体",
                        RecipeParameterType.EntityId,
                        true,
                        string.Empty,
                        null,
                        null,
                        string.Empty,
                        false,
                        RecipeSafetyLevel.CourseMayTighten)
                }));
            var discipline = new StubProvider(new RecipePackage(
                "测试学科",
                RecipeLayer.Discipline,
                recipes: new[]
                {
                    new RecipeDefinition(
                        "测试.作用",
                        RecipeMatchKind.CompatiblePair,
                        string.Empty,
                        "通用.过程",
                        string.Empty,
                        RecipeSafetyLevel.CourseMayTighten,
                        Array.Empty<string>(),
                        compatiblePair: new CompatiblePairContract(
                            "可作用源",
                            "可作用目标",
                            "作用组.来源",
                            "作用组.目标",
                            PairCompatibilityKind.EqualGroup,
                            false))
                }));
            var recipes = RecipeCatalog.Create(
                platform,
                new IRecipePackageProvider[] { discipline });

            var catalog = CourseFeatureCatalog.Create(recipes);

            Assert.That(catalog.TryGet("可作用源", out var source), Is.True);
            Assert.That(
                source.Parameters.Any(value =>
                    value.ColumnName == "参数.目标实体"
                    && value.Type == RecipeParameterType.EntityId),
                Is.True);
            Assert.That(catalog.TryGet("可作用目标", out var target), Is.True);
            Assert.That(
                target.Parameters.Any(value =>
                    value.ColumnName == "参数.目标实体"),
                Is.False);
        }

        private sealed class StubProvider : IRecipePackageProvider
        {
            private readonly RecipePackage _package;

            public StubProvider(RecipePackage package)
            {
                _package = package;
            }

            public string PackageId => _package.PackageId;
            public RecipePackage Load() => _package;
        }
    }
}
