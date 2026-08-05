using NUnit.Framework;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Domain
{
    public sealed class EntityRelationTests
    {
        [Test]
        public void 交互能力由交互目录集中拥有且不保留核心入口()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "可抓取",
                    "容器",
                    "可连接",
                    "可观察",
                    "可夹持",
                    "可覆盖",
                    "可破损",
                    "可定位源",
                    "可定位目标"
                },
                InteractionCapabilityIds.All);
            Assert.That(
                typeof(ICapability).Assembly.GetType(
                    "VirtualLab.Domain.Capabilities.CoreCapabilityIds"),
                Is.Null,
                "迁移后不能保留旧核心能力目录。");
        }

        [Test]
        public void Entity_exposes_registered_capability()
        {
            var tube = new ExperimentEntity(new EntityId("tube-1"));
            tube.AddCapability(new ContainerCapability(100m));

            Assert.That(tube.HasCapability<ContainerCapability>(), Is.True);
        }

        [Test]
        public void Entity_rejects_duplicate_concrete_capability_type()
        {
            var tube = new ExperimentEntity(new EntityId("tube-1"));
            tube.AddCapability(new ContainerCapability(100m));

            Assert.That(
                () => tube.AddCapability(new ContainerCapability(200m)),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Relation_graph_keeps_single_cover_for_a_container()
        {
            var graph = new RelationGraph(InteractionRelationSchemas.All);
            graph.Set(new EntityRelation(
                InteractionRelationTypeIds.Cover,
                new EntityId("stopper-1"),
                new EntityId("tube-1")));

            Assert.That(
                () => graph.Set(new EntityRelation(
                    InteractionRelationTypeIds.Cover,
                    new EntityId("stopper-2"),
                    new EntityId("tube-1"))),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Relation_graph_keeps_each_connection_endpoint_unique()
        {
            var graph = new RelationGraph(InteractionRelationSchemas.All);
            graph.Set(new EntityRelation(
                InteractionRelationTypeIds.Connection,
                new EntityId("tube-port"),
                new EntityId("hose-port")));

            Assert.That(
                () => graph.Set(new EntityRelation(
                    InteractionRelationTypeIds.Connection,
                    new EntityId("tube-port"),
                    new EntityId("water-trough-port"))),
                Throws.InvalidOperationException);
            Assert.That(
                () => graph.Set(new EntityRelation(
                    InteractionRelationTypeIds.Connection,
                    new EntityId("flask-port"),
                    new EntityId("hose-port"))),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Relation_graph_rejects_an_entity_containing_itself()
        {
            var graph = new RelationGraph(InteractionRelationSchemas.All);
            var tube = new EntityId("tube-1");

            Assert.That(
                () => graph.Set(new EntityRelation(InteractionRelationTypeIds.ContainedBy, tube, tube)),
                Throws.InvalidOperationException);
        }

        [Test]
        public void 关系图只执行注册模式而不识别业务关系名称()
        {
            var customType = new RelationTypeId("测试模块.关系.占用");
            var graph = new RelationGraph(new[]
            {
                new RelationSchema(customType, sourceUnique: true)
            });
            graph.Set(new EntityRelation(
                customType,
                new EntityId("来源"),
                new EntityId("目标一")));

            Assert.That(
                () => graph.Set(new EntityRelation(
                    customType,
                    new EntityId("来源"),
                    new EntityId("目标二"))),
                Throws.InvalidOperationException);
            Assert.That(
                () => graph.Set(new EntityRelation(
                    new RelationTypeId("未注册模块.关系.未知"),
                    new EntityId("来源二"),
                    new EntityId("目标三"))),
                Throws.InvalidOperationException);
            Assert.That(
                typeof(EntityRelation).Assembly.GetType(
                    "VirtualLab.Domain.Relations.RelationKind"),
                Is.Null,
                "关系枚举迁移后不能保留兼容入口。");
        }

        [Test]
        public void Removing_entity_from_world_removes_all_related_relations()
        {
            var world = new ExperimentWorld(InteractionRelationSchemas.All);
            var tube = new ExperimentEntity(new EntityId("tube-1"));
            var stopper = new ExperimentEntity(new EntityId("stopper-1"));
            var clamp = new ExperimentEntity(new EntityId("clamp-1"));
            world.AddEntity(tube);
            world.AddEntity(stopper);
            world.AddEntity(clamp);
            world.SetRelation(new EntityRelation(InteractionRelationTypeIds.Cover, stopper.Id, tube.Id));
            world.SetRelation(new EntityRelation(InteractionRelationTypeIds.FixedBy, tube.Id, clamp.Id));

            Assert.That(world.RemoveEntity(tube.Id), Is.True);
            Assert.That(world.Entities, Has.Count.EqualTo(2));
            Assert.That(world.Relations, Is.Empty);
        }
    }
}
