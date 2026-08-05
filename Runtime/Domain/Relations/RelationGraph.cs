using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Kernel;

namespace VirtualLab.Domain.Relations
{
    /// <summary>
    /// 按模块注册的关系模式维护关系图。图只执行通用模式约束，不识别业务关系名称。
    /// </summary>
    public sealed class RelationGraph
    {
        private readonly Dictionary<RelationKey, EntityRelation> _relations =
            new Dictionary<RelationKey, EntityRelation>();
        private readonly Dictionary<RelationTypeId, RelationSchema> _schemas =
            new Dictionary<RelationTypeId, RelationSchema>();
        private bool _schemasFrozen;

        public RelationGraph(IEnumerable<RelationSchema> schemas = null)
        {
            RegisterSchemas(schemas ?? Array.Empty<RelationSchema>());
        }

        public IReadOnlyCollection<EntityRelation> Relations =>
            new ReadOnlyCollection<EntityRelation>(_relations.Values.ToList());

        public IReadOnlyCollection<RelationSchema> Schemas =>
            new ReadOnlyCollection<RelationSchema>(_schemas.Values.ToList());

        public bool SchemasFrozen => _schemasFrozen;

        public void RegisterSchemas(IEnumerable<RelationSchema> schemas)
        {
            if (schemas == null)
            {
                throw new ArgumentNullException(nameof(schemas));
            }

            if (_schemasFrozen)
            {
                throw new InvalidOperationException("关系模式注册表已冻结，不能继续注册。");
            }

            foreach (var schema in schemas)
            {
                if (schema == null)
                {
                    throw new ArgumentException("关系模式集合不能包含空项。", nameof(schemas));
                }

                if (_schemas.TryGetValue(schema.TypeId, out var existing))
                {
                    if (!existing.Equals(schema))
                    {
                        throw new InvalidOperationException(
                            $"关系类型“{schema.TypeId}”注册了不同的模式。");
                    }

                    continue;
                }

                _schemas.Add(schema.TypeId, schema);
            }
        }

        public void FreezeSchemas() => _schemasFrozen = true;

        public RelationSchema RequireSchema(RelationTypeId typeId)
        {
            if (!_schemas.TryGetValue(typeId, out var schema))
            {
                throw new InvalidOperationException(
                    $"关系类型“{typeId}”尚未由模块注册。");
            }

            return schema;
        }

        public void Set(EntityRelation relation)
        {
            if (relation == null)
            {
                throw new ArgumentNullException(nameof(relation));
            }

            var schema = RequireSchema(relation.TypeId);
            ValidateShape(relation, schema);
            var key = RelationKey.Create(relation, schema.IsDirected);
            if (_relations.ContainsKey(key))
            {
                if (!schema.AllowDuplicateSet)
                {
                    throw new InvalidOperationException(
                        $"关系“{relation.TypeId}”不能重复设置。");
                }

                return;
            }

            EnsureCardinality(relation, schema);
            _relations.Add(key, relation);
        }

        public bool Remove(EntityRelation relation)
        {
            if (relation == null)
            {
                throw new ArgumentNullException(nameof(relation));
            }

            var schema = RequireSchema(relation.TypeId);
            ValidateShape(relation, schema);
            return _relations.Remove(RelationKey.Create(relation, schema.IsDirected));
        }

        public int RemoveAllFor(EntityId entityId)
        {
            var keys = _relations
                .Where(value => value.Value.Source == entityId
                    || value.Value.Target == entityId)
                .Select(value => value.Key)
                .ToArray();
            foreach (var key in keys)
            {
                _relations.Remove(key);
            }

            return keys.Length;
        }

        private static void ValidateShape(
            EntityRelation relation,
            RelationSchema schema)
        {
            if (!schema.AllowSelfRelation && relation.Source == relation.Target)
            {
                throw new InvalidOperationException(
                    $"关系“{schema.TypeId}”不允许来源和目标为同一实体。");
            }

            if (schema.PortPolicy == RelationPortPolicy.禁止
                && relation.HasPortEndpoints)
            {
                throw new InvalidOperationException(
                    $"关系“{schema.TypeId}”不允许声明端口。");
            }

            if (schema.PortPolicy == RelationPortPolicy.必须
                && !relation.HasPortEndpoints)
            {
                throw new InvalidOperationException(
                    $"关系“{schema.TypeId}”必须声明来源端口和目标端口。");
            }
        }

