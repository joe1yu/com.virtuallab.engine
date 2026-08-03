using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Kernel;

namespace VirtualLab.Domain.Relations
{
    public sealed class RelationGraph
    {
        private readonly Dictionary<RelationKey, EntityRelation> _relations = new Dictionary<RelationKey, EntityRelation>();

        public IReadOnlyCollection<EntityRelation> Relations
        {
            get
            {
                return new ReadOnlyCollection<EntityRelation>(new List<EntityRelation>(_relations.Values));
            }
        }

        public void Set(EntityRelation relation)
        {
            if (relation == null)
            {
                throw new ArgumentNullException(nameof(relation));
            }

            var key = new RelationKey(
                relation.Kind,
                relation.Source,
                relation.Target,
                relation.SourcePortId,
                relation.TargetPortId);
            EnsureRelationIsValid(relation, key);
            _relations[key] = relation;
        }

        public bool Remove(EntityRelation relation)
        {
            if (relation == null)
            {
                throw new ArgumentNullException(nameof(relation));
            }

            return _relations.Remove(new RelationKey(
                relation.Kind,
                relation.Source,
                relation.Target,
                relation.SourcePortId,
                relation.TargetPortId));
        }

        public int RemoveAllFor(EntityId entityId)
        {
            var keysToRemove = new List<RelationKey>();
            foreach (var pair in _relations)
            {
                if (pair.Value.Source == entityId || pair.Value.Target == entityId)
                {
                    keysToRemove.Add(pair.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _relations.Remove(key);
            }

            return keysToRemove.Count;
        }

        private void EnsureRelationIsValid(EntityRelation relation, RelationKey key)
        {
            if (relation.Kind == RelationKind.位于容器内 && relation.Source == relation.Target)
            {
                throw new InvalidOperationException("An entity cannot contain itself.");
            }

            foreach (var pair in _relations)
            {
                if (pair.Key.Equals(key))
                {
                    continue;
                }

                var existing = pair.Value;
                if (relation.Kind == RelationKind.覆盖对象 &&
                    existing.Kind == RelationKind.覆盖对象 &&
                    existing.Target == relation.Target)
                {
                    throw new InvalidOperationException("A target entity can only have one covering relation.");
                }

                if (relation.Kind == RelationKind.连接对象 &&
                    existing.Kind == RelationKind.连接对象 &&
                    SharesConnectionEndpoint(existing, relation))
                {
                    throw new InvalidOperationException("A connection endpoint can only participate in one connection relation.");
                }
            }
        }

        private static bool SharesConnectionEndpoint(EntityRelation first, EntityRelation second)
        {
            if (first.HasPortEndpoints && second.HasPortEndpoints)
            {
                return SamePort(
                           first.Source,
                           first.SourcePortId,
                           second.Source,
                           second.SourcePortId)
                       || SamePort(
                           first.Source,
                           first.SourcePortId,
                           second.Target,
                           second.TargetPortId)
                       || SamePort(
                           first.Target,
                           first.TargetPortId,
                           second.Source,
                           second.SourcePortId)
                       || SamePort(
                           first.Target,
                           first.TargetPortId,
                           second.Target,
                           second.TargetPortId);
            }

            // 旧存档或旧调用没有端口身份时，继续按实体端点执行保守唯一性约束。
            return first.Source == second.Source ||
                   first.Source == second.Target ||
                   first.Target == second.Source ||
                   first.Target == second.Target;
        }

        private static bool SamePort(
            EntityId firstEntity,
            string firstPortId,
            EntityId secondEntity,
            string secondPortId) =>
            firstEntity == secondEntity
            && string.Equals(
                firstPortId,
                secondPortId,
                StringComparison.Ordinal);

        private readonly struct RelationKey : IEquatable<RelationKey>
        {
            public RelationKey(
                RelationKind kind,
                EntityId source,
                EntityId target,
                string sourcePortId,
                string targetPortId)
            {
                Kind = kind;
                Source = source;
                Target = target;
                SourcePortId = sourcePortId;
                TargetPortId = targetPortId;
            }

            private RelationKind Kind { get; }

            private EntityId Source { get; }

            private EntityId Target { get; }

            private string SourcePortId { get; }

            private string TargetPortId { get; }

            public bool Equals(RelationKey other)
            {
                return Kind == other.Kind
                       && Source == other.Source
                       && Target == other.Target
                       && string.Equals(
                           SourcePortId,
                           other.SourcePortId,
                           StringComparison.Ordinal)
                       && string.Equals(
                           TargetPortId,
                           other.TargetPortId,
                           StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is RelationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (int)Kind;
                    hashCode = (hashCode * 397) ^ Source.GetHashCode();
                    hashCode = (hashCode * 397) ^ Target.GetHashCode();
                    hashCode = (hashCode * 397) ^
                               StringComparer.Ordinal.GetHashCode(
                                   SourcePortId ?? string.Empty);
                    hashCode = (hashCode * 397) ^
                               StringComparer.Ordinal.GetHashCode(
                                   TargetPortId ?? string.Empty);
                    return hashCode;
                }
            }
        }
    }
}
