using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Assessment;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Events;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Events;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Kernel;

namespace VirtualLab.Application
{
    public sealed class ExperimentSession
    {
        private readonly ExperimentWorld _world;
        private readonly EventCollector _eventCollector;
        private readonly HashSet<string> _processedCommandIds = new HashSet<string>(StringComparer.Ordinal);

        public ExperimentSession(string sessionId, ExperimentWorld world)
            : this(sessionId, world, new EventCollector())
        {
        }

        public ExperimentSession(
            string sessionId,
            ExperimentWorld world,
            EventCollector eventCollector)
            : this(
                sessionId,
                world,
                eventCollector,
                new SimulationTick(0),
                Array.Empty<string>())
        {
        }

        public ExperimentSession(
            string sessionId,
            ExperimentWorld world,
            EventCollector eventCollector,
            SimulationTick currentTick,
            IEnumerable<string> processedCommandIds)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("A session ID cannot be blank.", nameof(sessionId));
            }

            if (currentTick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(currentTick));
            }

            if (processedCommandIds == null)
            {
                throw new ArgumentNullException(nameof(processedCommandIds));
            }

            _world = world ?? throw new ArgumentNullException(nameof(world));
            _eventCollector = eventCollector ?? throw new ArgumentNullException(nameof(eventCollector));
            SessionId = sessionId.Trim();
            CurrentTick = currentTick;
            foreach (var commandId in processedCommandIds)
            {
                if (string.IsNullOrWhiteSpace(commandId))
                {
                    throw new ArgumentException(
                        "Processed command IDs cannot contain blank values.",
                        nameof(processedCommandIds));
                }

                if (!_processedCommandIds.Add(commandId.Trim()))
                {
                    throw new ArgumentException(
                        "Processed command IDs must be unique.",
                        nameof(processedCommandIds));
                }
            }

            var seededCommandIds = new HashSet<string>(
                _eventCollector.Events.Select(value => value.CommandId),
                StringComparer.Ordinal);
            if (!_processedCommandIds.IsSubsetOf(seededCommandIds))
            {
                throw new ArgumentException(
                    "Every processed command ID must have seeded event evidence.",
                    nameof(processedCommandIds));
            }
        }

        public string SessionId { get; }

        public SimulationTick CurrentTick { get; private set; }

        public IReadOnlyList<DomainEventEnvelope> Events => _eventCollector.Events;

        public ExperimentWorld World => _world;

        public IReadOnlyList<string> ProcessedCommandIds =>
            new ReadOnlyCollection<string>(
                _processedCommandIds
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList());

        public long LastEventSequence =>
            Events.Count == 0 ? 0L : Events[Events.Count - 1].Sequence;

        public CommandResult Execute(IExperimentCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (!string.Equals(command.SessionId, SessionId, StringComparison.Ordinal))
            {
                return CommandResult.Rejected("session.mismatch");
            }

            if (_processedCommandIds.Contains(command.CommandId))
            {
                return CommandResult.Rejected("command.duplicate");
            }

            if (command.Tick.Value < CurrentTick.Value)
            {
                return CommandResult.Rejected("tick.out_of_order");
            }

            if (!TryGetEntity(command.TargetEntityId, "target", out var target, out var rejection))
            {
                return rejection;
            }

            if (command is GrabCommand)
            {
                return RequireCapability<GrabbableCapability>(target, "grabbable", () => Accept(command, DomainEventTypes.EntityGrabbed));
            }

            if (command is ReleaseCommand)
            {
                return RequireCapability<GrabbableCapability>(target, "grabbable", () => Accept(command, DomainEventTypes.EntityReleased));
            }

            if (command is AttachCommand attach)
            {
                return ExecuteAttach(command, target, attach.SourceEntityId, DomainEventTypes.ConnectionEstablished);
            }

            if (command is DetachCommand detach)
            {
                return ExecuteAttach(command, target, detach.SourceEntityId, DomainEventTypes.ConnectionRemoved);
            }

            if (command is PlaceIntoCommand placeInto)
            {
                if (!TryGetEntity(placeInto.EntityId, "source", out _, out rejection))
                {
                    return rejection;
                }

                return RequireCapability<ContainerCapability>(target, "container", () => Accept(command, DomainEventTypes.ContainmentChanged));
            }

            if (command is CoverCommand cover)
            {
                if (!TryGetEntity(cover.CoverEntityId, "source", out _, out rejection))
                {
                    return rejection;
                }

                return RequireCapability<CoverableCapability>(target, "coverable", () => Accept(command, DomainEventTypes.SealConfirmed));
            }

            if (command is ObserveCommand)
            {
                return RequireCapability<ObservableCapability>(target, "observable", () => Accept(command, DomainEventTypes.ObservationAvailable));
            }

            throw new InvalidOperationException("The experiment session does not support the supplied command type.");
        }

        private CommandResult ExecuteAttach(IExperimentCommand command, ExperimentEntity target, EntityId sourceEntityId, string eventType)
        {
            if (!TryGetEntity(sourceEntityId, "source", out var source, out var rejection))
            {
                return rejection;
            }

            if (!source.HasCapability<ConnectorCapability>())
            {
                return CommandResult.Rejected("capability.connector.source.required");
            }

            return RequireCapability<ConnectorCapability>(target, "connector", () => Accept(command, eventType));
        }

        private CommandResult RequireCapability<TCapability>(ExperimentEntity entity, string capabilityName, Func<CommandResult> accepted)
            where TCapability : class, ICapability
        {
            if (!entity.HasCapability<TCapability>())
            {
                return CommandResult.Rejected("capability." + capabilityName + ".required");
            }

            return accepted();
        }

        private bool TryGetEntity(EntityId entityId, string role, out ExperimentEntity entity, out CommandResult rejection)
        {
            if (_world.TryGetEntity(entityId, out entity))
            {
                rejection = null;
                return true;
            }

            rejection = CommandResult.Rejected("entity." + role + ".not_found");
            return false;
        }

        private CommandResult Accept(IExperimentCommand command, string eventType)
        {
            var envelope = _eventCollector.Append(
                command.CommandId,
                command.Tick,
                new SemanticDomainEvent(eventType));
            CurrentTick = command.Tick;
            _processedCommandIds.Add(command.CommandId);
            return CommandResult.Accepted(new[] { envelope });
        }

        private sealed class SemanticDomainEvent : IDomainEvent
        {
            public SemanticDomainEvent(string eventType)
            {
                EventType = eventType;
            }

            public string EventType { get; }
        }
    }

}
