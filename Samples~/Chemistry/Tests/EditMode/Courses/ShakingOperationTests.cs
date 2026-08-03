using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Chemistry.Tests.Courses
{
    public sealed class ShakingOperationTests
    {
        [Test]
        public void 振荡只记录裁决后的混合程度()
        {
            var world = World(held: true, shakeable: true);
            var operations = new ShakingOperations();
            var session = Session(world, operations);

            var result = session.Execute(Request("命令.振荡", 0.5d, 2d));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(
                world.TryGetScalar("器材.锥形瓶.混合程度", out var mixing),
                Is.True);
            Assert.That(mixing.Value, Is.EqualTo(1d));
            Assert.That(
                world.TryGetScalar("表现.振幅", out _),
                Is.False);
            Assert.That(
                world.TryGetScalar("表现.动画曲线", out _),
                Is.False);
        }

        [Test]
        public void 振荡返回能力持有强度和持续时间的全部失败原因()
        {
            var world = World(held: false, shakeable: false);
            var operations = new ShakingOperations();
            var session = Session(world, operations);

            var result = session.Execute(Request("命令.非法振荡", 2d, 0d));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(
                result.RejectionCodes,
                Is.EqualTo(new[]
                {
                    "容器不可振荡",
                    "容器未被当前主体持有",
                    "振荡强度超出范围",
                    "振荡持续时间超出范围"
                }));
            Assert.That(
                world.TryGetScalar("器材.锥形瓶.混合程度", out _),
                Is.False);
        }

        private static ConfigDrivenCourseSession Session(
            ExperimentWorld world,
            ShakingOperations operations)
        {
            return ChemistryCourseRegistrations.CreateSession(
                world,
                new[] { Action() },
                shakingOperations: operations);
        }

        private static ConfiguredActionDefinition Action()
        {
            return ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.Shake,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    new ConfiguredMutationDefinition(
                        "变更.记录混合程度",
                        ShakingOperations.OperationId,
                        Parameters(
                            ("最小强度", StructuredValue.FromNumber(0.1d)),
                            ("最大强度", StructuredValue.FromNumber(1d)),
                            ("最短持续秒数", StructuredValue.FromNumber(0.1d)),
                            ("最长持续秒数", StructuredValue.FromNumber(10d)),
                            ("混合状态键后缀", StructuredValue.FromText(".混合程度")),
                            ("混合增量系数", StructuredValue.FromNumber(1d)),
                            ("不可振荡拒绝原因", StructuredValue.FromText("容器不可振荡")),
                            ("未持有拒绝原因", StructuredValue.FromText("容器未被当前主体持有")),
                            ("强度越界拒绝原因", StructuredValue.FromText("振荡强度超出范围")),
                            ("持续时间越界拒绝原因", StructuredValue.FromText("振荡持续时间超出范围"))))
                });
        }

        private static SemanticActionRequest Request(
            string commandId,
            double intensity,
            double duration)
        {
            return new SemanticActionRequest(
                commandId,
                ChemistrySemanticActionIds.Shake,
                "学生",
                "器材.锥形瓶",
                null,
                Parameters(
                    ("强度", StructuredValue.FromNumber(intensity)),
                    ("持续秒数", StructuredValue.FromNumber(duration))));
        }

        private static ExperimentWorld World(
            bool held,
            bool shakeable)
        {
            var world = new ExperimentWorld();
            world.AddEntity(
                new ExperimentEntity(new EntityId("学生")));
            var vessel = new ExperimentEntity(
                new EntityId("器材.锥形瓶"));
            if (shakeable)
            {
                vessel.AddCapability(new ShakeableCapability());
            }

            world.AddEntity(vessel);
            if (held)
            {
                world.SetRelation(
                    new EntityRelation(
                        RelationKind.由对象持有,
                        vessel.Id,
                        new EntityId("学生")));
            }

            return world;
        }

        private static IReadOnlyDictionary<string, StructuredValue> Parameters(
            params (string Key, StructuredValue Value)[] parameters)
        {
            var result = new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                result.Add(parameter.Key, parameter.Value);
            }

            return result;
        }
    }
}
