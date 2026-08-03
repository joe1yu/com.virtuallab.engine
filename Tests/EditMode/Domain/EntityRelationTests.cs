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
            var graph = new RelationGraph();
            graph.Set(new EntityRelation(
                RelationKind.覆盖对象,
                new EntityId("stopper-1"),
                new EntityId("tube-1")));

            Assert.That(
                () => graph.Set(new EntityRelation(
                    RelationKind.覆盖对象,
                    new EntityId("stopper-2"),
                    new EntityId("tube-1"))),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Relation_graph_keeps_each_connection_endpoint_unique()
        {
            var graph = new RelationGraph();
            graph.Set(new EntityRelation(
                RelationKind.连接对象,
                new EntityId("tube-port"),
                new EntityId("hose-port")));

            Assert.That(
                () => graph.Set(new EntityRelation(
                    RelationKind.连接对象,
                    new EntityId("tube-port"),
                    new EntityId("water-trough-port"))),
                Throws.InvalidOperationException);
            Assert.That(
                () => graph.Set(new EntityRelation(
                    RelationKind.连接对象,
                    new EntityId("flask-port"),
                    new EntityId("hose-port"))),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Relation_graph_rejects_an_entity_containing_itself()
        {
            var graph = new RelationGraph();
            var tube = new EntityId("tube-1");

            Assert.That(
                () => graph.Set(new EntityRelation(RelationKind.位于容器内, tube, tube)),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Removing_entity_from_world_removes_all_related_relations()
        {
            var world = new ExperimentWorld();
            var tube = new ExperimentEntity(new EntityId("tube-1"));
            var stopper = new ExperimentEntity(new EntityId("stopper-1"));
            var clamp = new ExperimentEntity(new EntityId("clamp-1"));
            world.AddEntity(tube);
            world.AddEntity(stopper);
            world.AddEntity(clamp);
            world.SetRelation(new EntityRelation(RelationKind.覆盖对象, stopper.Id, tube.Id));
            world.SetRelation(new EntityRelation(RelationKind.固定对象, tube.Id, clamp.Id));

            Assert.That(world.RemoveEntity(tube.Id), Is.True);
            Assert.That(world.Entities, Has.Count.EqualTo(2));
            Assert.That(world.Relations, Is.Empty);
        }
    }
}
