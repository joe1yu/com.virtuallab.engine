using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry
{
    public sealed class StoichiometricMixingReactionExecutor
    {
        public StoichiometricMixingResult Execute(
            MatterInventory inventory,
            EntityId locationId,
            ChemistryReactionDefinition reaction,
            decimal maximumReactionUnits,
            Temperature productTemperature,
            string commandId,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (reaction == null)
            {
                throw new ArgumentNullException(nameof(reaction));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (reaction.ProcessKind != ChemistryReactionProcessKind.Mixing)
            {
                throw new ArgumentException(
                    "A stoichiometric mixing executor requires a mixing reaction.",
                    nameof(reaction));
            }

            if (maximumReactionUnits <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumReactionUnits));
            }

            if (reaction.ReactionUnitsPerTick <= 0m ||
                reaction.Reactants.Count == 0 ||
                reaction.Products.Count == 0)
            {
                throw new ArgumentException(
                    "A mixing reaction requires terms and a positive rate.",
                    nameof(reaction));
            }

            var reactionUnits = Math.Min(
                maximumReactionUnits,
                reaction.ReactionUnitsPerTick);
            foreach (var reactant in reaction.Reactants)
            {
                ValidateTerm(reactant, nameof(reaction));
                reactionUnits = Math.Min(
                    reactionUnits,
                    inventory.Total(
                        locationId,
                        MatterBatchSelection.AtOrAboveTemperature(
                            reactant.SubstanceId,
                            new Temperature(
                                reaction.MinimumTemperatureCelsius)),
                        reactant.QuantityUnit).Value /
                    reactant.QuantityValue);
            }

            foreach (var product in reaction.Products)
            {
                ValidateTerm(product, nameof(reaction));
            }

            if (reactionUnits <= 0m)
            {
                return StoichiometricMixingResult.NoReaction;
            }

            inventory.CommitAtomically(
                transaction =>
                {
                    foreach (var reactant in reaction.Reactants)
                    {
                        var selected = transaction.Select(
                            locationId,
                            MatterBatchSelection.AtOrAboveTemperature(
                                reactant.SubstanceId,
                                new Temperature(
                                    reaction.MinimumTemperatureCelsius)),
                            reactant.QuantityUnit);
                        transaction.Consume(
                            selected,
                            new Quantity(
                                reactant.QuantityValue * reactionUnits,
                                reactant.QuantityUnit));
                    }

                    foreach (var product in reaction.Products)
                    {
                        transaction.Add(
                            locationId,
                            new SubstanceBatch(
                                product.SubstanceId,
                                new Quantity(
                                    product.QuantityValue * reactionUnits,
                                    product.QuantityUnit),
                                product.Phase,
                                productTemperature));
                    }
                },
                commandId,
                tick,
                new ReactionAdvancedEvent(reaction.Id, locationId),
                events);
            return new StoichiometricMixingResult(true, reactionUnits);
        }

        private static void ValidateTerm(
            ChemistryReactionTerm term,
            string parameterName)
        {
            if (term == null ||
                string.IsNullOrWhiteSpace(term.SubstanceId) ||
                term.QuantityValue <= 0m)
            {
                throw new ArgumentException(
                    "Mixing reaction terms must be positive and named.",
                    parameterName);
            }
        }
    }

    public readonly struct StoichiometricMixingResult
    {
        internal static readonly StoichiometricMixingResult NoReaction =
            new StoichiometricMixingResult(false, 0m);

        public StoichiometricMixingResult(
            bool didReact,
            decimal reactionUnits)
        {
            DidReact = didReact;
            ReactionUnits = reactionUnits;
        }

        public bool DidReact { get; }
        public decimal ReactionUnits { get; }
    }

    public sealed class MixtureTransferExecutor
    {
        // 3 envelope fields + 3 fields per component must fit the
        // persistence contract's stable 64-field payload ceiling.
        public const int MaxComponents = 20;
        public const int MaxEntityIdCharacters = 256;
        public const int MaxSubstanceIdCharacters = 256;

        public void Transfer(
            MatterInventory inventory,
            EntityId sourceId,
            EntityId targetId,
            IEnumerable<MixtureTransferComponent> components,
            string commandId,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            MixtureTransferValidation.ValidateEndpoint(
                sourceId,
                nameof(sourceId));
            MixtureTransferValidation.ValidateEndpoint(
                targetId,
                nameof(targetId));
            var requested = MixtureTransferValidation.CopyBounded(
                components,
                nameof(components));

            var domainEvent = new MixtureTransferredEvent(
                sourceId,
                targetId,
                requested);
            inventory.CommitAtomically(
                transaction =>
                {
                    foreach (var component in requested)
                    {
                        var selected = transaction.Select(
                            sourceId,
                            MatterBatchSelection.All(component.SubstanceId),
                            component.Quantity.Unit);
                        var moved = transaction.Consume(
                            selected,
                            component.Quantity);
                        foreach (var batch in moved)
                        {
                            transaction.Add(targetId, batch);
                        }
                    }
                },
                commandId,
                tick,
                domainEvent,
                events);
        }
    }

    public sealed class MixtureTransferComponent
    {
        public MixtureTransferComponent(
            string substanceId,
            Quantity quantity)
        {
            if (string.IsNullOrWhiteSpace(substanceId) ||
                substanceId.Trim().Length >
                    MixtureTransferExecutor.MaxSubstanceIdCharacters ||
                quantity.Value <= 0m ||
                !Enum.IsDefined(typeof(Unit), quantity.Unit))
            {
                throw new ArgumentException(
                    "A mixture component requires a bounded substance ID, known unit, and positive quantity.");
            }

            SubstanceId = substanceId.Trim();
            Quantity = quantity;
        }

        public string SubstanceId { get; }
        public Quantity Quantity { get; }
    }

    /// <summary>
    /// 一组混合物组分已作为同一原子操作完成转移的事实。
    /// </summary>
    public sealed class MixtureTransferredEvent : IDomainEvent
    {
        public MixtureTransferredEvent(
            EntityId sourceId,
            EntityId targetId,
            IEnumerable<MixtureTransferComponent> components)
        {
            MixtureTransferValidation.ValidateEndpoint(
                sourceId,
                nameof(sourceId));
            MixtureTransferValidation.ValidateEndpoint(
                targetId,
                nameof(targetId));
            SourceId = sourceId;
            TargetId = targetId;
            Components = new ReadOnlyCollection<MixtureTransferComponent>(
                MixtureTransferValidation.CopyBounded(
                    components,
                    nameof(components)));
        }

        public string EventType => ChemistryEventTypes.MixtureTransferred;
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public IReadOnlyList<MixtureTransferComponent> Components { get; }
    }

    internal static class MixtureTransferValidation
    {
        public static void ValidateEndpoint(
            EntityId entityId,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(entityId.Value) ||
                entityId.Value.Length >
                    MixtureTransferExecutor.MaxEntityIdCharacters)
            {
                throw new ArgumentException(
                    "A mixture endpoint requires a bounded entity ID.",
                    parameterName);
            }
        }

        public static List<MixtureTransferComponent> CopyBounded(
            IEnumerable<MixtureTransferComponent> components,
            string parameterName)
        {
            if (components == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var copy = new List<MixtureTransferComponent>(
                MixtureTransferExecutor.MaxComponents);
            foreach (var component in components)
            {
                if (copy.Count ==
                    MixtureTransferExecutor.MaxComponents)
                {
                    throw new ArgumentException(
                        "A mixture transfer exceeds the component limit.",
                        parameterName);
                }

                if (component == null)
                {
                    throw new ArgumentException(
                        "Mixture transfer components cannot be null.",
                        parameterName);
                }

                copy.Add(component);
            }

            if (copy.Count == 0 ||
                copy.Select(value =>
                        value.SubstanceId + "\n" +
                        (int)value.Quantity.Unit)
                    .Distinct(StringComparer.Ordinal).Count() !=
                    copy.Count)
            {
                throw new ArgumentException(
                    "Mixture transfer components must be non-empty and unique.",
                    parameterName);
            }

            return copy;
        }
    }
}
