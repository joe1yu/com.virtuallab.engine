using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class SemanticActionLifecycleTests
    {
        [Test]
        public void 接受操作时返回与请求实例一致的设备无关执行计划()
        {
            var session = Session(
                SemanticActionLifecycle.Continuous,
                "持续倾斜",
                SemanticActionPhase.Start);
            var request = Request(
                "命令.1",
                "操作实例.倾倒.1",
                SemanticActionPhase.Start,
                12.5d);

            var result = session.Execute(request);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Execution.OperationInstanceId,
                Is.EqualTo("操作实例.倾倒.1"));
            Assert.That(result.Execution.OperationId, Is.EqualTo("倾倒"));
            Assert.That(result.Execution.Lifecycle,
                Is.EqualTo(SemanticActionLifecycle.Continuous));
            Assert.That(result.Execution.ExecutionModeId,
                Is.EqualTo("持续倾斜"));
            Assert.That(result.Execution.Phase,
                Is.EqualTo(SemanticActionPhase.Start));
        }

        [Test]
        public void 同一命令标识改变操作阶段会被幂等边界拒绝()
        {
            var session = Session(
                SemanticActionLifecycle.Manipulation,
                "移动对象",
                SemanticActionPhase.Start);
            session.Execute(Request(
                "命令.1",
                "操作实例.1",
                SemanticActionPhase.Start,
                1d));

            var conflict = session.Execute(Request(
                "命令.1",
                "操作实例.1",
                SemanticActionPhase.Complete,
                2d));

            Assert.That(conflict.IsAccepted, Is.False);
            Assert.That(conflict.Execution, Is.Null);
            Assert.That(conflict.RejectionCode, Is.EqualTo("命令标识冲突"));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-0.01d)]
        public void 操作发生时刻必须有限且非负(double occurredAtSeconds)
        {
            Assert.That(
                () => Request(
                    "命令.非法时刻",
                    "操作实例.非法时刻",
                    SemanticActionPhase.Complete,
                    occurredAtSeconds),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static ConfigDrivenCourseSession Session(
            SemanticActionLifecycle lifecycle,
            string executionModeId,
            SemanticActionPhase phase)
        {
            var world = new ExperimentWorld();
            world.AddEntity(new ExperimentEntity(new EntityId("学生")));
            world.AddEntity(new ExperimentEntity(new EntityId("试管")));
            return new ConfigDrivenCourseSession(
                world,
                new StructuredRuleEvaluator(
                    Array.Empty<IStructuredFactReader>()),
                new ConfiguredStateOperationRegistry(),
                new[]
                {
                    ConfiguredActionDefinition.CreatePolicy(
                        "策略.倾倒.开始",
                        "开始倾倒",
                        "倾倒",
                        lifecycle,
                        executionModeId,
                        phase,
                        "试管",
                        string.Empty,
                        100,
                        ConfiguredActionPolicyEffect.Allow,
                        null,
                        Array.Empty<StructuredRuleDefinition>(),
                        Array.Empty<ConfiguredMutationDefinition>())
                });
        }

        private static SemanticActionRequest Request(
            string commandId,
            string operationInstanceId,
            SemanticActionPhase phase,
            double occurredAtSeconds)
        {
            return new SemanticActionRequest(
                commandId,
                "开始倾倒",
                operationInstanceId,
                phase,
                occurredAtSeconds,
                "学生",
                "试管",
                string.Empty,
                new Dictionary<string, StructuredValue>(
                    StringComparer.Ordinal));
        }
    }
}
