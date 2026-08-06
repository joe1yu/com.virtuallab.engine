using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Domain.WorldStates;
using VirtualLab.Kernel;

namespace VirtualLab.Teaching.Courses
{
    /// <summary>
    /// 教学模块拥有的世界状态类型。
    /// </summary>
    public static class TeachingWorldStateTypeIds
    {
        public static readonly WorldStateTypeId CourseMilestones =
            new WorldStateTypeId("教学.课程里程碑");
    }

    /// <summary>
    /// 按实体保存已经发生的课程里程碑。
    /// 里程碑只记录无法由当前权威世界状态持续推导的历史事实，记录后不可撤销。
    /// </summary>
    public sealed class TeachingMilestoneCollection : IWorldStateExtension
    {
        private readonly Func<EntityId, bool> _entityExists;
        private Dictionary<EntityId, HashSet<string>> _milestones =
            new Dictionary<EntityId, HashSet<string>>();

        internal TeachingMilestoneCollection(Func<EntityId, bool> entityExists)
        {
            _entityExists = entityExists
                ?? throw new ArgumentNullException(nameof(entityExists));
        }

        public IReadOnlyList<string> MilestonesOf(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return Array.Empty<string>();
            }

            return _milestones.TryGetValue(
                    new EntityId(entityId.Trim()),
                    out var milestones)
                ? milestones.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
        }

        public bool Contains(string entityId, string milestoneId) =>
            !string.IsNullOrWhiteSpace(entityId)
            && !string.IsNullOrWhiteSpace(milestoneId)
            && _milestones.TryGetValue(
                new EntityId(entityId.Trim()),
                out var milestones)
            && milestones.Contains(milestoneId.Trim());

        public void Record(string entityId, string milestoneId)
        {
            var normalizedEntityId = RequiredEntityId(entityId);
            var normalizedMilestoneId = RequiredMilestoneId(milestoneId);
            EnsureEntityExists(normalizedEntityId);
            if (!_milestones.TryGetValue(normalizedEntityId, out var milestones))
            {
                milestones = new HashSet<string>(StringComparer.Ordinal);
                _milestones.Add(normalizedEntityId, milestones);
            }

            milestones.Add(normalizedMilestoneId);
        }

        internal IReadOnlyList<KeyValuePair<EntityId, string>> Entries =>
            _milestones
                .SelectMany(pair => pair.Value.Select(milestone =>
                    new KeyValuePair<EntityId, string>(pair.Key, milestone)))
                .OrderBy(value => value.Key.Value, StringComparer.Ordinal)
                .ThenBy(value => value.Value, StringComparer.Ordinal)
                .ToArray();

        internal void ReplaceContents(
            IEnumerable<KeyValuePair<EntityId, string>> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var replacement = new Dictionary<EntityId, HashSet<string>>();
            foreach (var entry in entries)
            {
                var entityId = RequiredEntityId(entry.Key.Value);
                var milestoneId = RequiredMilestoneId(entry.Value);
                EnsureEntityExists(entityId);
                if (!replacement.TryGetValue(entityId, out var milestones))
                {
                    milestones = new HashSet<string>(StringComparer.Ordinal);
                    replacement.Add(entityId, milestones);
                }

                if (!milestones.Add(milestoneId))
                {
                    throw new InvalidOperationException(
                        $"实体“{entityId}”的课程里程碑“{milestoneId}”重复。");
                }
            }

            _milestones = replacement;
        }

        WorldStateTypeId IWorldStateExtension.TypeId =>
            TeachingWorldStateTypeIds.CourseMilestones;

        IWorldStateExtension IWorldStateExtension.CreateCopy(
            Func<EntityId, bool> entityExists)
        {
            var copy = new TeachingMilestoneCollection(entityExists);
            copy._milestones = CopyMilestones(_milestones);
            return copy;
        }

        void IWorldStateExtension.ValidateReplacement(IWorldStateExtension source)
        {
            if (!(source is TeachingMilestoneCollection))
            {
                throw new InvalidOperationException(
                    "课程里程碑只能从相同类型的世界状态恢复。");
            }
        }

        void IWorldStateExtension.ReplaceStateFrom(IWorldStateExtension source)
        {
            _milestones = CopyMilestones(
                ((TeachingMilestoneCollection)source)._milestones);
        }

        void IWorldStateExtension.RemoveEntityReferences(EntityId entityId)
        {
            _milestones.Remove(entityId);
        }

        private static Dictionary<EntityId, HashSet<string>> CopyMilestones(
            IReadOnlyDictionary<EntityId, HashSet<string>> source)
        {
            return source.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<string>(pair.Value, StringComparer.Ordinal));
        }

        private static EntityId RequiredEntityId(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                throw new ArgumentException(
                    "课程里程碑实体标识不能为空。",
                    nameof(entityId));
            }

            return new EntityId(entityId.Trim());
        }

        private static string RequiredMilestoneId(string milestoneId)
        {
            if (string.IsNullOrWhiteSpace(milestoneId))
            {
                throw new ArgumentException(
                    "课程里程碑名称不能为空。",
                    nameof(milestoneId));
            }

            return milestoneId.Trim();
        }

        private void EnsureEntityExists(EntityId entityId)
        {
            if (!_entityExists(entityId))
            {
                throw new InvalidOperationException(
                    $"课程里程碑引用的实体“{entityId}”不存在。");
            }
        }
    }
}
