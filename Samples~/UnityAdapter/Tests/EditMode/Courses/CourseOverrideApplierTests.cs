using System;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseOverrideApplierTests
    {
        [Test]
        public void 禁用默认保留允许候选并生成更高优先级拒绝策略()
        {
            var blueprint = ReadSingleEntityBlueprint(
                "铁架台",
                "可抓取",
                interactionRows:
                    "固定铁架台,禁用默认,抓取,铁架台,,10,,,,,,,,,铁架台已固定，不能抓取\n");
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);

            var result = new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                expanded.Model);

            Assert.That(result.IsSuccess, Is.True);
            var candidates = result.Model.Actions
                .Where(value =>
                    value.Definition.ActionId == CoreSemanticActionIds.Grab
                    && value.Definition.SourceEntityId == "铁架台")
                .ToArray();
            Assert.That(candidates, Has.Length.EqualTo(2));
            var allowed = candidates.Single(value =>
                value.Definition.PolicyEffect == "允许");
            var denied = candidates.Single(value =>
                value.Definition.PolicyEffect == "禁用");
            Assert.That(denied.Definition.Priority,
                Is.GreaterThan(allowed.Definition.Priority));
            Assert.That(denied.Definition.RejectionMessage,
                Is.EqualTo("铁架台已固定，不能抓取"));
            Assert.That(
                denied.Provenance.Sources.Select(value => value.Layer).Distinct(),
                Is.EquivalentTo(new[]
                {
                    ConfigurationLayer.Platform,
                    ConfigurationLayer.Course
                }));
        }

        [Test]
        public void 收紧默认只追加课程规则并保留原规则和结果()
        {
            var blueprint = ReadSingleEntityBlueprint(
                "大试管",
                "可抓取",
                interactionRows:
                    "限制高温抓取,收紧默认,抓取,大试管,,1,状态,大试管,温度,小于,50,,,,温度过高\n");
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);
            var before = expanded.Model.Actions.Single(value =>
                value.Definition.ActionId == CoreSemanticActionIds.Grab);

            var result = new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                expanded.Model);

            Assert.That(result.IsSuccess, Is.True);
            var after = result.Model.Actions.Single(value =>
                value.Definition.PolicyId == before.Definition.PolicyId);
            Assert.That(after.Definition.RuleIds,
                Is.SupersetOf(before.Definition.RuleIds));
            Assert.That(after.Definition.RuleIds.Count,
                Is.EqualTo(before.Definition.RuleIds.Count + 1));
            Assert.That(after.Definition.ResultGroupId,
                Is.EqualTo(before.Definition.ResultGroupId));
            Assert.That(
                result.Model.Rules.Any(value =>
                    value.Definition.RuleId.StartsWith(
                        "规则.课程限制.限制高温抓取",
                        StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void 不可弱化配方不能删除且冲突覆盖不会最后一行获胜()
        {
            var blueprint = ReadConnectionBlueprint(
                "覆盖甲,通用.连接,导气管,集气瓶,动作策略,删除,,\n"
                + "覆盖乙,通用.连接,导气管,集气瓶,表现效果,替换表现,表现方式,跟随实体\n"
                + "覆盖丙,通用.连接,导气管,集气瓶,表现效果,替换表现,表现方式,停止跟随\n");
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);

            var result = new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                expanded.Model);
            var codes = result.Diagnostics.Select(value => value.Code);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(codes,
                Does.Contain("override.safety.non-weakenable"));
            Assert.That(codes,
                Does.Contain("override.field.conflict"));
            Assert.That(
                result.Diagnostics
                    .Where(value =>
                        value.Code == "override.safety.non-weakenable")
                    .All(value =>
                        value.Provenance != null
                        && value.Provenance.Sources.Any(source =>
                            source.FileName == "高级覆盖.csv")),
                Is.True);
        }

        [Test]
        public void 替换表现方式不改变任何科学状态变化()
        {
            var blueprint = ReadConnectionBlueprint(
                "替换吸附表现,通用.连接,导气管,集气瓶,表现.端口吸附,替换表现,表现方式,从锚点分离\n");
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);
            var mutations = expanded.Model.StateChanges
                .Select(value => value.Definition.DefinitionId)
                .ToArray();

            var result = new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                expanded.Model);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Model.StateChanges.Select(value =>
                    value.Definition.DefinitionId),
                Is.EqualTo(mutations));
            Assert.That(
                result.Model.PresentationEffects.Any(value =>
                    value.Identity.LocalKey == "表现.端口吸附"
                    && value.Definition.ProtocolId ==
                    "transform.detach"),
                Is.True);
        }

        private static RecipeCatalog Catalog() =>
            RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

        private static CourseBlueprint ReadSingleEntityBlueprint(
            string entityId,
            string features,
            string interactionRows)
        {
            return Read(new[]
            {
                CourseFile(),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                    + $"{entityId},{entityId},{features},0|0|0,0|0|0\n"),
                new CourseBlueprintFile(
                    "交互规则.csv",
                    InteractionHeader() + interactionRows)
            });
        }

        private static CourseBlueprint ReadConnectionBlueprint(
            string advancedRows)
        {
            return Read(new[]
            {
                CourseFile(),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转,"
                    + "参数.端口.出口.兼容组,参数.端口.入口.兼容组\n"
                    + "导气管,导气管,可连接,0|0|0,0|0|0,组.导气,\n"
                    + "集气瓶,集气瓶,可连接,0|0|0,0|0|0,,组.导气\n"),
                new CourseBlueprintFile(
                    "高级覆盖.csv",
                    "覆盖ID,配方ID,来源实体,目标实体,生成项,操作,字段,值\n"
                    + advancedRows)
            });
        }

        private static CourseBlueprintFile CourseFile() =>
            new CourseBlueprintFile(
                "课程.csv",
                "课程ID,显示名称,学科配方包,实验Prefab\n"
                + "覆盖测试,覆盖测试,,环境.prefab\n");

        private static string InteractionHeader() =>
            "交互ID,处理方式,动作,来源,目标,顺序,要求类型,要求主体,"
            + "字段,比较,值,单位,结果配方,反馈配方,拒绝文案\n";

        private static CourseBlueprint Read(CourseBlueprintFile[] files)
        {
            var result = new CourseBlueprintReader().Read(
                new CourseBlueprintSource(files));
            Assert.That(result.IsSuccess, Is.True);
            return result.Blueprint;
        }
    }
}
