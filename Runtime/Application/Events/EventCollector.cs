using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Events
{
    public sealed class EventCollector : IProcessEventCollector
    {
        private readonly List<DomainEventEnvelope> _events;
        private readonly ReadOnlyCollection<DomainEventEnvelope> _readOnlyEvents;
        private long _nextSequence;
        private long _appendStorageOperationCount;

        public EventCollector()
        {
            _events = new List<DomainEventEnvelope>();
            _readOnlyEvents =
                new ReadOnlyCollection<DomainEventEnvelope>(_events);
            _nextSequence = 1;
        }

        public EventCollector(IReadOnlyList<DomainEventEnvelope> initialEvents)
        {
            if (initialEvents == null)
            {
                throw new ArgumentNullException(nameof(initialEvents));
            }

            _events = CopyAndValidate(initialEvents, out _nextSequence);
            _readOnlyEvents =
                new ReadOnlyCollection<DomainEventEnvelope>(_events);
        }

        public IReadOnlyList<DomainEventEnvelope> Events => _readOnlyEvents;

        internal long AppendStorageOperationCount =>
            _appendStorageOperationCount;

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
            if (commitState == null)
            {
                throw new ArgumentNullException(nameof(commitState));
            }

            var envelope = new DomainEventEnvelope(
                _nextSequence,
                commandId,
                tick,
                domainEvent);
            _events.Add(envelope);
            _appendStorageOperationCount = checked(
                _appendStorageOperationCount + 1);
            try
            {
                commitState();
                _nextSequence = checked(_nextSequence + 1);
            }
            catch
            {
                _events.RemoveAt(_events.Count - 1);
                _appendStorageOperationCount--;
                throw;
            }
        }

        private static void NoStateChange()
        {
        }

        private static List<DomainEventEnvelope> CopyAndValidate(
            IReadOnlyList<DomainEventEnvelope> initialEvents,
            out long nextSequence)
        {
            var copy = new List<DomainEventEnvelope>(initialEvents.Count);
            var expectedSequence = 1L;
            foreach (var domainEvent in initialEvents)
            {
                if (domainEvent == null)
                {
                    throw new ArgumentException(
                        "Initial events cannot contain null.",
                        nameof(initialEvents));
                }

                if (domainEvent.Sequence != expectedSequence)
                {
                    throw new ArgumentException(
                        "Initial event sequences must be contiguous and start at one.",
                        nameof(initialEvents));
                }

                copy.Add(domainEvent);
                expectedSequence = checked(expectedSequence + 1);
            }

            nextSequence = expectedSequence;
            return copy;
        }
    }
}
