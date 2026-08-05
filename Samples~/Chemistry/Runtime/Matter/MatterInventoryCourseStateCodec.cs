using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.WorldStates;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Matter
{
    /// <summary>
    /// 物质库存课程快照协议的字段集中定义，避免存档 Key 散落在业务实现中。
    /// </summary>
    public static class MatterInventorySnapshotKeys
    {
        public const string UnitEntry = "物质计量单位";
        public const string BatchEntry = "物质批次";
        public const string SubstanceId = "物质标识";
        public const string LocationId = "位置实体标识";
        public const string Value = "数值";
        public const string Unit = "单位";
        public const string Phase = "物态";
        public const string TemperatureCelsius = "温度摄氏度";
    }

    /// <summary>
    /// 化学模块拥有的物质库存存档编解码器。
    /// </summary>
    public sealed class MatterInventoryCourseStateCodec :
        ICourseWorldStateCodec
    {
        public WorldStateTypeId TypeId => MatterWorldStateTypeIds.Inventory;

        public CourseWorldState Capture(ExperimentWorld world)
        {
            var inventory = world.RequireMatterInventory();
            var entries = new List<CourseWorldStateEntry>();
            entries.AddRange(inventory.KnownUnits
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => Entry(
                    MatterInventorySnapshotKeys.UnitEntry,
                    (MatterInventorySnapshotKeys.SubstanceId, value.Key),
                    (MatterInventorySnapshotKeys.Unit, value.Value.ToString()))));
            entries.AddRange(inventory.Entries.Select(value => Entry(
                MatterInventorySnapshotKeys.BatchEntry,
                (MatterInventorySnapshotKeys.LocationId, value.LocationId.Value),
                (MatterInventorySnapshotKeys.SubstanceId, value.Batch.SubstanceId),
                (MatterInventorySnapshotKeys.Value,
                    value.Batch.Quantity.Value.ToString(CultureInfo.InvariantCulture)),
                (MatterInventorySnapshotKeys.Unit,
                    value.Batch.Quantity.Unit.ToString()),
                (MatterInventorySnapshotKeys.Phase, value.Batch.Phase.ToString()),
                (MatterInventorySnapshotKeys.TemperatureCelsius,
                    value.Batch.Temperature.Celsius.ToString(
                        CultureInfo.InvariantCulture)))));
            return new CourseWorldState(TypeId, entries);
        }

        public void Restore(ExperimentWorld world, CourseWorldState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.TypeId != TypeId)
            {
                throw new InvalidOperationException(
                    $"物质库存不能从世界状态“{state.TypeId}”恢复。");
            }

            var units = new List<KeyValuePair<string, Unit>>();
            var batches = new List<MatterInventoryEntry>();
            foreach (var entry in state.Entries)
            {
                switch (entry.EntryType)
                {
                    case MatterInventorySnapshotKeys.UnitEntry:
                        EnsureFields(entry, 2);
                        units.Add(new KeyValuePair<string, Unit>(
                            Read(entry, MatterInventorySnapshotKeys.SubstanceId),
                            ParseUnit(entry)));
                        break;
                    case MatterInventorySnapshotKeys.BatchEntry:
                        EnsureFields(entry, 6);
                        batches.Add(new MatterInventoryEntry(
                            new EntityId(Read(
                                entry,
                                MatterInventorySnapshotKeys.LocationId)),
                            new SubstanceBatch(
                                Read(entry, MatterInventorySnapshotKeys.SubstanceId),
                                new Quantity(
                                    ParseDecimal(
                                        entry,
                                        MatterInventorySnapshotKeys.Value),
                                    ParseUnit(entry)),
                                ParseEnum<MatterPhase>(
                                    entry,
                                    MatterInventorySnapshotKeys.Phase),
                                new Temperature(ParseDecimal(
                                    entry,
                                    MatterInventorySnapshotKeys
                                        .TemperatureCelsius)))));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"物质库存包含未知快照条目“{entry.EntryType}”。");
                }
            }

            world.RequireMatterInventory().ReplaceContents(units, batches);
        }

        private static CourseWorldStateEntry Entry(
            string entryType,
            params (string Key, string Value)[] values) =>
            new CourseWorldStateEntry(
                entryType,
                values.Select(value =>
                    new KeyValuePair<string, string>(value.Key, value.Value)));

        private static void EnsureFields(
            CourseWorldStateEntry entry,
            int expectedCount)
        {
            if (entry.Values.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"物质库存条目“{entry.EntryType}”的字段数量不正确。");
            }
        }

        private static string Read(
            CourseWorldStateEntry entry,
            string key)
        {
            if (!entry.Values.TryGetValue(key, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"物质库存条目“{entry.EntryType}”缺少字段“{key}”。");
            }

            return value;
        }

        private static decimal ParseDecimal(
            CourseWorldStateEntry entry,
            string key)
        {
            var text = Read(entry, key);
            if (!decimal.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value))
            {
                throw new InvalidOperationException(
                    $"物质库存字段“{key}”不是有效十进制数。");
            }

            return value;
        }

        private static Unit ParseUnit(CourseWorldStateEntry entry)
        {
            try
            {
                return new Unit(Read(entry, MatterInventorySnapshotKeys.Unit));
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException(
                    "物质库存字段“单位”不是有效计量单位标识。");
            }
        }

        private static TEnum ParseEnum<TEnum>(
            CourseWorldStateEntry entry,
            string key)
            where TEnum : struct
        {
            var text = Read(entry, key);
            if (!Enum.TryParse(text, false, out TEnum value)
                || !Enum.IsDefined(typeof(TEnum), value))
            {
                throw new InvalidOperationException(
                    $"物质库存字段“{key}”不是有效枚举值。");
            }

            return value;
        }
    }
}
