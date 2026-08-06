using System;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Interaction.Actions;
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
                Interaction(
                    "固定铁架台", "禁用默认", "抓取", "铁架台",
                    order: "10", rejection: "铁架台已固定，不能抓取"));
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);

            var result = new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                expanded.Model);

            Assert.That(result.IsSuccess, Is.True);
            var candidates = result.Model.Actions
                .Where(value =>
                    value.Definition.ActionId == InteractionSemanticActionIds.Grab
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
                Interaction(
                    "限制高温抓取", "收紧默认", "抓取", "大试管",
                    order: "1", requirementType: "状态",
                    requirementSubject: "大试管", field: "温度",
                    comparison: "小于", expected: "50",
                    rejection: "温度过高"));
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);
            var before = expanded.Model.Actions.Single(value =>
                value.Definition.ActionId == InteractionSemanticActionIds.Grab);

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
                Advanced("覆盖甲", "动作策略", "删除", string.Empty, string.Empty),
                Advanced("覆盖乙", "表现效果", "替换表现", "表现方式", "跟随实体"),
                Advanced("覆盖丙", "表现效果", "替换表现", "表现方式", "停止跟随"));
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
                            source.FileName == "内存覆盖测试")),
                Is.True);
        }

        [Test]
        public void 替换表现方式不改变任何科学状态变化()
        {
            var blueprint = ReadConnectionBlueprint(
                Advanced(
                    "替换吸附表现", "表现.端口吸附", "替换表现",
                    "表现方式", "从锚点分离"));
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
            CourseInteractionRuleBlueprint interaction) =>
            Blueprint(
                new[]
                {
                    CourseBlueprintTestFactory.Object(
                        entityId,
                        features.Split('|'))
                },
                interactions: new[] { interaction });

        private static CourseBlueprint ReadConnectionBlueprint(
            params CourseAdvancedOverrideBlueprint[] advanced) =>
            Blueprint(
                new[]
                {
                    CourseBlueprintTestFactory.Object(
                        "导气管",
                        new[] { "可连接" },
                        Pair("参数.端口.出口.兼容组", "组.导气")),
                    CourseBlueprintTestFactory.Object(
                        "集气瓶",
                        new[] { "可连接" },
                        Pair("参数.端口.入口.兼容组", "组.导气"))
                },
                advanced: advanced);

        private static CourseBlueprint Blueprint(
            CourseObjectBlueprint[] objects,
            CourseInteractionRuleBlueprint[] interactions = null,
            CourseAdvancedOverrideBlueprint[] advanced = null) =>
            new CourseBlueprint(
                CourseBlueprintTestFactory.Blueprint().Course,
                objects,
                Array.Empty<CourseInitialRelationBlueprint>(),
                interactions ?? Array.Empty<CourseInteractionRuleBlueprint>(),
                Array.Empty<CourseDisciplineProcessBlueprint>(),
                Array.Empty<CourseTeachingEvaluationBlueprint>(),
                Array.Empty<CoursePresentationOverrideBlueprint>(),
                Array.Empty<CourseAcceptanceRecordBlueprint>(),
                advanced ?? Array.Empty<CourseAdvancedOverrideBlueprint>());

        private static CourseInteractionRuleBlueprint Interaction(
            string id,
            string handling,
            string action,
            string source,
            string order,
            string requirementType = "",
            string requirementSubject = "",
            string field = "",
            string comparison = "",
            string expected = "",
            string rejection = "")
        {
            var origin = CourseBlueprintTestFactory.Source("操作特例.csv", 2, id);
            return new CourseInteractionRuleBlueprint(
                CourseBlueprintTestFactory.Values(origin,
                    Pair("交互ID", id), Pair("处理方式", handling),
                    Pair("动作", action), Pair("来源", source),
                    Pair("目标", string.Empty), Pair("顺序", order),
                    Pair("要求类型", requirementType),
                    Pair("要求主体", requirementSubject), Pair("字段", field),
                    Pair("比较", comparison), Pair("值", expected),
                    Pair("单位", string.Empty), Pair("结果配方", string.Empty),
                    Pair("反馈配方", string.Empty), Pair("拒绝文案", rejection)),
                origin);
        }

        private static CourseAdvancedOverrideBlueprint Advanced(
            string id,
            string generatedItem,
            string operation,
            string field,
            string value)
        {
            var origin = CourseBlueprintTestFactory.Source("内存覆盖测试", 2, id);
            return new CourseAdvancedOverrideBlueprint(
                CourseBlueprintTestFactory.Values(origin,
                    Pair("覆盖ID", id), Pair("配方ID", "通用.连接"),
                    Pair("来源实体", "导气管"), Pair("目标实体", "集气瓶"),
                    Pair("生成项", generatedItem), Pair("操作", operation),
                    Pair("字段", field), Pair("值", value)),
                origin);
        }

        private static System.Collections.Generic.KeyValuePair<string, string>
            Pair(string key, string value) =>
            new System.Collections.Generic.KeyValuePair<string, string>(key, value);
    }
}
