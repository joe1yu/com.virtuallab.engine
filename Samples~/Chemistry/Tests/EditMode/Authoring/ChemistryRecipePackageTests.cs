using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VirtualLab.Chemistry.Authoring;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Chemistry.Tests.Authoring
{
    public sealed class ChemistryRecipePackageTests
    {
        [Test]
        public void 化学共享配方只向配表人员暴露中文协议名称()
        {
            var root = Path.Combine(
                ChemistrySamplePaths.Root,
                "Editor",
                "Authoring",
                "Recipes",
                "化学基础");
            var actions = File.ReadAllText(Path.Combine(root, "操作.csv"));
            var stateChanges = File.ReadAllText(
                Path.Combine(root, "状态变化.csv"));
            var presentations = File.ReadAllText(
                Path.Combine(root, "表现.csv"));

            StringAssert.Contains("操作指令", actions);
            StringAssert.DoesNotContain("chemistry.", actions);
            StringAssert.Contains("状态变化方式", stateChanges);
            StringAssert.DoesNotContain("chemistry.", stateChanges);
            StringAssert.DoesNotContain("domain.", stateChanges);
            StringAssert.Contains("表现方式", presentations);
            StringAssert.DoesNotContain("transform.", presentations);
            StringAssert.DoesNotContain("renderer.", presentations);
            StringAssert.DoesNotContain("vfx.", presentations);
            StringAssert.DoesNotContain("liquid.", presentations);
        }

        [Test]
        public void 化学基础包以数据声明设备无关语义动作()
        {
            var provider = new ChemistryRecipePackageProvider();
            var package = provider.Load();
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[] { provider });

            Assert.That(catalog.IsValid, Is.True);
            Assert.That(package.PackageId, Is.EqualTo("化学基础"));
            Assert.That(
                package.Actions
                    .Select(value => value.SemanticCommandId)
                    .Distinct(),
                Is.SupersetOf(new[]
                {
                    "开始倾倒",
                    "结束倾倒",
                    "开始加热",
                    "结束加热",
                    "点燃",
                    "熄灭",
                    "振荡",
                    "收集气体",
                    "放置"
                }));
            Assert.That(
                package.Actions.All(value =>
                    value.Source != null
                    && value.Source.FileName == "操作.csv"),
                Is.True);
        }

        [Test]
        public void 学科过程记录编译为通用文本产物并可由化学Codec解码()
        {
            var blueprint = ReadChemistryBlueprint();
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new ChemistryRecipePackageProvider()
                });

            var result = new RecipeExpander().Expand(blueprint, catalog);

            Assert.That(
                result.IsSuccess,
                Is.True,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(value =>
                        $"{value.Code}: {value.Reason}")));
            var artifact = result.Model.GeneratedArtifacts.Single();
            Assert.That(artifact.Definition.ArtifactId,
                Is.EqualTo(ChemistryConfigurationKeys.Artifacts.RuntimeConfiguration));
            Assert.That(artifact.Definition.SuggestedFileName,
                Is.EqualTo("化学运行配置.json"));
            var configuration = new ChemistryConfigurationCodec()
                .DecodeCourseConfiguration(
                    Encoding.UTF8.GetString(artifact.Definition.Content),
                    new[] { "反应容器" });
            Assert.That(
                configuration.Substances.Select(value => value.Id),
                Is.SupersetOf(new[] { "water", "氧气" }));
            var courseWater = configuration.Substances.Single(value =>
                value.Id == "water");
            Assert.That(courseWater.DisplayName, Is.EqualTo("课程专用水"));
            Assert.That(courseWater.MolarMassValue, Is.EqualTo(99m));
            Assert.That(
                configuration.Reactions.Select(value => value.Id),
                Does.Contain("reaction.test"));
            Assert.That(configuration.InitialSubstances,
                Has.Count.EqualTo(1));
        }

        [Test]
        public void 倾倒和振荡按特征与作用组展开且不伪造端口()
        {
            var read = new CourseBlueprintReader().Read(
                new CourseBlueprintSource(new[]
                {
                    new CourseBlueprintFile(
                        "课程.csv",
                        "课程ID,显示名称,学科配方包,实验Prefab\n"
                        + "化学动作测试,化学动作测试,化学基础,环境.prefab\n"),
                    new CourseBlueprintFile(
                        "实验对象.csv",
                        "实体ID,显示名称,特征列表,初始位置,初始旋转,"
                        + "参数.作用组.倾倒\n"
                        + "量筒,量筒,可倾倒,0|0|0,0|0|0,组.液体\n"
                        + "烧杯,烧杯,容器,0|0|0,0|0|0,组.液体\n"
                        + "锥形瓶,锥形瓶,可振荡,0|0|0,0|0|0,\n")
                }));
            Assert.That(read.IsSuccess, Is.True);
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new ChemistryRecipePackageProvider()
                });

            var result = new RecipeExpander().Expand(
                read.Blueprint,
                catalog);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Model.Actions.Select(value =>
                    value.Definition.ActionId),
                Is.SupersetOf(new[]
                {
                    "开始倾倒",
                    "结束倾倒",
                    "振荡"
                }));
            Assert.That(result.Model.Ports, Is.Empty);
        }

        [Test]
        public void 紧凑过程配置按配方实体覆盖参数并追加操作()
        {
            var read = new CourseBlueprintReader().Read(
                new CourseBlueprintSource(new[]
                {
                    new CourseBlueprintFile(
                        "课程.csv",
                        "课程ID,显示名称,学科配方包,实验Prefab\n"
                        + "过程参数测试,过程参数测试,化学基础,环境.prefab\n"),
                    new CourseBlueprintFile(
                        "实验对象.csv",
                        "实体ID,显示名称,特征列表,初始位置,初始旋转,"
                        + "参数.作用组.倾倒\n"
                        + "试剂瓶,试剂瓶,可倾倒,0|0|0,0|0|0,组.液体\n"
                        + "烧杯,烧杯,容器,0|0|0,0|0|0,组.液体\n"),
                    new CourseBlueprintFile(
                        "学科过程.csv",
                        "配置ID,类型,配方,主体,来源,目标,操作名称,协议,参数\n"
                        + "过程.倾倒,过程,化学.倾倒,开始倾倒,试剂瓶,烧杯,,,"
                        + "物质标识=water;计量单位=毫升\n"
                        + "过程.附加转移,附加操作,化学.倾倒,开始倾倒,试剂瓶,烧杯,"
                        + "转移溶质,转移物质,物质标识=solute;数量=1\n")
                }));
            Assert.That(read.IsSuccess, Is.True);
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new ChemistryRecipePackageProvider()
                });

            var result = new RecipeExpander().Expand(
                read.Blueprint,
                catalog);

            Assert.That(
                result.IsSuccess,
                Is.True,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(value =>
                        $"{value.Code}: {value.Reason}")));
            var mutation = result.Model.StateChanges.Single(value =>
                value.Identity.RecipeId == "化学.倾倒"
                && value.Identity.SourceEntityId == "试剂瓶"
                && value.Identity.TargetEntityId == "烧杯"
                && value.Identity.LocalKey == "开始倾倒");
            Assert.That(
                mutation.Definition.Parameters["物质标识"],
                Is.EqualTo("water"));
            Assert.That(
                mutation.Definition.Parameters["计量单位"],
                Is.EqualTo("毫升"));
            Assert.That(
                mutation.Provenance.Sources.Select(value => value.FileName),
                Does.Contain("学科过程.csv"));
            var addedMutation = result.Model.StateChanges.Single(value =>
                value.Identity.RecipeId == "化学.倾倒"
                && value.Identity.SourceEntityId == "试剂瓶"
                && value.Identity.TargetEntityId == "烧杯"
                && value.Identity.LocalKey == "转移溶质");
            Assert.That(
                addedMutation.Definition.OperationId,
                Is.EqualTo("转移物质"));
            Assert.That(
                result.Model.ActionResultGroups.Single(value =>
                        value.Identity.RecipeId == "化学.倾倒"
                        && value.Identity.SourceEntityId == "试剂瓶"
                        && value.Identity.TargetEntityId == "烧杯"
                        && value.Identity.LocalKey == "开始倾倒")
                    .Definition.MutationIds,
                Does.Contain(addedMutation.Definition.MutationId));
        }

        private static CourseBlueprint ReadChemistryBlueprint()
        {
            var header =
                "配置ID,类型,配方,主体,来源,目标,操作名称,协议,参数\n";
            var rows = string.Join("\n", new[]
            {
                "物质.课程水,物质,化学.运行配置,water,,,,,"
                + "显示名称=课程专用水;物态=液体;摩尔质量=99",
                "物质.反应物,物质,化学.运行配置,source,,,,,"
                + "显示名称=反应物;物态=固体;摩尔质量=1",
                "物质.产物,物质,化学.运行配置,product,,,,,"
                + "显示名称=产物;物态=固体;摩尔质量=1",
                "反应.测试,反应,化学.运行配置,reaction.test,,,,,"
                + "过程类型=热分解;最低温度=0;每刻反应量=1;需要点燃=否",
                "反应项.反应物,反应物,化学.运行配置,reaction.test,source,,,,"
                + "物态=固体;数量=1;单位=克;每单位克数=1",
                "反应项.产物,产物,化学.运行配置,reaction.test,product,,,,"
                + "物态=固体;数量=1;单位=克;每单位克数=1",
                "初始.反应物,初始物质,化学.运行配置,反应容器,source,,,,"
                + "物态=固体;数量=1;单位=克;温度=20"
            }) + "\n";
            var read = new CourseBlueprintReader().Read(
                new CourseBlueprintSource(new[]
                {
                    new CourseBlueprintFile(
                        "课程.csv",
                        "课程ID,显示名称,学科配方包,实验Prefab\n"
                        + "化学测试,化学测试,化学基础,环境.prefab\n"),
                    new CourseBlueprintFile(
                        "实验对象.csv",
                        "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                        + "反应容器,反应容器,容器,0|0|0,0|0|0\n"),
                    new CourseBlueprintFile("学科过程.csv", header + rows)
                }));
            Assert.That(read.IsSuccess, Is.True);
            return read.Blueprint;
        }
    }
}
