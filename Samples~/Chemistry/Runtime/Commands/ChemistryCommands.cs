using System;
using VirtualLab.Application.Commands;
using VirtualLab.Kernel;

namespace VirtualLab.Chemistry.Commands
{
    public sealed class PourCommand : ExperimentCommand
    {
        public PourCommand(
            string commandId,
            string sessionId,
            EntityId sourceEntityId,
            EntityId targetEntityId,
            Quantity amount,
            SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
            if (!Enum.IsDefined(typeof(Unit), amount.Unit))
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            var value = decimal.Round(
                amount.Value,
                6,
                MidpointRounding.ToEven);
            if (value <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            SourceEntityId = RequireEntityId(
                sourceEntityId,
                nameof(sourceEntityId));
            Amount = new Quantity(value, amount.Unit);
        }

        public EntityId SourceEntityId { get; }
        public Quantity Amount { get; }
    }

    public sealed class ShakeCommand : ExperimentCommand
    {
        public ShakeCommand(
            string commandId,
            string sessionId,
            EntityId targetEntityId,
            decimal intensity,
            SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
            Intensity = NormalizeNonNegative(intensity, nameof(intensity));
        }

        public decimal Intensity { get; }
    }

    public sealed class IgniteCommand : ExperimentCommand
    {
        public IgniteCommand(
            string commandId,
            string sessionId,
            EntityId targetEntityId,
            EntityId ignitionSourceEntityId,
            SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
            IgnitionSourceEntityId = RequireEntityId(
                ignitionSourceEntityId,
                nameof(ignitionSourceEntityId));
        }

        public EntityId IgnitionSourceEntityId { get; }
    }

    public sealed class ApplyHeatCommand : ExperimentCommand
    {
        public ApplyHeatCommand(
            string commandId,
            string sessionId,
            EntityId targetEntityId,
            EntityId heatSourceEntityId,
            SimulationTick tick)
            : this(
                commandId,
                sessionId,
                targetEntityId,
                heatSourceEntityId,
                0L,
                tick)
        {
        }

        public ApplyHeatCommand(
            string commandId,
            string sessionId,
            EntityId targetEntityId,
            EntityId heatSourceEntityId,
            long durationTicks,
            SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
            if (durationTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationTicks));
            }

            HeatSourceEntityId = RequireEntityId(
                heatSourceEntityId,
                nameof(heatSourceEntityId));
            DurationTicks = durationTicks;
        }

        public EntityId HeatSourceEntityId { get; }
        public long DurationTicks { get; }
    }

    public sealed class ExtinguishCommand : ExperimentCommand
    {
        public ExtinguishCommand(
            string commandId,
            string sessionId,
            EntityId targetEntityId,
            SimulationTick tick)
            : base(commandId, sessionId, targetEntityId, tick)
        {
        }
    }
}
