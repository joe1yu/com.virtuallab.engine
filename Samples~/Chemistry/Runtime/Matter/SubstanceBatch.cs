using System;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Matter
{
    public enum MatterPhase
    {
        Solid,
        Liquid,
        Gas
    }

    public sealed class SubstanceBatch
    {
        public SubstanceBatch(
            string substanceId,
            Quantity quantity,
            MatterPhase phase,
            Temperature temperature)
        {
            if (string.IsNullOrWhiteSpace(substanceId))
            {
                throw new ArgumentException("A substance ID cannot be blank.", nameof(substanceId));
            }

            if (quantity.Value < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "A substance quantity cannot be negative.");
            }

            if (quantity.Unit.IsEmpty)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "A substance quantity must use a known unit.");
            }

            if (!Enum.IsDefined(typeof(MatterPhase), phase))
            {
                throw new ArgumentOutOfRangeException(nameof(phase), "A substance phase must be known.");
            }

            SubstanceId = substanceId.Trim();
            Quantity = quantity;
            Phase = phase;
            Temperature = temperature;
        }

        public string SubstanceId { get; }

        public Quantity Quantity { get; }

        public MatterPhase Phase { get; }

        public Temperature Temperature { get; }

        internal SubstanceBatch WithQuantity(Quantity quantity)
        {
            return new SubstanceBatch(SubstanceId, quantity, Phase, Temperature);
        }
    }
}
