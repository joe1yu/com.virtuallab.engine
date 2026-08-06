using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Courses;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    /// <summary>
    /// 统一协调一次性表现信号和权威持续状态。科学会话先完成事务，
    /// 表现分派随后执行，任何表现结果都不会写回科学世界。
    /// </summary>
    public sealed class CoursePresentationCoordinator
    {
        private readonly Func<
            IEnumerable<CoursePresentationStateDefinition>,
            IReadOnlyList<string>> _evaluatePresentationStates;
        private readonly IReadOnlyDictionary<
            string,
            CoursePresentationStateDefinition> _states;
        private readonly PresentationReactionEngine _reactions;
        private readonly IPresentationCommandDispatcher _dispatcher;
        private IReadOnlyList<string> _activeStateIds =
            Array.Empty<string>();
        private IReadOnlyList<PresentationEffectCommand> _lastCommands =
            Array.Empty<PresentationEffectCommand>();
        private IReadOnlyList<PresentationDispatchResult>
            _lastDispatchResults =
                Array.Empty<PresentationDispatchResult>();

        public CoursePresentationCoordinator(
            CourseRuntimeFacade runtime,
            IEnumerable<CoursePresentationStateDefinition> states,
            PresentationReactionEngine reactions,
            IPresentationCommandDispatcher dispatcher)
            : this(
                PresentationStateEvaluator(runtime),
                states,
                reactions,
                dispatcher)
        {
        }

        private CoursePresentationCoordinator(
            Func<
                IEnumerable<CoursePresentationStateDefinition>,
                IReadOnlyList<string>> evaluatePresentationStates,
            IEnumerable<CoursePresentationStateDefinition> states,
            PresentationReactionEngine reactions,
            IPresentationCommandDispatcher dispatcher)
        {
            _evaluatePresentationStates = evaluatePresentationStates
                ?? throw new ArgumentNullException(
                    nameof(evaluatePresentationStates));
            _states = CopyStates(states);
            _reactions = reactions ??
                throw new ArgumentNullException(nameof(reactions));
            _dispatcher = dispatcher ??
                throw new ArgumentNullException(nameof(dispatcher));
        }

        public IReadOnlyList<PresentationEffectCommand> LastCommands =>
            _lastCommands;

        public IReadOnlyList<PresentationDispatchResult>
            LastDispatchResults => _lastDispatchResults;

        public void InitializeOrRestore()
        {
            var commands = new List<PresentationEffectCommand>();
            var results = new List<PresentationDispatchResult>();
            DispatchSignal(
                new PresentationSignal(
                    PresentationSignalIds.CourseInitialized,
                    PresentationTriggerKind.CourseInitialized,
                    null,
                    null,
                    null,
                    Array.Empty<KeyValuePair<string, PresentationValue>>()),
                commands,
                results);
            var active = EvaluateActiveStateIds();
            SynchronizeActiveStates(
                active,
                commands,
                results);
            _activeStateIds = active;
            PublishLast(commands, results);
        }

        public void Present(
            SemanticActionRequest request,
            CommandResult result)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var commands = new List<PresentationEffectCommand>();
            var dispatchResults = new List<PresentationDispatchResult>();
            var before = _activeStateIds;

            DispatchActionSignal(
                request,
                result,
                commands,
                dispatchResults);
            DispatchDomainEventSignals(
                request,
                result,
                commands,
                dispatchResults);

            var after = EvaluateActiveStateIds();
            DispatchStateSignals(
                PresentationTriggerKind.StateEntered,
                after.Except(before, StringComparer.Ordinal),
                commands,
                dispatchResults);
            DispatchStateSignals(
                PresentationTriggerKind.StateExited,
                before.Except(after, StringComparer.Ordinal),
                commands,
                dispatchResults);
            SynchronizeActiveStates(
                after,
                commands,
                dispatchResults);
            _activeStateIds = after;
            PublishLast(commands, dispatchResults);
        }

        /// <summary>
        /// 将应用层的只读可操作性结果投影为通用表现信号。
        /// 此路径不读取规则，也不重新评估或改变权威科学状态。
        /// </summary>
        public void PresentAvailability(
            SemanticActionRequest request,
            ActionAvailability availability)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (availability == null)
            {
                throw new ArgumentNullException(nameof(availability));
            }

            EnsureAvailabilityMatches(request, availability);
            var commands = new List<PresentationEffectCommand>();
            var results = new List<PresentationDispatchResult>();
            DispatchSignal(
                new PresentationSignal(
                    request.ActionId,
                    PresentationTriggerKind.ActionAvailabilityChanged,
                    request.SourceEntityId,
                    request.SourceEntityId,
                    request.TargetEntityId,
                    new Dictionary<string, PresentationValue>
                    {
                        ["是否允许"] = PresentationValue.FromBoolean(
                            availability.IsAllowed),
                        ["可操作性分类"] = PresentationValue.FromText(
                            availability.Kind.ToString()),
                        ["拒绝代码"] = PresentationValue.FromText(
                            availability.RejectionCode ?? string.Empty),
                        ["文案ID"] = PresentationValue.FromText(
                            availability.MessageId ?? string.Empty)
                    }),
                commands,
                results);
            PublishLast(commands, results);
        }

        /// <summary>
        /// 接收学科持续过程产生的额外表现信号，但仍通过同一反应和分派边界。
        /// </summary>
        public void PresentSignal(PresentationSignal signal)
        {
            if (signal == null)
            {
                throw new ArgumentNullException(nameof(signal));
            }

            var commands = new List<PresentationEffectCommand>();
            var results = new List<PresentationDispatchResult>();
            var before = _activeStateIds;
            DispatchSignal(signal, commands, results);
            var after = EvaluateActiveStateIds();
            DispatchStateSignals(
                PresentationTriggerKind.StateEntered,
                after.Except(before, StringComparer.Ordinal),
                commands,
                results);
            DispatchStateSignals(
                PresentationTriggerKind.StateExited,
                before.Except(after, StringComparer.Ordinal),
                commands,
                results);
            SynchronizeActiveStates(after, commands, results);
            _activeStateIds = after;
            PublishLast(commands, results);
        }

        private IReadOnlyList<string> EvaluateActiveStateIds()
        {
            return _evaluatePresentationStates(_states.Values);
        }

        private static Func<
            IEnumerable<CoursePresentationStateDefinition>,
            IReadOnlyList<string>> PresentationStateEvaluator(
                CourseRuntimeFacade runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            return runtime.EvaluatePresentationStates;
        }

        private void DispatchActionSignal(
            SemanticActionRequest request,
            CommandResult result,
            ICollection<PresentationEffectCommand> commands,
            ICollection<PresentationDispatchResult> dispatchResults)
        {
            var payload = new Dictionary<string, PresentationValue>
            {
                ["是否允许"] =
                    PresentationValue.FromBoolean(result.IsAccepted),
                ["动作来源实体ID"] =
                    PresentationValue.FromText(request.SourceEntityId)
            };
            if (request.TargetEntityId != null)
            {
                payload["动作目标实体ID"] =
                    PresentationValue.FromText(request.TargetEntityId);
            }

            if (!result.IsAccepted && result.RejectionCode != null)
            {
                payload["拒绝原因"] =
                    PresentationValue.FromText(result.RejectionCode);
            }

            // 语义命令参数原样进入表现信号，表现配置可以按载荷键绑定，
            // 输入设备、内核规则和具体动画协议之间无需增加专用胶水代码。
            foreach (var parameter in request.Parameters)
            {
                if (!payload.ContainsKey(parameter.Key))
                {
                    payload.Add(
                        parameter.Key,
                        ConvertValue(parameter.Value));
                }
            }

            DispatchSignal(
                new PresentationSignal(
                    request.ActionId,
                    result.IsAccepted
                        ? PresentationTriggerKind.ActionAccepted
                        : PresentationTriggerKind.ActionRejected,
                    request.SourceEntityId,
                    request.SourceEntityId,
                    PresentationTargetEntityId(request),
                    payload),
                commands,
                dispatchResults);
        }

        /// <summary>
        /// 单实体动作没有领域目标时，表现语义中的“动作目标”指向操作者。
        /// 这样抓取跟随可以定位输入设备无关的操作者锚点。
        /// </summary>
        private static string PresentationTargetEntityId(
            SemanticActionRequest request) =>
            request.TargetEntityId ?? request.ActorEntityId;

        private void DispatchDomainEventSignals(
            SemanticActionRequest request,
            CommandResult result,
            ICollection<PresentationEffectCommand> commands,
            ICollection<PresentationDispatchResult> dispatchResults)
        {
            foreach (var envelope in result.Events)
            {
                var structuredPayload = envelope.Event switch
                {
                    ConfiguredCourseDomainEvent configured => configured.Payload,
                    PersistedCourseDomainEvent persisted => persisted.Payload,
                    _ => null
                };
                var payload = structuredPayload != null
                        ? structuredPayload.Select(value =>
                            new KeyValuePair<string, PresentationValue>(
                                value.Key,
                                ConvertValue(value.Value)))
                        : Array.Empty<
                            KeyValuePair<string, PresentationValue>>();
                DispatchSignal(
                    new PresentationSignal(
                        envelope.EventType,
                        PresentationTriggerKind.DomainEvent,
                        request.SourceEntityId,
                        request.SourceEntityId,
                        request.TargetEntityId,
                        payload),
                    commands,
                    dispatchResults);
            }
        }

        private void DispatchStateSignals(
            PresentationTriggerKind triggerKind,
            IEnumerable<string> stateIds,
            ICollection<PresentationEffectCommand> commands,
            ICollection<PresentationDispatchResult> dispatchResults)
        {
            foreach (var stateId in stateIds.OrderBy(
                         value => value,
                         StringComparer.Ordinal))
            {
                DispatchSignal(
                    StateSignal(_states[stateId], triggerKind),
                    commands,
                    dispatchResults);
            }
        }

        private void SynchronizeActiveStates(
            IEnumerable<string> stateIds,
            ICollection<PresentationEffectCommand> commands,
            ICollection<PresentationDispatchResult> dispatchResults)
        {
            var activeCommands = stateIds
                .OrderBy(value => value, StringComparer.Ordinal)
                .SelectMany(stateId => _reactions.React(
                    StateSignal(
                        _states[stateId],
                        PresentationTriggerKind.StateActive)))
                .ToArray();
            foreach (var command in activeCommands)
            {
                commands.Add(command);
            }

            foreach (var result in
                     _dispatcher.SynchronizeAuthoritativeState(
                         activeCommands))
            {
                dispatchResults.Add(result);
            }
        }

        private void DispatchSignal(
            PresentationSignal signal,
            ICollection<PresentationEffectCommand> commands,
            ICollection<PresentationDispatchResult> dispatchResults)
        {
            foreach (var command in _reactions.React(signal))
            {
                commands.Add(command);
                dispatchResults.Add(_dispatcher.Dispatch(command));
            }
        }

        private static PresentationSignal StateSignal(
            CoursePresentationStateDefinition state,
            PresentationTriggerKind triggerKind)
        {
            return new PresentationSignal(
                state.StateId,
                triggerKind,
                state.SubjectEntityId,
                state.ContextSourceEntityId,
                state.ContextTargetEntityId,
                Array.Empty<KeyValuePair<string, PresentationValue>>());
        }

        private void PublishLast(
            IEnumerable<PresentationEffectCommand> commands,
            IEnumerable<PresentationDispatchResult> results)
        {
            _lastCommands =
                new ReadOnlyCollection<PresentationEffectCommand>(
                    commands.ToArray());
            _lastDispatchResults =
                new ReadOnlyCollection<PresentationDispatchResult>(
                    results.ToArray());
        }

        private static IReadOnlyDictionary<
            string,
            CoursePresentationStateDefinition> CopyStates(
                IEnumerable<CoursePresentationStateDefinition> states)
        {
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            var copy =
                new Dictionary<string, CoursePresentationStateDefinition>(
                    StringComparer.Ordinal);
            foreach (var state in states)
            {
                if (state == null ||
                    !copy.TryAdd(state.StateId, state))
                {
                    throw new ArgumentException(
                        "表现状态不能包含空项或重复 ID。",
                        nameof(states));
                }
            }

            return new ReadOnlyDictionary<
                string,
                CoursePresentationStateDefinition>(copy);
        }

        private static PresentationValue ConvertValue(StructuredValue value)
        {
            return value.Kind switch
            {
                StructuredValueKind.Null => PresentationValue.Null(),
                StructuredValueKind.Boolean =>
                    PresentationValue.FromBoolean(value.Boolean),
                StructuredValueKind.Number =>
                    PresentationValue.FromNumber(value.Number),
                StructuredValueKind.Text =>
                    PresentationValue.FromText(value.Text),
                StructuredValueKind.TextList =>
                    PresentationValue.FromText(
                        string.Join(",", value.TextList)),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private static void EnsureAvailabilityMatches(
            SemanticActionRequest request,
            ActionAvailability availability)
        {
            if (!string.Equals(
                    request.ActionId,
                    availability.ActionId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    request.ActorEntityId,
                    availability.ActorEntityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    request.SourceEntityId,
                    availability.SourceEntityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    request.TargetEntityId,
                    availability.TargetEntityId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "可操作性结果与语义动作请求不一致。",
                    nameof(availability));
            }
        }
    }
}
