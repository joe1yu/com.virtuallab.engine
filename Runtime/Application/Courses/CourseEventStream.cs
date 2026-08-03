using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Events;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 将领域事件转换为课程存档能够长期保存的结构化事实。
    /// 学科包通过实现该端口保存自己的事件语义，通用应用层无需识别学科事件类型。
    /// </summary>
    public interface ICourseEventProjector
    {
        bool TryProject(
            IDomainEvent domainEvent,
            out IReadOnlyDictionary<string, StructuredValue> payload);
    }

    /// <summary>
    /// 课程运行期唯一的事件追加入口。内存信封、存档事件和全局序号在同一次提交中产生，
    /// 从而让表现、目标刷新、存档与报告引用同一条事实序列。
    /// </summary>
    public sealed class CourseEventStream : IProcessEventCollector
    {
        private readonly List<DomainEventEnvelope> _events =
            new List<DomainEventEnvelope>();
        private readonly List<CourseEventState> _states =
            new List<CourseEventState>();
        private readonly ReadOnlyCollection<DomainEventEnvelope> _readOnlyEvents;
        private readonly ReadOnlyCollection<CourseEventState> _readOnlyStates;
        private readonly IReadOnlyList<ICourseEventProjector> _projectors;
        private long _nextSequence;

        public CourseEventStream(
            IEnumerable<ICourseEventProjector> projectors = null)
            : this(
                Array.Empty<CourseEventState>(),
                projectors)
        {
        }

        public CourseEventStream(
            IEnumerable<CourseEventState> initialEvents,
            IEnumerable<ICourseEventProjector> projectors = null)
        {
            _readOnlyEvents =
                new ReadOnlyCollection<DomainEventEnvelope>(_events);
            _readOnlyStates =
                new ReadOnlyCollection<CourseEventState>(_states);
            _projectors = new ICourseEventProjector[]
                {
                    new ConfiguredEventProjector(),
                    new MatterEventProjector()
                }
                .Concat(projectors ?? Array.Empty<ICourseEventProjector>())
                .ToArray();

            Restore(initialEvents ?? throw new ArgumentNullException(
                nameof(initialEvents)));
        }

        public IReadOnlyList<DomainEventEnvelope> Events => _readOnlyEvents;

        public IReadOnlyList<CourseEventState> States => _readOnlyStates;

        public long NextSequence => _nextSequence;

        public DomainEventEnvelope Append(
            string commandId,
            SimulationTick tick,
            IDomainEvent domainEvent)
        {
            CommitAtomically(commandId, tick, domainEvent, NoStateChange);
            return _events[_events.Count - 1];
        }

        public void CommitAtomically(
            string commandId,
            SimulationTick tick,
            IDomainEvent domainEvent,
            Action commitState)
        {
            if (domainEvent == null)
            {
                throw new ArgumentNullException(nameof(domainEvent));
            }

            if (commitState == null)
            {
                throw new ArgumentNullException(nameof(commitState));
            }

            var payload = Project(domainEvent);
            var envelope = new DomainEventEnvelope(
                _nextSequence,
                commandId,
                tick,
                domainEvent);
            var state = new CourseEventState(
                _nextSequence,
                commandId,
                tick.Value,
                domainEvent.EventType,
                payload);
            _events.Add(envelope);
            _states.Add(state);
            try
            {
                commitState();
                _nextSequence = checked(_nextSequence + 1);
            }
            catch
            {
                _events.RemoveAt(_events.Count - 1);
                _states.RemoveAt(_states.Count - 1);
                throw;
            }
        }

        private IReadOnlyDictionary<string, StructuredValue> Project(
            IDomainEvent domainEvent)
        {
            foreach (var projector in _projectors)
            {
                if (projector.TryProject(domainEvent, out var payload))
                {
                    return payload ?? throw new InvalidOperationException(
                        $"事件投影器为“{domainEvent.EventType}”返回了空载荷。");
                }
            }

            // 未注册扩展事件仍保留类型、序号、命令与 Tick；学科若需要报告或重放字段，
            // 应注册投影器补充载荷，而不应让一次已完成的科学状态提交失败。
            return new ReadOnlyDictionary<string, StructuredValue>(
                new Dictionary<string, StructuredValue>());
        }

        private void Restore(IEnumerable<CourseEventState> initialEvents)
        {
            var expectedSequence = 1L;
            foreach (var state in initialEvents)
            {
                if (state == null || state.Sequence != expectedSequence)
                {
                    throw new ArgumentException(
                        "课程事件必须从 1 开始且序号连续。",
                        nameof(initialEvents));
                }

                _states.Add(state);
                _events.Add(new DomainEventEnvelope(
                    state.Sequence,
                    state.CommandId,
                    new SimulationTick(state.Tick),
                    new PersistedCourseDomainEvent(
                        state.EventType,
                        state.Payload)));
                expectedSequence = checked(expectedSequence + 1);
            }

            _nextSequence = expectedSequence;
        }

        internal CourseEventStreamCheckpoint CreateCheckpoint() =>
            new CourseEventStreamCheckpoint(
                _events.ToArray(),
                _states.ToArray(),
                _nextSequence);

        internal void RestoreCheckpoint(CourseEventStreamCheckpoint checkpoint)
        {
            if (checkpoint == null)
            {
                throw new ArgumentNullException(nameof(checkpoint));
            }

            _events.Clear();
            _states.Clear();
            _events.AddRange(checkpoint.Events);
            _states.AddRange(checkpoint.States);
            _nextSequence = checkpoint.NextSequence;
        }

        private static void NoStateChange()
        {
        }

        private sealed class ConfiguredEventProjector : ICourseEventProjector
        {
            public bool TryProject(
                IDomainEvent domainEvent,
                out IReadOnlyDictionary<string, StructuredValue> payload)
            {
                if (domainEvent is ConfiguredCourseDomainEvent configured)
                {
                    payload = configured.Payload;
                    return true;
                }

                payload = null;
                return false;
            }
        }

        private sealed class MatterEventProjector : ICourseEventProjector
        {
            public bool TryProject(
                IDomainEvent domainEvent,
                out IReadOnlyDictionary<string, StructuredValue> payload)
            {
                if (!(domainEvent is SubstanceTransferredEvent transferred))
                {
                    payload = null;
                    return false;
                }

                payload = new Dictionary<string, StructuredValue>
                {
                    [CourseConfigurationKeys.EventPayload.SourceEntityId] =
                        StructuredValue.FromText(
                        transferred.SourceId.Value),
                    [CourseConfigurationKeys.EventPayload.TargetEntityId] =
                        StructuredValue.FromText(
                        transferred.TargetId.Value),
                    [CourseConfigurationKeys.EventPayload.SubstanceId] =
                        StructuredValue.FromText(
                        transferred.SubstanceId),
                    [CourseConfigurationKeys.EventPayload.Quantity] =
                        StructuredValue.FromNumber(
                        (double)transferred.Quantity.Value),
                    [CourseConfigurationKeys.EventPayload.Unit] =
                        StructuredValue.FromText(
                        transferred.Quantity.Unit.ToString())
                };
                return true;
            }
        }
    }

    /// <summary>
    /// Tick 事务使用的内存检查点。它保留原始领域事件对象，回滚后不会把运行期
    /// 事件退化成仅供存档读取的事件包装。
    /// </summary>
    internal sealed class CourseEventStreamCheckpoint
    {
        public CourseEventStreamCheckpoint(
            DomainEventEnvelope[] events,
            CourseEventState[] states,
            long nextSequence)
        {
            Events = events ?? throw new ArgumentNullException(nameof(events));
            States = states ?? throw new ArgumentNullException(nameof(states));
            NextSequence = nextSequence;
        }

        public DomainEventEnvelope[] Events { get; }

        public CourseEventState[] States { get; }

        public long NextSequence { get; }
    }

    /// <summary>
    /// 从存档恢复的事件事实。它保留稳定事件类型与完整结构化载荷，供表现和报告重放。
    /// </summary>
    public sealed class PersistedCourseDomainEvent : IDomainEvent
    {
        public PersistedCourseDomainEvent(
            string eventType,
            IReadOnlyDictionary<string, StructuredValue> payload)
        {
            EventType = CourseContractGuard.Required(
                eventType,
                CourseConfigurationKeys.Common.EventType);
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public string EventType { get; }

        public IReadOnlyDictionary<string, StructuredValue> Payload { get; }
    }
}
