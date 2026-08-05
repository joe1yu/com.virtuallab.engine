using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Converters;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Courses;
using VirtualLab.Application.Events;
using VirtualLab.Domain.Relations;
using VirtualLab.Domain.WorldStates;
using VirtualLab.Kernel;

namespace VirtualLab.Infrastructure.Persistence
{
    /// <summary>
    /// 当前课程会话的存档信封。只保存恢复所需元数据和结构化状态，
    /// 不携带格式版本、引擎版本或内容指纹。
    /// </summary>
    public sealed class SessionArchive
    {
        public SessionArchive(
            string courseId,
            string sessionId,
            int randomSeed,
            long lastSequence,
            long currentTick,
            CourseSessionState state)
        {
            CourseId = Required(courseId, nameof(courseId));
            SessionId = Required(sessionId, nameof(sessionId));
            if (lastSequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lastSequence));
            }

            if (currentTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(currentTick));
            }

            RandomSeed = randomSeed;
            LastSequence = lastSequence;
            CurrentTick = currentTick;
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string CourseId { get; }
        public string SessionId { get; }
        public int RandomSeed { get; }
        public long LastSequence { get; }
        public long CurrentTick { get; }
        public CourseSessionState State { get; }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "存档标识不能为空。",
                    parameterName);
            }

            return value.Trim();
        }
    }

    public static class SessionArchiveReadLimits
    {
        public const long MaxFileBytes = 8L * 1024L * 1024L;
        public const int MaxJsonDepth = 64;
        public const int MaxEntities = 10000;
        public const int MaxRelations = 50000;
        public const int MaxWorldStates = 256;
        public const int MaxWorldStateEntries = 50000;
        public const int MaxProcesses = 10000;
        public const int MaxEvents = 100000;
        public const int MaxCommands = 100000;
        public const int MaxParameters = 256;
        public const int MaxTextCharacters = 65536;

        internal static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);
    }

    public static class SessionJson
    {
        private static readonly JsonSerializerSettings Settings =
            new JsonSerializerSettings
            {
                ContractResolver =
                    new CamelCasePropertyNamesContractResolver(),
                Converters =
                {
                    new StringEnumConverter
                    {
                        AllowIntegerValues = false
                    }
                },
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                Formatting = Formatting.Indented,
                MaxDepth = SessionArchiveReadLimits.MaxJsonDepth,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None
            };

        public static string Serialize(SessionArchive archive)
        {
            if (archive == null)
            {
                throw new ArgumentNullException(nameof(archive));
            }

            return JsonConvert.SerializeObject(
                ArchiveDocument.From(archive),
                Settings);
        }

        public static SessionArchive Deserialize(string json)
        {
            if (!TryDeserialize(json, out var archive, out var errorCode))
            {
                throw new JsonSerializationException(errorCode);
            }

            return archive;
        }

        public static bool TryDeserialize(
            string json,
            out SessionArchive archive,
            out string errorCode)
        {
            archive = null;
            errorCode = null;
            if (json == null)
            {
                errorCode = "session.data.invalid";
                return false;
            }

            if (!TryGetByteCount(json, out var byteCount)
                || byteCount > SessionArchiveReadLimits.MaxFileBytes)
            {
                errorCode = "session.data.too_large";
                return false;
            }

            try
            {
                JToken root;
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text)
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = SessionArchiveReadLimits.MaxJsonDepth
                })
                {
                    root = JToken.Load(
                        reader,
                        new JsonLoadSettings
                        {
                            CommentHandling = CommentHandling.Load,
                            DuplicatePropertyNameHandling =
                                DuplicatePropertyNameHandling.Error
                        });
                    if (reader.Read())
                    {
                        errorCode = "session.data.invalid";
                        return false;
                    }
                }

                var container = root as JContainer;
                if (!(root is JObject)
                    || container == null
                    || new[] { root }.Concat(container.Descendants()).Any(
                        value => value.Type == JTokenType.Comment
                            || value.Type == JTokenType.Null
                            || (value.Type == JTokenType.String
                                && value.Value<string>().Length
                                    > SessionArchiveReadLimits
                                        .MaxTextCharacters)))
                {
                    errorCode = "session.data.invalid";
                    return false;
                }

                var document = root.ToObject<ArchiveDocument>(
                    JsonSerializer.Create(Settings));
                archive = document?.ToArchive();
                if (archive == null)
                {
                    errorCode = "session.data.invalid";
                    return false;
                }

                return true;
            }
            catch (Exception error) when (
                error is JsonException
                || error is ArgumentException
                || error is InvalidOperationException
                || error is OverflowException
                || error is CourseStateRestoreException)
            {
                archive = null;
                errorCode = error is CourseStateRestoreException restore
                    ? restore.Code
                    : "session.data.invalid";
                return false;
            }
        }

        private static bool TryGetByteCount(string value, out int count)
        {
            try
            {
                count = SessionArchiveReadLimits.StrictUtf8
                    .GetByteCount(value);
                return true;
            }
            catch (EncoderFallbackException)
            {
                count = 0;
                return false;
            }
        }

        private sealed class ArchiveDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string CourseId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string SessionId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public int RandomSeed { get; set; }
            [JsonProperty(Required = Required.Always)]
            public long LastSequence { get; set; }
            [JsonProperty(Required = Required.Always)]
            public long CurrentTick { get; set; }
            [JsonProperty(Required = Required.Always)]
            public StateDocument State { get; set; }

            public static ArchiveDocument From(SessionArchive value)
            {
                return new ArchiveDocument
                {
                    CourseId = value.CourseId,
                    SessionId = value.SessionId,
                    RandomSeed = value.RandomSeed,
                    LastSequence = value.LastSequence,
                    CurrentTick = value.CurrentTick,
                    State = StateDocument.From(value.State)
                };
            }

            public SessionArchive ToArchive()
            {
                return new SessionArchive(
                    CourseId,
                    SessionId,
                    RandomSeed,
                    LastSequence,
                    CurrentTick,
                    Require(State, "state").ToState());
            }
        }

        private sealed class StateDocument
        {
            [JsonProperty(Required = Required.Always)]
            public List<EntityDocument> Entities { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<RelationDocument> Relations { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<WorldStateDocument> WorldStates { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<ScalarDocument> Scalars { get; set; }
            [JsonProperty(Required = Required.Default)]
            public List<SpatialPoseDocument> SpatialPoses { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<ProcessDocument> Processes { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<EventDocument> Events { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<CommandDocument> Commands { get; set; }
            [JsonProperty(Required = Required.Always)]
            public long NextEventSequence { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<string> CompletedGoalIds { get; set; }
            [JsonProperty(Required = Required.Always)]
            public int Score { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<string> RiskIds { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<EvidenceDocument> Evidence { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<string> Observations { get; set; }

            public static StateDocument From(CourseSessionState value)
            {
                return new StateDocument
                {
                    Entities = value.Entities.Select(EntityDocument.From)
                        .ToList(),
                    Relations = value.Relations.Select(RelationDocument.From)
                        .ToList(),
                    WorldStates = value.WorldStates
                        .Select(WorldStateDocument.From).ToList(),
                    Scalars = value.Scalars.Select(ScalarDocument.From).ToList(),
                    SpatialPoses = value.SpatialPoses
                        .Select(SpatialPoseDocument.From).ToList(),
                    Processes = value.Processes.Select(ProcessDocument.From)
                        .ToList(),
                    Events = value.Events.Select(EventDocument.From).ToList(),
                    Commands = value.Commands.Select(CommandDocument.From)
                        .ToList(),
                    NextEventSequence = value.NextEventSequence,
                    CompletedGoalIds = value.Goals.CompletedGoalIds.ToList(),
                    Score = value.Assessment.Score,
                    RiskIds = value.Assessment.RiskIds.ToList(),
                    Evidence = value.Assessment.Evidence
                        .Select(EvidenceDocument.From).ToList(),
                    Observations = value.Observations.ToList()
                };
            }

            public CourseSessionState ToState()
            {
                Count(Entities, SessionArchiveReadLimits.MaxEntities, "entities");
                Count(Relations, SessionArchiveReadLimits.MaxRelations, "relations");
                Count(WorldStates, SessionArchiveReadLimits.MaxWorldStates,
                    "worldStates");
                foreach (var state in Require(WorldStates, "worldStates"))
                {
                    var requiredState = Require(state, "world state");
                    Count(
                        requiredState.Entries,
                        SessionArchiveReadLimits.MaxWorldStateEntries,
                        "world state entries");
                    foreach (var entry in Require(
                        requiredState.Entries,
                        "world state entries"))
                    {
                        Count(
                            Require(entry, "world state entry").Values,
                            SessionArchiveReadLimits.MaxParameters,
                            "world state values");
                    }
                }

                Count(Scalars, SessionArchiveReadLimits.MaxWorldStateEntries,
                    "scalars");
                Count(SpatialPoses ?? new List<SpatialPoseDocument>(),
                    SessionArchiveReadLimits.MaxEntities,
                    "spatialPoses");
                Count(Processes, SessionArchiveReadLimits.MaxProcesses, "processes");
                Count(Events, SessionArchiveReadLimits.MaxEvents, "events");
                Count(Commands, SessionArchiveReadLimits.MaxCommands, "commands");
                Count(CompletedGoalIds, SessionArchiveReadLimits.MaxEntities,
                    "completedGoalIds");
                Count(RiskIds, SessionArchiveReadLimits.MaxEvents, "riskIds");
                Count(Evidence, SessionArchiveReadLimits.MaxEvents, "evidence");
                Count(Observations, SessionArchiveReadLimits.MaxEvents,
                    "observations");
                return CourseSessionState.RestoreCurrent(
                    Require(Entities, "entities").Select(value =>
                        Require(value, "entity").ToState()),
                    Require(Relations, "relations").Select(value =>
                        Require(value, "relation").ToState()),
                    Require(WorldStates, "worldStates").Select(value =>
                        Require(value, "world state").ToState()),
                    Require(Scalars, "scalars").Select(value =>
                        Require(value, "scalar").ToState()),
                    Require(Processes, "processes").Select(value =>
                        Require(value, "process").ToState()),
                    Require(Events, "events").Select(value =>
                        Require(value, "event").ToState()),
                    Require(Commands, "commands").Select(value =>
                        Require(value, "command").ToState()),
                    NextEventSequence,
                    new CourseGoalEvaluationResult(
                        Require(CompletedGoalIds, "completedGoalIds")),
                    new CourseAssessmentEvaluationResult(
                        Score,
                        Require(RiskIds, "riskIds"),
                        Require(Evidence, "evidence").Select(value =>
                            Require(value, "evidence item").ToState())),
                    Require(Observations, "observations"),
                    (SpatialPoses ?? new List<SpatialPoseDocument>())
                        .Select(value => Require(value, "spatial pose").ToState()));
            }
        }

        private sealed class CapabilityDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string CapabilityId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public decimal NumberValue { get; set; }
            public string TextValue { get; set; }
            public List<CapabilityTextPropertyDocument> TextProperties
            {
                get;
                set;
            }

            public CourseCapabilityState ToState()
            {
                var properties = TextProperties
                    ?? new List<CapabilityTextPropertyDocument>();
                Count(
                    properties,
                    SessionArchiveReadLimits.MaxParameters,
                    "capability text properties");
                return new CourseCapabilityState(
                    CapabilityId,
                    NumberValue,
                    TextValue,
                    properties.Select(value =>
                        Require(
                            value,
                            "capability text property").ToState()));
            }
        }

        private sealed class CapabilityTextPropertyDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string Key { get; set; }
            public string Value { get; set; }

            public KeyValuePair<string, string> ToState() =>
                new KeyValuePair<string, string>(Key, Value);
        }

        private sealed class EntityDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string EntityId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<CapabilityDocument> Capabilities { get; set; }

            public static EntityDocument From(CourseEntityState value) =>
                new EntityDocument
                {
                    EntityId = value.EntityId,
                    Capabilities = value.Capabilities.Select(item =>
                        new CapabilityDocument
                        {
                            CapabilityId = item.CapabilityId,
                            NumberValue = item.NumberValue,
                            TextValue = item.TextValue,
                            TextProperties = item.TextProperties.Select(property =>
                                new CapabilityTextPropertyDocument
                                {
                                    Key = property.Key,
                                    Value = property.Value
                                }).ToList()
                        }).ToList()
                };

            public CourseEntityState ToState()
            {
                Count(Capabilities, SessionArchiveReadLimits.MaxParameters,
                    "capabilities");
                return new CourseEntityState(
                    EntityId,
                    Require(Capabilities, "capabilities").Select(value =>
                        Require(value, "capability").ToState()));
            }
        }

        private sealed class RelationDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string TypeId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string SourceEntityId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string TargetEntityId { get; set; }
            public string SourcePortId { get; set; }
            public string TargetPortId { get; set; }

            public static RelationDocument From(CourseRelationState value) =>
                new RelationDocument
                {
                    TypeId = value.TypeId.Value,
                    SourceEntityId = value.SourceEntityId,
                    TargetEntityId = value.TargetEntityId,
                    SourcePortId = value.SourcePortId,
                    TargetPortId = value.TargetPortId
                };

            public CourseRelationState ToState() =>
                new CourseRelationState(
                    new RelationTypeId(TypeId),
                    SourceEntityId,
                    TargetEntityId,
                    SourcePortId,
                    TargetPortId);
        }

        private sealed class WorldStateDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string TypeId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<WorldStateEntryDocument> Entries { get; set; }

            public static WorldStateDocument From(CourseWorldState value) =>
                new WorldStateDocument
                {
                    TypeId = value.TypeId.Value,
                    Entries = value.Entries
                        .Select(WorldStateEntryDocument.From).ToList()
                };

            public CourseWorldState ToState() =>
                new CourseWorldState(
                    new WorldStateTypeId(TypeId),
                    Require(Entries, "world state entries").Select(value =>
                        Require(value, "world state entry").ToState()));
        }

        private sealed class WorldStateEntryDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string EntryType { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<WorldStateValueDocument> Values { get; set; }

            public static WorldStateEntryDocument From(
                CourseWorldStateEntry value) => new WorldStateEntryDocument
            {
                EntryType = value.EntryType,
                Values = value.Values.Select(item =>
                    new WorldStateValueDocument
                    {
                        Key = item.Key,
                        Value = item.Value
                    }).ToList()
            };

            public CourseWorldStateEntry ToState() =>
                new CourseWorldStateEntry(
                    EntryType,
                    Require(Values, "world state values").Select(value =>
                    {
                        var item = Require(value, "world state value");
                        return new KeyValuePair<string, string>(
                            item.Key,
                            item.Value);
                    }));
        }

        private sealed class WorldStateValueDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string Key { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string Value { get; set; }
        }

        private sealed class SpatialPoseDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string EntityId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public double PositionX { get; set; }
            [JsonProperty(Required = Required.Always)]
            public double PositionY { get; set; }
            [JsonProperty(Required = Required.Always)]
            public double PositionZ { get; set; }
            [JsonProperty(Required = Required.Always)]
            public double RotationX { get; set; }
            [JsonProperty(Required = Required.Always)]
            public double RotationY { get; set; }
            [JsonProperty(Required = Required.Always)]
            public double RotationZ { get; set; }

            public static SpatialPoseDocument From(
                CourseSpatialPoseState value) => new SpatialPoseDocument
            {
                EntityId = value.EntityId,
                PositionX = value.PositionX,
                PositionY = value.PositionY,
                PositionZ = value.PositionZ,
                RotationX = value.RotationX,
                RotationY = value.RotationY,
                RotationZ = value.RotationZ
            };

            public CourseSpatialPoseState ToState() =>
                new CourseSpatialPoseState(
                    EntityId,
                    PositionX,
                    PositionY,
                    PositionZ,
                    RotationX,
                    RotationY,
                    RotationZ);
        }

        private sealed class ScalarDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string Key { get; set; }
            [JsonProperty(Required = Required.Always)]
            public double Value { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string UnitId { get; set; }

            public static ScalarDocument From(CourseScalarState value) =>
                new ScalarDocument
                {
                    Key = value.Key,
                    Value = value.Value,
                    UnitId = value.UnitId
                };

            public CourseScalarState ToState() =>
                new CourseScalarState(Key, Value, UnitId);
        }

        private sealed class ProcessDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string ProcessId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string EntityId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public Dictionary<string, string> TextParameters { get; set; }
            [JsonProperty(Required = Required.Always)]
            public Dictionary<string, double> NumberParameters { get; set; }

            public static ProcessDocument From(CourseProcessState value) =>
                new ProcessDocument
                {
                    ProcessId = value.ProcessId,
                    EntityId = value.EntityId,
                    TextParameters = value.TextParameters.ToDictionary(
                        item => item.Key,
                        item => item.Value,
                        StringComparer.Ordinal),
                    NumberParameters = value.NumberParameters.ToDictionary(
                        item => item.Key,
                        item => item.Value,
                        StringComparer.Ordinal)
                };

            public CourseProcessState ToState()
            {
                Count(TextParameters, SessionArchiveReadLimits.MaxParameters,
                    "process text parameters");
                Count(NumberParameters, SessionArchiveReadLimits.MaxParameters,
                    "process number parameters");
                return new CourseProcessState(
                    ProcessId,
                    EntityId,
                    Require(TextParameters, "textParameters"),
                    Require(NumberParameters, "numberParameters"));
            }
        }

        private sealed class StructuredValueDocument
        {
            [JsonProperty(Required = Required.Always)]
            public StructuredValueKind Kind { get; set; }
            [JsonProperty(Required = Required.Always)]
            public bool Boolean { get; set; }
            [JsonProperty(Required = Required.Always)]
            public double Number { get; set; }
            public string Text { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<string> TextList { get; set; }

            public static StructuredValueDocument From(StructuredValue value) =>
                new StructuredValueDocument
                {
                    Kind = value.Kind,
                    Boolean = value.Boolean,
                    Number = value.Number,
                    Text = value.Text,
                    TextList = value.TextList.ToList()
                };

            public StructuredValue ToState() =>
                new StructuredValue(
                    Kind,
                    Boolean,
                    Number,
                    Text,
                    Require(TextList, "textList"));
        }

        private sealed class EventDocument
        {
            [JsonProperty(Required = Required.Always)]
            public long Sequence { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string CommandId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public long Tick { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string EventType { get; set; }
            [JsonProperty(Required = Required.Always)]
            public Dictionary<string, StructuredValueDocument> Payload
            {
                get;
                set;
            }

            public static EventDocument From(CourseEventState value) =>
                From(
                    value.Sequence,
                    value.CommandId,
                    value.Tick,
                    value.EventType,
                    value.Payload);

            public static EventDocument From(DomainEventEnvelope value)
            {
                var configured = value.Event as ConfiguredCourseDomainEvent;
                if (configured == null)
                {
                    throw new InvalidOperationException(
                        "当前课程存档只支持配置课程领域事件。");
                }

                return From(
                    value.Sequence,
                    value.CommandId,
                    value.Tick.Value,
                    value.EventType,
                    configured.Payload);
            }

            private static EventDocument From(
                long sequence,
                string commandId,
                long tick,
                string eventType,
                IEnumerable<KeyValuePair<string, StructuredValue>> payload) =>
                new EventDocument
                {
                    Sequence = sequence,
                    CommandId = commandId,
                    Tick = tick,
                    EventType = eventType,
                    Payload = payload.ToDictionary(
                        item => item.Key,
                        item => StructuredValueDocument.From(item.Value),
                        StringComparer.Ordinal)
                };

            public CourseEventState ToState() =>
                new CourseEventState(
                    Sequence,
                    CommandId,
                    Tick,
                    EventType,
                    ToPayload());

            public DomainEventEnvelope ToEnvelope() =>
                new DomainEventEnvelope(
                    Sequence,
                    CommandId,
                    new SimulationTick(Tick),
                    new ConfiguredCourseDomainEvent(
                        EventType,
                        ToPayload().ToDictionary(
                            item => item.Key,
                            item => item.Value,
                            StringComparer.Ordinal)));

            private IEnumerable<KeyValuePair<string, StructuredValue>>
                ToPayload()
            {
                Count(Payload, SessionArchiveReadLimits.MaxParameters,
                    "event payload");
                return Require(Payload, "payload").Select(item =>
                    new KeyValuePair<string, StructuredValue>(
                        item.Key,
                        Require(item.Value, "payload value").ToState()));
            }
        }

        private sealed class RequestDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string CommandId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string ActionId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string ActorEntityId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string SourceEntityId { get; set; }
            public string TargetEntityId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public Dictionary<string, StructuredValueDocument> Parameters
            {
                get;
                set;
            }

            public static RequestDocument From(SemanticActionRequest value) =>
                new RequestDocument
                {
                    CommandId = value.CommandId,
                    ActionId = value.ActionId,
                    ActorEntityId = value.ActorEntityId,
                    SourceEntityId = value.SourceEntityId,
                    TargetEntityId = value.TargetEntityId,
                    Parameters = value.Parameters.ToDictionary(
                        item => item.Key,
                        item => StructuredValueDocument.From(item.Value),
                        StringComparer.Ordinal)
                };

            public SemanticActionRequest ToState()
            {
                Count(Parameters, SessionArchiveReadLimits.MaxParameters,
                    "command parameters");
                return new SemanticActionRequest(
                    CommandId,
                    ActionId,
                    ActorEntityId,
                    SourceEntityId,
                    TargetEntityId,
                    Require(Parameters, "parameters").Select(item =>
                        new KeyValuePair<string, StructuredValue>(
                            item.Key,
                            Require(item.Value, "parameter value").ToState())));
            }
        }

        private sealed class ResultDocument
        {
            [JsonProperty(Required = Required.Always)]
            public bool IsAccepted { get; set; }
            public string RejectionCode { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<string> RejectionCodes { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<EventDocument> Events { get; set; }

            public static ResultDocument From(CommandResult value) =>
                new ResultDocument
                {
                    IsAccepted = value.IsAccepted,
                    RejectionCode = value.RejectionCode,
                    RejectionCodes = value.RejectionCodes.ToList(),
                    Events = value.Events.Select(EventDocument.From).ToList()
                };

            public CommandResult ToState()
            {
                if (IsAccepted)
                {
                    return CommandResult.Accepted(
                        Require(Events, "result events")
                            .Select(value =>
                                Require(value, "result event").ToEnvelope())
                            .ToArray());
                }

                return CommandResult.Rejected(
                    RejectionCode,
                    Require(RejectionCodes, "rejectionCodes"));
            }
        }

        private sealed class CommandDocument
        {
            [JsonProperty(Required = Required.Always)]
            public RequestDocument Request { get; set; }
            [JsonProperty(Required = Required.Always)]
            public ResultDocument Result { get; set; }

            public static CommandDocument From(CourseExecutedCommandState value) =>
                new CommandDocument
                {
                    Request = RequestDocument.From(value.Request),
                    Result = ResultDocument.From(value.Result)
                };

            public CourseExecutedCommandState ToState() =>
                new CourseExecutedCommandState(
                    Require(Request, "request").ToState(),
                    Require(Result, "result").ToState());
        }

        private sealed class EvidenceDocument
        {
            [JsonProperty(Required = Required.Always)]
            public string AssessmentId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string RiskId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public int ScoreDelta { get; set; }
            [JsonProperty(Required = Required.Always)]
            public string Prompt { get; set; }
            public string CommandId { get; set; }
            [JsonProperty(Required = Required.Always)]
            public CourseConsequenceSeverity Severity { get; set; }
            [JsonProperty(Required = Required.Always)]
            public CourseConsequenceRecoverability Recoverability { get; set; }
            [JsonProperty(Required = Required.Always)]
            public List<string> BlockedGoalIds { get; set; }

            public static EvidenceDocument From(
                CourseAssessmentEvidence value) =>
                new EvidenceDocument
                {
                    AssessmentId = value.AssessmentId,
                    RiskId = value.RiskId,
                    ScoreDelta = value.ScoreDelta,
                    Prompt = value.Prompt,
                    CommandId = value.CommandId,
                    Severity = value.Severity,
                    Recoverability = value.Recoverability,
                    BlockedGoalIds = value.BlockedGoalIds.ToList()
                };

            public CourseAssessmentEvidence ToState()
            {
                Count(
                    BlockedGoalIds,
                    SessionArchiveReadLimits.MaxEntities,
                    "blockedGoalIds");
                return new CourseAssessmentEvidence(
                    AssessmentId,
                    RiskId,
                    ScoreDelta,
                    Prompt,
                    CommandId,
                    Severity,
                    Recoverability,
                    Require(BlockedGoalIds, "blockedGoalIds"));
            }
        }

        private static T Require<T>(T value, string field)
            where T : class
        {
            if (value == null)
            {
                throw new JsonSerializationException(
                    "缺少存档字段：" + field);
            }

            return value;
        }

        private static void Count<T>(
            ICollection<T> values,
            int maximum,
            string field)
        {
            if (values == null || values.Count > maximum)
            {
                throw new JsonSerializationException(
                    "存档集合超出限制：" + field);
            }
        }
    }

    public sealed class StoreResult
    {
        private StoreResult(string path, string errorCode)
        {
            Path = path;
            ErrorCode = errorCode;
        }

        public bool IsSuccess => ErrorCode == null;
        public string ErrorCode { get; }
        public string Path { get; }

        public static StoreResult Success(string path) =>
            new StoreResult(path, null);

        public static StoreResult Failure(string errorCode) =>
            new StoreResult(null, errorCode);
    }

    public sealed class SessionLoadResult
    {
        private SessionLoadResult(
            SessionArchive archive,
            string errorCode)
        {
            Archive = archive;
            ErrorCode = errorCode;
        }

        public bool IsSuccess => Archive != null;
        public string ErrorCode { get; }
        public SessionArchive Archive { get; }

        public static SessionLoadResult Success(SessionArchive archive) =>
            new SessionLoadResult(archive, null);

        public static SessionLoadResult Failure(string errorCode) =>
            new SessionLoadResult(null, errorCode);
    }

    public sealed class LocalSessionStore
    {
        private readonly string _rootDirectory;
        private readonly bool _rootIsValid;

        public LocalSessionStore(string rootDirectory)
        {
            try
            {
                _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                    ? null
                    : Path.GetFullPath(rootDirectory.Trim());
                _rootIsValid = _rootDirectory != null
                    && !ContainsReparsePoint(_rootDirectory);
            }
            catch (Exception error) when (
                error is ArgumentException
                || error is NotSupportedException
                || error is PathTooLongException)
            {
                _rootDirectory = null;
                _rootIsValid = false;
            }
        }

        public StoreResult Save(string relativePath, SessionArchive archive)
        {
            if (!TryResolve(relativePath, out var targetPath))
            {
                return StoreResult.Failure("session.path.invalid");
            }

            if (archive == null)
            {
                return StoreResult.Failure("session.data.invalid");
            }

            string json;
            try
            {
                json = SessionJson.Serialize(archive);
                if (SessionArchiveReadLimits.StrictUtf8.GetByteCount(json)
                    > SessionArchiveReadLimits.MaxFileBytes)
                {
                    return StoreResult.Failure("session.data.too_large");
                }
            }
            catch (Exception error) when (
                error is JsonException
                || error is ArgumentException
                || error is InvalidOperationException)
            {
                return StoreResult.Failure("session.data.invalid");
            }

            var directory = Path.GetDirectoryName(targetPath);
            var temporary = Path.Combine(
                directory,
                "." + Path.GetFileName(targetPath) + "."
                    + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(directory);
                if (ContainsReparsePoint(_rootDirectory)
                    || ContainsReparsePoint(directory))
                {
                    return StoreResult.Failure("session.path.invalid");
                }

                File.WriteAllText(
                    temporary,
                    json,
                    SessionArchiveReadLimits.StrictUtf8);
                if (File.Exists(targetPath))
                {
                    File.Replace(temporary, targetPath, null);
                }
                else
                {
                    File.Move(temporary, targetPath);
                }

                return StoreResult.Success(targetPath);
            }
            catch (Exception error) when (
                error is IOException
                || error is UnauthorizedAccessException
                || error is ArgumentException
                || error is NotSupportedException
                || error is PathTooLongException)
            {
                return StoreResult.Failure("session.write.failed");
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (Exception error) when (
                    error is IOException
                    || error is UnauthorizedAccessException)
                {
                }
            }
        }

        public SessionLoadResult Load(string relativePath)
        {
            if (!TryResolve(relativePath, out var targetPath))
            {
                return SessionLoadResult.Failure("session.path.invalid");
            }

            try
            {
                if (ContainsReparsePoint(_rootDirectory)
                    || ContainsReparsePoint(targetPath))
                {
                    return SessionLoadResult.Failure("session.path.invalid");
                }

                var file = new FileInfo(targetPath);
                if (!file.Exists)
                {
                    return SessionLoadResult.Failure("session.read.failed");
                }

                if (file.Length > SessionArchiveReadLimits.MaxFileBytes)
                {
                    return SessionLoadResult.Failure(
                        "session.data.too_large");
                }

                var json = File.ReadAllText(
                    targetPath,
                    SessionArchiveReadLimits.StrictUtf8);
                return SessionJson.TryDeserialize(
                    json,
                    out var archive,
                    out var errorCode)
                    ? SessionLoadResult.Success(archive)
                    : SessionLoadResult.Failure(errorCode);
            }
            catch (DecoderFallbackException)
            {
                return SessionLoadResult.Failure("session.data.invalid");
            }
            catch (Exception error) when (
                error is IOException
                || error is UnauthorizedAccessException
                || error is ArgumentException
                || error is NotSupportedException
                || error is PathTooLongException)
            {
                return SessionLoadResult.Failure("session.read.failed");
            }
        }

        private bool TryResolve(string relativePath, out string resolvedPath)
        {
            resolvedPath = null;
            if (!_rootIsValid
                || string.IsNullOrWhiteSpace(relativePath)
                || Path.IsPathRooted(relativePath))
            {
                return false;
            }

            try
            {
                var candidate = Path.GetFullPath(
                    Path.Combine(_rootDirectory, relativePath.Trim()));
                var prefix = _rootDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                resolvedPath = candidate;
                return true;
            }
            catch (Exception error) when (
                error is ArgumentException
                || error is NotSupportedException
                || error is PathTooLongException)
            {
                return false;
            }
        }

        private static bool ContainsReparsePoint(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            var current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if ((Directory.Exists(current) || File.Exists(current))
                    && (File.GetAttributes(current)
                        & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                current = Path.GetDirectoryName(current);
            }

            return false;
        }
    }
}
