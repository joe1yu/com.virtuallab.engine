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
        public static readonly WorldStateTypeId NamedStates =
            new WorldStateTypeId("教学.状态.命名状态");
    }

    /// <summary>
    /// 按实体保存可并存的命名教学状态。状态名称由课程表达具体含义，
    /// 集合本身只维护生命周期、复制和实体引用完整性。
    /// </summary>
    public sealed class TeachingStateCollection : IWorldStateExtension
    {
        private readonly Func<EntityId, bool> _entityExists;
        private Dictionary<EntityId, HashSet<string>> _states =
            new Dictionary<EntityId, HashSet<string>>();

        internal TeachingStateCollection(Func<EntityId, bool> entityExists)
        {
            _entityExists = entityExists
                ?? throw new ArgumentNullException(nameof(entityExists));
        }

        public IReadOnlyList<string> StatesOf(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return Array.Empty<string>();
            }

            return _states.TryGetValue(new EntityId(entityId.Trim()), out var states)
                ? states.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
        }

        public bool Contains(string entityId, string stateId) =>
            !string.IsNullOrWhiteSpace(entityId)
            && !string.IsNullOrWhiteSpace(stateId)
            && _states.TryGetValue(new EntityId(entityId.Trim()), out var states)
            && states.Contains(stateId.Trim());

        public void Add(string entityId, string stateId)
        {
            var normalizedEntityId = RequiredEntityId(entityId);
            var normalizedStateId = RequiredStateId(stateId);
            EnsureEntityExists(normalizedEntityId);
            if (!_states.TryGetValue(normalizedEntityId, out var states))
            {
                states = new HashSet<string>(StringComparer.Ordinal);
                _states.Add(normalizedEntityId, states);
            }

            states.Add(normalizedStateId);
        }

        public void Remove(string entityId, string stateId)
        {
            var normalizedEntityId = RequiredEntityId(entityId);
            var normalizedStateId = RequiredStateId(stateId);
            EnsureEntityExists(normalizedEntityId);
            if (!_states.TryGetValue(normalizedEntityId, out var states))
            {
                return;
            }

            states.Remove(normalizedStateId);
            if (states.Count == 0)
            {
                _states.Remove(normalizedEntityId);
            }
        }

        internal IReadOnlyList<KeyValuePair<EntityId, string>> Entries =>
            _states
                .SelectMany(pair => pair.Value.Select(state =>
                    new KeyValuePair<EntityId, string>(pair.Key, state)))
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
                var stateId = RequiredStateId(entry.Value);
                EnsureEntityExists(entityId);
                if (!replacement.TryGetValue(entityId, out var states))
                {
                    states = new HashSet<string>(StringComparer.Ordinal);
                    replacement.Add(entityId, states);
                }

                if (!states.Add(stateId))
                {
                    throw new InvalidOperationException(
                        $"实体“{entityId}”的教学状态“{stateId}”重复。");
                }
            }

            _states = replacement;
        }

        WorldStateTypeId IWorldStateExtension.TypeId =>
            TeachingWorldStateTypeIds.NamedStates;

        IWorldStateExtension IWorldStateExtension.CreateCopy(
            Func<EntityId, bool> entityExists)
        {
            var copy = new TeachingStateCollection(entityExists);
            copy._states = CopyStates(_states);
            return copy;
        }

        void IWorldStateExtension.ValidateReplacement(IWorldStateExtension source)
        {
            if (!(source is TeachingStateCollection))
            {
                throw new InvalidOperationException(
                    "命名教学状态只能从相同类型的世界状态恢复。");
            }
        }

        void IWorldStateExtension.ReplaceStateFrom(IWorldStateExtension source)
        {
            _states = CopyStates(((TeachingStateCollection)source)._states);
        }

        void IWorldStateExtension.RemoveEntityReferences(EntityId entityId)
        {
            _states.Remove(entityId);
        }

        private static Dictionary<EntityId, HashSet<string>> CopyStates(
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
                throw new ArgumentException("教学状态实体标识不能为空。", nameof(entityId));
            }

            return new EntityId(entityId.Trim());
        }

        private static string RequiredStateId(string stateId)
        {
            if (string.IsNullOrWhiteSpace(stateId))
            {
                throw new ArgumentException("教学状态名称不能为空。", nameof(stateId));
            }

            return stateId.Trim();
        }

        private void EnsureEntityExists(EntityId entityId)
        {
            if (!_entityExists(entityId))
            {
                throw new InvalidOperationException(
                    $"教学状态引用的实体“{entityId}”不存在。");
            }
        }
    }
}
