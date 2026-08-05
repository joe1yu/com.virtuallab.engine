using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Events;
using VirtualLab.Domain;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 学科运行时实现该端口，把持续过程推进接入统一课程时钟。
    /// 应用层只定义调用契约，不依赖具体学科或 Unity 生命周期。
    /// </summary>
    public interface ICourseProcessAdvancer
    {
        void AdvanceProcesses(
            double elapsedSeconds,
            SimulationTick tick,
            IProcessEventCollector events);
    }

    /// <summary>
    /// 空间状态由运行平台负责采集和恢复；应用层只保存设备无关的数值姿态，
    /// 不依赖 Unity Transform、物理引擎或输入设备。
    /// </summary>
    public interface ICourseSpatialStatePort
    {
        IReadOnlyList<CourseSpatialPoseState> Capture();
        void Restore(IEnumerable<CourseSpatialPoseState> poses);
    }

    public sealed class CourseObservationRecord
    {
        public CourseObservationRecord(
            string commandId,
            SimulationTick tick,
            string actorEntityId,
            string observedEntityId)
        {
            CommandId = CourseContractGuard.Required(commandId, "观察命令 ID");
            if (tick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            Tick = tick;
            ActorEntityId = CourseContractGuard.Required(
                actorEntityId,
                "观察者实体 ID");
            ObservedEntityId = CourseContractGuard.Required(
                observedEntityId,
                "被观察实体 ID");
        }

        public string CommandId { get; }
        public SimulationTick Tick { get; }
        public string ActorEntityId { get; }
        public string ObservedEntityId { get; }
    }

    public sealed class CourseTickResult
    {
        public CourseTickResult(
            SimulationTick tick,
            double elapsedSeconds,
            IEnumerable<DomainEventEnvelope> events)
        {
            Tick = tick;
            if (tick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            if (double.IsNaN(elapsedSeconds)
                || double.IsInfinity(elapsedSeconds)
                || elapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            }

            ElapsedSeconds = elapsedSeconds;
            Events = new ReadOnlyCollection<DomainEventEnvelope>(
                (events ?? throw new ArgumentNullException(nameof(events)))
                .ToArray());
        }

        public SimulationTick Tick { get; }
        public double ElapsedSeconds { get; }
        public IReadOnlyList<DomainEventEnvelope> Events { get; }
    }

    /// <summary>
    /// 课程运行时的应用服务门面。所有外部驱动统一通过这里执行命令、
    /// 推进时钟和过程，并读取目标、评价、观察及单调递增的领域事件历史。
    /// </summary>
    public sealed class CourseRuntimeFacade
    {
        private readonly ExperimentWorld _world;
        private readonly ConfigDrivenCourseSession _session;
        private readonly CourseGoalEvaluator _goalEvaluator;
        private readonly IReadOnlyList<CourseGoalRuleDefinition> _goalRules;
        private readonly ICourseProcessAdvancer _processAdvancer;
        private readonly CourseEventStream _eventStream;
        private readonly List<CourseObservationRecord> _observations =
            new List<CourseObservationRecord>();
        private readonly ReadOnlyCollection<CourseObservationRecord>
            _readOnlyObservations;
        private readonly Dictionary<string, ExecutedCommand> _commands =
            new Dictionary<string, ExecutedCommand>(StringComparer.Ordinal);
        private ICourseSpatialStatePort _spatialState;

        private CourseGoalEvaluationResult _goals;
        private CourseAssessmentEvaluationResult _assessment;
        private CourseOutcomeEvaluationResult _outcome;

        public CourseRuntimeFacade(ConfigDrivenCourseSession session)
            : this(
                session?.World
                    ?? throw new ArgumentNullException(nameof(session)),
                session,
                Array.Empty<IStructuredFactReader>(),
                Array.Empty<CourseGoalRuleDefinition>(),
                null)
        {
        }

        public CourseRuntimeFacade(
            ExperimentWorld world,
            ConfigDrivenCourseSession session,
            IEnumerable<IStructuredFactReader> factReaders,
            IEnumerable<CourseGoalRuleDefinition> goalRules,
            ICourseProcessAdvancer processAdvancer = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            if (!ReferenceEquals(_world, _session.World))
            {
                throw new ArgumentException(
                    "课程会话与运行时门面必须引用同一个权威实验世界。",
                    nameof(session));
            }

            _goalEvaluator = new CourseGoalEvaluator(
                new StructuredRuleEvaluator(
                    factReaders
                    ?? throw new ArgumentNullException(nameof(factReaders))));
            _goalRules = CourseContractGuard.CopyUnique(
                goalRules,
                value => value.GoalId,
                "课程运行时目标规则");
            _processAdvancer = processAdvancer;
            _eventStream = _session.EventStream;
            _readOnlyObservations =
                new ReadOnlyCollection<CourseObservationRecord>(_observations);
            CurrentTick = new SimulationTick(0);
            RefreshEvaluation();
        }

        public SimulationTick CurrentTick { get; private set; }

        public CourseGoalEvaluationResult Goals => _goals;

        public CourseAssessmentEvaluationResult Assessment => _assessment;

        public CourseOutcomeEvaluationResult Outcome => _outcome;

        public IReadOnlyList<CourseObservationRecord> Observations =>
            _readOnlyObservations;

        public IReadOnlyList<DomainEventEnvelope> EventHistory =>
            _eventStream.Events;

        public IReadOnlyList<CourseEventState> EventStates =>
            _eventStream.States;

        public ActionAvailability QueryAvailability(
            SemanticActionRequest request) =>
            _session.QueryAvailability(request);

        public void ConfigureSpatialStatePort(ICourseSpatialStatePort port)
        {
            _spatialState = port
                ?? throw new ArgumentNullException(nameof(port));
        }

        public CommandResult Execute(SemanticActionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_commands.TryGetValue(request.CommandId, out var previous))
            {
                if (ConfigDrivenCourseSession.RequestsMatch(
                        previous.Request,
                        request))
                {
                    return previous.Result;
                }

                // 冲突请求仍交给会话产生统一拒绝语义，但不会重复评价或记录观察。
                return _session.Execute(request, CurrentTick);
            }

            var result = _session.Execute(request, CurrentTick);
            _commands.Add(
                request.CommandId,
                new ExecutedCommand(request, result));
            if (result.IsAccepted
                && string.Equals(
                    request.ActionId,
                    InteractionSemanticActionIds.Observe,
                    StringComparison.Ordinal))
            {
                _observations.Add(new CourseObservationRecord(
                    request.CommandId,
                    CurrentTick,
                    request.ActorEntityId,
                    request.SourceEntityId));
            }

            RefreshEvaluation();
            return result;
        }

        public CourseTickResult Tick(double elapsedSeconds)
        {
            ValidateElapsedSeconds(elapsedSeconds);
            var nextTick = new SimulationTick(
                checked(CurrentTick.Value + 1));
            var firstEventIndex = _eventStream.Events.Count;
            var worldCheckpoint = _world.CreateCheckpoint();
            var eventCheckpoint = _eventStream.CreateCheckpoint();
            try
            {
                _processAdvancer?.AdvanceProcesses(
                    elapsedSeconds,
                    nextTick,
                    _eventStream);
            }
            catch
            {
                _world.RestoreCheckpoint(worldCheckpoint);
                _eventStream.RestoreCheckpoint(eventCheckpoint);
                throw;
            }

            CurrentTick = nextTick;
            RefreshEvaluation();
            return new CourseTickResult(
                CurrentTick,
                elapsedSeconds,
                _eventStream.Events.Skip(firstEventIndex));
        }

        public IReadOnlyList<string> EvaluatePresentationStates(
            IEnumerable<CoursePresentationStateDefinition> states) =>
            _session.EvaluatePresentationStates(states);

        public CourseSessionState ExportState() => _session.ExportState(
            _spatialState?.Capture());

        public void RestoreSpatialState(CourseSessionState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            _spatialState?.Restore(state.SpatialPoses);
        }

        private void RefreshEvaluation()
        {
            _goals = _goalEvaluator.Evaluate(_world, _goalRules);
            _assessment = _session.CurrentAssessment;
            _outcome = CourseOutcomeEvaluator.Evaluate(
                _goalRules.Select(value => value.GoalId),
                _goals,
                _assessment);
            _session.RecordEvaluation(
                _goals,
                _assessment,
                _observations
                    .Select(value => value.ObservedEntityId)
                    .Distinct(StringComparer.Ordinal));
        }

        private static void ValidateElapsedSeconds(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds)
                || double.IsInfinity(elapsedSeconds)
                || elapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds),
                    "Tick 秒数必须是有限的非负数。");
            }
        }

        private sealed class ExecutedCommand
        {
            public ExecutedCommand(
                SemanticActionRequest request,
                CommandResult result)
            {
                Request = request;
                Result = result;
            }

            public SemanticActionRequest Request { get; }
            public CommandResult Result { get; }
        }
    }
}
