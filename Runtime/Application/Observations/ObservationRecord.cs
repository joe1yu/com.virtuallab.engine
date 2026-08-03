using System;
using VirtualLab.Application.Events;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Observations
{
    /// <summary>Immutable projection of a received domain event for learning evidence.</summary>
    public sealed class ObservationRecord
    {
        public ObservationRecord(DomainEventEnvelope domainEvent)
        {
            if (domainEvent == null)
            {
                throw new ArgumentNullException(nameof(domainEvent));
            }

            EventSequence = domainEvent.Sequence;
            CommandId = domainEvent.CommandId;
            Tick = domainEvent.Tick;
            EventType = domainEvent.EventType;
        }

        public long EventSequence { get; }

        public string CommandId { get; }

        public SimulationTick Tick { get; }

        public string EventType { get; }
    }
}
