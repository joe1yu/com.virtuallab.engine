using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseBlueprintContentCompilationTests
    {
        [Test]
        public void 初始关系类型必须由已选择配方包声明()
        {
            var origin = Source("初始关系.csv", 2, "未知关系");
            var blueprint = Blueprint(
                relations: new[]
                {
                    new CourseInitialRelationBlueprint(
                        "未知关系", "未知模块.关系.覆盖", "瓶盖", "试管",
                        string.Empty, string.Empty, origin)
                });

            var result = new CourseBlueprintCompiler().Compile(
                blueprint,
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.initial-relation.type-unknown"));
        }

        [Test]
        public void 相同教学目标的多条条件按且合并()
        {
            var blueprint = Blueprint(evaluations: new[]
            {
                Evaluation("目标.收集氧气", "目标", "集气瓶一", 2),
                Evaluation("目标.收集氧气", "目标", "集气瓶二", 3)
            });

            var result = Expand(blueprint);

            Assert.That(result.IsSuccess, Is.True,
                string.Join("\n", result.Diagnostics.Select(value =>
                    value.Code + ": " + value.Reason)));
            var goal = result.Model.TeachingGoals.Single().Definition;
            Assert.That(goal.GoalId, Is.EqualTo("目标.收集氧气"));
            Assert.That(goal.Conditions, Has.Count.EqualTo(2));
        }

        [Test]
        public void 相同教学标识的元数据不一致时拒绝展开()
        {
            var blueprint = Blueprint(evaluations: new[]
            {
                Evaluation("目标.收集氧气", "目标", "集气瓶一", 2),
                Evaluation("目标.收集氧气", "风险", "集气瓶二", 3)
            });

            var result = Expand(blueprint);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.evaluation.metadata-conflict"));
            Assert.That(result.Model.Evaluations, Is.Empty);
        }

        [Test]
        public void 验收动作参数按场景和顺序合并为一个请求()
        {
            var blueprint = Blueprint(acceptance: new[]
            {
                Acceptance("正常制氧", 10, "动作", "抓取", "试管", parameter: "功率", value: "600", line: 2),
                Acceptance("正常制氧", 10, "动作", "抓取", "试管", parameter: "模式", value: "缓慢", line: 3),
                Acceptance("正常制氧", 20, "断言", line: 4)
            });

            var result = Expand(blueprint);

            Assert.That(result.IsSuccess, Is.True,
                string.Join("\n", result.Diagnostics.Select(value =>
                    value.Code + ": " + value.Reason)));
            var scenario = result.Model.AcceptanceScenarios.Single().Definition;
            Assert.That(scenario.Steps.Select(value => value.Order),
                Is.EqualTo(new[] { 10, 20 }));
            Assert.That(scenario.Steps[0].ActionRequest.Parameters.Keys,
                Is.EquivalentTo(new[] { "功率", "模式" }));
            Assert.That(scenario.Steps[0].ActionRequest.Parameters["功率"].Kind,
                Is.EqualTo(StructuredValueKind.Number));
        }

        [Test]
        public void 验收同一顺序混合操作和断言时拒绝展开()
        {
            var blueprint = Blueprint(acceptance: new[]
            {
                Acceptance("冲突场景", 1, "动作", "抓取", "试管", line: 2),
                Acceptance("冲突场景", 1, "断言", line: 3)
            });

            var result = Expand(blueprint);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.acceptance.record-kind-conflict"));
        }

        [Test]
        public void 表现覆盖只改变表现模型且校验插槽与参数类型()
        {
            var validBlueprint = Blueprint(presentations: new[]
            {
                Presentation("液面覆盖", "插槽.液体", "数值", "0.5")
            });
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(validBlueprint, catalog);
            var valid = new CourseOverrideApplier().Apply(
                validBlueprint, catalog, expanded.Model);
            var invalid = Apply(Blueprint(presentations: new[]
            {
                Presentation("非法液面", "插槽.燃烧", "文本", "一半")
            }));

            Assert.That(valid.IsSuccess, Is.True);
            Assert.That(
                valid.Model.StateChanges.Select(value => value.DefinitionId),
                Is.EqualTo(expanded.Model.StateChanges.Select(value =>
                    value.DefinitionId)));
            Assert.That(valid.Model.PresentationEffects.Single(value =>
                    value.Definition.EffectId == "液面覆盖")
                .Definition.ParameterValues["液面比例"].Number,
                Is.EqualTo(0.5d));
            Assert.That(invalid.IsSuccess, Is.False);
            Assert.That(invalid.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.presentation.slot-mismatch")
                    .And.Contain("blueprint.presentation.parameter-type-mismatch"));
        }

        [Test]
        public void 未注册表现触发类型返回编译诊断而不是中断工作台()
        {
            var origin = Source("表现.csv", 2, "未知触发");
            var presentation = new CoursePresentationOverrideBlueprint(
                Values(origin,
                    Pair("覆盖ID", "未知触发"), Pair("触发类型", "不存在的触发"),
                    Pair("触发值", string.Empty), Pair("触发来源", string.Empty),
                    Pair("触发目标", string.Empty), Pair("对象或状态", "试管"),
                    Pair("表现原语", "显示提示"), Pair("作用位置", "全局接收器"),
                    Pair("位置ID", string.Empty), Pair("参数名", "文案"),
                    Pair("参数类型", "文本"), Pair("参数值", "测试")),
                origin);

            var result = new CourseBlueprintCompiler().Compile(
                Blueprint(presentations: new[] { presentation }),
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.override.failed"));
        }

        private static RecipeExpansionResult Expand(CourseBlueprint blueprint) =>
            new RecipeExpander().Expand(blueprint, Catalog());

        private static CourseOverrideResult Apply(CourseBlueprint blueprint)
        {
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);
            return new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                expanded.Model);
        }

        private static RecipeCatalog Catalog() =>
            RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

        private static CourseBlueprint Blueprint(
            CourseInitialRelationBlueprint[] relations = null,
            CourseTeachingEvaluationBlueprint[] evaluations = null,
            CoursePresentationOverrideBlueprint[] presentations = null,
            CourseAcceptanceRecordBlueprint[] acceptance = null) =>
            new CourseBlueprint(
                CourseBlueprintTestFactory.Blueprint().Course,
                new[]
                {
                    CourseBlueprintTestFactory.Object("学生", Array.Empty<string>()),
                    CourseBlueprintTestFactory.Object("瓶盖", new[] { "可覆盖来源" }),
                    CourseBlueprintTestFactory.Object("试管", new[] { "可被覆盖", "可抓取" }),
                    CourseBlueprintTestFactory.Object("集气瓶一", Array.Empty<string>()),
                    CourseBlueprintTestFactory.Object("集气瓶二", Array.Empty<string>()),
                    CourseBlueprintTestFactory.Object("酒精灯", Array.Empty<string>())
                },
                relations ?? Array.Empty<CourseInitialRelationBlueprint>(),
                Array.Empty<CourseInteractionRuleBlueprint>(),
                Array.Empty<CourseDisciplineProcessBlueprint>(),
                evaluations ?? Array.Empty<CourseTeachingEvaluationBlueprint>(),
                presentations ?? Array.Empty<CoursePresentationOverrideBlueprint>(),
                acceptance ?? Array.Empty<CourseAcceptanceRecordBlueprint>(),
                Array.Empty<CourseAdvancedOverrideBlueprint>());

        private static CourseTeachingEvaluationBlueprint Evaluation(
            string id,
            string type,
            string subject,
            int line)
        {
            var origin = Source("教学条件.csv", line, id);
            return new CourseTeachingEvaluationBlueprint(Values(origin,
                Pair("评价ID", id), Pair("类型", type),
                Pair("显示名称", "收集氧气"), Pair("触发类型", "状态满足"),
                Pair("触发值", string.Empty), Pair("顺序", "10"),
                Pair("条件类型", string.Empty), Pair("主体", subject),
                Pair("字段", "来源内容体积"), Pair("比较", "大于"),
                Pair("值", "0"), Pair("单位", "毫升"),
                Pair("分值变化", "0"), Pair("提示文案", string.Empty)), origin);
        }

        private static CourseAcceptanceRecordBlueprint Acceptance(
            string scenario,
            int order,
            string kind,
            string action = "",
            string source = "",
            string target = "",
            string parameter = "",
            string value = "",
            int line = 2)
        {
            var origin = Source("验收场景.csv", line, scenario);
            return new CourseAcceptanceRecordBlueprint(Values(origin,
                Pair("场景ID", scenario), Pair("顺序", order.ToString()),
                Pair("记录类型", kind), Pair("动作", action),
                Pair("来源", source), Pair("目标", target),
                Pair("参数名", parameter), Pair("参数值", value),
                Pair("断言类型", "状态"), Pair("对象", "试管"),
                Pair("字段", "来源内容体积"), Pair("比较", "大于"),
                Pair("期望值", "0"), Pair("单位", "毫升")), origin);
        }

        private static CoursePresentationOverrideBlueprint Presentation(
            string id,
            string slot,
            string parameterType,
            string value)
        {
            var origin = Source("表现.csv", 2, id);
            return new CoursePresentationOverrideBlueprint(Values(origin,
                Pair("覆盖ID", id), Pair("触发类型", "课程初始化"),
                Pair("触发值", string.Empty), Pair("触发来源", string.Empty),
                Pair("触发目标", string.Empty), Pair("对象或状态", "试管"),
                Pair("表现原语", "更新液面"), Pair("作用位置", "表现插槽"),
                Pair("位置ID", slot), Pair("参数名", "液面比例"),
                Pair("参数类型", parameterType), Pair("参数值", value)), origin);
        }

        private static VirtualLab.Unity.Authoring.Diagnostics.ConfigurationSource Source(
            string file,
            int line,
            string id) => CourseBlueprintTestFactory.Source(file, line, id);

        private static IReadOnlyDictionary<string, BlueprintValue> Values(
            VirtualLab.Unity.Authoring.Diagnostics.ConfigurationSource source,
            params KeyValuePair<string, string>[] values) =>
            CourseBlueprintTestFactory.Values(source, values);

        private static KeyValuePair<string, string> Pair(string key, string value) =>
            new KeyValuePair<string, string>(key, value);
    }
}
