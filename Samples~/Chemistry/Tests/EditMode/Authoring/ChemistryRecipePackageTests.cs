using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VirtualLab.Chemistry.Authoring;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
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
            var blueprint = Blueprint(
                Object("量筒", "可倾倒", "参数.作用组.倾倒", "组.液体"),
                Object("烧杯", "容器", "参数.作用组.倾倒", "组.液体"),
                Object("锥形瓶", "可振荡"));
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new ChemistryRecipePackageProvider()
                });

            var result = new RecipeExpander().Expand(
                blueprint,
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
            var blueprint = Blueprint(
                new[]
                {
                    Object("试剂瓶", "可倾倒", "参数.作用组.倾倒", "组.液体"),
                    Object("烧杯", "容器", "参数.作用组.倾倒", "组.液体")
                },
                new[]
                {
                    Process("过程.倾倒", "过程参数", "化学.倾倒", "开始倾倒",
                        "试剂瓶", "烧杯", parameters: new[]
                        {
                            Pair("物质标识", "water"), Pair("计量单位", "毫升")
                        }),
                    Process("过程.附加转移", "过程操作", "化学.倾倒", "开始倾倒",
                        "试剂瓶", "烧杯", "转移溶质", "转移物质",
                        new[] { Pair("物质标识", "solute"), Pair("数量", "1") })
                });
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new ChemistryRecipePackageProvider()
                });

            var result = new RecipeExpander().Expand(
                blueprint,
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
                Does.Contain("过程编译测试"));
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
            var rows = new[]
            {
                Process("物质.课程水", "物质", "化学.运行配置", "water",
                    parameters: new[] { Pair("显示名称", "课程专用水"), Pair("物态", "液体"), Pair("摩尔质量", "99") }),
                Process("物质.反应物", "物质", "化学.运行配置", "source",
                    parameters: new[] { Pair("显示名称", "反应物"), Pair("物态", "固体"), Pair("摩尔质量", "1") }),
                Process("物质.产物", "物质", "化学.运行配置", "product",
                    parameters: new[] { Pair("显示名称", "产物"), Pair("物态", "固体"), Pair("摩尔质量", "1") }),
                Process("反应.测试", "反应", "化学.运行配置", "reaction.test",
                    parameters: new[] { Pair("过程类型", "热分解"), Pair("最低温度", "0"), Pair("每刻反应量", "1"), Pair("需要点燃", "否") }),
                Process("反应项.反应物", "反应项", "化学.运行配置", "reaction.test", "source", "反应物",
                    parameters: new[] { Pair("物态", "固体"), Pair("数量", "1"), Pair("单位", "克"), Pair("每单位克数", "1") }),
                Process("反应项.产物", "反应项", "化学.运行配置", "reaction.test", "product", "产物",
                    parameters: new[] { Pair("物态", "固体"), Pair("数量", "1"), Pair("单位", "克"), Pair("每单位克数", "1") }),
                Process("初始.反应物", "初始物质", "化学.运行配置", "反应容器", "source",
                    parameters: new[] { Pair("物态", "固体"), Pair("数量", "1"), Pair("单位", "克"), Pair("温度", "20") })
            };
            return Blueprint(
                new[] { Object("反应容器", "容器") },
                rows);
        }

        private static CourseBlueprint Blueprint(params CourseObjectBlueprint[] objects) =>
            Blueprint(objects, Array.Empty<CourseDisciplineProcessBlueprint>());

        private static CourseBlueprint Blueprint(
            CourseObjectBlueprint[] objects,
            CourseDisciplineProcessBlueprint[] processes) =>
            new CourseBlueprint(
                new CourseBlueprintCourse(
                    "化学测试", "化学测试", new[] { "化学基础" }, "学生",
                    "环境.prefab", Source("课程.csv", 2, "化学测试")),
                objects,
                Array.Empty<CourseInitialRelationBlueprint>(),
                Array.Empty<CourseInteractionRuleBlueprint>(),
                processes,
                Array.Empty<CourseTeachingEvaluationBlueprint>(),
                Array.Empty<CoursePresentationOverrideBlueprint>(),
                Array.Empty<CourseAcceptanceRecordBlueprint>(),
                Array.Empty<CourseAdvancedOverrideBlueprint>());

        private static CourseObjectBlueprint Object(
            string id,
            string feature,
            string parameter = null,
            string value = null)
        {
            var origin = Source("实验对象.csv", 2, id);
            var extensions = string.IsNullOrEmpty(parameter)
                ? Array.Empty<KeyValuePair<string, string>>()
                : new[] { Pair(parameter, value) };
            return new CourseObjectBlueprint(
                id, id, new[] { feature }, new BlueprintVector3(0, 0, 0),
                new BlueprintVector3(0, 0, 0), Values(origin, extensions), origin);
        }

        private static CourseDisciplineProcessBlueprint Process(
            string id,
            string type,
            string recipe,
            string subject,
            string source = "",
            string target = "",
            string operation = "",
            string protocol = "",
            KeyValuePair<string, string>[] parameters = null)
        {
            var origin = Source("过程编译测试", 2, id);
            return new CourseDisciplineProcessBlueprint(
                Values(origin,
                    Pair("定义ID", id), Pair("记录类型", type),
                    Pair("学科配方", recipe), Pair("主体", subject),
                    Pair("来源", source), Pair("目标", target),
                    Pair("启动交互", operation), Pair("停止交互", protocol)),
                origin,
                Values(
                    origin,
                    (parameters ?? Array.Empty<KeyValuePair<string, string>>())
                    .Select(pair => Pair("参数." + pair.Key, pair.Value))
                    .ToArray()));
        }

        private static IReadOnlyDictionary<string, BlueprintValue> Values(
            ConfigurationSource source,
            params KeyValuePair<string, string>[] pairs) =>
            new ReadOnlyDictionary<string, BlueprintValue>(pairs.ToDictionary(
                pair => pair.Key,
                pair => new BlueprintValue(pair.Key, pair.Value, source),
                StringComparer.Ordinal));

        private static ConfigurationSource Source(string file, int line, string id) =>
            new ConfigurationSource(
                ConfigurationLayer.Course, "化学测试", file, line, 1, id);

        private static KeyValuePair<string, string> Pair(string key, string value) =>
            new KeyValuePair<string, string>(key, value);
    }
}
