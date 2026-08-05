using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Application.Events;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Kernel;
using VirtualLab.Measurement;
using VirtualLab.Spatial.Courses;

namespace VirtualLab.Chemistry.Tests.Courses
{
    public sealed class CombustionOperationTests
    {
        [Test]
        public void 安全液体不足时允许燃烧并发布可评价的后果事件()
        {
            var world = CombustionWorld(true, true, 300m);
            Add(world, "器材.安全瓶", new ContainerCapability(100m));
            var riskKey = "器材.安全瓶.风险.热损伤";
            var eventType = "实验风险.热损伤";
            var action = ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.Ignite,
                "点燃",
                SemanticActionLifecycle.Instant,
                "接触点火",
                SemanticActionPhase.Complete,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    Mutation(
                        "开始燃烧并检查安全液体",
                        CombustionOperations.IgniteOperationId,
                        ("反应标识", StructuredValue.FromText("反应.碳燃烧")),
                        ("燃料标识", StructuredValue.FromText("碳")),
                        ("氧化剂标识", StructuredValue.FromText("氧气")),
                        ("最低点燃温度摄氏度", StructuredValue.FromNumber(200d)),
                        ("最大点火距离米", StructuredValue.FromNumber(0.2d)),
                        ("每秒反应单位", StructuredValue.FromNumber(1d)),
                        ("点火源无效拒绝原因", StructuredValue.FromText("点火源无效")),
                        ("点火距离过远拒绝原因", StructuredValue.FromText("点火距离过远")),
                        ("对象不可燃拒绝原因", StructuredValue.FromText("对象不可燃")),
                        ("缺少氧化剂拒绝原因", StructuredValue.FromText("缺少氧气")),
                        ("燃料温度不足拒绝原因", StructuredValue.FromText("燃料温度不足")),
                        ("安全容器实体标识", StructuredValue.FromText("器材.安全瓶")),
                        ("最小安全液体体积毫升", StructuredValue.FromNumber(5d)),
                        ("液体不足拒绝原因", StructuredValue.FromText("瓶底未留水")),
                        ("液体不足风险状态键", StructuredValue.FromText(riskKey))),
                    Mutation(
                        "发布热损伤后果",
                        ConfiguredStateOperationIds.EventEmitWhenScalar,
                        ("状态键", StructuredValue.FromText(riskKey)),
                        ("期望值", StructuredValue.FromNumber(1d)),
                        ("事件类型", StructuredValue.FromText(eventType)))
                });
            var operations = new CombustionOperations(
                new[] { CarbonReaction() });
            var session = ChemistryCourseRegistrations.CreateSession(
                world,
                new[] { action },
                combustionOperations: operations);

