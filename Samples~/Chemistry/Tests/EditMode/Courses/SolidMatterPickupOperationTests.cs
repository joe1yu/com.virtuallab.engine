using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Tests.Courses
{
    public sealed class SolidMatterPickupOperationTests
    {
        [Test]
        public void 药匙取固体由Chemistry转移库存并保持总量守恒()
        {
            var world = World(10m);
            var session = Session(world);

            var result = session.Execute(Request("命令.取用", 8d));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(Quantity(world, "高锰酸钾广口瓶"), Is.EqualTo(7m));
            Assert.That(Quantity(world, "药匙"), Is.EqualTo(3m));
            Assert.That(
                world.RequireMatterInventory().Total(
                    "高锰酸钾",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(10m));
            var contentChanged = (ConfiguredCourseDomainEvent)
                result.Events.Single().Event;
            Assert.That(
                contentChanged.EventType,
                Is.EqualTo(SolidMatterPickupOperations
                    .ToolContentChangedEventType));
            Assert.That(
                contentChanged.Payload[
                    ChemistryConfigurationKeys.Common.Quantity].Number,
                Is.EqualTo(3d));
        }

        [Test]
        public void 试剂瓶被覆盖时拒绝取用且库存不变()
        {
            var world = World(10m, covered: true);
            var session = Session(world);

            var result = session.Execute(Request("命令.被覆盖", 3d));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(
                result.RejectionCodes,
                Does.Contain(SolidMatterPickupRejectionCodes.ContainerCovered));
            Assert.That(Quantity(world, "高锰酸钾广口瓶"), Is.EqualTo(10m));
            Assert.That(Quantity(world, "药匙"), Is.Zero);
        }

        [Test]
        public void 同名物质不是固态时拒绝取用且不误转移()
        {
            var world = World(0m);
            world.RequireMatterInventory().Add(
                new EntityId("高锰酸钾广口瓶"),
                new SubstanceBatch(
                    "高锰酸钾",
                    new Quantity(10m, ChemistryUnits.Gram),
                    MatterPhase.Liquid,
                    new Temperature(20m)));
            var session = Session(world);

            var result = session.Execute(Request("命令.相态错误", 3d));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(
                result.RejectionCodes,
                Does.Contain(SolidMatterPickupRejectionCodes.SubstanceMissing));
            Assert.That(Quantity(world, "高锰酸钾广口瓶"), Is.EqualTo(10m));
            Assert.That(Quantity(world, "药匙"), Is.Zero);
        }

        private static ConfigDrivenCourseSession Session(ExperimentWorld world)
        {
            var action = ConfiguredActionDefinition.CreateGeneric(
                ChemistrySemanticActionIds.PickUpSolidMatter,
                "取用固体",
                SemanticActionLifecycle.Instant,
                "即时执行",
                SemanticActionPhase.Complete,
                Array.Empty<StructuredRuleDefinition>(),
                SolidMatterPickupOperations.CreateMutations(
                    "高锰酸钾",
                    3d));
            return ChemistryCourseRegistrations.CreateSession(
                world,
                new[] { action });
        }

        private static SemanticActionRequest Request(
            string commandId,
            double quantity)
        {
            return new SemanticActionRequest(
                commandId,
                ChemistrySemanticActionIds.PickUpSolidMatter,
                "操作.取用固体",
                SemanticActionPhase.Complete,
                0d,
                "学生",
                "药匙",
                "高锰酸钾广口瓶",
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    [ChemistryConfigurationKeys.Common.SubstanceId] =
                        StructuredValue.FromText("高锰酸钾"),
                    [ChemistryConfigurationKeys.Common.Quantity] =
                        StructuredValue.FromNumber(quantity)
                });
        }

        private static ExperimentWorld World(
            decimal quantity,
            bool covered = false)
        {
            var world = new ExperimentWorld(InteractionRelationSchemas.All);
            Add(world, "学生");
            Add(
                world,
                "药匙",
                new SolidMatterCarrierCapability());
            Add(
                world,
                "高锰酸钾广口瓶",
                new ContainerCapability(100m));
            Add(world, "高锰酸钾广口瓶盖");
            if (covered)
            {
                world.SetRelation(new EntityRelation(
                    InteractionRelationTypeIds.Cover,
                    new EntityId("高锰酸钾广口瓶盖"),
                    new EntityId("高锰酸钾广口瓶")));
            }

            world.RequireMatterInventory().Add(
                new EntityId("高锰酸钾广口瓶"),
                new SubstanceBatch(
                    "高锰酸钾",
                    new Quantity(quantity, ChemistryUnits.Gram),
                    MatterPhase.Solid,
                    new Temperature(20m)));
            return world;
        }

        private static decimal Quantity(
            ExperimentWorld world,
            string entityId) =>
            world.RequireMatterInventory().Total(
                new EntityId(entityId),
                "高锰酸钾",
                ChemistryUnits.Gram).Value;

        private static void Add(
            ExperimentWorld world,
            string entityId,
            params ICapability[] capabilities)
        {
            var entity = new ExperimentEntity(new EntityId(entityId));
            foreach (var capability in capabilities)
            {
                entity.AddCapability(capability);
            }

            world.AddEntity(entity);
        }
    }
}
