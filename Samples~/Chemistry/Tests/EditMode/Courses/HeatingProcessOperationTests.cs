using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Application.Events;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Processes;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Tests.Courses
{
    public sealed class HeatingProcessOperationTests
    {
        [Test]
        public void 加热按功率效率热容和散热参数改变温度并触发反应()
        {
            var world = HeatingWorld(connected: false);
            var reaction = SimpleThermalReaction();
            var operations = new HeatingProcessOperations(
                new[] { reaction });
            var session = Session(world, operations);
            var events = new EventCollector();

            Assert.That(
                session.Execute(Begin("命令.开始加热")).IsAccepted,
                Is.True);
            Assert.That(
                world.Relations,
                Has.Some.Matches<EntityRelation>(
                    value => value.TypeId == ChemistryRelationTypeIds.HeatedBy
                        && value.Source == new EntityId("器材.试管")
                        && value.Target == new EntityId("器材.酒精灯")));
            operations.Advance(
                world,
                2d,
                new SimulationTick(1),
                events);
            Assert.That(
                session.Execute(End("命令.结束加热")).IsAccepted,
                Is.True);

            Assert.That(
                world.TryGetScalar("器材.试管.温度", out var temperature),
                Is.True);
            Assert.That(temperature.Value, Is.EqualTo(40d).Within(0.000001d));
            Assert.That(
                world.Matter.Total(
                    new EntityId("器材.试管"),
                    "产物",
                    Unit.Gram).Value,
                Is.EqualTo(1m));
            Assert.That(
                world.IsProcessActive(
                    HeatingProcessOperations.HeatingProcessId,
                    new EntityId("器材.试管")),
                Is.False);
            Assert.That(
                world.Relations,
                Has.None.Matches<EntityRelation>(
                    value => value.TypeId == ChemistryRelationTypeIds.HeatedBy));
        }

        [Test]
        public void 反应事件提交失败时温度和物质都回滚()
        {
            var world = HeatingWorld(connected: false);
            var operations = new HeatingProcessOperations(
                new[] { SimpleThermalReaction() });
            var session = Session(world, operations);
            session.Execute(Begin("命令.回滚测试开始"));

            Assert.Throws<InvalidOperationException>(() =>
                operations.Advance(
                    world,
                    2d,
                    new SimulationTick(1),
                    new ThrowingCollector()));

            Assert.That(
                world.TryGetScalar("器材.试管.温度", out var temperature),
                Is.True);
            Assert.That(temperature.Value, Is.EqualTo(20d));
            Assert.That(
                world.Matter.Total(
                    new EntityId("器材.试管"),
                    "反应物",
                    Unit.Gram).Value,
                Is.EqualTo(1m));
        }

        [Test]
        public void 停热时仍连接导管才记录倒吸风险()
        {
            var connectedWorld = HeatingWorld(connected: true);
            var connectedOperations = new HeatingProcessOperations(
                Array.Empty<ChemicalReactionDefinition>());
            var connectedSession = Session(
                connectedWorld,
                connectedOperations,
                includeReaction: false);
            connectedSession.Execute(Begin("命令.连接时开始"));
            connectedSession.Execute(End("命令.连接时停热"));

            Assert.That(
                connectedWorld.TryGetScalar(
                    "器材.试管.倒吸风险",
                    out var risk),
                Is.True);
            Assert.That(risk.Value, Is.EqualTo(1d));

            var safeWorld = HeatingWorld(connected: true);
            var safeOperations = new HeatingProcessOperations(
                Array.Empty<ChemicalReactionDefinition>());
            var safeSession = Session(
                safeWorld,
                safeOperations,
                includeReaction: false);
            safeSession.Execute(Begin("命令.安全顺序开始"));
            safeWorld.RemoveRelation(
                new EntityRelation(
                    InteractionRelationTypeIds.Connection,
                    new EntityId("器材.试管"),
                    new EntityId("器材.导管")));
            safeSession.Execute(End("命令.先移导管后停热"));

            Assert.That(
                safeWorld.TryGetScalar(
                    "器材.试管.倒吸风险",
                    out var safeRisk),
                Is.True);
            Assert.That(safeRisk.Value, Is.Zero);
        }

        [Test]
        public void 停热风险可以检测指定导管与任意对象的连接()
        {
            var world = HeatingWorld(connected: false);
            Add(world, "器材.水槽");
            world.SetRelation(new EntityRelation(
                InteractionRelationTypeIds.Connection,
                new EntityId("器材.导管"),
                new EntityId("器材.水槽")));
            var operations = new HeatingProcessOperations(
                Array.Empty<ChemicalReactionDefinition>());
            var session = ChemistryCourseRegistrations.CreateSession(
                world,
                new[]
                {
                    BeginAction(includeReaction: false),
                    EndAction(detectAnyRiskEntityConnection: true)
                },
                heatingOperations: operations);

            Assert.That(
                session.Execute(Begin("命令.任意连接开始加热")).IsAccepted,
                Is.True);
            Assert.That(
                session.Execute(End("命令.任意连接时停热")).IsAccepted,
                Is.True);
            Assert.That(
                world.TryGetScalar("器材.试管.倒吸风险", out var risk),
                Is.True);
            Assert.That(risk.Value, Is.EqualTo(1d));
        }

        private static ConfigDrivenCourseSession Session(
            ExperimentWorld world,
            HeatingProcessOperations operations,
            bool includeReaction = true)
        {
            return ChemistryCourseRegistrations.CreateSession(
                world,
                new[]
                {
                    BeginAction(includeReaction),
                    EndAction()
                },
                heatingOperations: operations);
        }

        private static ConfiguredActionDefinition BeginAction(
            bool includeReaction)
        {
            var parameters = new List<(string Key, StructuredValue Value)>
            {
                ("热功率", StructuredValue.FromNumber(100d)),
                ("效率", StructuredValue.FromNumber(1d)),
                ("热容", StructuredValue.FromNumber(10d)),
                ("散热系数", StructuredValue.FromNumber(0d)),
                ("环境温度摄氏度", StructuredValue.FromNumber(20d))
            };
            if (includeReaction)
            {
                parameters.Add(
                    ("反应标识", StructuredValue.FromText("反应.受热")));
                parameters.Add(
                    ("反应阈值摄氏度", StructuredValue.FromNumber(30d)));
                parameters.Add(
                    (HeatingProcessOperations.ReactionUnitsPerAdvanceParameter,
                        StructuredValue.FromNumber(1d)));
            }

            return ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.BeginHeating,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    Mutation(
                        "开始加热",
                        HeatingProcessOperations.BeginOperationId,
                        parameters.ToArray())
                });
        }

        private static ConfiguredActionDefinition EndAction(
            bool detectAnyRiskEntityConnection = false)
        {
            var parameters = new List<
                (string Key, StructuredValue Value)>
            {
                ("停热连接触发风险", StructuredValue.FromBoolean(true)),
                ("风险状态键后缀", StructuredValue.FromText(".倒吸风险")),
                ("风险连接实体标识", StructuredValue.FromText("器材.导管"))
            };
            if (detectAnyRiskEntityConnection)
            {
                parameters.Add((
                    "检测风险实体任意连接",
                    StructuredValue.FromBoolean(true)));
            }

            return ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.EndHeating,
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    Mutation(
                        "结束加热",
                        HeatingProcessOperations.EndOperationId,
                        parameters.ToArray())
                });
        }

        private static SemanticActionRequest Begin(string commandId)
        {
            return Request(commandId, ChemistrySemanticActionIds.BeginHeating);
        }

        private static SemanticActionRequest End(string commandId)
        {
            return Request(commandId, ChemistrySemanticActionIds.EndHeating);
        }

        private static SemanticActionRequest Request(
            string commandId,
            string actionId)
        {
            return new SemanticActionRequest(
                commandId,
                actionId,
                "学生",
                "器材.试管",
                "器材.酒精灯",
                Parameters());
        }

        private static ExperimentWorld HeatingWorld(bool connected)
        {
            var world = new ExperimentWorld(InteractionRelationSchemas.All);
            Add(world, "学生");
            Add(world, "器材.试管", new HeatableCapability());
            Add(world, "器材.酒精灯", new Heat来源对象能力(100m));
            Add(world, "器材.导管", new ConnectorCapability("导气"));
            world.SetScalar(
                "器材.试管.温度",
                20d,
                new WorldScalarUnit("摄氏度"),
                null,
                null);
            world.Matter.Add(
                new EntityId("器材.试管"),
                new SubstanceBatch(
                    "反应物",
                    new Quantity(1m, Unit.Gram),
                    MatterPhase.Solid,
                    new Temperature(20m)));
            if (connected)
            {
                world.SetRelation(
                    new EntityRelation(
                        InteractionRelationTypeIds.Connection,
                        new EntityId("器材.试管"),
                        new EntityId("器材.导管")));
            }

            return world;
        }

        private static ChemicalReactionDefinition SimpleThermalReaction()
        {
            return new ChemicalReactionDefinition(
                "反应.受热",
                new[]
                {
                    new ReactionTerm("反应物", new Quantity(1m, Unit.Gram), MatterPhase.Solid, 1m)
                },
                new[]
                {
                    new ReactionTerm("产物", new Quantity(1m, Unit.Gram), MatterPhase.Solid, 1m)
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

        private sealed class ThrowingCollector : IProcessEventCollector
        {
            public void CommitAtomically(
                string commandId,
                SimulationTick tick,
                IDomainEvent domainEvent,
                Action commitState)
            {
                throw new InvalidOperationException("模拟事件提交失败。");
            }
        }
    }
}
