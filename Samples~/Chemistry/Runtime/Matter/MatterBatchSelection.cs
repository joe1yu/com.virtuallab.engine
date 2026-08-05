using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Matter
{
    public readonly struct MatterBatchSelection
    {
        private MatterBatchSelection(
            string substanceId,
            bool hasMinimumTemperature,
            Temperature minimumTemperature,
            bool hasPhase,
            MatterPhase phase)
        {
            if (string.IsNullOrWhiteSpace(substanceId))
            {
                throw new ArgumentException("A selected substance ID cannot be blank.", nameof(substanceId));
            }

            SubstanceId = substanceId.Trim();
            HasMinimumTemperature = hasMinimumTemperature;
            MinimumTemperature = minimumTemperature;
            HasPhase = hasPhase;
            Phase = phase;
        }

        public string SubstanceId { get; }

        public bool HasMinimumTemperature { get; }

        public Temperature MinimumTemperature { get; }

        public bool HasPhase { get; }

        public MatterPhase Phase { get; }

        public static MatterBatchSelection All(string substanceId)
        {
            return new MatterBatchSelection(
                substanceId,
                false,
                default,
                false,
                default);
        }

        public static MatterBatchSelection AtOrAboveTemperature(
            string substanceId,
            Temperature minimumTemperature)
        {
            return new MatterBatchSelection(
                substanceId,
                true,
                minimumTemperature,
                false,
                default);
        }

        /// <summary>
        /// 只选择指定相态的物质批次，避免固体取用误消费同名液态或气态批次。
        /// </summary>
        public static MatterBatchSelection InPhase(
            string substanceId,
            MatterPhase phase)
        {
            return new MatterBatchSelection(
                substanceId,
                false,
                default,
                true,
                phase);
        }

        internal bool Matches(SubstanceBatch batch)
        {
            return string.Equals(batch.SubstanceId, SubstanceId, StringComparison.Ordinal)
                && (!HasPhase || batch.Phase == Phase)
                && (!HasMinimumTemperature
                    || batch.Temperature.Celsius >= MinimumTemperature.Celsius);
        }
    }

    public sealed class MatterBatchSelectionResult
    {
        internal MatterBatchSelectionResult(
            object transactionToken,
            EntityId locationId,
            MatterBatchSelection selection,
            Quantity quantity,
            IList<SubstanceBatch> batches)
        {
            TransactionToken = transactionToken;
            LocationId = locationId;
            Selection = selection;
            Quantity = quantity;
            Batches = new ReadOnlyCollection<SubstanceBatch>(
                new List<SubstanceBatch>(batches));
        }

        public MatterBatchSelection Selection { get; }

        public Quantity Quantity { get; }

        internal object TransactionToken { get; }

        internal EntityId LocationId { get; }

        internal IReadOnlyList<SubstanceBatch> Batches { get; }
    }
}
