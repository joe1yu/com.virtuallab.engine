using System;
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
                Is.EqualTo("表现覆盖.csv"));
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
            Assert.That(diagnostic.FileName, Is.EqualTo("表现覆盖.csv"));
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
            var source = new CourseBlueprintSource(new[]
            {
                new CourseBlueprintFile(
                    "课程.csv",
                    "课程ID,显示名称,学科配方包,实验Prefab\n"
                    + "目录集成,目录集成,,环境.prefab\n"),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                    + "试管,试管,可夹持,0|0|0,0|0|0\n"),
                new CourseBlueprintFile(
                    "表现覆盖.csv",
                    "覆盖ID,对象或状态,表现原语,作用位置,位置ID,参数名,参数类型,参数值\n"
                    + $"测试闪光覆盖,试管,{primitive},全局接收器,,"
                    + $"{parameterName},{parameterType},{parameterValue}\n")
            });
            var read = new CourseBlueprintReader().Read(source);
            Assert.That(
                read.Diagnostics.Select(value => value.Reason),
                Is.Empty);
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());
            var expanded = new RecipeExpander().Expand(
                read.Blueprint,
                catalog);
            Assert.That(expanded.IsSuccess, Is.True);
            return (read.Blueprint, catalog, expanded);
        }

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
