using System;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CoreRecipeExpansionTests
    {
        [Test]
        public void 可抓取特征从平台CSV自动生成抓取释放状态表现和预制体要求()
        {
            var blueprint = ReadBlueprint("大试管", "可抓取");
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            Assert.That(catalog.IsValid, Is.True);
            var result = new RecipeExpander().Expand(blueprint, catalog);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(
                result.Model.Actions.Select(value =>
                    value.Definition.PolicyId),
                Is.EquivalentTo(new[]
                {
                    "策略.通用抓取.大试管",
                    "策略.通用释放.大试管"
                }));
            Assert.That(
                result.Model.StateChanges.Select(value =>
                    value.Definition.OperationId),
                Is.EquivalentTo(new[]
                {
                    "设置关系",
                    "移除关系"
                }));
            Assert.That(
                result.Model.PresentationStates.Select(value =>
                    value.Definition.StateId),
                Does.Contain("状态.大试管被学生持有"));
            Assert.That(
                result.Model.PresentationEffects.Select(value =>
                    value.Definition.ProtocolId),
                Is.EquivalentTo(new[]
                {
                    "interaction.follow-anchor",
                    "interaction.stop-follow"
                }));
            Assert.That(
                result.Model.PrefabContracts.Select(value =>
                    value.Definition.Identifier),
                Does.Contain("抓取锚点"));

            var grab = result.Model.Actions.Single(value =>
                value.Definition.PolicyId == "策略.通用抓取.大试管");
            Assert.That(
                grab.Provenance.Sources
                    .Select(value => value.Layer)
                    .Distinct(),
                Is.EquivalentTo(new[]
                {
                    ConfigurationLayer.Course,
                    ConfigurationLayer.Platform
                }));
            Assert.That(
                grab.Provenance.Sources.Any(value =>
                    value.FileName == "实验对象.csv" && value.Line == 2),
                Is.True);
            Assert.That(
                grab.Provenance.Sources.Any(value =>
                    value.FileName == "操作.csv" && value.Line > 1),
                Is.True);
        }

        [Test]
        public void 未声明可抓取特征的墙壁不生成抓取候选()
        {
            var blueprint = ReadBlueprint("墙壁", "可夹持");
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            var result = new RecipeExpander().Expand(blueprint, catalog);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Model.Entities.Single().Definition.EntityId,
                Is.EqualTo("墙壁"));
            Assert.That(result.Model.Actions, Is.Empty);
            Assert.That(result.Model.StateChanges, Is.Empty);
            Assert.That(result.Model.PresentationEffects, Is.Empty);
            Assert.That(result.Model.PrefabContracts, Is.Empty);
        }

        [Test]
        public void 可观察特征自动获得平台默认提示表现()
        {
            var blueprint = ReadBlueprint("试管", "可观察");
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            var result = new RecipeExpander().Expand(blueprint, catalog);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Model.Actions.Single().Definition.PresentationGroupId,
                Is.Not.Empty);
            Assert.That(result.Model.PresentationGroups.Count(), Is.EqualTo(1));
            Assert.That(
                result.Model.PresentationEffects.Select(value =>
                    value.Definition.ProtocolId),
                Does.Contain("ui.message"));
        }

        [Test]
        public void 平台提供者从七张结构化CSV保留配方来源行()
        {
            var package = new CoreRecipePackageProvider().Load();

            Assert.That(package.PackageId, Is.EqualTo("平台通用"));
            Assert.That(package.Recipes.Select(value => value.RecipeId),
                Does.Contain("通用.抓取"));
            Assert.That(
                package.Actions
                    .Where(value => value.RecipeId == "通用.抓取")
                    .Select(value => value.SemanticCommandId),
                Is.EquivalentTo(new[] { "抓取", "释放" }));
            Assert.That(
                package.Actions.All(value =>
                    value.Source != null
                    && value.Source.FileName == "操作.csv"
                    && value.Source.Line > 1),
                Is.True);
        }

        private static CourseBlueprint ReadBlueprint(
            string entityId,
            string features)
        {
            var source = new CourseBlueprintSource(new[]
            {
                new CourseBlueprintFile(
                    "课程.csv",
                    "课程ID,显示名称,学科配方包,环境Prefab\n"
                    + "配方测试,配方测试,,环境.prefab\n"),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,Prefab,特征列表,初始位置,初始旋转\n"
                    + $"{entityId},{entityId},{entityId}.prefab,{features},0|0|0,0|0|0\n")
            });
            var read = new CourseBlueprintReader().Read(source);
            Assert.That(read.IsSuccess, Is.True);
            return read.Blueprint;
        }
    }
}
