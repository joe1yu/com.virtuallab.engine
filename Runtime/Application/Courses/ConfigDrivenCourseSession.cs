using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Events;
using VirtualLab.Domain;
using VirtualLab.Domain.Events;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Courses
{
    public enum ConfiguredActionPolicyEffect
    {
        Allow,
        Deny
    }

    /// <summary>
    /// 一个语义动作对应的有序规则和状态变更。
    /// </summary>
    public sealed class ConfiguredActionDefinition
    {
        public static ConfiguredActionDefinition CreateGeneric(
            string actionId,
            IEnumerable<StructuredRuleDefinition> rules,
            IEnumerable<ConfiguredMutationDefinition> mutations)
        {
            return new ConfiguredActionDefinition(
                actionId,
                actionId,
                null,
                null,
                true,
                100,
                ConfiguredActionPolicyEffect.Allow,
                null,
                null,
                rules,
                mutations);
        }

        public ConfiguredActionDefinition(
            string policyId,
            string actionId,
            string sourceEntityId,
            string targetEntityId,
            IEnumerable<StructuredRuleDefinition> rules,
            IEnumerable<ConfiguredMutationDefinition> mutations,
            int priority = 100,
            ConfiguredActionPolicyEffect effect =
                ConfiguredActionPolicyEffect.Allow,
            string messageId = null,
            string rejectionCode = null,
            bool matchesAnyEntities = false)
            : this(
                policyId,
                actionId,
                sourceEntityId,
                targetEntityId,
                matchesAnyEntities,
                priority,
                effect,
                messageId,
                rejectionCode,
                rules,
                mutations)
        {
        }

        public static ConfiguredActionDefinition CreatePolicy(
            string policyId,
            string actionId,
            string sourceEntityId,
            string targetEntityId,
            int priority,
            ConfiguredActionPolicyEffect effect,
            string messageId,
            IEnumerable<StructuredRuleDefinition> rules,
            IEnumerable<ConfiguredMutationDefinition> mutations,
            string rejectionCode = null)
        {
            return new ConfiguredActionDefinition(
                policyId,
                actionId,
                sourceEntityId,
                targetEntityId,
                false,
                priority,
                effect,
                messageId,
                rejectionCode,
                rules,
                mutations);
        }

        private ConfiguredActionDefinition(
            string policyId,
            string actionId,
            string sourceEntityId,
            string targetEntityId,
            bool matchesAnyEntities,
            int priority,
            ConfiguredActionPolicyEffect effect,
            string messageId,
            string rejectionCode,
            IEnumerable<StructuredRuleDefinition> rules,
            IEnumerable<ConfiguredMutationDefinition> mutations)
        {
            PolicyId = CourseContractGuard.Required(policyId, "动作策略 ID");
            ActionId = CourseContractGuard.Required(actionId, "动作 ID");
            if (priority < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(priority),
                    priority,
                    $"动作策略“{PolicyId}”的优先级不能小于 0。");
            }

            if (!Enum.IsDefined(typeof(ConfiguredActionPolicyEffect), effect))
            {
                throw new ArgumentOutOfRangeException(nameof(effect));
            }

            SourceEntityId = matchesAnyEntities
                ? CourseContractGuard.Optional(sourceEntityId)
                : CourseContractGuard.Required(
                    sourceEntityId,
                    $"动作策略“{PolicyId}”的来源实体 ID");
            TargetEntityId = CourseContractGuard.Optional(targetEntityId);
            MatchesAnyEntities = matchesAnyEntities;
            Priority = priority;
            Effect = effect;
            MessageId = CourseContractGuard.Optional(messageId);
            RejectionCode = CourseContractGuard.Optional(rejectionCode);
            if (Effect == ConfiguredActionPolicyEffect.Deny &&
                RejectionCode == null)
            {
                throw new ArgumentException(
                    $"禁用策略“{PolicyId}”必须声明拒绝代码。",
                    nameof(rejectionCode));
            }

            Rules = CourseContractGuard.CopyUnique(
                rules,
                value => value.RuleId,
                $"动作策略“{PolicyId}”的规则");
            Mutations = CourseContractGuard.CopyUnique(
                mutations,
                value => value.MutationId,
                $"动作策略“{PolicyId}”的状态变更");
            if (Effect == ConfiguredActionPolicyEffect.Deny &&
                Mutations.Count > 0)
            {
                throw new ArgumentException(
                    $"禁用策略“{PolicyId}”不能包含状态变更。",
                    nameof(mutations));
            }
        }

        public string PolicyId { get; }

        public string ActionId { get; }

        public string SourceEntityId { get; }

        public string TargetEntityId { get; }

        public bool MatchesAnyEntities { get; }

        public int Priority { get; }

        public ConfiguredActionPolicyEffect Effect { get; }

        public string MessageId { get; }

        public string RejectionCode { get; }

        public IReadOnlyList<StructuredRuleDefinition> Rules { get; }

        public IReadOnlyList<ConfiguredMutationDefinition> Mutations { get; }

        public bool Matches(SemanticActionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return string.Equals(
                    ActionId,
                    request.ActionId,
                    StringComparison.Ordinal)
                && (MatchesAnyEntities
                    || (string.Equals(
                            SourceEntityId,
                            request.SourceEntityId,
                            StringComparison.Ordinal)
                        && string.Equals(
                            TargetEntityId,
                            request.TargetEntityId,
                            StringComparison.Ordinal)));
        }
    }

    /// <summary>
    /// 配置驱动课程的事务边界与命令幂等边界。
    /// </summary>
    public sealed class ConfigDrivenCourseSession
    {
        private const string ActionNotConfigured = "动作未配置";
        private const string ActionPolicyNotApplicable = "动作策略不适用";
        private const string StateOperationFailed = "状态操作失败";
        private const string CommandIdConflict = "命令标识冲突";

        private readonly ExperimentWorld _world;
        private readonly StructuredRuleEvaluator _ruleEvaluator;
        private readonly ConfiguredStateOperationRegistry _operationRegistry;
        private readonly IReadOnlyDictionary<
            string,
            IReadOnlyList<ConfiguredActionDefinition>>
            _actions;
        private readonly CourseAssessmentEvaluator _assessmentEvaluator;
        private readonly IReadOnlyList<CourseActionAssessmentDefinition>
            _actionAssessments;
        private readonly int _maximumScore;
        private readonly Dictionary<string, CourseExecutedCommandState> _commands =
            new Dictionary<string, CourseExecutedCommandState>(
                StringComparer.Ordinal);
        private readonly CourseEventStream _eventStream;
        private CourseGoalEvaluationResult _goalEvaluation =
            new CourseGoalEvaluationResult(Array.Empty<string>());
        private CourseAssessmentEvaluationResult _assessmentEvaluation =
            new CourseAssessmentEvaluationResult(
                0,
                Array.Empty<string>(),
                Array.Empty<CourseAssessmentEvidence>());
        private IReadOnlyList<string> _observations = Array.Empty<string>();

        public ConfigDrivenCourseSession(
            ExperimentWorld world,
            StructuredRuleEvaluator ruleEvaluator,
            ConfiguredStateOperationRegistry operationRegistry,
            IEnumerable<ConfiguredActionDefinition> actions,
            CourseAssessmentEvaluator assessmentEvaluator = null,
            IEnumerable<CourseActionAssessmentDefinition>
                actionAssessments = null,
            int maximumScore = 0,
            CourseEventStream eventStream = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _ruleEvaluator = ruleEvaluator
                ?? throw new ArgumentNullException(nameof(ruleEvaluator));
            _operationRegistry = operationRegistry
                ?? throw new ArgumentNullException(nameof(operationRegistry));
            _actions = CopyActions(actions);
            _assessmentEvaluator = assessmentEvaluator;
            _actionAssessments = (actionAssessments
                ?? Array.Empty<CourseActionAssessmentDefinition>()).ToArray();
            if (maximumScore < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumScore));
            }

            _maximumScore = maximumScore;
            _eventStream = eventStream ?? new CourseEventStream();
            _assessmentEvaluation = new CourseAssessmentEvaluationResult(
                maximumScore,
                Array.Empty<string>(),
                Array.Empty<CourseAssessmentEvidence>());
        }

        internal ExperimentWorld World => _world;

        internal CourseAssessmentEvaluationResult CurrentAssessment =>
            _assessmentEvaluation;

        internal CourseEventStream EventStream => _eventStream;

        /// <summary>
        /// 使用与实际执行相同的候选选择和规则裁决查询动作可操作性。
        /// 查询只读取实验世界，不记录命令，也不执行任何状态变更。
        /// </summary>
        public ActionAvailability QueryAvailability(
            SemanticActionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return EvaluateAction(request).Availability;
        }

        public CommandResult Execute(SemanticActionRequest request)
        {
            return Execute(request, new SimulationTick(0));
        }

        internal CommandResult Execute(
            SemanticActionRequest request,
            SimulationTick tick)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_commands.TryGetValue(
                request.CommandId,
                out var previous))
            {
                return RequestsMatch(previous.Request, request)
                    ? previous.Result
                    : CommandResult.Rejected(CommandIdConflict);
            }

            var evaluated = EvaluateAction(request);
            if (!evaluated.Availability.IsAllowed)
            {
                return Remember(
                    request,
                    CommandResult.Rejected(
                        evaluated.Availability.RejectionCode,
                        evaluated.Availability.RejectionCodes));
            }

            var action = evaluated.Action;
            try
            {
                var applied = _operationRegistry.ApplyAtomically(
                    request,
                    _world,
                    action.Mutations);
                var events = new List<DomainEventEnvelope>(
                    applied.EmittedEvents.Count);
                foreach (var configuredEvent in applied.EmittedEvents)
                {
                    events.Add(_eventStream.Append(
                        request.CommandId,
                        tick,
                        new ConfiguredCourseDomainEvent(
                            configuredEvent.EventType,
                            configuredEvent.Payload)));
                }

                return Remember(
                    request,
                    CommandResult.Accepted(events));
            }
            catch (ConfiguredStateOperationException exception)
            {
                if (exception.InnerException
                    is ConfiguredOperationRejectedException rejected)
                {
                    return Remember(
                        request,
                        CommandResult.Rejected(
                            rejected.RejectionCodes[0],
                            rejected.RejectionCodes));
                }

                return Remember(
                    request,
                    CommandResult.Rejected(
                        StateOperationFailed,
                        new[]
                        {
                            StateOperationFailed,
                            $"{StateOperationFailed}.{exception.OperationId}"
                        }));
            }
        }

        private EvaluatedAction EvaluateAction(
            SemanticActionRequest request)
        {
            if (!_actions.TryGetValue(request.ActionId, out var candidates))
            {
                return EvaluatedAction.Rejected(
                    Availability(
                        request,
                        ActionAvailabilityKind.Unsupported,
                        ActionNotConfigured,
                        null,
                        new[] { ActionNotConfigured }));
            }

            var matched = candidates
                .Where(value => value.Matches(request))
                .ToArray();
            if (matched.Length == 0)
            {
                return EvaluatedAction.Rejected(
                    Availability(
                        request,
                        ActionAvailabilityKind.Unsupported,
                        ActionPolicyNotApplicable,
                        null,
                        new[] { ActionPolicyNotApplicable }));
            }

            var context = new StructuredRuleContext(request, _world);
            var rejectionCodes = new List<string>();
            string blockedMessageId = null;

            // 明确禁用是课程语义，不是“没有配置”。只要禁用策略适用，
            // 就优先返回 Disabled，让表现层得到稳定、可解释的结果。
            foreach (var candidate in matched.Where(value =>
                         value.Effect == ConfiguredActionPolicyEffect.Deny))
            {
                var decision = _ruleEvaluator.Evaluate(
                    candidate.Rules,
                    context);
                if (decision.IsAccepted)
                {
                    return EvaluatedAction.Rejected(
                        Availability(
                            request,
                            ActionAvailabilityKind.Disabled,
                            candidate.RejectionCode,
                            candidate.MessageId,
                            new[] { candidate.RejectionCode }));
                }

                AddUnique(rejectionCodes, decision.RejectionCodes);
            }

            // 同一实体选择器可以有多个允许候选，用于表达逻辑“或”。
            foreach (var candidate in matched.Where(value =>
                         value.Effect == ConfiguredActionPolicyEffect.Allow))
            {
                var decision = _ruleEvaluator.Evaluate(
                    candidate.Rules,
                    context);
                if (decision.IsAccepted)
                {
                    return EvaluatedAction.Allowed(
                        candidate,
                        Availability(
                            request,
                            ActionAvailabilityKind.Allowed,
                            null,
                            null,
                            Array.Empty<string>()));
                }

                if (blockedMessageId == null)
                {
                    blockedMessageId = candidate.MessageId;
                }

                AddUnique(rejectionCodes, decision.RejectionCodes);
            }

            if (rejectionCodes.Count == 0)
            {
                rejectionCodes.Add(ActionPolicyNotApplicable);
            }

            return EvaluatedAction.Rejected(
                Availability(
                    request,
                    ActionAvailabilityKind.TemporarilyBlocked,
                    rejectionCodes[0],
                    blockedMessageId,
                    rejectionCodes));
        }

        private static ActionAvailability Availability(
            SemanticActionRequest request,
            ActionAvailabilityKind kind,
            string rejectionCode,
            string messageId,
            IEnumerable<string> rejectionCodes)
        {
            return new ActionAvailability(
                request.ActionId,
                request.ActorEntityId,
                request.SourceEntityId,
                request.TargetEntityId,
                kind,
                rejectionCode,
                messageId,
                rejectionCodes);
        }

        private static void AddUnique(
            ICollection<string> destination,
            IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                if (!destination.Contains(value))
                {
                    destination.Add(value);
                }
            }
        }

        /// <summary>
        /// 使用与科学动作相同的结构化规则引擎评估权威表现状态。
        /// 该方法只读取实验世界，不产生领域变更或表现命令。
        /// </summary>
        public IReadOnlyList<string> EvaluatePresentationStates(
            IEnumerable<CoursePresentationStateDefinition> states)
        {
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            return states
                .Where(state =>
                {
                    if (state == null)
                    {
                        throw new ArgumentException(
                            "表现状态不能包含空项。",
                            nameof(states));
                    }

                    var request = new SemanticActionRequest(
                        "表现状态评估:" + state.StateId,
                        "表现状态.评估",
                        "系统",
                        state.ContextSourceEntityId,
                        state.ContextTargetEntityId,
                        Array.Empty<
                            KeyValuePair<string, StructuredValue>>());
                    return _ruleEvaluator.Evaluate(
                        state.Rules,
                        new StructuredRuleContext(request, _world))
                        .IsAccepted;
                })
                .Select(state => state.StateId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        public void RecordEvaluation(
            CourseGoalEvaluationResult goals,
            CourseAssessmentEvaluationResult assessment,
            IEnumerable<string> observations)
        {
            _goalEvaluation = goals
                ?? throw new ArgumentNullException(nameof(goals));
            _assessmentEvaluation = assessment
                ?? throw new ArgumentNullException(nameof(assessment));
            _observations = CourseContractGuard.CopyStrings(
                observations,
                "实验观察");
        }

        public CourseSessionState ExportState(
            IEnumerable<CourseSpatialPoseState> spatialPoses = null)
        {
            return CourseSessionState.Capture(
                _world,
                _eventStream.States,
                _commands.Values,
                _eventStream.NextSequence,
                _goalEvaluation,
                _assessmentEvaluation,
                _observations,
                spatialPoses);
        }

        public static ConfigDrivenCourseSession Restore(
            CourseRuntimeDefinition runtime,
            CourseSessionState state)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var session = runtime.CreateSession(
                state.RestoreWorld(runtime.PrepareWorld),
                state.Events);
            if (session._eventStream.NextSequence != state.NextEventSequence)
            {
                throw new CourseStateRestoreException(
                    "session.event-sequence.invalid",
                    "存档中的下一事件序号与课程事件流不一致。");
            }
            session._goalEvaluation = state.Goals;
            session._assessmentEvaluation = state.Assessment;
            session._observations = state.Observations.ToArray();
            foreach (var command in state.Commands)
            {
                session._commands.Add(command.Request.CommandId, command);
            }

            return session;
        }

        public static ConfigDrivenCourseSession Restore(
            CourseRuntimeDefinition runtime,
            string serializedState)
        {
            throw new CourseStateRestoreException(
                "session.format.unsupported",
                "输入不是当前内存结构的会话状态。");
        }

        private CommandResult Remember(
            SemanticActionRequest request,
            CommandResult result)
        {
            if (_assessmentEvaluator != null)
            {
                _assessmentEvaluation =
                    _assessmentEvaluator.EvaluateAction(
                        _world,
                        request,
                        result,
                        _actionAssessments,
                        _maximumScore,
                        _assessmentEvaluation);
            }

            _commands.Add(
                request.CommandId,
                new CourseExecutedCommandState(request, result));
            return result;
        }

        internal static bool RequestsMatch(
            SemanticActionRequest left,
            SemanticActionRequest right)
        {
            if (!string.Equals(left.ActionId, right.ActionId, StringComparison.Ordinal)
                || !string.Equals(left.ActorEntityId, right.ActorEntityId, StringComparison.Ordinal)
                || !string.Equals(left.SourceEntityId, right.SourceEntityId, StringComparison.Ordinal)
                || !string.Equals(left.TargetEntityId, right.TargetEntityId, StringComparison.Ordinal)
                || left.Parameters.Count != right.Parameters.Count)
            {
                return false;
            }

            foreach (var parameter in left.Parameters)
            {
                if (!right.Parameters.TryGetValue(parameter.Key, out var other)
                    || !ValuesMatch(parameter.Value, other))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValuesMatch(
            StructuredValue left,
            StructuredValue right)
        {
            if (left.Kind != right.Kind)
            {
                return false;
            }

            return left.Kind switch
            {
                StructuredValueKind.Null => true,
                StructuredValueKind.Boolean => left.Boolean == right.Boolean,
                StructuredValueKind.Number => left.Number.Equals(right.Number),
                StructuredValueKind.Text => string.Equals(
                    left.Text,
                    right.Text,
                    StringComparison.Ordinal),
                StructuredValueKind.TextList => left.TextList.SequenceEqual(
                    right.TextList,
                    StringComparer.Ordinal),
                _ => false
            };
        }

        private static IReadOnlyDictionary<
            string,
            IReadOnlyList<ConfiguredActionDefinition>> CopyActions(
                IEnumerable<ConfiguredActionDefinition> actions)
        {
            var copy = CourseContractGuard.CopyUnique(
                actions,
                value => value.PolicyId,
                "配置动作");
            var grouped = copy
                .GroupBy(value => value.ActionId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => (IReadOnlyList<ConfiguredActionDefinition>)
                        new ReadOnlyCollection<ConfiguredActionDefinition>(
                            value
                                .OrderByDescending(item => item.Priority)
                                .ThenBy(
                                    item => item.PolicyId,
                                    StringComparer.Ordinal)
                                .ToList()),
                    StringComparer.Ordinal);
            return new ReadOnlyDictionary<
                string,
                IReadOnlyList<ConfiguredActionDefinition>>(grouped);
        }

        private sealed class EvaluatedAction
        {
            private EvaluatedAction(
                ConfiguredActionDefinition action,
                ActionAvailability availability)
            {
                Action = action;
                Availability = availability
                    ?? throw new ArgumentNullException(
                        nameof(availability));
            }

            public ConfiguredActionDefinition Action { get; }

            public ActionAvailability Availability { get; }

            public static EvaluatedAction Allowed(
                ConfiguredActionDefinition action,
                ActionAvailability availability)
            {
                return new EvaluatedAction(
                    action ?? throw new ArgumentNullException(nameof(action)),
                    availability);
            }

            public static EvaluatedAction Rejected(
                ActionAvailability availability)
            {
                return new EvaluatedAction(null, availability);
            }
        }

    }

    public sealed class ConfiguredCourseDomainEvent : IDomainEvent
    {
        public ConfiguredCourseDomainEvent(
            string eventType,
            IReadOnlyDictionary<string, StructuredValue> payload)
        {
            EventType = eventType;
            Payload = payload
                ?? throw new ArgumentNullException(nameof(payload));
        }

        public string EventType { get; }

        public IReadOnlyDictionary<string, StructuredValue> Payload { get; }
    }
}
