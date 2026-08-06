using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Presentation;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Recipes;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class PresentationCatalogIntegrationTests
    {
        [Test]
        public void 蓝图表现覆盖拒绝目录中不存在的原语()
        {
            var context = ReadAndExpand(
                "未知原语",
                string.Empty,
                string.Empty,
                string.Empty);

            var result = new CourseOverrideApplier().Apply(
                context.Blueprint,
                context.Catalog,
                context.Expanded.Model);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.presentation.primitive-unknown"));
            Assert.That(
                result.Diagnostics.Single(value =>
                    value.Code == "blueprint.presentation.primitive-unknown")
                    .FileName,
                Is.EqualTo("表现.csv"));
        }

        [Test]
        public void 注入新原语后无需修改中文词典即可编译蓝图覆盖()
        {
            var context = ReadAndExpand(
                "测试闪光",
                string.Empty,
                string.Empty,
                string.Empty);

            var result = new CourseOverrideApplier(TestFlashCatalog()).Apply(
                context.Blueprint,
                context.Catalog,
                context.Expanded.Model);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Model.PresentationEffects.Single().Definition.ProtocolId,
                Is.EqualTo("test.flash"));
            Assert.That(
                TestFlashCatalog().CreateExecutors().Single().EffectId,
                Is.EqualTo("test.flash"));
        }

        [Test]
        public void 蓝图覆盖的枚举参数拒绝目录允许值之外的文本()
        {
            var context = ReadAndExpand(
                "测试闪光",
                "模式",
                "文本",
                "未知");

            var result = new CourseOverrideApplier(
                TestFlashCatalog(withMode: true)).Apply(
                context.Blueprint,
                context.Catalog,
                context.Expanded.Model);

            Assert.That(result.IsSuccess, Is.False);
            var diagnostic = result.Diagnostics.Single(value =>
                value.Code
                == "blueprint.presentation.parameter-value-invalid");
            Assert.That(diagnostic.FileName, Is.EqualTo("表现.csv"));
            Assert.That(diagnostic.Line, Is.EqualTo(2));
            StringAssert.Contains("允许值集合", diagnostic.Reason);
        }

        private static (
            CourseBlueprint Blueprint,
            RecipeCatalog Catalog,
            RecipeExpansionResult Expanded) ReadAndExpand(
                string primitive,
                string parameterName,
                string parameterType,
                string parameterValue)
        {
            var origin = CourseBlueprintTestFactory.Source(
                "表现.csv", 2, "测试闪光覆盖");
            var presentation = new CoursePresentationOverrideBlueprint(
                CourseBlueprintTestFactory.Values(origin,
                    Pair("覆盖ID", "测试闪光覆盖"),
                    Pair("触发类型", "课程初始化"),
                    Pair("触发值", string.Empty),
                    Pair("触发来源", string.Empty),
                    Pair("触发目标", string.Empty),
                    Pair("对象或状态", "试管"),
                    Pair("表现原语", primitive),
                    Pair("作用位置", "全局接收器"),
                    Pair("位置ID", string.Empty),
                    Pair("参数名", parameterName),
                    Pair("参数类型", parameterType),
                    Pair("参数值", parameterValue)),
                origin);
            var baseBlueprint = CourseBlueprintTestFactory.Blueprint(
                CourseBlueprintTestFactory.Object(
                    "试管", new[] { "可夹持" }));
            var blueprint = new CourseBlueprint(
                baseBlueprint.Course,
                baseBlueprint.Objects,
                baseBlueprint.InitialRelations,
                baseBlueprint.InteractionRules,
                baseBlueprint.DisciplineProcesses,
                baseBlueprint.TeachingEvaluations,
                new[] { presentation },
                baseBlueprint.AcceptanceRecords,
                baseBlueprint.AdvancedOverrides);
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());
            var expanded = new RecipeExpander().Expand(
                blueprint,
                catalog);
            Assert.That(expanded.IsSuccess, Is.True);
            return (blueprint, catalog, expanded);
        }

        private static KeyValuePair<string, string> Pair(
            string key,
            string value) => new KeyValuePair<string, string>(key, value);

        private static PresentationEffectCatalog TestFlashCatalog(
            bool withMode = false) =>
            new PresentationEffectCatalog(new[]
            {
                new PresentationEffectDescriptor(
                    "test.flash",
                    "测试闪光",
                    "test.channel",
                    PresentationEffectLifecycle.OneShot,
                    new[] { PresentationEffectLifecycle.OneShot },
                    new PresentationTargetContract(
                        new[] { PresentationLocationKind.GlobalReceiver },
                        Array.Empty<PresentationSlotKind>(),
                        Array.Empty<SemanticAnchorKind>(),
                        Array.Empty<PresentationContextEntityKind>()),
                    withMode
                        ? new[]
                        {
                            new PresentationParameterDescriptor(
                                "模式",
                                PresentationParameterKind.EnumText,
                                true,
                                null,
                                null,
                                null,
                                new[] { "快速", "缓慢" },
                                null)
                        }
                        : Array.Empty<PresentationParameterDescriptor>(),
                    () => new RecordingExecutor("test.flash"))
            });

        private sealed class RecordingExecutor : IPresentationEffectExecutor
        {
            public RecordingExecutor(string effectId)
            {
                EffectId = effectId;
            }

            public string EffectId { get; }

            public void Execute(
                PreparedPresentationEffect effect,
                PresentationExecutionContext context)
            {
            }

            public void Stop(
                PreparedPresentationEffect effect,
                PresentationExecutionContext context)
            {
            }
        }
    }
}
