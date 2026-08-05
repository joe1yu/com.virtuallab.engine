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
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Relations;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Processes;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Chemistry.Tests.Courses
{
    public sealed class MatterTransferOperationTests
    {
        [Test]
        public void 倾倒过程按流量转移物质并保持总量守恒()
        {
            var world = CreateWorld(20m, 20m);
            var operations = new MatterTransferOperations();
            var session = Session(world, operations);
            var before = world.Matter.Total("水", Unit.Millilitre);

            var begin = session.Execute(
                BeginRequest("命令.开始倾倒", 70d, true, 5d));
            operations.Advance(
                world,
                2d,
                new SimulationTick(1),
                new EventCollector());
            var end = session.Execute(EndRequest("命令.结束倾倒"));

            Assert.That(begin.IsAccepted, Is.True);
            Assert.That(end.IsAccepted, Is.True);
            Assert.That(
                world.Matter.Total(
                    new EntityId("器材.试管"),
                    "水",
                    Unit.Millilitre).Value,
                Is.EqualTo(10m));
            Assert.That(
                world.Matter.Total("水", Unit.Millilitre),
                Is.EqualTo(before));
            Assert.That(
                world.IsProcessActive(
                    MatterTransferOperations.PourProcessId,
                    new EntityId("器材.量筒")),
                Is.False);
        }

        [Test]
        public void 开始倾倒一次返回角度对准持有和单位的全部失败原因()
        {
            var world = CreateWorld(20m, 20m, held: false, Unit.Gram);
            var operations = new MatterTransferOperations();
            var session = Session(world, operations);

            var result = session.Execute(
                BeginRequest("命令.非法倾倒", 20d, false, 5d));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(
                result.RejectionCodes,
                Is.EqualTo(new[]
                {
                    "倾角不足",
                    "出口未对准",
                    "容器未被当前主体持有",
                    "来源物质单位不是毫升"
                }));
            Assert.That(world.Matter.Total("水", Unit.Gram).Value, Is.EqualTo(20m));
        }

        [Test]
        public void 容量不足时按配置停止且不产生物质()
        {
            var world = CreateWorld(20m, 5m);
            var operations = new MatterTransferOperations();
            var session = Session(world, operations);
            var events = new EventCollector();
            var before = world.Matter.Total("水", Unit.Millilitre);

            Assert.That(
                session.Execute(
                    BeginRequest("命令.容量测试", 70d, true, 5d))
                    .IsAccepted,
                Is.True);
            operations.Advance(
                world,
                2d,
                new SimulationTick(1),
                events);

            Assert.That(
                world.Matter.Total(
                    new EntityId("器材.试管"),
                    "水",
                    Unit.Millilitre).Value,
                Is.EqualTo(5m));
            Assert.That(world.Matter.Total("水", Unit.Millilitre), Is.EqualTo(before));
            Assert.That(
                world.IsProcessActive(
                    MatterTransferOperations.PourProcessId,
                    new EntityId("器材.量筒")),
                Is.False);
        }

        [Test]
        public void 来源不足时只转移剩余物质并停止()
        {
            var world = CreateWorld(3m, 20m);
            var operations = new MatterTransferOperations();
            var session = Session(world, operations);

            session.Execute(BeginRequest("命令.来源不足", 70d, true, 5d));
            operations.Advance(
                world,
                2d,
                new SimulationTick(1),
                new EventCollector());

            Assert.That(
                world.Matter.Total(
                    new EntityId("器材.试管"),
                    "水",
                    Unit.Millilitre).Value,
                Is.EqualTo(3m));
            Assert.That(
                world.IsProcessActive(
                    MatterTransferOperations.PourProcessId,
                    new EntityId("器材.量筒")),
                Is.False);
        }

        [Test]
        public void 溢出事件策略报告未转移量且保持守恒()
        {
            var world = CreateWorld(20m, 5m);
            var operations = new MatterTransferOperations();
            var session = Session(world, operations, "溢出事件");
            var events = new EventCollector();
            var before = world.Matter.Total("水", Unit.Millilitre);

            session.Execute(BeginRequest("命令.溢出策略", 70d, true, 5d));
            operations.Advance(
                world,
                2d,
                new SimulationTick(1),
                events);

            Assert.That(world.Matter.Total("水", Unit.Millilitre), Is.EqualTo(before));
            Assert.That(
                events.Events,
                Has.Some.Matches<VirtualLab.Application.Events.DomainEventEnvelope>(
                    value => value.EventType == "倾倒.已达到容量"));
        }

        [Test]
        public void 拒绝开始策略在来源总量超过剩余容量时拒绝()
        {
            var world = CreateWorld(20m, 5m);
            var operations = new MatterTransferOperations();
            var session = Session(world, operations, "拒绝开始");

            var result = session.Execute(
                BeginRequest("命令.拒绝超容", 70d, true, 5d));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.RejectionCode, Is.EqualTo("目标容量不足"));
            Assert.That(world.ActiveProcesses, Is.Empty);
        }

        [Test]
        public void 配置化物质转移可覆盖动作来源目标并保持守恒()
        {
            var world = CreateWorld(20m, 20m);
            Add(world, "器材.收集瓶", new ContainerCapability(100m));
            var operations = new MatterTransferOperations();
            var action = ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.CollectGas,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    Mutation(
                        "收集水",
                        MatterTransferOperations.TransferOperationId,
                        ("来源实体标识", StructuredValue.FromText("器材.量筒")),
                        ("目标实体标识", StructuredValue.FromText("器材.收集瓶")),
                        ("物质标识", StructuredValue.FromText("水")),
                        ("数量", StructuredValue.FromNumber(7d)),
                        ("计量单位", StructuredValue.FromText("毫升")))
                });
            var session = ChemistryCourseRegistrations.CreateSession(
                world,
                new[] { action },
                operations);
            var before = world.Matter.Total("水", Unit.Millilitre);

            var result = session.Execute(
                new SemanticActionRequest(
                    "命令.收集",
                    ChemistrySemanticActionIds.CollectGas,
                    "学生",
                    "器材.试管",
                    "器材.收集瓶",
                    Parameters()));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(
                world.Matter.Total(
                    new EntityId("器材.收集瓶"),
                    "水",
                    Unit.Millilitre).Value,
                Is.EqualTo(7m));
            Assert.That(
                world.Matter.Total("水", Unit.Millilitre),
                Is.EqualTo(before));
        }

        [Test]
        public void 倾倒事件提交失败时转移和停止过程一起回滚()
        {
            var world = CreateWorld(20m, 5m);
            var operations = new MatterTransferOperations();
            var session = Session(world, operations, "溢出事件");
            session.Execute(BeginRequest("命令.倾倒事务回滚", 70d, true, 5d));

            Assert.Throws<InvalidOperationException>(() =>
                operations.Advance(
                    world,
                    2d,
                    new SimulationTick(1),
                    new ThrowingCollector()));

            Assert.That(
                world.Matter.Total(
                    new EntityId("器材.量筒"),
                    "水",
                    Unit.Millilitre).Value,
                Is.EqualTo(20m));
            Assert.That(
                world.Matter.Total(
                    new EntityId("器材.试管"),
                    "水",
                    Unit.Millilitre).Value,
                Is.EqualTo(0m));
            Assert.That(
                world.IsProcessActive(
                    MatterTransferOperations.PourProcessId,
                    new EntityId("器材.量筒")),
                Is.True);
        }

        [Test]
        public void 化学运行时恢复后继续执行操作并累计安全评价()
        {
            var operations = new MatterTransferOperations();
            var runtime = ChemistryCourseRegistrations.CreateRuntimeDefinition(
                new[] { BeginAction("停止"), EndAction() },
                new[]
                {
                    new CourseActionAssessmentDefinition(
                        "评价.倾角不足",
                        ChemistrySemanticActionIds.BeginPour,
                        "倾角不足",
                        "风险.洒出",
                        -10,
                        "请增大倾角后再倾倒。")
                },
                100,
                operations);
            var session = runtime.CreateSession(CreateWorld(20m, 20m));
            session.Execute(
                BeginRequest("命令.恢复前错误倾倒", 20d, false, 5d));
            Assert.That(
                session.Execute(
                    BeginRequest("命令.恢复前开始倾倒", 70d, true, 5d))
                    .IsAccepted,
                Is.True);

            var restored = ConfigDrivenCourseSession.Restore(
                runtime,
                session.ExportState());
            var rejected = restored.Execute(
                BeginRequest("命令.恢复后错误倾倒", 20d, false, 5d));
            var ended = restored.Execute(
                EndRequest("命令.恢复后结束倾倒"));
            var state = restored.ExportState();

            Assert.That(rejected.IsAccepted, Is.False);
            Assert.That(ended.IsAccepted, Is.True);
            Assert.That(state.Assessment.Score, Is.EqualTo(80));
            Assert.That(
                state.Assessment.Evidence
                    .Select(value => value.CommandId),
                Is.EqualTo(new[]
                {
                    "命令.恢复前错误倾倒",
                    "命令.恢复后错误倾倒"
                }));
        }

        private static ConfigDrivenCourseSession Session(
            ExperimentWorld world,
            MatterTransferOperations operations,
            string capacityPolicy = "停止")
        {
            return ChemistryCourseRegistrations.CreateSession(
                world,
                new[] { BeginAction(capacityPolicy), EndAction() },
                operations);
        }

        private static ConfiguredActionDefinition BeginAction(
            string capacityPolicy)
        {
            return ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.BeginPour,
                new[]
                {
                    Rule(10, ChemistryStructuredFactFields.倾倒角度, StructuredRuleOperator.大于等于, StructuredValue.FromNumber(45d), "倾角不足"),
                    Rule(20, ChemistryStructuredFactFields.倾倒口已对准, StructuredRuleOperator.等于, StructuredValue.FromBoolean(true), "出口未对准"),
                    Rule(30, InteractionStructuredFactFields.来源对象持有者, StructuredRuleOperator.等于, StructuredValue.FromText("学生"), "容器未被当前主体持有"),
                    Rule(40, ChemistryStructuredFactFields.来源内容单位, StructuredRuleOperator.等于, StructuredValue.FromText("Millilitre"), "来源物质单位不是毫升")
                },
                new[]
                {
                    Mutation(
                        "开始倾倒过程",
                        MatterTransferOperations.BeginOperationId,
                        ("物质标识", StructuredValue.FromText("水")),
                        ("容量处理策略", StructuredValue.FromText(capacityPolicy)),
                        ("容量不足拒绝原因", StructuredValue.FromText("目标容量不足")))
                });
        }

        private static ConfiguredActionDefinition EndAction()
        {
            return ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.EndPour,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    Mutation(
                        "结束倾倒过程",
                        MatterTransferOperations.EndOperationId)
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

        private static ConfiguredMutationDefinition Mutation(
            string id,
            string operationId,
            params (string Key, StructuredValue Value)[] parameters)
        {
            return new ConfiguredMutationDefinition(
                $"变更.{id}",
                operationId,
                Parameters(parameters));
        }

        private static SemanticActionRequest BeginRequest(
            string commandId,
            double angle,
            bool aligned,
            double rate)
        {
            return new SemanticActionRequest(
                commandId,
                ChemistrySemanticActionIds.BeginPour,
                "学生",
                "器材.量筒",
                "器材.试管",
                Parameters(
                    ("倾角度数", StructuredValue.FromNumber(angle)),
                    ("出口是否对准目标入口", StructuredValue.FromBoolean(aligned)),
                    ("请求流量毫升每秒", StructuredValue.FromNumber(rate))));
        }

        private static SemanticActionRequest EndRequest(string commandId)
        {
            return new SemanticActionRequest(
                commandId,
                ChemistrySemanticActionIds.EndPour,
                "学生",
                "器材.量筒",
                "器材.试管",
                Parameters());
        }

        private static ExperimentWorld CreateWorld(
            decimal sourceAmount,
            decimal targetCapacity,
            bool held = true,
            Unit sourceUnit = Unit.Millilitre)
        {
            var world = new ExperimentWorld(InteractionRelationSchemas.All);
            Add(world, "学生");
            Add(
                world,
                "器材.量筒",
                new GrabbableCapability(),
                new PourableCapability(),
                new ContainerCapability(100m));
            Add(
                world,
                "器材.试管",
                new ContainerCapability(targetCapacity));
            if (held)
            {
                world.SetRelation(
                    new EntityRelation(
                        InteractionRelationTypeIds.HeldBy,
                        new EntityId("器材.量筒"),
                        new EntityId("学生")));
            }

            world.Matter.Add(
                new EntityId("器材.量筒"),
                new SubstanceBatch(
                    "水",
                    new Quantity(sourceAmount, sourceUnit),
                    MatterPhase.Liquid,
                    new Temperature(20m)));
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

        private sealed class ThrowingCollector : IProcessEventCollector
        {
            public void CommitAtomically(
                string commandId,
                SimulationTick tick,
                IDomainEvent domainEvent,
                Action commitState)
            {
                throw new InvalidOperationException("模拟倾倒事件提交失败。");
            }
        }
    }
}
