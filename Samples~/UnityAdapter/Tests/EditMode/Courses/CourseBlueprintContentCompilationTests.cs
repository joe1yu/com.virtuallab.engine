using System;
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
            var source = new CourseBlueprintSource(new[]
            {
                new CourseBlueprintFile(
                    "课程.csv",
                    "课程ID,显示名称,学科配方包,实验Prefab\n"
                    + "关系测试,关系测试,,环境.prefab\n"),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                    + "瓶盖,瓶盖,可覆盖,0|1|0,0|0|0\n"
                    + "试管,试管,可覆盖,0|0|0,0|0|0\n"),
                new CourseBlueprintFile(
                    "初始关系.csv",
                    "关系ID,关系类型,来源实体,目标实体\n"
                    + "初始关系.未知,未知模块.关系.覆盖,瓶盖,试管\n")
            });

            var result = new CourseBlueprintCompiler().Compile(
                source,
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.initial-relation.type-unknown"));
        }

        [Test]
        public void 相同评价的多行条件按且合并并保留独立评价元数据()
        {
            var blueprint = Read(
                EvaluationTable(
                    "收集氧气,目标,收集氧气,状态变化,oxygen.changed,1,物质数量,集气瓶,oxygen,大于等于,32,克,20,完成收集\n"
                    + "收集氧气,目标,收集氧气,状态变化,oxygen.changed,1,关系存在,集气瓶,位于,等于,水槽,,20,完成收集\n"
                    + "防止倒吸,风险,防止倒吸,动作拒绝,停止加热,2,状态,大试管,温度,大于,80,摄氏度,-5,可能发生倒吸\n"));

            var result = Expand(blueprint);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Model.Evaluations.Count, Is.EqualTo(2));
            var goal = result.Model.Evaluations.Single(value =>
                value.Definition.EvaluationId == "收集氧气").Definition;
            Assert.That(goal.EvaluationType, Is.EqualTo("目标"));
            Assert.That(goal.DisplayName, Is.EqualTo("收集氧气"));
            Assert.That(goal.TriggerType, Is.EqualTo("状态变化"));
            Assert.That(goal.TriggerValue, Is.EqualTo("oxygen.changed"));
            Assert.That(goal.Order, Is.EqualTo(1));
            Assert.That(goal.ScoreDelta, Is.EqualTo(20));
            Assert.That(goal.PromptMessage, Is.EqualTo("完成收集"));
            Assert.That(goal.Conditions.Count, Is.EqualTo(2));
            Assert.That(goal.ConditionRuleIds.Count, Is.EqualTo(2));

            var risk = result.Model.Evaluations.Single(value =>
                value.Definition.EvaluationId == "防止倒吸").Definition;
            Assert.That(risk.EvaluationType, Is.EqualTo("风险"));
            Assert.That(risk.ScoreDelta, Is.EqualTo(-5));
            Assert.That(risk.PromptMessage, Is.EqualTo("可能发生倒吸"));
            Assert.That(
                result.Model.TeachingGoals.Single().Definition.GoalId,
                Is.EqualTo("收集氧气"));
            Assert.That(
                result.Model.TeachingRisks.Single().Definition.RiskId,
                Is.EqualTo("防止倒吸"));
            Assert.That(
                result.Model.TeachingScores.Select(value =>
                    value.Definition.EvaluationId),
                Is.EquivalentTo(new[] { "收集氧气", "防止倒吸" }));
            Assert.That(
                result.Model.TeachingHints.Select(value =>
                    value.Definition.EvaluationId),
                Is.EquivalentTo(new[] { "收集氧气", "防止倒吸" }));
        }

        [Test]
        public void 相同评价元数据不一致会失败且不采用最后一行()
        {
            var blueprint = Read(
                EvaluationTable(
                    "收集氧气,目标,收集氧气,状态变化,oxygen.changed,1,物质数量,集气瓶,oxygen,大于等于,16,克,20,完成收集\n"
                    + "收集氧气,风险,另一个名称,课程结算,,2,物质数量,集气瓶,oxygen,大于等于,32,克,-5,冲突提示\n"));

            var result = Expand(blueprint);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.evaluation.metadata-conflict"));
            Assert.That(result.Model.Evaluations, Is.Empty);
        }

        [Test]
        public void 验收动作参数按场景和顺序合并为一个语义请求()
        {
            var blueprint = Read(
                AcceptanceTable(
                    "正常制氧,10,动作,开始加热,酒精灯,大试管,功率,600,,,,,,\n"
                    + "正常制氧,10,动作,开始加热,酒精灯,大试管,模式,缓慢,,,,,,\n"
                    + "正常制氧,20,断言,,,,,,物质数量,集气瓶,oxygen,大于等于,32,克\n"));

            var result = Expand(blueprint);

            Assert.That(result.IsSuccess, Is.True);
            var scenario = result.Model.AcceptanceScenarios.Single().Definition;
            Assert.That(scenario.Steps.Select(value => value.Order),
                Is.EqualTo(new[] { 10, 20 }));
            var action = scenario.Steps[0].ActionRequest;
            Assert.That(
                action.ActionId,
                Is.EqualTo("开始加热"));
            Assert.That(action.SourceEntityId, Is.EqualTo("酒精灯"));
            Assert.That(action.TargetEntityId, Is.EqualTo("大试管"));
            Assert.That(action.Parameters.Keys,
                Is.EquivalentTo(new[] { "功率", "模式" }));
            Assert.That(
                action.Parameters["功率"].Kind,
                Is.EqualTo(StructuredValueKind.Number));
            Assert.That(
                scenario.Steps[1].Assertions.Count,
                Is.EqualTo(1));
        }

        [Test]
        public void 验收同一顺序混合动作和断言会失败()
        {
            var blueprint = Read(
                AcceptanceTable(
                    "冲突场景,1,动作,抓取,试管,,,,,,,,,\n"
                    + "冲突场景,1,断言,,,,,,状态,试管,温度,小于,50,摄氏度\n"));

            var result = Expand(blueprint);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.acceptance.record-kind-conflict"));
            Assert.That(result.Model.AcceptanceScenarios, Is.Empty);
        }

        [Test]
        public void 表现覆盖通过统一目录编译且不增加科学状态变化()
        {
            var blueprint = Read(
                PresentationTable(
                    "液面覆盖,试管,更新液面,表现插槽,插槽.液体,液面比例,数值,0.5\n"));
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);
            var mutationIds = expanded.Model.StateChanges
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
                Is.EqualTo(mutationIds));
            var effect = result.Model.PresentationEffects.Single(value =>
                value.Definition.EffectId == "液面覆盖").Definition;
            Assert.That(effect.ProtocolId, Is.EqualTo("liquid.set-level"));
            Assert.That(
                effect.LocationKind,
                Is.EqualTo(CoursePresentationLocationKind.PresentationSlot));
            Assert.That(effect.LocationId, Is.EqualTo("插槽.液体"));
            Assert.That(effect.ParameterValues["液面比例"].Number,
                Is.EqualTo(0.5d));
        }

        [Test]
        public void 表现覆盖的非法插槽与参数在编译期返回诊断()
        {
            var blueprint = Read(
                PresentationTable(
                    "非法液面,试管,更新液面,表现插槽,插槽.燃烧,液面比例,文本,一半\n"));
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);

            var result = new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                expanded.Model);
            var codes = result.Diagnostics.Select(value => value.Code);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(codes,
                Does.Contain("blueprint.presentation.slot-mismatch"));
            Assert.That(codes,
                Does.Contain("blueprint.presentation.parameter-type-mismatch"));
        }

        [Test]
        public void 评价表现和验收在输入行倒序后仍保持确定顺序()
        {
            var normal = Read(
                EvaluationTable(
                    "乙评价,目标,乙,课程结算,,20,状态,试管,温度,大于,20,摄氏度,0,\n"
                    + "甲评价,目标,甲,课程结算,,10,状态,试管,温度,大于,10,摄氏度,0,\n"),
                PresentationTable(
                    "乙覆盖,试管,显示提示,全局接收器,,文案,文本,乙\n"
                    + "甲覆盖,试管,显示提示,全局接收器,,文案,文本,甲\n"),
                AcceptanceTable(
                    "乙场景,20,动作,释放,试管,,,,,,,,,\n"
                    + "甲场景,10,动作,抓取,试管,,,,,,,,,\n"));
            var reversed = Read(
                EvaluationTable(
                    "甲评价,目标,甲,课程结算,,10,状态,试管,温度,大于,10,摄氏度,0,\n"
                    + "乙评价,目标,乙,课程结算,,20,状态,试管,温度,大于,20,摄氏度,0,\n"),
                PresentationTable(
                    "甲覆盖,试管,显示提示,全局接收器,,文案,文本,甲\n"
                    + "乙覆盖,试管,显示提示,全局接收器,,文案,文本,乙\n"),
                AcceptanceTable(
                    "甲场景,10,动作,抓取,试管,,,,,,,,,\n"
                    + "乙场景,20,动作,释放,试管,,,,,,,,,\n"));

            var normalResult = Apply(normal);
            var reversedResult = Apply(reversed);

            Assert.That(normalResult.IsSuccess, Is.True);
            Assert.That(reversedResult.IsSuccess, Is.True);
            Assert.That(
                normalResult.Model.Evaluations.Select(value =>
                    value.DefinitionId),
                Is.EqualTo(reversedResult.Model.Evaluations.Select(value =>
                    value.DefinitionId)));
            Assert.That(
                normalResult.Model.PresentationEffects.Select(value =>
                    value.DefinitionId),
                Is.EqualTo(reversedResult.Model.PresentationEffects.Select(
                    value => value.DefinitionId)));
            Assert.That(
                normalResult.Model.AcceptanceScenarios.Select(value =>
                    value.DefinitionId),
                Is.EqualTo(reversedResult.Model.AcceptanceScenarios.Select(
                    value => value.DefinitionId)));
        }

        private static RecipeExpansionResult Expand(CourseBlueprint blueprint) =>
            new RecipeExpander().Expand(blueprint, Catalog());

        private static CourseOverrideResult Apply(CourseBlueprint blueprint)
        {
            var catalog = Catalog();
            var expanded = new RecipeExpander().Expand(blueprint, catalog);
            Assert.That(expanded.IsSuccess, Is.True);
            return new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                expanded.Model);
        }

        private static RecipeCatalog Catalog() =>
            RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

        private static CourseBlueprint Read(
            params CourseBlueprintFile[] optionalFiles)
        {
            var files = new[]
            {
                new CourseBlueprintFile(
                    "课程.csv",
                    "课程ID,显示名称,学科配方包,实验Prefab\n"
                    + "内容编译测试,内容编译测试,,环境.prefab\n"),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                    + "试管,试管,可夹持,0|0|0,0|0|0\n"
                    + "酒精灯,酒精灯,可夹持,0|0|0,0|0|0\n"
                    + "大试管,大试管,可夹持,0|0|0,0|0|0\n"
                    + "集气瓶,集气瓶,可夹持,0|0|0,0|0|0\n"
                    + "水槽,水槽,可夹持,0|0|0,0|0|0\n")
            }.Concat(optionalFiles);
            var result = new CourseBlueprintReader().Read(
                new CourseBlueprintSource(files));
            Assert.That(result.IsSuccess, Is.True);
            return result.Blueprint;
        }

        private static CourseBlueprintFile EvaluationTable(string rows) =>
            new CourseBlueprintFile(
                "教学评价.csv",
                "评价ID,类型,显示名称,触发类型,触发值,顺序,条件类型,主体,"
                + "字段,比较,值,单位,分值变化,提示文案\n"
                + rows);

        private static CourseBlueprintFile PresentationTable(string rows) =>
            new CourseBlueprintFile(
                "表现覆盖.csv",
                "覆盖ID,对象或状态,表现原语,作用位置,位置ID,参数名,参数类型,参数值\n"
                + rows);

        private static CourseBlueprintFile AcceptanceTable(string rows) =>
            new CourseBlueprintFile(
                "验收场景.csv",
                "场景ID,顺序,记录类型,动作,来源,目标,参数名,参数值,"
                + "断言类型,对象,字段,比较,期望值,单位\n"
                + rows);
    }
}
