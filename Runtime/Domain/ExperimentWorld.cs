using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Domain.WorldStates;
using VirtualLab.Kernel;

namespace VirtualLab.Domain
{
    public readonly struct WorldScalarUnit : IEquatable<WorldScalarUnit>
    {
        public WorldScalarUnit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A scalar unit ID cannot be blank.",
                    nameof(id));
            }

            Id = id.Trim();
        }

        public string Id { get; }

        public bool Equals(WorldScalarUnit other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is WorldScalarUnit other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id == null ? 0 : StringComparer.Ordinal.GetHashCode(Id);
        }
    }

    public readonly struct WorldScalarValue
    {
        public WorldScalarValue(double value, WorldScalarUnit unit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A world scalar value must be finite.");
            }

            Value = value;
            Unit = unit;
        }

        public double Value { get; }

        public WorldScalarUnit Unit { get; }
    }

    public sealed class ExperimentWorld
    {
        private readonly Dictionary<EntityId, ExperimentEntity> _entities =
            new Dictionary<EntityId, ExperimentEntity>();
        private readonly RelationGraph _relationGraph;
        private readonly Dictionary<string, WorldScalarValue> _scalars =
            new Dictionary<string, WorldScalarValue>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorldProcessState> _activeProcesses =
            new Dictionary<string, WorldProcessState>(StringComparer.Ordinal);
        private readonly Dictionary<WorldStateTypeId, IWorldStateExtension>
            _stateExtensions =
                new Dictionary<WorldStateTypeId, IWorldStateExtension>();
        private bool _worldStateTypesFrozen;
        private bool _transactionActive;

        public ExperimentWorld(IEnumerable<RelationSchema> relationSchemas = null)
        {
            _relationGraph = new RelationGraph(relationSchemas);
        }

        public IReadOnlyCollection<WorldStateTypeId> WorldStateTypes =>
            new ReadOnlyCollection<WorldStateTypeId>(
                _stateExtensions.Keys
                    .OrderBy(value => value.Value, StringComparer.Ordinal)
                    .ToArray());

        public bool WorldStateTypesFrozen => _worldStateTypesFrozen;

        public IReadOnlyCollection<ExperimentEntity> Entities =>
            new ReadOnlyCollection<ExperimentEntity>(
                new List<ExperimentEntity>(_entities.Values));

        public IReadOnlyCollection<EntityRelation> Relations =>
            _relationGraph.Relations;

        public IReadOnlyCollection<RelationSchema> RelationSchemas =>
            _relationGraph.Schemas;

        public bool RelationSchemasFrozen => _relationGraph.SchemasFrozen;

        /// <summary>
        /// 在会话启动前安装模块关系模式。相同模式可重复安装，不同模式直接拒绝。
        /// </summary>
        public void RegisterRelationSchemas(IEnumerable<RelationSchema> schemas) =>
            _relationGraph.RegisterSchemas(schemas);

        public void FreezeRelationSchemas() => _relationGraph.FreezeSchemas();

        public RelationSchema RequireRelationSchema(RelationTypeId typeId) =>
            _relationGraph.RequireSchema(typeId);

        public IReadOnlyCollection<WorldProcessState> ActiveProcesses =>
            new ReadOnlyCollection<WorldProcessState>(
                new List<WorldProcessState>(_activeProcesses.Values));

        public IReadOnlyDictionary<string, WorldScalarValue> Scalars =>
            new ReadOnlyDictionary<string, WorldScalarValue>(
                new Dictionary<string, WorldScalarValue>(
                    _scalars,
                    StringComparer.Ordinal));

        public void AddEntity(ExperimentEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (_entities.ContainsKey(entity.Id))
            {
                throw new InvalidOperationException(
                    "An entity with the same ID is already registered in this world.");
            }

            _entities.Add(entity.Id, entity);
        }

        /// <summary>
        /// 安装一个由模块拥有的世界状态。相同状态类型不能重复注册。
        /// </summary>
        public void RegisterWorldState(IWorldStateExtension state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (_transactionActive)
            {
                throw new InvalidOperationException("世界事务执行期间不能注册新的状态类型。");
            }

            if (_worldStateTypesFrozen)
            {
                throw new InvalidOperationException("世界状态类型注册表已冻结。");
            }

            var typeId = state.TypeId;
            if (string.IsNullOrWhiteSpace(typeId.Value))
            {
                throw new ArgumentException("世界状态类型标识不能为空。", nameof(state));
            }

            if (!_stateExtensions.TryAdd(typeId, state))
            {
                throw new InvalidOperationException(
                    $"世界状态类型“{typeId}”已经注册。");
            }
        }

        /// <summary>
        /// 冻结已安装的状态类型。之后仍可修改状态内容，但不能改变世界的模块组成。
        /// </summary>
        public void FreezeWorldStateTypes()
        {
            _worldStateTypesFrozen = true;
        }

        public bool TryGetWorldState<TState>(
            WorldStateTypeId typeId,
            out TState state)
            where TState : class, IWorldStateExtension
        {
            if (_stateExtensions.TryGetValue(typeId, out var registered)
                && registered is TState typed)
            {
                state = typed;
                return true;
            }

            state = null;
            return false;
        }

        public TState RequireWorldState<TState>(WorldStateTypeId typeId)
            where TState : class, IWorldStateExtension
        {
            if (!_stateExtensions.TryGetValue(typeId, out var registered))
            {
                throw new InvalidOperationException(
                    $"世界尚未安装状态类型“{typeId}”。");
            }

            if (!(registered is TState typed))
            {
                throw new InvalidOperationException(
                    $"世界状态“{typeId}”不是请求的类型“{typeof(TState).FullName}”。");
            }

            return typed;
        }

        public bool RemoveEntity(EntityId entityId)
        {
            if (!_entities.ContainsKey(entityId))
            {
                return false;
            }

            // 先在全部独立副本上完成清理和校验，任一模块失败都不会改变当前世界。
            var preparedStates = CopyWorldStates(ContainsEntity);
            foreach (var state in preparedStates.Values)
            {
                state.RemoveEntityReferences(entityId);
            }

            ValidateWorldStateReplacements(preparedStates);
            _entities.Remove(entityId);
            _relationGraph.RemoveAllFor(entityId);
            ReplaceWorldStatesFrom(preparedStates);
            return true;
        }

        public bool ContainsEntity(EntityId entityId)
        {
            return _entities.ContainsKey(entityId);
        }

        public bool TryGetEntity(
            EntityId entityId,
            out ExperimentEntity entity)
        {
            return _entities.TryGetValue(entityId, out entity);
        }

        public void SetRelation(EntityRelation relation)
        {
            if (relation == null)
            {
                throw new ArgumentNullException(nameof(relation));
            }

            if (!_entities.ContainsKey(relation.Source)
                || !_entities.ContainsKey(relation.Target))
            {
                throw new InvalidOperationException(
                    "Both entities in a relation must be registered in the world.");
            }

            _relationGraph.Set(relation);
        }

        public bool RemoveRelation(EntityRelation relation)
        {
            return _relationGraph.Remove(relation);
        }

        public void SetScalar(
            string stateKey,
            double value,
            WorldScalarUnit unit,
            double? minimum,
            double? maximum)
        {
            var key = RequireText(stateKey, "scalar state key");
            ValidateBounds(value, minimum, maximum);
            EnsureUnitStable(key, unit);
            _scalars[key] = new WorldScalarValue(value, unit);
        }

        public void AddScalar(
            string stateKey,
            double delta,
            WorldScalarUnit unit,
            double? minimum,
            double? maximum)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delta),
                    delta,
                    "A scalar delta must be finite.");
            }

            var key = RequireText(stateKey, "scalar state key");
            EnsureUnitStable(key, unit);
            var current = _scalars.TryGetValue(key, out var existing)
                ? existing.Value
                : 0d;
            var next = current + delta;
            ValidateBounds(next, minimum, maximum);
            _scalars[key] = new WorldScalarValue(next, unit);
        }

        public bool TryGetScalar(
            string stateKey,
            out WorldScalarValue value)
        {
            return _scalars.TryGetValue(
                RequireText(stateKey, "scalar state key"),
                out value);
        }

        public void StartProcess(string processId, EntityId entityId)
        {
            StartProcess(
                processId,
                entityId,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, double>(StringComparer.Ordinal));
        }

        public void StartProcess(
            string processId,
            EntityId entityId,
            IEnumerable<KeyValuePair<string, string>> textParameters,
            IEnumerable<KeyValuePair<string, double>> numberParameters)
        {
            if (!ContainsEntity(entityId))
            {
                throw new InvalidOperationException(
                    "A process entity must exist in the world.");
            }

            var state = new WorldProcessState(
                processId,
                entityId,
                textParameters,
                numberParameters);
            if (!_activeProcesses.TryAdd(
                ProcessKey(processId, entityId),
                state))
            {
                throw new InvalidOperationException(
                    "The configured process is already active.");
            }
        }

        public void StopProcess(string processId, EntityId entityId)
        {
            if (!_activeProcesses.Remove(ProcessKey(processId, entityId)))
            {
                throw new InvalidOperationException(
                    "The configured process is not active.");
            }
        }

        public bool IsProcessActive(string processId, EntityId entityId)
        {
            return _activeProcesses.ContainsKey(ProcessKey(processId, entityId));
        }

        public bool TryGetProcess(
            string processId,
            EntityId entityId,
            out WorldProcessState state)
        {
            return _activeProcesses.TryGetValue(
                ProcessKey(processId, entityId),
                out state);
        }

        /// <summary>
        /// 在临时世界中执行变更，全部成功后才提交关系、模块状态和过程状态。
        /// </summary>
        public void CommitAtomically(Action<ExperimentWorld> prepare)
        {
            if (prepare == null)
            {
                throw new ArgumentNullException(nameof(prepare));
            }

            if (_transactionActive)
            {
                throw new InvalidOperationException(
                    "World transactions cannot be nested.");
            }

            _transactionActive = true;
            try
            {
                var prepared = CreateTransactionalCopy();
                prepare(prepared);
                EnsureEntityTopologyUnchanged(prepared);
                EnsureWorldStateTopologyUnchanged(prepared);
                ValidateWorldStateReplacements(prepared._stateExtensions);
                ReplaceRelationsFrom(prepared);
                ReplaceWorldStatesFrom(prepared._stateExtensions);
                ReplaceConfiguredStateFrom(prepared);
            }
            finally
            {
                _transactionActive = false;
            }
        }

        /// <summary>
        /// 创建仅供应用层事务恢复使用的世界检查点。能力定义不可变，检查点
        /// 复制关系、模块状态、标量和持续过程，不携带 Unity 表现状态。
        /// </summary>
        public ExperimentWorld CreateCheckpoint() => CreateTransactionalCopy();

        /// <summary>
        /// 恢复由当前世界创建的检查点。实体拓扑必须保持一致，避免用恢复机制
        /// 绕过课程实体生命周期约束。
        /// </summary>
        public void RestoreCheckpoint(ExperimentWorld checkpoint)
        {
            if (checkpoint == null)
            {
                throw new ArgumentNullException(nameof(checkpoint));
            }

            EnsureEntityTopologyUnchanged(checkpoint);
            EnsureWorldStateTopologyUnchanged(checkpoint);
            ValidateWorldStateReplacements(checkpoint._stateExtensions);
            ReplaceRelationsFrom(checkpoint);
            ReplaceWorldStatesFrom(checkpoint._stateExtensions);
            ReplaceConfiguredStateFrom(checkpoint);
        }

        private ExperimentWorld CreateTransactionalCopy()
        {
            var copy = new ExperimentWorld(RelationSchemas);
            if (RelationSchemasFrozen)
            {
                copy.FreezeRelationSchemas();
            }
            foreach (var entity in _entities.Values)
            {
                var entityCopy = new ExperimentEntity(entity.Id);
                foreach (var capability in entity.Capabilities)
                {
                    // 能力对象均为不可变定义，可以在事务副本间共享。
                    entityCopy.AddCapability(capability);
                }

                copy.AddEntity(entityCopy);
            }

            foreach (var state in _stateExtensions.Values)
            {
                copy.RegisterWorldState(state.CreateCopy(copy.ContainsEntity));
            }

            if (WorldStateTypesFrozen)
            {
                copy.FreezeWorldStateTypes();
            }

            foreach (var relation in Relations)
            {
                copy.SetRelation(
                    new EntityRelation(
                        relation.TypeId,
                        relation.Source,
                        relation.Target,
                        relation.SourcePortId,
                        relation.TargetPortId));
            }

            foreach (var scalar in _scalars)
            {
                copy._scalars.Add(scalar.Key, scalar.Value);
            }

            foreach (var process in _activeProcesses)
            {
                copy._activeProcesses.Add(process.Key, process.Value);
            }

            return copy;
        }

        private Dictionary<WorldStateTypeId, IWorldStateExtension>
            CopyWorldStates(Func<EntityId, bool> entityExists)
        {
            return _stateExtensions.ToDictionary(
                value => value.Key,
                value => value.Value.CreateCopy(entityExists));
        }

        private void EnsureEntityTopologyUnchanged(ExperimentWorld prepared)
        {
            if (_entities.Count != prepared._entities.Count
                || _entities.Keys.Any(
                    entityId => !prepared._entities.ContainsKey(entityId)))
            {
                throw new InvalidOperationException(
                    "Configured state operations cannot add or remove entities.");
            }
        }

        private void EnsureWorldStateTopologyUnchanged(ExperimentWorld prepared)
        {
            if (_stateExtensions.Count != prepared._stateExtensions.Count
                || _stateExtensions.Keys.Any(
                    typeId => !prepared._stateExtensions.ContainsKey(typeId)))
            {
                throw new InvalidOperationException(
                    "世界状态事务不能安装或移除状态类型。");
            }
        }

        private void ValidateWorldStateReplacements(
            IReadOnlyDictionary<WorldStateTypeId, IWorldStateExtension> source)
        {
            foreach (var current in _stateExtensions)
            {
                if (!source.TryGetValue(current.Key, out var replacement))
                {
                    throw new InvalidOperationException(
                        $"替换状态中缺少世界状态类型“{current.Key}”。");
                }

                current.Value.ValidateReplacement(replacement);
            }
        }

        private void ReplaceWorldStatesFrom(
            IReadOnlyDictionary<WorldStateTypeId, IWorldStateExtension> source)
        {
            foreach (var current in _stateExtensions)
            {
                current.Value.ReplaceStateFrom(source[current.Key]);
            }
        }

        private void ReplaceRelationsFrom(ExperimentWorld prepared)
        {
            foreach (var relation in Relations)
            {
                _relationGraph.Remove(relation);
            }

            foreach (var relation in prepared.Relations)
            {
                _relationGraph.Set(
                    new EntityRelation(
                        relation.TypeId,
                        relation.Source,
                        relation.Target,
                        relation.SourcePortId,
                        relation.TargetPortId));
            }
        }

        private void ReplaceConfiguredStateFrom(ExperimentWorld prepared)
        {
            _scalars.Clear();
            foreach (var scalar in prepared._scalars)
            {
                _scalars.Add(scalar.Key, scalar.Value);
            }

            _activeProcesses.Clear();
            foreach (var process in prepared._activeProcesses)
            {
                _activeProcesses.Add(process.Key, process.Value);
            }
        }

        private void EnsureUnitStable(
            string stateKey,
            WorldScalarUnit unit)
        {
            if (string.IsNullOrWhiteSpace(unit.Id))
            {
                throw new ArgumentException(
                    "A scalar unit ID cannot be blank.",
                    nameof(unit));
            }

            if (_scalars.TryGetValue(stateKey, out var existing)
                && !existing.Unit.Equals(unit))
            {
                throw new InvalidOperationException(
                    $"Scalar state '{stateKey}' cannot change units.");
            }
        }

        private static void ValidateBounds(
            double value,
            double? minimum,
            double? maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A scalar value must be finite.");
            }

            if ((minimum.HasValue && !IsFinite(minimum.Value))
                || (maximum.HasValue && !IsFinite(maximum.Value)))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimum),
                    "Scalar bounds must be finite.");
            }

            if (minimum.HasValue
                && maximum.HasValue
                && minimum.Value > maximum.Value)
            {
                throw new ArgumentException(
                    "A scalar minimum cannot exceed its maximum.");
            }

            if ((minimum.HasValue && value < minimum.Value)
                || (maximum.HasValue && value > maximum.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "A scalar value is outside its configured bounds.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string ProcessKey(
            string processId,
            EntityId entityId)
        {
            return RequireText(processId, "process ID")
                + "\u001f"
                + entityId.Value;
        }

        private static string RequireText(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    $"A {context} cannot be blank.",
                    nameof(value));
            }

            return value.Trim();
        }
    }
}
