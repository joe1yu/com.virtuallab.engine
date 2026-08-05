using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Domain;
using VirtualLab.Domain.WorldStates;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 一个模块状态快照条目。条目类型和字段均由拥有该状态的模块解释。
    /// </summary>
    public sealed class CourseWorldStateEntry
    {
        public CourseWorldStateEntry(
            string entryType,
            IEnumerable<KeyValuePair<string, string>> values)
        {
            EntryType = CourseContractGuard.Required(entryType, "模块状态条目类型");
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var copy = values.Select(value => new KeyValuePair<string, string>(
                    CourseContractGuard.Required(value.Key, "模块状态字段名"),
                    value.Value ?? throw new ArgumentException(
                        "模块状态字段值不能为 null。",
                        nameof(values))))
                .ToArray();
            if (copy.GroupBy(value => value.Key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
            {
                throw new ArgumentException(
                    "同一模块状态条目不能包含重复字段。",
                    nameof(values));
            }

            Values = new ReadOnlyDictionary<string, string>(
                copy.ToDictionary(
                    value => value.Key,
                    value => value.Value,
                    StringComparer.Ordinal));
        }

        public string EntryType { get; }

        public IReadOnlyDictionary<string, string> Values { get; }
    }

    /// <summary>
    /// 一个世界状态扩展的纯数据快照。Application 只保存结构，不理解模块字段。
    /// </summary>
    public sealed class CourseWorldState
    {
        public CourseWorldState(
            WorldStateTypeId typeId,
            IEnumerable<CourseWorldStateEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(typeId.Value))
            {
                throw new ArgumentException("模块状态类型标识不能为空。", nameof(typeId));
            }

            TypeId = typeId;
            Entries = (entries ?? throw new ArgumentNullException(nameof(entries)))
                .Select(entry => entry ?? throw new ArgumentException(
                    "模块状态快照不能包含空条目。",
                    nameof(entries)))
                .ToArray();
        }

        public WorldStateTypeId TypeId { get; }

        public IReadOnlyList<CourseWorldStateEntry> Entries { get; }
    }

    /// <summary>
    /// 模块负责在自己的权威世界状态与通用课程快照之间转换。
    /// </summary>
    public interface ICourseWorldStateCodec
    {
        WorldStateTypeId TypeId { get; }

        CourseWorldState Capture(ExperimentWorld world);

        void Restore(ExperimentWorld world, CourseWorldState state);
    }

    /// <summary>
    /// 会话级世界状态编解码器集合。存档必须与已安装的权威状态类型完全一致，
    /// 不在运行时猜测缺失模块或保留旧结构兼容。
    /// </summary>
    public sealed class CourseWorldStateCodecRegistry
    {
        private readonly IReadOnlyDictionary<WorldStateTypeId, ICourseWorldStateCodec>
            _codecs;

        public CourseWorldStateCodecRegistry(
            IEnumerable<ICourseWorldStateCodec> codecs = null)
        {
            var values = (codecs ?? Array.Empty<ICourseWorldStateCodec>())
                .ToArray();
            if (values.Any(value => value == null
                || string.IsNullOrWhiteSpace(value.TypeId.Value)))
            {
                throw new ArgumentException("世界状态编解码器不能包含空项或空标识。", nameof(codecs));
            }

            var duplicate = values
                .GroupBy(value => value.TypeId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException(
                    $"世界状态“{duplicate.Key}”的编解码器重复。",
                    nameof(codecs));
            }

            _codecs = new ReadOnlyDictionary<WorldStateTypeId, ICourseWorldStateCodec>(
                values.ToDictionary(value => value.TypeId));
        }

        public IReadOnlyList<CourseWorldState> Capture(ExperimentWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            EnsureCodecCoverage(world.WorldStateTypes);
            return world.WorldStateTypes
                .OrderBy(value => value.Value, StringComparer.Ordinal)
                .Select(typeId =>
                {
                    var state = _codecs[typeId].Capture(world)
                        ?? throw new InvalidOperationException(
                            $"世界状态“{typeId}”的编解码器返回了空快照。");
                    if (state.TypeId != typeId)
                    {
                        throw new InvalidOperationException(
                            $"世界状态编解码器“{typeId}”返回了类型“{state.TypeId}”。");
                    }

                    return state;
                })
                .ToArray();
        }

        public void Restore(
            ExperimentWorld world,
            IEnumerable<CourseWorldState> states)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var snapshots = (states ?? throw new ArgumentNullException(nameof(states)))
                .Select(state => state ?? throw new ArgumentException(
                    "世界状态快照不能包含空项。",
                    nameof(states)))
                .ToArray();
            var duplicate = snapshots
                .GroupBy(value => value.TypeId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidOperationException(
                    $"存档中的世界状态“{duplicate.Key}”重复。");
            }

            EnsureCodecCoverage(world.WorldStateTypes);
            var byType = snapshots.ToDictionary(value => value.TypeId);
            var installed = world.WorldStateTypes.ToHashSet();
            if (byType.Count != installed.Count
                || byType.Keys.Any(typeId => !installed.Contains(typeId)))
            {
                throw new InvalidOperationException(
                    "存档中的世界状态类型与当前课程安装的模块状态不一致。");
            }

            foreach (var typeId in installed.OrderBy(
                value => value.Value,
                StringComparer.Ordinal))
            {
                _codecs[typeId].Restore(world, byType[typeId]);
            }
        }

        private void EnsureCodecCoverage(
            IEnumerable<WorldStateTypeId> installedTypes)
        {
            var installed = installedTypes.ToHashSet();
            if (_codecs.Count != installed.Count
                || installed.Any(typeId => !_codecs.ContainsKey(typeId)))
            {
                throw new InvalidOperationException(
                    "已安装的权威世界状态没有一一对应的课程存档编解码器。");
            }
        }
    }
}
