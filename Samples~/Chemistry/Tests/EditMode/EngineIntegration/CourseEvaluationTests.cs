using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Courses;
using VirtualLab.Application.Events;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseEvaluationTests
    {
        [Test]
        public void 已执行动作产生风险事件时记录评价而不是伪装成动作拒绝()
        {
            var evaluator = new CourseAssessmentEvaluator(
                new StructuredRuleEvaluator(
                    CoreCourseRegistrations.CreateFactReaders()));
            var request = new SemanticActionRequest(
                "命令.不规范加热",
                "化学.开始加热",
                "学生",
                "大试管",
                "酒精灯",
                Array.Empty<KeyValuePair<string, StructuredValue>>());
            var eventType = "实验风险.装置漏气";
            var actionResult = CommandResult.Accepted(new[]
            {
                new DomainEventEnvelope(
                    1,
                    request.CommandId,
                    new SimulationTick(0),
                    new ConfiguredCourseDomainEvent(
                        eventType,
                        new Dictionary<string, StructuredValue>()))
            });

            var result = evaluator.EvaluateAction(
                World(),
                request,
                actionResult,
                new[]
                {
                    CourseActionAssessmentDefinition.ForDomainEvent(
                        "评价.装置漏气",
                        eventType,
                        "风险.装置漏气",
                        -10,
                        "加热已开始，但装置漏气会降低氧气纯度。")
                },
                100);

            Assert.That(actionResult.IsAccepted, Is.True);
            Assert.That(result.Score, Is.EqualTo(90));
            Assert.That(result.RiskIds, Is.EqualTo(new[] { "风险.装置漏气" }));
            Assert.That(result.Evidence.Single().CommandId,
                Is.EqualTo(request.CommandId));
        }

        [Test]
        public void 合法动作顺序不同仍由最终权威状态完成目标()
        {
            var first = EvaluateAfterGrabOrder("器材.甲", "器材.乙");
            var second = EvaluateAfterGrabOrder("器材.乙", "器材.甲");

            Assert.That(first.CompletedGoalIds, Is.EqualTo(new[] { "目标.全部拿起" }));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void 安全评价记录证据风险和扣分但不修改科学事实()
        {
            var world = World();
            world.SetScalar(
                "器材.甲.温度",
                90d,
                new WorldScalarUnit("摄氏度"),
                null,
                null);
            var action = ConfiguredActionDefinition.CreateGeneric(
                CoreSemanticActionIds.Grab,
                new[]
                {
                    Rule(
                        10,
                        ChemistryStructuredFactFields.来源对象温度,
                        StructuredRuleOperator.小于等于,
                        StructuredValue.FromNumber(60d),
                        "器材温度过高")
                },
                Array.Empty<ConfiguredMutationDefinition>());
            var session = ChemistryCourseRegistrations.CreateSession(
                world,
                new[] { action });
            var request = new SemanticActionRequest(
                "命令.徒手抓高温器材",
                CoreSemanticActionIds.Grab,
                "学生",
                "器材.甲",
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>());
            var actionResult = session.Execute(request);
            var evaluator = new CourseAssessmentEvaluator(
                new StructuredRuleEvaluator(
                    CoreCourseRegistrations.CreateFactReaders()
                        .Concat(ChemistryCourseRegistrations.CreateFactReaders())));
            var before = world.TryGetScalar("器材.甲.温度", out var temperature)
                ? temperature.Value
                : 0d;

            var rejectedEvaluation = evaluator.EvaluateAction(
                request,
                actionResult,
                new[]
                {
                    new CourseActionAssessmentDefinition(
                        "评价.徒手接触高温器材",
                        CoreSemanticActionIds.Grab,
                        "器材温度过高",
                        "风险.烫伤",
                        -10,
                        "请使用坩埚钳。")
                },
                maximumScore: 100);
            Assert.That(
                world.TryGetScalar("器材.甲.温度", out temperature),
                Is.True);
            Assert.That(temperature.Value, Is.EqualTo(before));
            world.SetScalar(
                "器材.甲.温度",
                20,
                new WorldScalarUnit("摄氏度"),
                null,
                null);
            var safeRequest = new SemanticActionRequest(
                "命令.抓取常温器材",
                CoreSemanticActionIds.Grab,
                "学生",
                "器材.甲",
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>());
            var safeActionResult = session.Execute(safeRequest);
            var result = evaluator.EvaluateAction(
                safeRequest,
                safeActionResult,
                new[]
                {
                    new CourseActionAssessmentDefinition(
                        "评价.徒手接触高温器材",
                        CoreSemanticActionIds.Grab,
                        "器材温度过高",
                        "风险.烫伤",
                        -10,
                        "请使用坩埚钳。")
                },
                maximumScore: 100,
                previous: rejectedEvaluation);
            session.RecordEvaluation(
                new CourseGoalEvaluationResult(Array.Empty<string>()),
                result,
                Array.Empty<string>());

            Assert.That(result.Score, Is.EqualTo(90));
            Assert.That(actionResult.IsAccepted, Is.False);
            Assert.That(safeActionResult.IsAccepted, Is.True);
            Assert.That(result.RiskIds, Is.EqualTo(new[] { "风险.烫伤" }));
            Assert.That(result.Evidence.Count, Is.EqualTo(1));
            Assert.That(
                session.ExportState().Assessment,
                Is.EqualTo(result));
            Assert.That(world.TryGetScalar("器材.甲.温度", out temperature), Is.True);
            Assert.That(temperature.Value, Is.EqualTo(20d));
        }

        [Test]
        public void 动作拒绝风险只有在结构化评价条件满足时才记录()
        {
            var world = World();
            world.SetScalar(
                "器材.甲.温度",
                50,
                new WorldScalarUnit("摄氏度"),
                null,
                null);
            var evaluator = new CourseAssessmentEvaluator(
                new StructuredRuleEvaluator(
                    CoreCourseRegistrations.CreateFactReaders()
                        .Concat(ChemistryCourseRegistrations
                            .CreateFactReaders())));
            var condition = new CourseConditionDefinition(
                "条件.确实高温",
                "学生",
                "器材.甲",
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>(),
                new[]
                {
                    Rule(
                        10,
                        ChemistryStructuredFactFields.来源对象温度,
                        StructuredRuleOperator.大于,
                        StructuredValue.FromNumber(80),
                        "温度尚未达到风险阈值")
                });
            var assessment = new CourseActionAssessmentDefinition(
                "评价.高温拒绝",
                CoreSemanticActionIds.Grab,
                "器材温度过高",
                "风险.烫伤",
                -10,
                "请使用坩埚钳。",
                new[] { condition });
            var request = new SemanticActionRequest(
                "命令.风险条件",
                CoreSemanticActionIds.Grab,
                "学生",
                "器材.甲",
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>());

            var notHot = evaluator.EvaluateAction(
                world,
                request,
                CommandResult.Rejected("器材温度过高"),
                new[] { assessment },
                100);
            world.SetScalar(
                "器材.甲.温度",
                90,
                new WorldScalarUnit("摄氏度"),
                null,
                null);
            var hot = evaluator.EvaluateAction(
                world,
                request,
                CommandResult.Rejected("器材温度过高"),
                new[] { assessment },
                100);

            Assert.That(notHot.Evidence, Is.Empty);
            Assert.That(hot.RiskIds, Is.EqualTo(new[] { "风险.烫伤" }));
            Assert.That(hot.Score, Is.EqualTo(90));
        }

        [Test]
        public void 会话执行动作时自动累计安全评价()
        {
            var world = World();
            world.SetScalar(
                "器材.甲.温度",
                90d,
                new WorldScalarUnit("摄氏度"),
                null,
                null);
            var action = ConfiguredActionDefinition.CreateGeneric(
                CoreSemanticActionIds.Grab,
                new[]
                {
                    Rule(
                        10,
                        ChemistryStructuredFactFields.来源对象温度,
                        StructuredRuleOperator.小于等于,
                        StructuredValue.FromNumber(60d),
                        "器材温度过高")
                },
                Array.Empty<ConfiguredMutationDefinition>());
            var runtime = new CourseRuntimeDefinition(
                CoreCourseRegistrations.CreateFactReaders()
                    .Concat(ChemistryCourseRegistrations.CreateFactReaders()),
                new[] { action },
                new[]
                {
                    new CourseActionAssessmentDefinition(
                        "评价.徒手接触高温器材",
                        CoreSemanticActionIds.Grab,
                        "器材温度过高",
                        "风险.烫伤",
                        -10,
                        "请使用坩埚钳。")
                },
                maximumScore: 100);
            var session = runtime.CreateSession(world);

            session.Execute(Request("命令.第一次"));
            session.Execute(Request("命令.第二次"));
            var assessment = session.ExportState().Assessment;

            Assert.That(assessment.Score, Is.EqualTo(80));
            Assert.That(assessment.RiskIds, Is.EqualTo(new[] { "风险.烫伤" }));
            Assert.That(assessment.Evidence.Count, Is.EqualTo(2));
            Assert.That(
                assessment.Evidence.Select(value => value.CommandId),
                Is.EqualTo(new[] { "命令.第一次", "命令.第二次" }));
        }

        [Test]
        public void 永久阻断后果只标记指定目标并判定本轮实验失败()
        {
            var goals = new CourseGoalEvaluationResult(
                new[] { "目标.已完成" });
            var assessment = new CourseAssessmentEvaluationResult(
                70,
                new[] { "风险.集气瓶损坏" },
                new[]
                {
                    new CourseAssessmentEvidence(
                        "评价.集气瓶热损伤",
                        "风险.集气瓶损坏",
                        -30,
                        "集气瓶已损坏，无法继续观察铁丝燃烧。",
                        "命令.错误燃烧",
                        CourseConsequenceSeverity.EquipmentOrSampleDamage,
                        CourseConsequenceRecoverability.GoalPermanentlyBlocked,
                        new[] { "目标.观察铁丝燃烧" })
                });

            var outcome = CourseOutcomeEvaluator.Evaluate(
                new[] { "目标.已完成", "目标.观察铁丝燃烧", "目标.其他" },
                goals,
                assessment);

            Assert.That(outcome.RunStatus, Is.EqualTo(CourseRunStatus.Failed));
            Assert.That(outcome.Quality, Is.EqualTo(CourseResultQuality.Degraded));
            Assert.That(
                outcome.BlockedGoalIds,
                Is.EqualTo(new[] { "目标.观察铁丝燃烧" }));
            Assert.That(
                outcome.BlockedGoalIds,
                Does.Not.Contain("目标.其他"));
        }

        private static CourseGoalEvaluationResult EvaluateAfterGrabOrder(
            string first,
            string second)
        {
            var world = World();
            world.SetRelation(Relation(RelationKind.由对象持有, first, "学生"));
            world.SetRelation(Relation(RelationKind.由对象持有, second, "学生"));
            var evaluator = new CourseGoalEvaluator(
                new StructuredRuleEvaluator(
                    CoreCourseRegistrations.CreateFactReaders()));
            return evaluator.Evaluate(
                world,
                new[]
                {
                    new CourseGoalRuleDefinition(
                        "目标.全部拿起",
                        new[]
                        {
                            Condition("条件.甲已拿起", "学生", "器材.甲"),
                            Condition("条件.乙已拿起", "学生", "器材.乙")
                        })
                });
        }

        private static SemanticActionRequest Request(string commandId) =>
            new SemanticActionRequest(
                commandId,
                CoreSemanticActionIds.Grab,
                "学生",
                "器材.甲",
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>());

        private static CourseConditionDefinition Condition(
            string id,
            string actor,
            string source)
        {
            return new CourseConditionDefinition(
                id,
                actor,
                source,
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>(),
                new[]
                {
                    Rule(
                        10,
                        InteractionStructuredFactFields.来源对象已被操作者拿起,
                        StructuredRuleOperator.等于,
                        StructuredValue.FromBoolean(true),
                        "尚未由当前主体持有")
                });
        }

        private static StructuredRuleDefinition Rule(
            int order,
            StructuredFactField field,
            StructuredRuleOperator ruleOperator,
            StructuredValue expected,
            string rejectionCode)
        {
            return new StructuredRuleDefinition(
                $"规则.{order}.{rejectionCode}",
                order,
                field,
                ruleOperator,
                expected,
                rejectionCode);
        }

        private static ExperimentWorld World()
        {
            var world = new ExperimentWorld();
            Add(world, "学生");
            Add(world, "器材.甲", new GrabbableCapability());
            Add(world, "器材.乙", new GrabbableCapability());
            return world;
        }

        private static void Add(
            ExperimentWorld world,
            string id,
            params ICapability[] capabilities)
        {
            var entity = new ExperimentEntity(new EntityId(id));
            foreach (var capability in capabilities)
            {
                entity.AddCapability(capability);
            }

            world.AddEntity(entity);
        }

        private static EntityRelation Relation(
            RelationKind kind,
            string source,
            string target)
        {
            return new EntityRelation(
                kind,
                new EntityId(source),
                new EntityId(target));
        }
    }
}
