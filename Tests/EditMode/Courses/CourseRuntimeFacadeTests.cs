using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Processes;
using VirtualLab.Infrastructure.Reporting;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseRuntimeFacadeTests
    {
        [Test]
        public void 统一执行命令Tick目标评价观察和事件历史()
        {
            var world = World();
            var readers = CoreCourseRegistrations.CreateFactReaders();
            var runtime = new CourseRuntimeDefinition(
                readers,
                Actions(),
                new[]
                {
                    new CourseActionAssessmentDefinition(
                        "评价.危险操作",
                        "动作.危险",
                        "拒绝.危险",
                        "风险.危险操作",
                        -10,
                        "当前条件不允许执行危险操作。")
                },
                maximumScore: 100);
            var process = new TestProcessAdvancer();
            var session = runtime.CreateSession(world);
            var facade = new CourseRuntimeFacade(
                world,
                session,
                readers,
                GoalRules(),
                process);

            Assert.That(facade.CurrentTick.Value, Is.Zero);
            Assert.That(facade.Goals.CompletedGoalIds, Is.Empty);
            Assert.That(facade.Assessment.Score, Is.EqualTo(100));

            var completion = facade.Execute(Request(
                "命令.完成",
                "动作.完成"));
            var repeated = facade.Execute(Request(
                "命令.完成",
                "动作.完成"));
            var observation = facade.Execute(Request(
                "命令.观察",
                CoreSemanticActionIds.Observe));
            var rejected = facade.Execute(Request(
                "命令.危险",
                "动作.危险"));
            var tick = facade.Tick(0.5d);

            Assert.That(completion.IsAccepted, Is.True);
            Assert.That(repeated, Is.SameAs(completion));
            Assert.That(observation.IsAccepted, Is.True);
            Assert.That(rejected.IsAccepted, Is.False);
            Assert.That(
                facade.Goals.CompletedGoalIds,
                Is.EqualTo(new[] { "目标.完成课程" }));
            Assert.That(facade.Assessment.Score, Is.EqualTo(90));
            Assert.That(
                facade.Assessment.RiskIds,
                Is.EqualTo(new[] { "风险.危险操作" }));
            Assert.That(facade.Observations, Has.Count.EqualTo(1));
            Assert.That(
                facade.Observations[0].ObservedEntityId,
                Is.EqualTo("器材"));
            Assert.That(facade.CurrentTick.Value, Is.EqualTo(1));
            Assert.That(tick.Tick, Is.EqualTo(facade.CurrentTick));
            Assert.That(tick.Events, Has.Count.EqualTo(1));
            Assert.That(process.AdvanceCount, Is.EqualTo(1));
            Assert.That(facade.EventHistory, Has.Count.EqualTo(2));
            Assert.That(
                facade.EventHistory[0].EventType,
                Is.EqualTo("课程门面.命令已接受"));
            Assert.That(facade.EventHistory[0].Sequence, Is.EqualTo(1));
            Assert.That(facade.EventHistory[0].Tick.Value, Is.Zero);
            Assert.That(
                facade.EventHistory[1].EventType,
                Is.EqualTo("课程门面.过程已推进"));
            Assert.That(facade.EventHistory[1].Sequence, Is.EqualTo(2));
            Assert.That(facade.EventHistory[1].Tick.Value, Is.EqualTo(1));

            var state = facade.ExportState();
            Assert.That(
                state.Goals.CompletedGoalIds,
                Is.EqualTo(new[] { "目标.完成课程" }));
            Assert.That(state.Assessment, Is.EqualTo(facade.Assessment));
            Assert.That(state.Observations, Is.EqualTo(new[] { "器材" }));
            Assert.That(state.Events.Count, Is.EqualTo(2));
            Assert.That(
                state.Events[1].EventType,
                Is.EqualTo("课程门面.过程已推进"));
            Assert.That(state.Events[1].Sequence, Is.EqualTo(2));
            Assert.That(state.NextEventSequence, Is.EqualTo(3));

            var reportTimeline = new CourseReportTimeline(facade);
            Assert.That(reportTimeline.LastSequence, Is.EqualTo(2));
            Assert.That(
                reportTimeline.Events[1],
                Is.SameAs(facade.EventStates[1]));
            Assert.That(
                reportTimeline.CreateMetadata("课程", "会话", 7)
                    .LastSequence,
                Is.EqualTo(state.Events[1].Sequence));

            var restored = ConfigDrivenCourseSession.Restore(runtime, state);
            Assert.That(
                restored.ExportState().Events.Select(value => value.EventType),
                Is.EqualTo(state.Events.Select(value => value.EventType)));
        }

        [Test]
        public void 需重启后果判定实验失败但不锁死后续自由操作()
        {
            const string eventType = "实验风险.终止测试";
            var world = World();
            var readers = CoreCourseRegistrations.CreateFactReaders();
            var accident = ConfiguredActionDefinition.CreateGeneric(
                "动作.造成事故",
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    new ConfiguredMutationDefinition(
                        "变化.发布事故",
                        ConfiguredStateOperationIds.EventEmit,
                        Parameters((
                            "事件类型",
                            StructuredValue.FromText(eventType))))
                });
            var continueObservation =
                ConfiguredActionDefinition.CreateGeneric(
                    "动作.继续观察",
                    Array.Empty<StructuredRuleDefinition>(),
                    Array.Empty<ConfiguredMutationDefinition>());
            var runtime = new CourseRuntimeDefinition(
                readers,
                new[] { accident, continueObservation },
                new[]
                {
                    CourseActionAssessmentDefinition.ForDomainEvent(
                        "评价.事故",
                        eventType,
                        "风险.必须重做",
                        -30,
                        "本次实验需要重新开始。",
                        severity: CourseConsequenceSeverity.SafetyIncident,
                        recoverability: CourseConsequenceRecoverability
                            .RestartRequired)
                },
                100);
            var facade = new CourseRuntimeFacade(
                world,
                runtime.CreateSession(world),
                readers,
                GoalRules());

            var accidentResult = facade.Execute(Request(
                "命令.造成事故",
                "动作.造成事故"));
            var followUpResult = facade.Execute(Request(
                "命令.继续观察",
                "动作.继续观察"));

            Assert.That(accidentResult.IsAccepted, Is.True);
            Assert.That(facade.Outcome.RunStatus, Is.EqualTo(
                CourseRunStatus.Failed));
            Assert.That(facade.Outcome.Quality, Is.EqualTo(
                CourseResultQuality.Degraded));
            Assert.That(
                facade.Outcome.BlockedGoalIds,
                Is.EqualTo(new[] { "目标.完成课程" }));
            Assert.That(followUpResult.IsAccepted, Is.True,
                "实验失败只描述结局，仍应允许观察和清理等自由操作。");
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Tick拒绝非法时间且不推进时钟(double elapsedSeconds)
        {
            var world = World();
            var session = CoreCourseRegistrations.CreateSession(
                world,
                Array.Empty<ConfiguredActionDefinition>());
            var facade = new CourseRuntimeFacade(session);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                facade.Tick(elapsedSeconds));
            Assert.That(facade.CurrentTick.Value, Is.Zero);
            Assert.That(facade.EventHistory, Is.Empty);
        }

        [Test]
        public void Tick中任一过程失败时回滚整帧状态事件和时钟()
        {
            var world = World();
            var session = CoreCourseRegistrations.CreateSession(
                world,
                Actions());
            var facade = new CourseRuntimeFacade(
                world,
                session,
                CoreCourseRegistrations.CreateFactReaders(),
                Array.Empty<CourseGoalRuleDefinition>(),
                new FailingProcessAdvancer(world));
            facade.Execute(Request("命令.已有事件", "动作.完成"));
            var existingEvent = facade.EventHistory.Single().Event;

            Assert.Throws<InvalidOperationException>(() => facade.Tick(0.5));

            Assert.That(facade.CurrentTick.Value, Is.Zero);
            Assert.That(facade.EventHistory, Has.Count.EqualTo(1));
            Assert.That(facade.EventHistory.Single().Event,
                Is.SameAs(existingEvent));
            Assert.That(world.TryGetScalar("器材.失败状态", out _), Is.False);
        }

        [Test]
        public void 条件事件只在风险状态成立时发布并进入统一事件流()
        {
            var world = World();
            var action = ConfiguredActionDefinition.CreateGeneric(
                "动作.产生风险后果",
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    new ConfiguredMutationDefinition(
                        "变化.记录风险",
                        ConfiguredStateOperationIds.ScalarSet,
                        Parameters(
                            ("状态键", StructuredValue.FromText(
                                "器材.风险.测试")),
                            ("数值", StructuredValue.FromNumber(1)),
                            ("单位", StructuredValue.FromText("布尔")))),
                    new ConfiguredMutationDefinition(
                        "变化.发布风险事件",
                        ConfiguredStateOperationIds.EventEmitWhenScalar,
                        Parameters(
                            ("状态键", StructuredValue.FromText(
                                "器材.风险.测试")),
                            ("期望值", StructuredValue.FromNumber(1)),
                            ("事件类型", StructuredValue.FromText(
                                "实验风险.测试"))))
                });
            var session = CoreCourseRegistrations.CreateSession(
                world,
                new[] { action });
            var facade = new CourseRuntimeFacade(session);

            var result = facade.Execute(Request(
                "命令.产生风险后果",
                "动作.产生风险后果"));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Events.Select(value => value.EventType),
                Is.EqualTo(new[] { "实验风险.测试" }));
            Assert.That(facade.EventHistory.Single().Event,
                Is.TypeOf<ConfiguredCourseDomainEvent>());
        }

        private static IEnumerable<ConfiguredActionDefinition> Actions()
        {
            yield return ConfiguredActionDefinition.CreateGeneric(
                "动作.完成",
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    new ConfiguredMutationDefinition(
                        "变化.完成进度",
                        "设置标量",
                        Parameters(
                            ("状态键", StructuredValue.FromText(
                                "器材.课程进度")),
                            ("数值", StructuredValue.FromNumber(1d)),
                            ("单位", StructuredValue.FromText("进度")))),
                    new ConfiguredMutationDefinition(
                        "变化.发布完成事件",
                        "发布事件",
                        Parameters(
                            ("事件类型", StructuredValue.FromText(
                                "课程门面.命令已接受"))))
                });
            yield return ConfiguredActionDefinition.CreateGeneric(
                CoreSemanticActionIds.Observe,
                Array.Empty<StructuredRuleDefinition>(),
                Array.Empty<ConfiguredMutationDefinition>());
            yield return ConfiguredActionDefinition.CreateGeneric(
                "动作.危险",
                new[]
                {
                    new StructuredRuleDefinition(
                        "规则.拒绝危险操作",
                        10,
                        StructuredFactField.来源对象存在,
                        StructuredRuleOperator.等于,
                        StructuredValue.FromBoolean(false),
                        "拒绝.危险")
                },
                Array.Empty<ConfiguredMutationDefinition>());
        }

        private static IEnumerable<CourseGoalRuleDefinition> GoalRules()
        {
            yield return new CourseGoalRuleDefinition(
                "目标.完成课程",
                new[]
                {
                    new CourseConditionDefinition(
                        "条件.课程进度完成",
                        "学生",
                        "器材",
                        null,
                        Array.Empty<
                            KeyValuePair<string, StructuredValue>>(),
                        new[]
                        {
                            new StructuredRuleDefinition(
                                "规则.课程进度完成",
                                10,
                                StructuredFactField.来源对象进度,
                                StructuredRuleOperator.大于等于,
                                StructuredValue.FromNumber(1d),
                                "课程尚未完成")
                        })
                });
        }

        private static SemanticActionRequest Request(
            string commandId,
            string actionId) =>
            new SemanticActionRequest(
                commandId,
                actionId,
                "学生",
                "器材",
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>());

        private static IReadOnlyDictionary<string, StructuredValue>
            Parameters(
                params (string Name, StructuredValue Value)[] parameters)
        {
            var result = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                result.Add(parameter.Name, parameter.Value);
            }

            return result;
        }

        private static ExperimentWorld World()
        {
            var world = new ExperimentWorld();
            world.AddEntity(new ExperimentEntity(new EntityId("学生")));
            world.AddEntity(new ExperimentEntity(new EntityId("器材")));
            return world;
        }

        private sealed class TestProcessAdvancer : ICourseProcessAdvancer
        {
            public int AdvanceCount { get; private set; }

            public void AdvanceProcesses(
                double elapsedSeconds,
                SimulationTick tick,
                IProcessEventCollector events)
            {
                events.CommitAtomically(
                    "过程.统一推进",
                    tick,
                    new TestProcessEvent(),
                    () => AdvanceCount++);
            }
        }

        private sealed class FailingProcessAdvancer : ICourseProcessAdvancer
        {
            private readonly ExperimentWorld _world;

            public FailingProcessAdvancer(ExperimentWorld world)
            {
                _world = world;
            }

            public void AdvanceProcesses(
                double elapsedSeconds,
                SimulationTick tick,
                IProcessEventCollector events)
            {
                events.CommitAtomically(
                    "过程.先提交后失败",
                    tick,
                    new TestProcessEvent(),
                    () => _world.SetScalar(
                        "器材.失败状态",
                        1,
                        new WorldScalarUnit("布尔"),
                        null,
                        null));
                throw new InvalidOperationException("模拟后续过程失败");
            }
        }

        private sealed class TestProcessEvent : IDomainEvent
        {
            public string EventType => "课程门面.过程已推进";
        }
    }
}
