using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Actions;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Interaction.Courses;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;
using VirtualLab.Spatial.Courses;
using VirtualLab.Teaching.Courses;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class InteractionSemanticActionTests
    {
        [Test]
        public void 交互动作由交互模块集中拥有且不保留核心入口()
        {
            Assert.That(
                typeof(InteractionSemanticActionIds).Assembly.GetName().Name,
                Is.EqualTo("VirtualLab.Interaction"),
                "交互动作协议必须由独立交互程序集拥有。");
            Assert.That(InteractionSemanticActionIds.Grab, Is.EqualTo("抓取"));
            Assert.That(InteractionSemanticActionIds.Release, Is.EqualTo("释放"));
            Assert.That(InteractionSemanticActionIds.Place, Is.EqualTo("放置"));
            Assert.That(InteractionSemanticActionIds.Take, Is.EqualTo("拿出"));
            Assert.That(InteractionSemanticActionIds.Position, Is.EqualTo("定位"));
            Assert.That(InteractionSemanticActionIds.Cover, Is.EqualTo("覆盖"));
            Assert.That(InteractionSemanticActionIds.Uncover, Is.EqualTo("揭开"));
            Assert.That(InteractionSemanticActionIds.Connect, Is.EqualTo("连接"));
            Assert.That(InteractionSemanticActionIds.Disconnect, Is.EqualTo("断开"));
            Assert.That(InteractionSemanticActionIds.Observe, Is.EqualTo("观察"));
            Assert.That(
                InteractionSemanticActionIds.All.Distinct().Count(),
                Is.EqualTo(InteractionSemanticActionIds.All.Count),
                "交互动作标识不能重复。");
            Assert.That(
                typeof(InteractionSemanticActionIds).Assembly.GetType(
                    "VirtualLab.Application.Courses.CoreSemanticActionIds"),
                Is.Null,
                "迁移后不能保留旧核心动作目录。");
        }

        [Test]
        public void 抓取由通用能力和持有状态裁决()
        {
            var world = WorldWith(
                Entity("学生"),
                Entity("器材.桌面"),
                Entity("器材.试管", new GrabbableCapability()));
            world.SetScalar(
                "器材.试管.温度",
                25d,
                new WorldScalarUnit("摄氏度"),
                null,
                null);
            var session = Session(world, GrabAction());

            var accepted = session.Execute(
                Request("命令.抓取", InteractionSemanticActionIds.Grab, "器材.试管", "器材.桌面"));

            Assert.That(accepted.IsAccepted, Is.True);
            Assert.That(
                world.Relations.Single().Target,
                Is.EqualTo(new EntityId("学生")));

            var alreadyHeld = session.Execute(
                Request("命令.重复抓取", InteractionSemanticActionIds.Grab, "器材.试管", "学生"));
            Assert.That(alreadyHeld.IsAccepted, Is.False);
            Assert.That(alreadyHeld.RejectionCodes, Does.Contain("器材已被持有"));
            Assert.That(world.Relations, Has.Count.EqualTo(1));

            var missingWorld = WorldWith(Entity("学生"));
            var missingResult = Session(missingWorld, GrabAction()).Execute(
                Request("命令.抓不存在器材", InteractionSemanticActionIds.Grab, "器材.不存在", "学生"));
            Assert.That(missingResult.RejectionCodes, Does.Contain("来源实体不存在"));
            Assert.That(missingWorld.Relations, Is.Empty);
        }

        [Test]
        public void 释放只移除当前主体的权威持有关系()
        {
            var world = WorldWith(
                Entity("学生"),
                Entity("另一学生"),
                Entity("器材.试管", new GrabbableCapability()),
                Entity("器材.烧杯", new GrabbableCapability()));
            world.SetRelation(Relation(InteractionRelationTypeIds.HeldBy, "器材.试管", "学生"));
            world.SetRelation(Relation(InteractionRelationTypeIds.HeldBy, "器材.烧杯", "另一学生"));
            var session = Session(world, ReleaseAction());

            var rejected = session.Execute(
                Request(
                    "命令.他人释放",
                    InteractionSemanticActionIds.Release,
                    "器材.试管",
                    null,
                    "另一学生"));
            Assert.That(rejected.IsAccepted, Is.False);

            var accepted = session.Execute(
                Request("命令.释放", InteractionSemanticActionIds.Release, "器材.试管", null));

            Assert.That(accepted.IsAccepted, Is.True);
            Assert.That(
                world.Relations,
                Has.None.Matches<EntityRelation>(
                    value => value.Source == new EntityId("器材.试管")));
            Assert.That(
                world.Relations,
                Has.Some.Matches<EntityRelation>(
                    value => value.Source == new EntityId("器材.烧杯")));
        }

        [Test]
        public void 连接返回全部失败原因且断开只影响指定端口()
        {
            var world = WorldWith(
                Entity("学生"),
                Entity("端口.来源", new ConnectorCapability("导气")),
                Entity("端口.错误目标", new ConnectorCapability("排水")),
                Entity("端口.已连接", new ConnectorCapability("导气")),
                Entity("端口.另一来源", new ConnectorCapability("导气")),
                Entity("端口.另一目标", new ConnectorCapability("导气")),
                Entity("端口.空闲来源", new ConnectorCapability("导气")),
                Entity("端口.兼容目标", new ConnectorCapability("导气")));
            world.SetRelation(
                Relation(
                    InteractionRelationTypeIds.Connection,
                    "端口.来源",
                    "端口.已连接"));
            world.SetRelation(
                Relation(
                    InteractionRelationTypeIds.Connection,
                    "端口.另一来源",
                    "端口.另一目标"));
            var session = Session(world, ConnectAction(), DisconnectAction());

            var rejected = session.Execute(
                Request(
                    "命令.错误连接",
                    InteractionSemanticActionIds.Connect,
                    "端口.来源",
                    "端口.错误目标",
                    (SpatialRequestParameterKeys.DistanceMeters,
                        StructuredValue.FromNumber(0.5d))));

            Assert.That(rejected.IsAccepted, Is.False);
            Assert.That(
                rejected.RejectionCodes,
                Is.EqualTo(new[] { "来源端口已占用", "端口不兼容", "连接距离过远" }));
            Assert.That(world.Relations, Has.Count.EqualTo(2));

            var disconnected = session.Execute(
                Request(
                    "命令.断开指定端口",
                    InteractionSemanticActionIds.Disconnect,
                    "端口.来源",
                    "端口.已连接"));
            Assert.That(disconnected.IsAccepted, Is.True);
            Assert.That(world.Relations, Has.Count.EqualTo(1));
            Assert.That(
                world.Relations.Single().Source,
                Is.EqualTo(new EntityId("端口.另一来源")));

            var connected = session.Execute(
                Request(
                    "命令.正确连接",
                    InteractionSemanticActionIds.Connect,
                    "端口.空闲来源",
                    "端口.兼容目标",
                    (SpatialRequestParameterKeys.DistanceMeters,
                        StructuredValue.FromNumber(0.05d))));
            Assert.That(connected.IsAccepted, Is.True);
            Assert.That(world.Relations, Has.Count.EqualTo(2));

            var occupiedTarget = session.Execute(
                Request(
                    "命令.目标已占用",
                    InteractionSemanticActionIds.Connect,
                    "端口.来源",
                    "端口.兼容目标",
                    (SpatialRequestParameterKeys.DistanceMeters,
                        StructuredValue.FromNumber(0.05d))));
            Assert.That(
                occupiedTarget.RejectionCodes,
                Is.EqualTo(new[] { "目标端口已占用" }));
        }

        [Test]
        public void 同一实体的不同端口可以同时参与独立连接()
        {
            var world = WorldWith(
                Entity("学生"),
                Entity(
                    "单孔橡皮塞",
                    new ConnectorCapability(new[]
                    {
                        new ConnectionPortDefinition(
                            "单孔橡皮塞.出口",
                            "试管"),
                        new ConnectionPortDefinition(
                            "单孔橡皮塞.入口",
                            "橡皮塞")
                    })),
                Entity(
                    "大试管",
                    new ConnectorCapability(new[]
                    {
                        new ConnectionPortDefinition(
                            "大试管.入口",
                            "试管")
                    })),
                Entity(
                    "导气管",
                    new ConnectorCapability(new[]
                    {
                        new ConnectionPortDefinition(
                            "导气管.出口",
                            "橡皮塞")
                    })));
            var session = Session(world, ConnectAction(), DisconnectAction());

            var stopperToTube = session.Execute(Request(
                "命令.橡皮塞连接试管",
                InteractionSemanticActionIds.Connect,
                "单孔橡皮塞",
                "大试管",
                (SpatialRequestParameterKeys.DistanceMeters,
                    StructuredValue.FromNumber(0.01d))));
            var pipeToStopper = session.Execute(Request(
                "命令.导气管连接橡皮塞",
                InteractionSemanticActionIds.Connect,
                "导气管",
                "单孔橡皮塞",
                (SpatialRequestParameterKeys.DistanceMeters,
                    StructuredValue.FromNumber(0.01d))));

            Assert.That(stopperToTube.IsAccepted, Is.True);
            Assert.That(pipeToStopper.IsAccepted, Is.True);
            Assert.That(world.Relations, Has.Count.EqualTo(2));
            Assert.That(
                world.Relations.Select(value => value.SourcePortId),
                Does.Contain("单孔橡皮塞.出口")
                    .And.Contain("导气管.出口"));
            Assert.That(
                world.Relations.Select(value => value.TargetPortId),
                Does.Contain("大试管.入口")
                    .And.Contain("单孔橡皮塞.入口"));
        }

        [Test]
        public void 放置与覆盖建立显式关系而不根据视觉接近推断()
        {
            var world = WorldWith(
                Entity("学生"),
                Entity("器材.试管"),
                Entity("器材.试管架"),
                Entity("器材.瓶塞"),
                Entity("器材.集气瓶"));
            var session = Session(world, PlaceAction(), CoverAction());

            var placed = session.Execute(
                Request(
                    "命令.放置",
                    InteractionSemanticActionIds.Place,
                    "器材.试管架",
                    "器材.试管",
                    (SpatialRequestParameterKeys.IsContacting,
                        StructuredValue.FromBoolean(true))));
            var covered = session.Execute(
                Request(
                    "命令.覆盖",
                    InteractionSemanticActionIds.Cover,
                    "器材.瓶塞",
                    "器材.集气瓶",
                    (SpatialRequestParameterKeys.IsContacting,
                        StructuredValue.FromBoolean(true))));

            Assert.That(placed.IsAccepted, Is.True);
            Assert.That(covered.IsAccepted, Is.True);
            Assert.That(
                world.Relations,
                Has.Some.Matches<EntityRelation>(
                    value => value.TypeId == InteractionRelationTypeIds.ContainedBy
                        && value.Source == new EntityId("器材.试管架")
                        && value.Target == new EntityId("器材.试管")));
            Assert.That(
                world.Relations,
                Has.Some.Matches<EntityRelation>(
                    value => value.TypeId == InteractionRelationTypeIds.Cover
                        && value.Source == new EntityId("器材.瓶塞")
                        && value.Target == new EntityId("器材.集气瓶")));
        }

        [Test]
        public void 目标持有和目标教学状态事实读取动作目标而不是来源()
        {
            var world = WorldWith(
                Entity("学生"),
                Entity("来源"),
                Entity("目标", new GrabbableCapability()));
            world.SetRelation(Relation(
                InteractionRelationTypeIds.HeldBy,
                "目标",
                "学生"));
            var modules = CourseRuntimeModuleScope.Create(
                new CoreCourseRuntimeModule(),
                new InteractionCourseRuntimeModule(),
                new TeachingCourseRuntimeModule());
            modules.PrepareWorld(world);
            world.RequireTeachingStates().Add("目标", "已准备");
            var context = new StructuredRuleContext(
                Request("命令.检查目标事实", "test.inspect", "来源", "目标"),
                world);
            var readers = modules.FactReaders;

            Assert.That(
                readers.Single(value =>
                        value.Field == InteractionStructuredFactFields.目标对象已被操作者拿起)
                    .Read(context).Boolean,
                Is.True);
            Assert.That(
                readers.Single(value =>
                        value.Field == TeachingStructuredFactFields.目标对象教学状态)
                    .Read(context).TextList,
                Is.EqualTo(new[] { "已准备" }));
        }

        [Test]
        public void 连接和固定事实直接读取权威关系()
        {
            var world = WorldWith(
                Entity("镊子"),
                Entity("棉花团"),
                Entity("铁架台试管夹"),
                Entity("大试管"));
            world.SetRelation(Relation(
                InteractionRelationTypeIds.Connection,
                "镊子",
                "棉花团"));
            world.SetRelation(Relation(
                InteractionRelationTypeIds.FixedBy,
                "铁架台试管夹",
                "大试管"));
            var readers = InteractionCourseRegistrations.CreateFactReaders();

            var connected = readers.Single(value => value.Field ==
                InteractionStructuredFactFields.来源对象连接对象);
            Assert.That(
                connected.Read(new StructuredRuleContext(
                    Request(
                        "命令.检查夹持",
                        "检查",
                        "棉花团",
                        null),
                    world)).TextList,
                Is.EqualTo(new[] { "镊子" }),
                "连接是无向关系，从任一端都应读取另一端。");

            var fixedObjects = readers.Single(value => value.Field ==
                InteractionStructuredFactFields.来源对象固定对象);
            Assert.That(
                fixedObjects.Read(new StructuredRuleContext(
                    Request(
                        "命令.检查固定",
                        "检查",
                        "铁架台试管夹",
                        null),
                    world)).TextList,
                Is.EqualTo(new[] { "大试管" }));
        }

        [Test]
        public void 同一容器可以容纳多个对象但同一对象不能同时位于两个容器()
        {
            var world = WorldWith(
                Entity("学生"),
                Entity("集气瓶一"),
                Entity("集气瓶二"),
                Entity("水槽"),
                Entity("实验台"));
            var session = Session(world, PlaceAction());

            var first = session.Execute(Request(
                "命令.放置第一只集气瓶",
                InteractionSemanticActionIds.Place,
                "集气瓶一",
                "水槽",
                (SpatialRequestParameterKeys.IsContacting,
                    StructuredValue.FromBoolean(true))));
            var second = session.Execute(Request(
                "命令.放置第二只集气瓶",
                InteractionSemanticActionIds.Place,
                "集气瓶二",
                "水槽",
                (SpatialRequestParameterKeys.IsContacting,
                    StructuredValue.FromBoolean(true))));
            var duplicateLocation = session.Execute(Request(
                "命令.重复放置第一只集气瓶",
                InteractionSemanticActionIds.Place,
                "集气瓶一",
                "实验台",
                (SpatialRequestParameterKeys.IsContacting,
                    StructuredValue.FromBoolean(true))));

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second.IsAccepted, Is.True);
            Assert.That(duplicateLocation.IsAccepted, Is.False);
            Assert.That(
                world.Relations.Count(value =>
                    value.TypeId == InteractionRelationTypeIds.ContainedBy
                    && value.Target == new EntityId("水槽")),
                Is.EqualTo(2));
        }

        private static ConfigDrivenCourseSession Session(
            ExperimentWorld world,
            params ConfiguredActionDefinition[] actions)
        {
            return new CourseRuntimeDefinition(
                    CourseRuntimeModuleScope.Create(
                        new CoreCourseRuntimeModule(),
                        new InteractionCourseRuntimeModule(),
                        new SpatialCourseRuntimeModule()),
                    actions,
                    Array.Empty<CourseActionAssessmentDefinition>(),
                    0)
                .CreateSession(world);
        }

        private static ConfiguredActionDefinition GrabAction()
        {
            return Action(
                InteractionSemanticActionIds.Grab,
                new[]
                {
                    Rule(10, CoreStructuredFactFields.来源对象存在, StructuredRuleOperator.等于, StructuredValue.FromBoolean(true), "来源实体不存在"),
                    Rule(20, CoreStructuredFactFields.来源对象能力, StructuredRuleOperator.包含, StructuredValue.FromText("可抓取"), "器材不可抓取"),
                    Rule(30, InteractionStructuredFactFields.来源对象持有者, StructuredRuleOperator.为空, StructuredValue.Null(), "器材已被持有")
                },
                Mutation(
                    "建立持有关系",
                    ConfiguredStateOperationIds.RelationSet,
                    ("关系类型", StructuredValue.FromText("交互.关系.持有")),
                    ("目标实体引用", StructuredValue.FromText("操作者"))));
        }

        private static ConfiguredActionDefinition ReleaseAction()
        {
            return Action(
                InteractionSemanticActionIds.Release,
                new[]
                {
                    Rule(10, InteractionStructuredFactFields.来源对象已被操作者拿起, StructuredRuleOperator.等于, StructuredValue.FromBoolean(true), "器材不是当前主体持有")
                },
                Mutation(
                    "移除持有关系",
                    ConfiguredStateOperationIds.RelationRemove,
                    ("关系类型", StructuredValue.FromText("交互.关系.持有")),
                    ("目标实体引用", StructuredValue.FromText("操作者"))));
        }

        private static ConfiguredActionDefinition ConnectAction()
        {
            return Action(
                InteractionSemanticActionIds.Connect,
                new[]
                {
                    Rule(10, InteractionStructuredFactFields.来源连接点占用状态, StructuredRuleOperator.为空, StructuredValue.Null(), "来源端口已占用"),
                    Rule(20, InteractionStructuredFactFields.目标连接点占用状态, StructuredRuleOperator.为空, StructuredValue.Null(), "目标端口已占用"),
                    Rule(30, InteractionStructuredFactFields.连接标签相匹配, StructuredRuleOperator.等于, StructuredValue.FromBoolean(true), "端口不兼容"),
                    Rule(40, SpatialStructuredFactFields.对象间距离, StructuredRuleOperator.小于等于, StructuredValue.FromNumber(0.1d), "连接距离过远")
                },
                Mutation("建立端口连接", ConfiguredStateOperationIds.RelationSet, ("关系类型", StructuredValue.FromText("交互.关系.连接"))));
        }

        private static ConfiguredActionDefinition DisconnectAction()
        {
            return Action(
                InteractionSemanticActionIds.Disconnect,
                Array.Empty<StructuredRuleDefinition>(),
                Mutation("移除端口连接", ConfiguredStateOperationIds.RelationRemove, ("关系类型", StructuredValue.FromText("交互.关系.连接"))));
        }

        private static ConfiguredActionDefinition PlaceAction()
        {
            return Action(
                InteractionSemanticActionIds.Place,
                new[]
                {
                    Rule(10, SpatialStructuredFactFields.对象正在接触, StructuredRuleOperator.等于, StructuredValue.FromBoolean(true), "未接触放置位置")
                },
                Mutation("建立放置关系", ConfiguredStateOperationIds.RelationSet, ("关系类型", StructuredValue.FromText("交互.关系.位于容器内"))));
        }

        private static ConfiguredActionDefinition CoverAction()
        {
            return Action(
                InteractionSemanticActionIds.Cover,
                new[]
                {
                    Rule(10, SpatialStructuredFactFields.对象正在接触, StructuredRuleOperator.等于, StructuredValue.FromBoolean(true), "未接触覆盖位置")
                },
                Mutation("建立覆盖关系", ConfiguredStateOperationIds.RelationSet, ("关系类型", StructuredValue.FromText("交互.关系.覆盖"))));
        }

        private static ConfiguredActionDefinition Action(
            string actionId,
            IEnumerable<StructuredRuleDefinition> rules,
            params ConfiguredMutationDefinition[] mutations)
        {
            return ConfiguredActionDefinition.CreateGeneric(
                actionId,
                rules,
                mutations);
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

        private static SemanticActionRequest Request(
            string commandId,
            string actionId,
            string source,
            string target,
            string actor,
            params (string Key, StructuredValue Value)[] parameters)
        {
            return new SemanticActionRequest(
                commandId,
                actionId,
                actor,
                source,
                target,
                Parameters(parameters));
        }

        private static SemanticActionRequest Request(
            string commandId,
            string actionId,
            string source,
            string target,
            params (string Key, StructuredValue Value)[] parameters)
        {
            return Request(
                commandId,
                actionId,
                source,
                target,
                "学生",
                parameters);
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

        private static ExperimentEntity Entity(
            string id,
            params ICapability[] capabilities)
        {
            var entity = new ExperimentEntity(new EntityId(id));
            foreach (var capability in capabilities)
            {
                entity.AddCapability(capability);
            }

            return entity;
        }

        private static ExperimentWorld WorldWith(params ExperimentEntity[] entities)
        {
            var world = new ExperimentWorld(InteractionRelationSchemas.All);
            foreach (var entity in entities)
            {
                world.AddEntity(entity);
            }

            return world;
        }

        private static EntityRelation Relation(
            RelationTypeId kind,
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