        private void EnsureCardinality(
            EntityRelation candidate,
            RelationSchema schema)
        {
            foreach (var existing in _relations.Values)
            {
                if (existing.TypeId != candidate.TypeId)
                {
                    continue;
                }

                var conflict = schema.SourceUnique
                        && existing.Source == candidate.Source
                    || schema.TargetUnique
                        && existing.Target == candidate.Target
                    || schema.EndpointUnique
                        && SharesEndpoint(existing, candidate);
                if (conflict)
                {
                    throw new InvalidOperationException(
                        $"关系“{schema.TypeId}”违反模块注册的唯一性约束。");
                }
            }
        }

        private static bool SharesEndpoint(
            EntityRelation first,
            EntityRelation second)
        {
            // 实体级和端口级端点都是可选端口关系的正式表示；只要一侧没有
            // 细分端口，就按实体占用判断，避免实体级端点绕过唯一性约束。
            if (!first.HasPortEndpoints || !second.HasPortEndpoints)
            {
                return first.Source == second.Source
                    || first.Source == second.Target
                    || first.Target == second.Source
                    || first.Target == second.Target;
            }

            return SamePort(first.Source, first.SourcePortId, second.Source, second.SourcePortId)
                || SamePort(first.Source, first.SourcePortId, second.Target, second.TargetPortId)
                || SamePort(first.Target, first.TargetPortId, second.Source, second.SourcePortId)
                || SamePort(first.Target, first.TargetPortId, second.Target, second.TargetPortId);
        }

        private static bool SamePort(
            EntityId firstEntity,
            string firstPortId,
            EntityId secondEntity,
            string secondPortId) =>
            firstEntity == secondEntity
            && string.Equals(firstPortId, secondPortId, StringComparison.Ordinal);

        private readonly struct RelationKey : IEquatable<RelationKey>
        {
            private RelationKey(
                RelationTypeId typeId,
                EntityId source,
                EntityId target,
                string sourcePortId,
                string targetPortId)
            {
                TypeId = typeId;
                Source = source;
                Target = target;
                SourcePortId = sourcePortId;
                TargetPortId = targetPortId;
            }

            private RelationTypeId TypeId { get; }
            private EntityId Source { get; }
            private EntityId Target { get; }
            private string SourcePortId { get; }
            private string TargetPortId { get; }

            public static RelationKey Create(EntityRelation relation, bool directed)
            {
                if (directed || CompareEndpoints(
                        relation.Source,
                        relation.SourcePortId,
                        relation.Target,
                        relation.TargetPortId) <= 0)
                {
                    return new RelationKey(
                        relation.TypeId,
                        relation.Source,
                        relation.Target,
                        relation.SourcePortId,
                        relation.TargetPortId);
                }

                return new RelationKey(
                    relation.TypeId,
                    relation.Target,
                    relation.Source,
                    relation.TargetPortId,
                    relation.SourcePortId);
            }

            public bool Equals(RelationKey other) =>
                TypeId == other.TypeId
                && Source == other.Source
                && Target == other.Target
                && string.Equals(SourcePortId, other.SourcePortId, StringComparison.Ordinal)
                && string.Equals(TargetPortId, other.TargetPortId, StringComparison.Ordinal);

            public override bool Equals(object obj) =>
                obj is RelationKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = TypeId.GetHashCode();
                    hashCode = (hashCode * 397) ^ Source.GetHashCode();
                    hashCode = (hashCode * 397) ^ Target.GetHashCode();
                    hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(SourcePortId ?? string.Empty);
                    hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(TargetPortId ?? string.Empty);
                    return hashCode;
                }
            }

            private static int CompareEndpoints(
                EntityId firstEntity,
                string firstPort,
                EntityId secondEntity,
                string secondPort)
            {
                var entityComparison = string.Compare(
                    firstEntity.Value,
                    secondEntity.Value,
                    StringComparison.Ordinal);
                return entityComparison != 0
                    ? entityComparison
                    : string.Compare(firstPort, secondPort, StringComparison.Ordinal);
            }
        }
    }
}
