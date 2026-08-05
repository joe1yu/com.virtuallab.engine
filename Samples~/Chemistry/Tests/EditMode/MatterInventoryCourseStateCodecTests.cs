using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Tests
{
    public sealed class MatterInventoryCourseStateCodecTests
    {
        [Test]
        public void 化学课程存档往返保持物质库存精度()
        {
            const decimal quantity = 0.1234567890123456789012345678m;
            var world = WorldWithTube();
            world.RequireMatterInventory().Add(
                new EntityId("器材.试管"),
                new SubstanceBatch(
                    "水",
                    new Quantity(quantity, Unit.Millilitre),
                    MatterPhase.Liquid,
                    new Temperature(23.456789m)));
            var runtime = Runtime();
            var state = runtime.CreateSession(world).ExportState();

            var restored = ConfigDrivenCourseSession.Restore(runtime, state);

            Assert.That(restored.ExportState(), Is.EqualTo(state));
            Assert.That(
                state.WorldStates.Single().TypeId,
                Is.EqualTo(MatterWorldStateTypeIds.Inventory));
            Assert.That(
                state.WorldStates.Single().Entries
                    .Single(entry => entry.EntryType
                        == MatterInventorySnapshotKeys.BatchEntry)
                    .Values[MatterInventorySnapshotKeys.Value],
                Is.EqualTo(quantity.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Test]
        public void 化学课程恢复拒绝物质库存中的未知实体引用()
        {
            var world = WorldWithTube();
            world.RequireMatterInventory().Add(
                new EntityId("器材.试管"),
                new SubstanceBatch(
                    "水",
                    new Quantity(10m, Unit.Millilitre),
                    MatterPhase.Liquid,
                    new Temperature(25m)));
            var runtime = Runtime();
            var state = runtime.CreateSession(world).ExportState();
            var snapshot = state.WorldStates.Single();
            var invalidEntries = snapshot.Entries.Select(entry =>
            {
                if (entry.EntryType != MatterInventorySnapshotKeys.BatchEntry)
                {
                    return entry;
                }

                var values = entry.Values.ToDictionary(
                    value => value.Key,
                    value => value.Value,
                    StringComparer.Ordinal);
                values[MatterInventorySnapshotKeys.LocationId] = "器材.不存在";
                return new CourseWorldStateEntry(entry.EntryType, values);
            });
            var invalid = state.WithWorldStates(new[]
            {
                new CourseWorldState(snapshot.TypeId, invalidEntries)
            });

            var error = Assert.Throws<CourseStateRestoreException>(() =>
                ConfigDrivenCourseSession.Restore(runtime, invalid));

            Assert.That(error.Code, Is.EqualTo("session.state.invalid"));
        }

        private static ExperimentWorld WorldWithTube()
        {
            var world = new ExperimentWorld();
            world.AddEntity(new ExperimentEntity(new EntityId("器材.试管")));
            return world;
        }

        private static CourseRuntimeDefinition Runtime() =>
            ChemistryCourseRegistrations.CreateRuntimeDefinition(
                Array.Empty<ConfiguredActionDefinition>(),
                Array.Empty<CourseActionAssessmentDefinition>(),
                0);
    }
}
