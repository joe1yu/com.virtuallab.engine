using System;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Commands
{
    public abstract class ExperimentCommand : IExperimentCommand
    {
        protected ExperimentCommand(string commandId, string sessionId, EntityId targetEntityId, SimulationTick tick)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("A command ID cannot be blank.", nameof(commandId));
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("A session ID cannot be blank.", nameof(sessionId));
            }

            EnsureEntityId(targetEntityId, nameof(targetEntityId));
            if (tick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "A command tick cannot be negative.");
            }

            CommandId = commandId.Trim();
            SessionId = sessionId.Trim();
            TargetEntityId = targetEntityId;
            Tick = tick;
        }

        public string CommandId { get; }

        public string SessionId { get; }

        public EntityId TargetEntityId { get; }

        public SimulationTick Tick { get; }

        protected static EntityId RequireEntityId(EntityId entityId, string parameterName)
        {
            EnsureEntityId(entityId, parameterName);
            return entityId;
        }

        protected static decimal NormalizeNonNegative(decimal value, string parameterName)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(parameterName, "A command parameter cannot be negative.");
            }

            return decimal.Round(value, 6, MidpointRounding.ToEven);
        }

        private static void EnsureEntityId(EntityId entityId, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(entityId.Value))
            {
                throw new ArgumentException("An entity ID cannot be blank.", parameterName);
            }
        }
    }

    public sealed class GrabCommand : ExperimentCommand
    {
        public GrabCommand(string commandId, string sessionId, EntityId targetEntityId, SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
        }
    }

    public sealed class ReleaseCommand : ExperimentCommand
    {
        public ReleaseCommand(string commandId, string sessionId, EntityId targetEntityId, SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
        }
    }

    public sealed class AttachCommand : ExperimentCommand
    {
        public AttachCommand(string commandId, string sessionId, EntityId sourceEntityId, EntityId targetEntityId, SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
            SourceEntityId = RequireEntityId(sourceEntityId, nameof(sourceEntityId));
        }

        public EntityId SourceEntityId { get; }
    }

    public sealed class DetachCommand : ExperimentCommand
    {
        public DetachCommand(string commandId, string sessionId, EntityId sourceEntityId, EntityId targetEntityId, SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
            SourceEntityId = RequireEntityId(sourceEntityId, nameof(sourceEntityId));
        }

        public EntityId SourceEntityId { get; }
    }

    public sealed class PlaceIntoCommand : ExperimentCommand
    {
        public PlaceIntoCommand(string commandId, string sessionId, EntityId entityId, EntityId containerEntityId, SimulationTick tick)
            : base(commandId, sessionId, containerEntityId, tick)
        {
            EntityId = RequireEntityId(entityId, nameof(entityId));
        }

        public EntityId EntityId { get; }
    }

    public sealed class CoverCommand : ExperimentCommand
    {
        public CoverCommand(string commandId, string sessionId, EntityId coverEntityId, EntityId targetEntityId, SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
            CoverEntityId = RequireEntityId(coverEntityId, nameof(coverEntityId));
        }

        public EntityId CoverEntityId { get; }
    }

    public sealed class ObserveCommand : ExperimentCommand
    {
        public ObserveCommand(string commandId, string sessionId, EntityId targetEntityId, SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
        }
    }
}