            var result = session.Execute(
                IgniteRequest("命令.缺少安全液体仍点燃", 0.1d));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(world.TryGetScalar(riskKey, out var risk), Is.True);
            Assert.That(risk.Value, Is.EqualTo(1d));
            Assert.That(result.Events.Select(value => value.EventType),
                Is.EqualTo(new[] { eventType }));
        }

        [Test]
        public void 点燃建立燃烧过程并按反应定义生成产物()
        {
            var world = CombustionWorld(true, true, 300m);
            var operations = new CombustionOperations(
                new[] { CarbonReaction() });
            var session = Session(world, operations);
            var events = new EventCollector();

            var ignited = session.Execute(IgniteRequest("命令.点燃", 0.1d));
            operations.Advance(
                world,
                1d,
                new SimulationTick(1),
                events);
            var extinguished = session.Execute(
                Request(
                    "命令.熄灭",
                    ChemistrySemanticActionIds.Extinguish,
                    0d));

            Assert.That(ignited.IsAccepted, Is.True);
            Assert.That(extinguished.IsAccepted, Is.True);
            Assert.That(
                world.RequireMatterInventory().Total(
                    new EntityId("器材.燃烧匙"),
                    "二氧化碳",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(3m));
            Assert.That(
                events.Events.Select(value => value.EventType),
                Is.EqualTo(new[] { "气体.已生成" }));
            Assert.That(
                events.Events.Any(value =>
                    value.EventType.Contains("animation")
                    || value.EventType.Contains("particle")),
                Is.False);
        }

        [Test]
        public void 点燃一次返回热源距离可燃性氧气和温度的全部原因()
        {
            var world = CombustionWorld(false, false, 20m);
            var operations = new CombustionOperations(
                new[] { CarbonReaction() });
            var session = Session(world, operations);

            var result = session.Execute(
                IgniteRequest("命令.非法点燃", 1d));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(
                result.RejectionCodes,
                Is.EqualTo(new[]
                {
                    "点火源无效",
                    "点火距离过远",
                    "对象不可燃",
                    "缺少氧气",
                    "燃料温度不足"
                }));
            Assert.That(world.ActiveProcesses, Is.Empty);
        }

        [Test]
        public void 反应物耗尽后权威燃烧过程自动停止()
        {
            var world = CombustionWorld(true, true, 300m);
            var operations = new CombustionOperations(
                new[] { CarbonReaction() });
            var session = Session(world, operations);
            session.Execute(IgniteRequest("命令.耗尽点燃", 0.1d));

            operations.Advance(world, 1d, new SimulationTick(1), new EventCollector());
            operations.Advance(world, 1d, new SimulationTick(2), new EventCollector());

            Assert.That(
                world.IsProcessActive(
                    CombustionOperations.CombustionProcessId,
                    new EntityId("器材.燃烧匙")),
                Is.False);
        }

        [Test]
        public void 点燃可从配置指定容器取得恰好一个反应单位的氧化剂()
        {
            var world = CombustionWorld(true, true, 300m);
            Add(world, "器材.氧气瓶", new ContainerCapability(100m));
            world.RequireMatterInventory().Transfer(
                new EntityId("器材.燃烧匙"),
                new EntityId("器材.氧气瓶"),
                "氧气",
                new Quantity(4m, ChemistryUnits.Gram),
                new SimulationTick(0),
                new EventCollector());
            var operations = new CombustionOperations(
                new[] { CarbonReaction() });
            var action = ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.Ignite,
                "点燃",
                SemanticActionLifecycle.Instant,
                "接触点火",
                SemanticActionPhase.Complete,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    Mutation(
                        "开始跨容器燃烧",
                        CombustionOperations.IgniteOperationId,
                        ("反应标识", StructuredValue.FromText("反应.碳燃烧")),
                        ("燃料标识", StructuredValue.FromText("碳")),
                        ("氧化剂标识", StructuredValue.FromText("氧气")),
                        ("氧化剂来源实体标识", StructuredValue.FromText("器材.氧气瓶")),
                        ("最低点燃温度摄氏度", StructuredValue.FromNumber(200d)),
                        ("最大点火距离米", StructuredValue.FromNumber(0.2d)),
                        ("每秒反应单位", StructuredValue.FromNumber(1d)),
                        ("点火源无效拒绝原因", StructuredValue.FromText("点火源无效")),
                        ("点火距离过远拒绝原因", StructuredValue.FromText("点火距离过远")),
                        ("对象不可燃拒绝原因", StructuredValue.FromText("对象不可燃")),
                        ("缺少氧化剂拒绝原因", StructuredValue.FromText("缺少氧气")),
                        ("燃料温度不足拒绝原因", StructuredValue.FromText("燃料温度不足")))
                });
            var session = ChemistryCourseRegistrations.CreateSession(
                world,
                new[] { action },
                combustionOperations: operations);

            var result = session.Execute(IgniteRequest("命令.跨容器点燃", 0.1d));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(
                world.RequireMatterInventory().Total(
                    new EntityId("器材.燃烧匙"),
                    "氧气",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(2m));
            Assert.That(
                world.RequireMatterInventory().Total(
                    new EntityId("器材.氧气瓶"),
                    "氧气",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(2m));
        }

        private static ConfigDrivenCourseSession Session(
            ExperimentWorld world,
            CombustionOperations operations)
        {
            return ChemistryCourseRegistrations.CreateSession(
                world,
                new[] { IgniteAction(), ExtinguishAction() },
                combustionOperations: operations);
        }

        private static ConfiguredActionDefinition IgniteAction()
        {
            return ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.Ignite,
                "点燃",
                SemanticActionLifecycle.Instant,
                "接触点火",
                SemanticActionPhase.Complete,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    Mutation(
                        "开始燃烧",
                        CombustionOperations.IgniteOperationId,
                        ("反应标识", StructuredValue.FromText("反应.碳燃烧")),
                        ("燃料标识", StructuredValue.FromText("碳")),
                        ("氧化剂标识", StructuredValue.FromText("氧气")),
                        ("最低点燃温度摄氏度", StructuredValue.FromNumber(200d)),
                        ("最大点火距离米", StructuredValue.FromNumber(0.2d)),
                        ("每秒反应单位", StructuredValue.FromNumber(1d)),
                        ("点火源无效拒绝原因", StructuredValue.FromText("点火源无效")),
                        ("点火距离过远拒绝原因", StructuredValue.FromText("点火距离过远")),
                        ("对象不可燃拒绝原因", StructuredValue.FromText("对象不可燃")),
                        ("缺少氧化剂拒绝原因", StructuredValue.FromText("缺少氧气")),
                        ("燃料温度不足拒绝原因", StructuredValue.FromText("燃料温度不足")))
                });
        }

        private static ConfiguredActionDefinition ExtinguishAction()
        {
            return ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.Extinguish,
                "熄灭",
                SemanticActionLifecycle.Instant,
                "覆盖熄灭",
                SemanticActionPhase.Complete,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    Mutation(
                        "停止燃烧",
                        CombustionOperations.ExtinguishOperationId)
                });
        }

        private static SemanticActionRequest IgniteRequest(
            string commandId,
            double distance)
        {
            return Request(
                commandId,
                ChemistrySemanticActionIds.Ignite,
                distance);
        }

        private static SemanticActionRequest Request(
            string commandId,
            string actionId,
            double distance)
        {
            return new SemanticActionRequest(
                commandId,
                actionId,
                "操作." + commandId,
                SemanticActionPhase.Complete,
                0d,
                "学生",
                "器材.燃烧匙",
                "器材.点火器",
                Parameters(
                    (SpatialRequestParameterKeys.DistanceMeters,
                        StructuredValue.FromNumber(distance))));
        }

        private static ExperimentWorld CombustionWorld(
            bool validIgniter,
            bool combustible,
            decimal temperature)
        {
            var world = new ExperimentWorld();
            Add(world, "学生");
            Add(
                world,
                "器材.燃烧匙",
                combustible
                    ? new ICapability[] { new CombustibleCapability() }
                    : Array.Empty<ICapability>());
            Add(
                world,
                "器材.点火器",
                validIgniter
                    ? new ICapability[] { new Heat来源对象能力(100m) }
                    : Array.Empty<ICapability>());
            world.RequireMatterInventory().Add(
                new EntityId("器材.燃烧匙"),
                new SubstanceBatch(
                    "碳",
                    new Quantity(2m, ChemistryUnits.Gram),
                    MatterPhase.Solid,
                    new Temperature(temperature)));
            if (validIgniter)
            {
                world.RequireMatterInventory().Add(
                    new EntityId("器材.燃烧匙"),
                    new SubstanceBatch(
                        "氧气",
                        new Quantity(4m, ChemistryUnits.Gram),
                        MatterPhase.Gas,
                        new Temperature(20m)));
            }

            return world;
        }

        private static ChemicalReactionDefinition CarbonReaction()
        {
            return new ChemicalReactionDefinition(
                "反应.碳燃烧",
                new[]
                {
                    new ReactionTerm("碳", new Quantity(1m, ChemistryUnits.Gram), MatterPhase.Solid, 1m),
                    new ReactionTerm("氧气", new Quantity(2m, ChemistryUnits.Gram), MatterPhase.Gas, 1m)
                },
                new[]
                {
                    new ReactionTerm("二氧化碳", new Quantity(3m, ChemistryUnits.Gram), MatterPhase.Gas, 1m)
                });
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

        private static ConfiguredMutationDefinition Mutation(
            string id,
            string operationId,
            params (string Key, StructuredValue Value)[] parameters)
        {
            return new ConfiguredMutationDefinition(
                "变更." + id,
                operationId,
                Parameters(parameters));
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
