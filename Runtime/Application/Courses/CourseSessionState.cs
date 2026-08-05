using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using VirtualLab.Application.Commands;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Courses
{
    public sealed class CourseStateRestoreException : Exception
    {
        public CourseStateRestoreException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    public sealed class CourseRuntimeDefinition
    {
        private readonly IReadOnlyList<IStructuredFactReader> _readers;
        private readonly IReadOnlyList<ConfiguredActionDefinition> _actions;
        private readonly IReadOnlyList<CourseActionAssessmentDefinition>
            _actionAssessments;
        private readonly int _maximumScore;
        private readonly Func<ConfiguredStateOperationRegistry> _registryFactory;
        private readonly IReadOnlyList<ICourseEventProjector> _eventProjectors;
        private readonly Func<ExperimentWorld, ICourseProcessAdvancer>
            _processAdvancerFactory;
        private readonly Action<ExperimentWorld> _worldPreparation;
        private readonly CourseCapabilityStateCodecRegistry
            _capabilityStateCodecs;
        private readonly CourseWorldStateCodecRegistry _worldStateCodecs;

        public CourseRuntimeDefinition(
            CourseRuntimeModuleScope modules,
            IEnumerable<ConfiguredActionDefinition> actions,
            IEnumerable<CourseActionAssessmentDefinition> actionAssessments,
            int maximumScore)
            : this(
                (modules ?? throw new ArgumentNullException(nameof(modules)))
                    .FactReaders,
                actions,
                actionAssessments,
                maximumScore,
                modules.CreateStateOperationRegistry,
                modules.EventProjectors,
                modules.CreateProcessAdvancer,
                modules.PrepareWorld,
                modules.CapabilityStateCodecs,
                modules.WorldStateCodecs)
        {
        }

        public CourseRuntimeDefinition(
            IEnumerable<IStructuredFactReader> readers,
            IEnumerable<ConfiguredActionDefinition> actions)
            : this(
                readers,
                actions,
                Array.Empty<CourseActionAssessmentDefinition>(),
                0,
                () => new ConfiguredStateOperationRegistry())
        {
        }

        public CourseRuntimeDefinition(
            IEnumerable<IStructuredFactReader> readers,
            IEnumerable<ConfiguredActionDefinition> actions,
            IEnumerable<CourseActionAssessmentDefinition> actionAssessments,
            int maximumScore)
            : this(
                readers,
                actions,
                actionAssessments,
                maximumScore,
                () => new ConfiguredStateOperationRegistry(),
                Array.Empty<ICourseEventProjector>())
        {
        }

        public CourseRuntimeDefinition(
            IEnumerable<IStructuredFactReader> readers,
            IEnumerable<ConfiguredActionDefinition> actions,
            IEnumerable<CourseActionAssessmentDefinition> actionAssessments,
            int maximumScore,
            Func<ConfiguredStateOperationRegistry> registryFactory,
            IEnumerable<ICourseEventProjector> eventProjectors = null,
            Func<ExperimentWorld, ICourseProcessAdvancer>
                processAdvancerFactory = null,
            Action<ExperimentWorld> worldPreparation = null,
            CourseCapabilityStateCodecRegistry capabilityStateCodecs = null,
            CourseWorldStateCodecRegistry worldStateCodecs = null)
        {
            _readers = (readers
                ?? throw new ArgumentNullException(nameof(readers))).ToArray();
            _actions = (actions
                ?? throw new ArgumentNullException(nameof(actions))).ToArray();
            _actionAssessments = (actionAssessments
                ?? throw new ArgumentNullException(
                    nameof(actionAssessments))).ToArray();
            if (maximumScore < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumScore));
            }

            _maximumScore = maximumScore;
            _registryFactory = registryFactory
                ?? throw new ArgumentNullException(nameof(registryFactory));
            _eventProjectors = (eventProjectors
                ?? Array.Empty<ICourseEventProjector>()).ToArray();
            _processAdvancerFactory = processAdvancerFactory;
            _worldPreparation = worldPreparation ?? FreezeRegisteredWorldContracts;
            _capabilityStateCodecs = capabilityStateCodecs
                ?? new CourseCapabilityStateCodecRegistry();
            _worldStateCodecs = worldStateCodecs
                ?? new CourseWorldStateCodecRegistry();
        }

        public ConfigDrivenCourseSession CreateSession(ExperimentWorld world)
        {
            return CreateSession(world, Array.Empty<CourseEventState>());
        }

        public IReadOnlyList<IStructuredFactReader> FactReaders => _readers;

        internal CourseCapabilityStateCodecRegistry CapabilityStateCodecs =>
            _capabilityStateCodecs;

        internal CourseWorldStateCodecRegistry WorldStateCodecs =>
            _worldStateCodecs;

        public ICourseProcessAdvancer CreateProcessAdvancer(
            ExperimentWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            return _processAdvancerFactory?.Invoke(world);
        }

        internal void PrepareWorld(ExperimentWorld world)
        {
            _worldPreparation?.Invoke(
                world ?? throw new ArgumentNullException(nameof(world)));
        }

        private static void FreezeRegisteredWorldContracts(ExperimentWorld world)
        {
            if (!world.RelationSchemasFrozen)
            {
                world.FreezeRelationSchemas();
            }

            if (!world.WorldStateTypesFrozen)
            {
                world.FreezeWorldStateTypes();
            }
        }

        internal ConfigDrivenCourseSession CreateSession(
            ExperimentWorld world,
            IEnumerable<CourseEventState> initialEvents)
        {
            PrepareWorld(world);
            var evaluator = new StructuredRuleEvaluator(_readers);
            return new ConfigDrivenCourseSession(
                world,
                evaluator,
                _registryFactory()
                    ?? throw new InvalidOperationException(
                        "状态操作注册表工厂返回了空值。"),
                _actions,
                new CourseAssessmentEvaluator(evaluator),
                _actionAssessments,
                _maximumScore,
                new CourseEventStream(initialEvents, _eventProjectors),
                _capabilityStateCodecs,
                _worldStateCodecs);
        }
    }

    public sealed class CourseCapabilityState
    {
        public CourseCapabilityState(
            string capabilityId,
            decimal numberValue = 0m,
            string textValue = null,
            IEnumerable<KeyValuePair<string, string>> textProperties = null)
        {
            CapabilityId = CourseContractGuard.Required(
                capabilityId,
                "能力标识");
            NumberValue = numberValue;
            TextValue = CourseContractGuard.Optional(textValue);
            var properties = (textProperties
                    ?? Array.Empty<KeyValuePair<string, string>>())
                .Select(value => new KeyValuePair<string, string>(
                    CourseContractGuard.Required(
                        value.Key,
                        "能力文本属性键"),
                    CourseContractGuard.Optional(value.Value)))
                .ToArray();
            if (properties
                    .GroupBy(value => value.Key, StringComparer.Ordinal)
                    .Any(value => value.Count() > 1))
            {
                throw new ArgumentException(
                    "能力文本属性不能包含重复键。",
                    nameof(textProperties));
            }

            TextProperties = new ReadOnlyDictionary<string, string>(
                properties.ToDictionary(
                    value => value.Key,
                    value => value.Value,
                    StringComparer.Ordinal));
        }

        public string CapabilityId { get; }
        public decimal NumberValue { get; }
        public string TextValue { get; }
        public IReadOnlyDictionary<string, string> TextProperties { get; }
    }

    public sealed class CourseEntityState
    {
        public CourseEntityState(
            string entityId,
            IEnumerable<CourseCapabilityState> capabilities)
        {
            EntityId = entityId;
            Capabilities = capabilities.ToArray();
        }

        public string EntityId { get; }
        public IReadOnlyList<CourseCapabilityState> Capabilities { get; }
    }

    public sealed class CourseRelationState
    {
        public CourseRelationState(
            RelationTypeId typeId,
            string sourceEntityId,
            string targetEntityId,
            string sourcePortId = null,
            string targetPortId = null)
        {
            TypeId = typeId;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            SourcePortId = CourseContractGuard.Optional(sourcePortId);
            TargetPortId = CourseContractGuard.Optional(targetPortId);
            if ((SourcePortId == null) != (TargetPortId == null))
            {
                throw new ArgumentException(
                    "课程连接关系状态必须同时包含两个端口 ID。");
            }
        }

        public RelationTypeId TypeId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public string SourcePortId { get; }
        public string TargetPortId { get; }
    }

    public sealed class CourseScalarState
    {
        public CourseScalarState(string key, double value, string unitId)
        {
            Key = key;
            Value = value;
            UnitId = unitId;
        }

        public string Key { get; }
        public double Value { get; }
        public string UnitId { get; }
    }

    public sealed class CourseProcessState
    {
        public CourseProcessState(WorldProcessState state)
            : this(
                state?.ProcessId,
                state?.EntityId.Value,
                state?.TextParameters,
                state?.NumberParameters)
        {
        }

        public CourseProcessState(
            string processId,
            string entityId,
            IEnumerable<KeyValuePair<string, string>> textParameters,
            IEnumerable<KeyValuePair<string, double>> numberParameters)
        {
            ProcessId = CourseContractGuard.Required(processId, "过程 ID");
            EntityId = CourseContractGuard.Required(entityId, "过程实体 ID");
            TextParameters = new ReadOnlyDictionary<string, string>(
                (textParameters
                    ?? throw new ArgumentNullException(nameof(textParameters)))
                .ToDictionary(value => value.Key, value => value.Value));
            NumberParameters = new ReadOnlyDictionary<string, double>(
                (numberParameters
                    ?? throw new ArgumentNullException(nameof(numberParameters)))
                .ToDictionary(value => value.Key, value => value.Value));
        }

        public string ProcessId { get; }
        public string EntityId { get; }
        public IReadOnlyDictionary<string, string> TextParameters { get; }
        public IReadOnlyDictionary<string, double> NumberParameters { get; }
    }

    public sealed class CourseEventState
    {
        public CourseEventState(
            long sequence,
            string commandId,
            long tick,
            string eventType,
            IEnumerable<KeyValuePair<string, StructuredValue>> payload)
        {
            Sequence = sequence;
            CommandId = commandId;
            Tick = tick;
            EventType = eventType;
            Payload = new ReadOnlyDictionary<string, StructuredValue>(
                payload.ToDictionary(value => value.Key, value => value.Value));
        }

        public long Sequence { get; }
        public string CommandId { get; }
        public long Tick { get; }
        public string EventType { get; }
        public IReadOnlyDictionary<string, StructuredValue> Payload { get; }
    }

    public sealed class CourseExecutedCommandState
    {
        public CourseExecutedCommandState(
            SemanticActionRequest request,
            CommandResult result)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public SemanticActionRequest Request { get; }
        public CommandResult Result { get; }
    }

    public sealed class CourseSpatialPoseState
    {
        public CourseSpatialPoseState(
            string entityId,
            double positionX,
            double positionY,
            double positionZ,
            double rotationX,
            double rotationY,
            double rotationZ)
        {
            EntityId = CourseContractGuard.Required(entityId, "空间实体 ID");
            var values = new[]
            {
                positionX,
                positionY,
                positionZ,
                rotationX,
                rotationY,
                rotationZ
            };
            if (values.Any(value => double.IsNaN(value)
                || double.IsInfinity(value)))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(positionX),
                    "空间姿态只能包含有限数值。");
            }

            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
        }

        public string EntityId { get; }
        public double PositionX { get; }
        public double PositionY { get; }
        public double PositionZ { get; }
        public double RotationX { get; }
        public double RotationY { get; }
        public double RotationZ { get; }
    }

    /// <summary>
    /// 当前结构的权威会话快照，不携带版本号或内容指纹。
    /// </summary>
    public sealed class CourseSessionState : IEquatable<CourseSessionState>
    {
        internal CourseSessionState(
            IEnumerable<CourseEntityState> entities,
            IEnumerable<CourseRelationState> relations,
            IEnumerable<CourseWorldState> worldStates,
            IEnumerable<CourseScalarState> scalars,
            IEnumerable<CourseProcessState> processes,
            IEnumerable<CourseEventState> events,
            IEnumerable<CourseExecutedCommandState> commands,
            long nextEventSequence,
            CourseGoalEvaluationResult goals,
            CourseAssessmentEvaluationResult assessment,
            IEnumerable<string> observations,
            IEnumerable<CourseSpatialPoseState> spatialPoses = null)
        {
            Entities = entities.ToArray();
            Relations = relations.ToArray();
            WorldStates = worldStates.ToArray();
            if (WorldStates.Any(value => value == null)
                || WorldStates.GroupBy(value => value.TypeId)
                    .Any(group => group.Count() > 1))
            {
                throw new ArgumentException(
                    "世界状态快照不能包含空项或重复状态类型。",
                    nameof(worldStates));
            }

            Scalars = scalars.ToArray();
            Processes = processes.ToArray();
            Events = events.ToArray();
            Commands = commands.ToArray();
            NextEventSequence = nextEventSequence;
            Goals = goals;
            Assessment = assessment;
            Observations = observations.ToArray();
            SpatialPoses = (spatialPoses
                ?? Array.Empty<CourseSpatialPoseState>()).ToArray();
            if (SpatialPoses.Any(value => value == null)
                || SpatialPoses.GroupBy(
                        value => value.EntityId,
                        StringComparer.Ordinal)
                    .Any(value => value.Count() > 1))
            {
                throw new ArgumentException(
                    "空间姿态不能包含空项或重复实体 ID。",
                    nameof(spatialPoses));
            }
        }

        public IReadOnlyList<CourseEntityState> Entities { get; }
        public IReadOnlyList<CourseRelationState> Relations { get; }
        public IReadOnlyList<CourseWorldState> WorldStates { get; }
        public IReadOnlyList<CourseScalarState> Scalars { get; }
        public IReadOnlyList<CourseProcessState> Processes { get; }
        public IReadOnlyList<CourseEventState> Events { get; }
        public IReadOnlyList<CourseExecutedCommandState> Commands { get; }
        public long NextEventSequence { get; }
        public CourseGoalEvaluationResult Goals { get; }
        public CourseAssessmentEvaluationResult Assessment { get; }
        public IReadOnlyList<string> Observations { get; }
        public IReadOnlyList<CourseSpatialPoseState> SpatialPoses { get; }

        /// <summary>
        /// 从当前存档结构恢复权威状态。创建时立即验证实体引用、单位、
        /// 过程和日志，拒绝把损坏数据带入课程会话。
        /// </summary>
        public static CourseSessionState RestoreCurrent(
            IEnumerable<CourseEntityState> entities,
            IEnumerable<CourseRelationState> relations,
            IEnumerable<CourseWorldState> worldStates,
            IEnumerable<CourseScalarState> scalars,
            IEnumerable<CourseProcessState> processes,
            IEnumerable<CourseEventState> events,
            IEnumerable<CourseExecutedCommandState> commands,
            long nextEventSequence,
            CourseGoalEvaluationResult goals,
            CourseAssessmentEvaluationResult assessment,
            IEnumerable<string> observations,
            IEnumerable<CourseSpatialPoseState> spatialPoses = null)
        {
            var state = new CourseSessionState(
                entities ?? throw new ArgumentNullException(nameof(entities)),
                relations ?? throw new ArgumentNullException(nameof(relations)),
                worldStates
                    ?? throw new ArgumentNullException(nameof(worldStates)),
                scalars ?? throw new ArgumentNullException(nameof(scalars)),
                processes ?? throw new ArgumentNullException(nameof(processes)),
                events ?? throw new ArgumentNullException(nameof(events)),
                commands ?? throw new ArgumentNullException(nameof(commands)),
                nextEventSequence,
                goals ?? throw new ArgumentNullException(nameof(goals)),
                assessment ?? throw new ArgumentNullException(nameof(assessment)),
                observations
                    ?? throw new ArgumentNullException(nameof(observations)),
                spatialPoses);
            state.ValidateCoreReferences();
            state.ValidateJournal();
            return state;
        }

        public CourseSessionState WithRelations(
            IEnumerable<CourseRelationState> relations) =>
            new CourseSessionState(
                Entities,
                relations,
                WorldStates,
                Scalars,
                Processes,
                Events,
                Commands,
                NextEventSequence,
                Goals,
                Assessment,
                Observations,
                SpatialPoses);

        public CourseSessionState WithWorldStates(
            IEnumerable<CourseWorldState> worldStates) =>
            new CourseSessionState(
                Entities,
                Relations,
                worldStates,
                Scalars,
                Processes,
                Events,
                Commands,
                NextEventSequence,
                Goals,
                Assessment,
                Observations,
                SpatialPoses);

        public bool Equals(CourseSessionState other) =>
            other != null && Canonical() == other.Canonical();

        public override bool Equals(object obj) =>
            Equals(obj as CourseSessionState);

        public override int GetHashCode() => Canonical().GetHashCode();

        internal static CourseSessionState Capture(
            ExperimentWorld world,
            CourseCapabilityStateCodecRegistry capabilityStateCodecs,
            CourseWorldStateCodecRegistry worldStateCodecs,
            IEnumerable<CourseEventState> events,
            IEnumerable<CourseExecutedCommandState> commands,
            long nextEventSequence,
            CourseGoalEvaluationResult goals,
            CourseAssessmentEvaluationResult assessment,
            IEnumerable<string> observations,
            IEnumerable<CourseSpatialPoseState> spatialPoses = null)
        {
            if (capabilityStateCodecs == null)
            {
                throw new ArgumentNullException(nameof(capabilityStateCodecs));
            }

            if (worldStateCodecs == null)
            {
                throw new ArgumentNullException(nameof(worldStateCodecs));
            }

            return new CourseSessionState(
                world.Entities.Select(value => new CourseEntityState(
                    value.Id.Value,
                    value.Capabilities.Select(
                        capabilityStateCodecs.Capture))),
                world.Relations.Select(value => new CourseRelationState(
                    value.TypeId,
                    value.Source.Value,
                    value.Target.Value,
                    value.SourcePortId,
                    value.TargetPortId)),
                worldStateCodecs.Capture(world),
                world.Scalars.Select(value => new CourseScalarState(
                    value.Key,
                    value.Value.Value,
                    value.Value.Unit.Id)),
                world.ActiveProcesses.Select(value => new CourseProcessState(value)),
                events,
                commands,
                nextEventSequence,
                goals,
                assessment,
                observations,
                spatialPoses);
        }

        internal ExperimentWorld RestoreWorld(
            Action<ExperimentWorld> prepareWorld = null,
            CourseCapabilityStateCodecRegistry capabilityStateCodecs = null,
            CourseWorldStateCodecRegistry worldStateCodecs = null)
        {
            try
            {
                var codecs = capabilityStateCodecs
                    ?? new CourseCapabilityStateCodecRegistry();
                var stateCodecs = worldStateCodecs
                    ?? new CourseWorldStateCodecRegistry();
                ValidateJournal();
                var world = prepareWorld == null
                    ? new ExperimentWorld(Relations
                        .Select(value => value.TypeId)
                        .Distinct()
                        .Select(value => new RelationSchema(
                            value,
                            allowSelfRelation: true,
                            portPolicy: RelationPortPolicy.可选)))
                    : new ExperimentWorld();
                prepareWorld?.Invoke(world);
                foreach (var entityState in Entities)
                {
                    var entity = new ExperimentEntity(
                        new EntityId(entityState.EntityId));
                    foreach (var capability in entityState.Capabilities)
                    {
                        entity.AddCapability(codecs.Restore(capability));
                    }

                    world.AddEntity(entity);
                }

                foreach (var relation in Relations)
                {
                    var source = new EntityId(relation.SourceEntityId);
                    var target = new EntityId(relation.TargetEntityId);
                    if (!world.ContainsEntity(source)
                        || !world.ContainsEntity(target))
                    {
                        throw new CourseStateRestoreException(
                            "session.reference.invalid",
                            "关系引用了不存在的实体。");
                    }

                    world.SetRelation(new EntityRelation(
                        relation.TypeId,
                        source,
                        target,
                        relation.SourcePortId,
                        relation.TargetPortId));
                }

                stateCodecs.Restore(world, WorldStates);

                foreach (var scalar in Scalars)
                {
                    world.SetScalar(
                        scalar.Key,
                        scalar.Value,
                        new WorldScalarUnit(scalar.UnitId),
                        null,
                        null);
                }

                foreach (var process in Processes)
                {
                    var entityId = new EntityId(process.EntityId);
                    if (!world.ContainsEntity(entityId))
                    {
                        throw new CourseStateRestoreException(
                            "session.state.invalid",
                            $"过程引用了不存在的实体“{process.EntityId}”。");
                    }

                    world.StartProcess(
                        process.ProcessId,
                        entityId,
                        process.TextParameters,
                        process.NumberParameters);
                }

                return world;
            }
            catch (CourseStateRestoreException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException)
            {
                throw new CourseStateRestoreException(
                    "session.state.invalid",
                    $"会话状态不合法：{exception.Message}");
            }
        }

        private void ValidateJournal()
        {
            if (NextEventSequence <= 0)
            {
                throw new CourseStateRestoreException(
                    "session.state.invalid",
                    "下一事件序号必须为正数。");
            }

            var eventSequences = new HashSet<long>();
            foreach (var item in Events)
            {
                if (item.Sequence <= 0
                    || item.Tick < 0
                    || !eventSequences.Add(item.Sequence)
                    || string.IsNullOrWhiteSpace(item.CommandId)
                    || string.IsNullOrWhiteSpace(item.EventType))
                {
                    throw new CourseStateRestoreException(
                        "session.state.invalid",
                        "事件日志包含非法序号、时间或必填字段。");
                }
            }

            if (Events.Count > 0
                && NextEventSequence <= Events.Max(value => value.Sequence))
            {
                throw new CourseStateRestoreException(
                    "session.state.invalid",
                    "下一事件序号必须大于已有事件序号。");
            }

            var commandIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in Commands)
            {
                if (command == null
                    || !commandIds.Add(command.Request.CommandId))
                {
                    throw new CourseStateRestoreException(
                        "session.state.invalid",
                        "命令日志包含空项或重复命令 ID。");
                }
            }
        }

        private void ValidateCoreReferences()
        {
            var entityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in Entities)
            {
                if (entity == null
                    || string.IsNullOrWhiteSpace(entity.EntityId)
                    || !entityIds.Add(entity.EntityId)
                    || entity.Capabilities.Any(capability => capability == null))
                {
                    throw new CourseStateRestoreException(
                        "session.state.invalid",
                        "实体状态包含空项、空标识、重复标识或空能力。");
                }
            }

            if (Relations.Any(value => value == null
                    || !entityIds.Contains(value.SourceEntityId)
                    || !entityIds.Contains(value.TargetEntityId))
                || SpatialPoses.Any(value =>
                    !entityIds.Contains(value.EntityId))
                || Processes.Any(value => value == null
                    || !entityIds.Contains(value.EntityId)))
            {
                throw new CourseStateRestoreException(
                    "session.reference.invalid",
                    "关系、空间姿态或过程引用了不存在的实体。");
            }
        }

        private string Canonical()
        {
            var values = new List<string>
            {
                "next:" + NextEventSequence,
                "score:" + Assessment.Score,
                "goals:" + string.Join(
                    string.Empty,
                    Goals.CompletedGoalIds.Select(Token)),
                "risks:" + string.Join(
                    string.Empty,
                    Assessment.RiskIds.Select(Token)),
                "observations:" + string.Join(
                    string.Empty,
                    Observations.Select(Token))
            };
            values.AddRange(Entities.OrderBy(value => value.EntityId).Select(
                value => "entity:" + Token(value.EntityId) + string.Join(
                    string.Empty,
                    value.Capabilities.Select(CapabilityCanonical).OrderBy(x => x))));
            values.AddRange(Relations.OrderBy(value => value.SourceEntityId).ThenBy(value => value.TargetEntityId).Select(
                value => $"relation:{value.TypeId}:{Token(value.SourceEntityId)}{Token(value.TargetEntityId)}"
                         + $"{Token(value.SourcePortId)}{Token(value.TargetPortId)}"));
            values.AddRange(WorldStates
                .OrderBy(value => value.TypeId.Value, StringComparer.Ordinal)
                .Select(value => "world-state:"
                    + Token(value.TypeId.Value)
                    + string.Join(
                        string.Empty,
                        value.Entries.Select(WorldStateEntryCanonical))));
            values.AddRange(Scalars.OrderBy(value => value.Key).Select(
                value => $"scalar:{Token(value.Key)}:{value.Value.ToString("R", CultureInfo.InvariantCulture)}:{Token(value.UnitId)}"));
            values.AddRange(SpatialPoses.OrderBy(value => value.EntityId).Select(
                value => $"pose:{Token(value.EntityId)}:"
                    + $"{value.PositionX.ToString("R", CultureInfo.InvariantCulture)}:"
                    + $"{value.PositionY.ToString("R", CultureInfo.InvariantCulture)}:"
                    + $"{value.PositionZ.ToString("R", CultureInfo.InvariantCulture)}:"
                    + $"{value.RotationX.ToString("R", CultureInfo.InvariantCulture)}:"
                    + $"{value.RotationY.ToString("R", CultureInfo.InvariantCulture)}:"
                    + value.RotationZ.ToString("R", CultureInfo.InvariantCulture)));
            values.AddRange(Processes.OrderBy(value => value.ProcessId).ThenBy(value => value.EntityId).Select(
                value => $"process:{Token(value.ProcessId)}{Token(value.EntityId)}:{string.Join(string.Empty, value.TextParameters.OrderBy(x => x.Key).Select(x => Token(x.Key) + Token(x.Value)))}:{string.Join(string.Empty, value.NumberParameters.OrderBy(x => x.Key).Select(x => Token(x.Key) + x.Value.ToString("R", CultureInfo.InvariantCulture)))}"));
            values.AddRange(Events.OrderBy(value => value.Sequence).Select(
                value => $"event:{value.Sequence}:{Token(value.CommandId)}:{value.Tick}:{Token(value.EventType)}:{ParametersCanonical(value.Payload)}"));
            values.AddRange(Commands.OrderBy(value => value.Request.CommandId).Select(
                value => "command:" + CommandCanonical(value)));
            values.AddRange(Assessment.Evidence.Select(
                value => $"evidence:{Token(value.AssessmentId)}"
                    + $"{Token(value.RiskId)}:{value.ScoreDelta}:"
                    + $"{Token(value.Prompt)}{Token(value.CommandId)}:"
                    + $"{value.Severity}:{value.Recoverability}:"
                    + string.Join(
                        string.Empty,
                        value.BlockedGoalIds.Select(Token))));
            return string.Join("\n", values);
        }

        private static string CapabilityCanonical(CourseCapabilityState state) =>
            $"{Token(state.CapabilityId)}:{state.NumberValue.ToString(CultureInfo.InvariantCulture)}:{Token(state.TextValue)}"
            + string.Join(
                string.Empty,
                state.TextProperties
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => Token(value.Key) + Token(value.Value)));

        private static string WorldStateEntryCanonical(
            CourseWorldStateEntry entry) =>
            Token(entry.EntryType)
            + string.Join(
                string.Empty,
                entry.Values
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => Token(value.Key) + Token(value.Value)));

        private static string CommandCanonical(
            CourseExecutedCommandState state)
        {
            var request = state.Request;
            var result = state.Result;
            var events = result.Events.Select(value =>
            {
                var payload = value.Event is ConfiguredCourseDomainEvent configured
                    ? ParametersCanonical(configured.Payload)
                    : string.Empty;
                return $"{value.Sequence}:{Token(value.CommandId)}:{value.Tick.Value}:{Token(value.EventType)}:{payload}";
            });
            return $"{Token(request.CommandId)}{Token(request.ActionId)}"
                + $"{Token(request.ActorEntityId)}{Token(request.SourceEntityId)}"
                + $"{Token(request.TargetEntityId)}"
                + ParametersCanonical(request.Parameters)
                + $":{result.IsAccepted}:{Token(result.RejectionCode)}"
                + string.Join(
                    string.Empty,
                    result.RejectionCodes.Select(Token))
                + string.Join(string.Empty, events);
        }

        private static string ParametersCanonical(
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters) =>
            string.Join(
                string.Empty,
                parameters
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value =>
                        Token(value.Key)
                        + Token(StructuredValueCanonical(value.Value))));

        private static string Token(string value) =>
            value == null ? "-1:" : value.Length + ":" + value;

        private static string StructuredValueCanonical(StructuredValue value) =>
            value.Kind switch
            {
                StructuredValueKind.Null => "null",
                StructuredValueKind.Boolean => value.Boolean ? "true" : "false",
                StructuredValueKind.Number => value.Number.ToString(
                    "R",
                    CultureInfo.InvariantCulture),
                StructuredValueKind.Text => "text:" + Token(value.Text),
                StructuredValueKind.TextList => "list:"
                    + string.Join(string.Empty, value.TextList.Select(Token)),
                _ => throw new ArgumentOutOfRangeException()
            };

    }
}
