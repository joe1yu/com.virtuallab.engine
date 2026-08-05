using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Events;
using VirtualLab.Domain;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 配置表可引用的通用状态操作协议。协议实现统一在注册表中维护。
    /// </summary>
    public static class ConfiguredStateOperationIds
    {
        public const string RelationSet = "设置关系";
        public const string RelationRemove = "移除关系";
        public const string ScalarSet = "设置标量";
        public const string ScalarAdd = "增加标量";
        public const string ProcessStart = "开始过程";
        public const string ProcessStop = "停止过程";
        public const string EventEmit = "发布事件";
        public const string EventEmitWhenScalar = "条件发布事件";

        public static readonly IReadOnlyList<string> All = new[]
        {
            RelationSet,
            RelationRemove,
            ScalarSet,
            ScalarAdd,
            ProcessStart,
            ProcessStop,
            EventEmit,
            EventEmitWhenScalar
        };
    }

    /// <summary>
    /// 一次配置化状态变化及其类型化参数。
    /// </summary>
    public sealed class ConfiguredMutationDefinition
    {
        public ConfiguredMutationDefinition(
            string mutationId,
            string operationId,
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters)
        {
            MutationId = CourseContractGuard.Required(
                mutationId,
                "状态变更 ID");
            OperationId = CourseContractGuard.Required(
                operationId,
                $"状态变更“{MutationId}”的操作 ID");
            Parameters = CopyParameters(parameters);
        }

        public string MutationId { get; }

        public string OperationId { get; }

        public IReadOnlyDictionary<string, StructuredValue> Parameters { get; }

        private IReadOnlyDictionary<string, StructuredValue> CopyParameters(
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(
                    nameof(parameters),
                    $"状态变更“{MutationId}”的参数集合不能为空。");
            }

            var copy = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var pair in parameters)
            {
                var key = CourseContractGuard.Required(
                    pair.Key,
                    $"状态变更“{MutationId}”的参数名");
                if (pair.Value == null || !copy.TryAdd(key, pair.Value))
                {
                    throw new ArgumentException(
                        $"状态变更“{MutationId}”的参数“{key}”为空或重复。",
                        nameof(parameters));
                }
            }

            return new ReadOnlyDictionary<string, StructuredValue>(copy);
        }
    }

    /// <summary>
    /// 配置状态操作的执行协议；学科包可以注册新实现而无需修改课程会话。
    /// </summary>
    public interface IConfiguredStateOperation
    {
        string OperationId { get; }

        void Apply(
            SemanticActionRequest request,
            ExperimentWorld world,
            ConfiguredMutationDefinition mutation);
    }

    /// <summary>
    /// 为配置操作异常补充状态变化与操作协议上下文，便于定位作者配置。
    /// </summary>
    public sealed class ConfiguredStateOperationException : Exception
    {
        public ConfiguredStateOperationException(
            string mutationId,
            string operationId,
            Exception innerException)
            : base(
                $"状态变更“{mutationId}”执行操作“{operationId}”失败。",
                innerException)
        {
            MutationId = mutationId;
            OperationId = operationId;
        }

        public string MutationId { get; }

        public string OperationId { get; }
    }

    /// <summary>
    /// 类型化状态操作发现领域前置条件不满足时，携带可直接返回给调用方的原因。
    /// </summary>
    public sealed class ConfiguredOperationRejectedException : Exception
    {
        public ConfiguredOperationRejectedException(
            IEnumerable<string> rejectionCodes)
            : base("配置状态操作拒绝执行。")
        {
            if (rejectionCodes == null)
            {
                throw new ArgumentNullException(nameof(rejectionCodes));
            }

            var copy = rejectionCodes
                .Select(value => CourseContractGuard.Required(
                    value,
                    "状态操作拒绝原因"))
                .ToArray();
            if (copy.Length == 0)
            {
                throw new ArgumentException(
                    "状态操作拒绝原因不能为空。",
                    nameof(rejectionCodes));
            }

            RejectionCodes = new ReadOnlyCollection<string>(copy);
        }

        public IReadOnlyList<string> RejectionCodes { get; }
    }

    /// <summary>
    /// 一批状态操作成功完成后产生的领域事件集合。
    /// </summary>
    public sealed class ConfiguredStateOperationBatchResult
    {
        internal ConfiguredStateOperationBatchResult(
            IEnumerable<ConfiguredEventRecord> emittedEvents)
        {
            EmittedEvents = new ReadOnlyCollection<ConfiguredEventRecord>(
                emittedEvents.ToArray());
        }

        public IReadOnlyList<ConfiguredEventRecord> EmittedEvents { get; }
    }

    /// <summary>
    /// 配置操作暂存的事件类型与结构化载荷；只在整批操作成功后发布。
    /// </summary>
    public sealed class ConfiguredEventRecord
    {
        internal ConfiguredEventRecord(
            string eventType,
            IEnumerable<KeyValuePair<string, StructuredValue>> payload)
        {
            EventType = eventType;
            Payload = new ReadOnlyDictionary<string, StructuredValue>(
                payload.ToDictionary(
                    value => value.Key,
                    value => value.Value,
                    StringComparer.Ordinal));
        }

        public string EventType { get; }

        public IReadOnlyDictionary<string, StructuredValue> Payload { get; }
    }

    /// <summary>
    /// 状态操作白名单。领域事件先缓冲，全部操作成功后才交给会话发布。
    /// </summary>
    public sealed class ConfiguredStateOperationRegistry
    {
        private const string SourceAnchorId = "来源锚点ID";
        private const string TargetAnchorId = "目标锚点ID";

        private readonly Dictionary<string, IConfiguredStateOperation>
            _operations =
                new Dictionary<string, IConfiguredStateOperation>(
                    StringComparer.Ordinal);
        private readonly List<ConfiguredEventRecord> _pendingEvents =
            new List<ConfiguredEventRecord>();
        private bool _isFrozen;

        public ConfiguredStateOperationRegistry(bool registerBuiltInOperations = true)
        {
            if (registerBuiltInOperations)
            {
                RegisterBuiltInOperations();
            }
        }

        public bool IsFrozen => _isFrozen;

        /// <summary>
        /// 显式注册当前通用层提供的状态操作。模块组合使用此入口，
        /// 避免由注册表构造函数隐式决定课程能力。
        /// </summary>
        public void RegisterBuiltInOperations()
        {
            Register(new RelationSetOperation());
            Register(new RelationRemoveOperation());
            Register(new ScalarSetOperation());
            Register(new ScalarAddOperation());
            Register(new ProcessStartOperation());
            Register(new ProcessStopOperation());
            Register(new EventEmitOperation(this));
            Register(new EventEmitWhenScalarOperation(this));
        }

        /// <summary>
        /// 冻结后不再允许改变操作集合，保证同一会话的协议稳定。
        /// </summary>
        public void Freeze()
        {
            _isFrozen = true;
        }

        public void Register(IConfiguredStateOperation operation)
        {
            if (_isFrozen)
            {
                throw new InvalidOperationException(
                    "状态操作注册表已冻结，不能继续注册操作。");
            }

            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var operationId = CourseContractGuard.Required(
                operation.OperationId,
                "状态操作 ID");
            if (!_operations.TryAdd(operationId, operation))
            {
                throw new ArgumentException(
                    $"状态操作“{operationId}”已注册。",
                    nameof(operation));
            }
        }

        public ConfiguredStateOperationBatchResult ApplyAtomically(
            SemanticActionRequest request,
            ExperimentWorld world,
            IEnumerable<ConfiguredMutationDefinition> mutations)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (mutations == null)
            {
                throw new ArgumentNullException(nameof(mutations));
            }

            var mutationList = mutations.ToArray();
            if (mutationList.Any(value => value == null))
            {
                throw new ArgumentException(
                    "状态变更集合不能包含空项。",
                    nameof(mutations));
            }

            Freeze();
            _pendingEvents.Clear();

            ConfiguredMutationDefinition current = null;
            try
            {
                world.CommitAtomically(
                    preparedWorld =>
                    {
                        foreach (var mutation in mutationList)
                        {
                            current = mutation;
                            if (!_operations.TryGetValue(
                                mutation.OperationId,
                                out var operation))
                            {
                                throw new InvalidOperationException(
                                    $"状态操作“{mutation.OperationId}”未注册。");
                            }

                            operation.Apply(
                                request,
                                preparedWorld,
                                mutation);
                        }
                    });

                var result = new ConfiguredStateOperationBatchResult(
                    _pendingEvents);
                _pendingEvents.Clear();
                return result;
            }
            catch (Exception exception)
            {
                _pendingEvents.Clear();
                if (exception is ConfiguredStateOperationException)
                {
                    throw;
                }

                throw new ConfiguredStateOperationException(
                    current?.MutationId ?? "未知状态变更",
                    current?.OperationId ?? "未知状态操作",
                    exception);
            }
        }

        private static string ReadText(
            ConfiguredMutationDefinition mutation,
            string key,
            string fallback = null)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value))
            {
                if (fallback != null)
                {
                    return fallback;
                }

                throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”缺少参数“{key}”。");
            }

            if (value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”的参数“{key}”必须是非空文本。");
            }

            return value.Text.Trim();
        }

        private static string ReadOptionalText(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”的参数“{key}”必须是非空文本。");
            }

            return value.Text.Trim();
        }

        private static double ReadNumber(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Number)
            {
                throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”的参数“{key}”必须是数值。");
            }

            return value.Number;
        }

        private static double? ReadOptionalNumber(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value.Kind != StructuredValueKind.Number)
            {
                throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”的参数“{key}”必须是数值。");
            }

            return value.Number;
        }

        private static RelationTypeId ReadRelationTypeId(
            ConfiguredMutationDefinition mutation)
        {
            var text = ReadText(
                mutation,
                CourseConfigurationKeys.Mutation.RelationTypeId);
            return new RelationTypeId(text);
        }

        private static EntityId ReadEntityId(
            ConfiguredMutationDefinition mutation,
            string key,
            string referenceKey,
            SemanticActionRequest request,
            string fallback)
        {
            if (mutation.Parameters.TryGetValue(
                referenceKey,
                out var reference))
            {
                if (reference.Kind != StructuredValueKind.Text)
                {
                    throw new ArgumentException(
                        $"状态变更“{mutation.MutationId}”的参数“{referenceKey}”必须是文本。");
                }

                var referencedId = reference.Text switch
                {
                    "操作者" => request.ActorEntityId,
                    "来源" => request.SourceEntityId,
                    "目标" => request.TargetEntityId,
                    _ => throw new ArgumentException(
                        $"实体引用“{reference.Text}”无效。")
                };
                return new EntityId(referencedId);
            }

            var value = ReadText(mutation, key, fallback);
            return new EntityId(value);
        }

        private static EntityRelation CreateRelation(
            SemanticActionRequest request,
            ExperimentWorld world,
            ConfiguredMutationDefinition mutation)
        {
            var typeId = ReadRelationTypeId(mutation);
            var schema = world.RequireRelationSchema(typeId);
            var source = ReadEntityId(
                mutation,
                CourseConfigurationKeys.Mutation.SourceEntityId,
                CourseConfigurationKeys.Mutation.SourceEntityReference,
                request,
                request.SourceEntityId);
            var target = ReadEntityId(
                mutation,
                CourseConfigurationKeys.Mutation.TargetEntityId,
                CourseConfigurationKeys.Mutation.TargetEntityReference,
                request,
                request.TargetEntityId);
            var sourcePortId = ReadOptionalText(
                mutation,
                CourseConfigurationKeys.Mutation.SourcePortId);
            var targetPortId = ReadOptionalText(
                mutation,
                CourseConfigurationKeys.Mutation.TargetPortId);
            if ((sourcePortId == null) != (targetPortId == null))
            {
                throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”必须同时声明来源端口和目标端口。");
            }

            if (sourcePortId == null
                && schema.PortPolicy != RelationPortPolicy.禁止)
            {
                if (TryResolveConnectionPorts(
                        world,
                        request,
                        out var resolved))
                {
                    sourcePortId = resolved.SourcePort.PortId;
                    targetPortId = resolved.TargetPort.PortId;
                }
                else if (schema.PortPolicy == RelationPortPolicy.必须)
                {
                    throw new ArgumentException(
                        $"状态变更“{mutation.MutationId}”无法解析关系“{typeId}”的端口。");
                }
            }

            return new EntityRelation(
                typeId,
                source,
                target,
                sourcePortId,
                targetPortId);
        }

        private static bool TryResolveConnectionPorts(
            ExperimentWorld world,
            SemanticActionRequest request,
            out ConnectionPortPair pair)
        {
            pair = null;
            if (string.IsNullOrWhiteSpace(request.TargetEntityId))
            {
                return false;
            }

            return ConnectionPortResolver.TryResolve(
                world,
                new EntityId(request.SourceEntityId),
                new EntityId(request.TargetEntityId),
                PreferredPortId(request, SourceAnchorId),
                PreferredPortId(request, TargetAnchorId),
                out pair);
        }

        private static string PreferredPortId(
            SemanticActionRequest request,
            string key)
        {
            if (!request.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                return null;
            }

            return value.Text.Trim();
        }

        private static IReadOnlyDictionary<string, StructuredValue>
            CreateEventPayload(
                SemanticActionRequest request,
                ConfiguredMutationDefinition mutation)
        {
            var payload = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal)
            {
                [CourseConfigurationKeys.EventPayload.ActionId] =
                    StructuredValue.FromText(request.ActionId),
                [CourseConfigurationKeys.EventPayload.ActorEntityId] =
                    StructuredValue.FromText(request.ActorEntityId),
                [CourseConfigurationKeys.EventPayload.SourceEntityId] =
                    StructuredValue.FromText(request.SourceEntityId),
                [CourseConfigurationKeys.EventPayload.TargetEntityId] =
                    request.TargetEntityId == null
                    ? StructuredValue.Null()
                    : StructuredValue.FromText(request.TargetEntityId)
            };
            foreach (var parameter in mutation.Parameters)
            {
                if (string.Equals(
                    parameter.Key,
                    CourseConfigurationKeys.Mutation.EventType,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                if (!payload.TryAdd(parameter.Key, parameter.Value))
                {
                    throw new ArgumentException(
                        $"事件载荷参数“{parameter.Key}”与保留字段冲突。");
                }
            }

            return payload;
        }

        private sealed class RelationSetOperation :
            IConfiguredStateOperation
        {
            public string OperationId => ConfiguredStateOperationIds.RelationSet;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var relation = CreateRelation(request, world, mutation);
                world.SetRelation(relation);
            }
        }

        private sealed class RelationRemoveOperation :
            IConfiguredStateOperation
        {
            public string OperationId =>
                ConfiguredStateOperationIds.RelationRemove;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var relation = CreateRelation(request, world, mutation);
                if (world.RemoveRelation(relation)
                    || !world.RequireRelationSchema(relation.TypeId)
                        .RequireExistingOnRemove)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"待移除的关系“{relation.TypeId}”不存在。");
            }
        }

        private sealed class ScalarSetOperation : IConfiguredStateOperation
        {
            public string OperationId => ConfiguredStateOperationIds.ScalarSet;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                world.SetScalar(
                    ReadText(mutation, CourseConfigurationKeys.Mutation.StateKey),
                    ReadNumber(mutation, CourseConfigurationKeys.Mutation.Value),
                    new WorldScalarUnit(ReadText(
                        mutation,
                        CourseConfigurationKeys.Mutation.Unit)),
                    ReadOptionalNumber(
                        mutation,
                        CourseConfigurationKeys.Mutation.Minimum),
                    ReadOptionalNumber(
                        mutation,
                        CourseConfigurationKeys.Mutation.Maximum));
            }
        }

        private sealed class ScalarAddOperation : IConfiguredStateOperation
        {
            public string OperationId => ConfiguredStateOperationIds.ScalarAdd;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                world.AddScalar(
                    ReadText(mutation, CourseConfigurationKeys.Mutation.StateKey),
                    ReadNumber(
                        mutation,
                        CourseConfigurationKeys.Mutation.Increment),
                    new WorldScalarUnit(ReadText(
                        mutation,
                        CourseConfigurationKeys.Mutation.Unit)),
                    ReadOptionalNumber(
                        mutation,
                        CourseConfigurationKeys.Mutation.Minimum),
                    ReadOptionalNumber(
                        mutation,
                        CourseConfigurationKeys.Mutation.Maximum));
            }
        }

        private sealed class ProcessStartOperation :
            IConfiguredStateOperation
        {
            public string OperationId =>
                ConfiguredStateOperationIds.ProcessStart;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var processId = ReadText(
                    mutation,
                    CourseConfigurationKeys.Mutation.ProcessId);
                var entityId = ReadText(
                    mutation,
                    CourseConfigurationKeys.Mutation.EntityId,
                    request.SourceEntityId);
                world.StartProcess(processId, new EntityId(entityId));
            }
        }

        private sealed class ProcessStopOperation :
            IConfiguredStateOperation
        {
            public string OperationId =>
                ConfiguredStateOperationIds.ProcessStop;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var processId = ReadText(
                    mutation,
                    CourseConfigurationKeys.Mutation.ProcessId);
                var entityId = ReadText(
                    mutation,
                    CourseConfigurationKeys.Mutation.EntityId,
                    request.SourceEntityId);
                world.StopProcess(processId, new EntityId(entityId));
            }
        }

        private sealed class EventEmitOperation : IConfiguredStateOperation
        {
            private readonly ConfiguredStateOperationRegistry _owner;

            public EventEmitOperation(ConfiguredStateOperationRegistry owner)
            {
                _owner = owner;
            }

            public string OperationId => ConfiguredStateOperationIds.EventEmit;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var eventType = ReadText(
                    mutation,
                    CourseConfigurationKeys.Mutation.EventType);
                if (!EventTypeProtocol.IsStable(eventType))
                {
                    throw new ArgumentException(
                        $"事件类型“{eventType}”必须使用自然中文名称。");
                }

                _owner._pendingEvents.Add(
                    new ConfiguredEventRecord(
                        eventType,
                        CreateEventPayload(request, mutation)));
            }
        }

        /// <summary>
        /// 只有权威标量等于配置值时才发送事件。学科操作可以先写入风险状态，
        /// 再由该通用操作把后果转换为可评价、可表现的领域事件。
        /// </summary>
        private sealed class EventEmitWhenScalarOperation :
            IConfiguredStateOperation
        {
            private readonly ConfiguredStateOperationRegistry _owner;

            public EventEmitWhenScalarOperation(
                ConfiguredStateOperationRegistry owner)
            {
                _owner = owner;
            }

            public string OperationId =>
                ConfiguredStateOperationIds.EventEmitWhenScalar;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var scalarKey = ReadText(
                    mutation,
                    CourseConfigurationKeys.Mutation.StateKey);
                var expected = ReadNumber(
                    mutation,
                    CourseConfigurationKeys.Mutation.ExpectedValue);
                if (!world.TryGetScalar(scalarKey, out var actual)
                    || Math.Abs(actual.Value - expected) > 0.000001d)
                {
                    return;
                }

                var eventType = ReadText(
                    mutation,
                    CourseConfigurationKeys.Mutation.EventType);
                if (!EventTypeProtocol.IsStable(eventType))
                {
                    throw new ArgumentException(
                        $"事件类型“{eventType}”必须使用自然中文名称。");
                }

                _owner._pendingEvents.Add(new ConfiguredEventRecord(
                    eventType,
                    CreateEventPayload(request, mutation)));
            }
        }
    }
}
