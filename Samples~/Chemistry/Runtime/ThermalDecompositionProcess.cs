using System;
using System.Collections.Generic;
using VirtualLab.Domain;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry
{
    /// <summary>
    /// 化学学科包内置领域事件的稳定名称。
    /// </summary>
    public static class ChemistryEventTypes
    {
        // 反应过程事件。
        public const string GasGenerated = "气体.已生成";
        public const string ReactionAdvanced = "反应.已推进";
        public const string MixtureTransferred = "混合物.已转移";

        // 倾倒过程事件。
        public const string PourCapacityReached = "倾倒.已达到容量";
        public const string PourAdvanced = "倾倒.已推进";
    }

    public sealed class ThermalDecompositionProcess : IProcessHandler
    {
        private readonly EntityId _locationId;
        private readonly ChemicalReactionDefinition _reaction;
        private readonly Temperature _minimumTemperature;

        public ThermalDecompositionProcess(
            EntityId locationId,
            ChemicalReactionDefinition reaction,
            Temperature minimumTemperature,
            ReactionRate rate)
        {
            if (string.IsNullOrWhiteSpace(locationId.Value))
            {
                throw new ArgumentException("A process location ID cannot be blank.", nameof(locationId));
            }

            if (rate.ReactionUnitsPerTick <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(rate), "A thermal reaction rate must be valid.");
            }

            _locationId = locationId;
            _reaction = reaction ?? throw new ArgumentNullException(nameof(reaction));
            _minimumTemperature = minimumTemperature;
            Rate = rate;
        }

        public ReactionRate Rate { get; }

        public void Advance(
            ExperimentWorld world,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            ReactionProgress.Advance(
                world.Matter,
                _locationId,
                _reaction,
                Rate,
                true,
                _minimumTemperature,
                _minimumTemperature,
                tick,
                events);
        }
    }

    internal static class ReactionProgress
    {
        public static bool Advance(
            MatterInventory inventory,
            EntityId locationId,
            ChemicalReactionDefinition reaction,
            ReactionRate rate,
            bool canReact,
            Temperature? minimumReactantTemperature,
            Temperature productTemperature,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            var generatedGas = false;
            foreach (var product in reaction.Products)
            {
                generatedGas |= product.Phase == MatterPhase.Gas;
            }

            var preparedEvent = generatedGas
                ? (IDomainEvent)new GasGeneratedEvent(reaction.Id, locationId)
                : new ReactionAdvancedEvent(reaction.Id, locationId);
            var advanced = false;

            inventory.CommitAtomically(
                transaction =>
                {
                    foreach (var reactant in reaction.Reactants)
                    {
                        transaction.RegisterUnit(
                            reactant.SubstanceId,
                            reactant.Quantity.Unit);
                    }

                    foreach (var product in reaction.Products)
                    {
                        transaction.RegisterUnit(
                            product.SubstanceId,
                            product.Quantity.Unit);
                    }

                    if (!canReact)
                    {
                        return;
                    }

                    var availableUnits = rate.ReactionUnitsPerTick;
                    var selectedReactants =
                        new List<KeyValuePair<ReactionTerm, MatterBatchSelectionResult>>();
                    foreach (var reactant in reaction.Reactants)
                    {
                        var selection = minimumReactantTemperature.HasValue
                            ? MatterBatchSelection.AtOrAboveTemperature(
                                reactant.SubstanceId,
                                minimumReactantTemperature.Value)
                            : MatterBatchSelection.All(reactant.SubstanceId);
                        var selected = transaction.Select(
                            locationId,
                            selection,
                            reactant.Quantity.Unit);
                        selectedReactants.Add(
                            new KeyValuePair<ReactionTerm, MatterBatchSelectionResult>(
                                reactant,
                                selected));
                        var reactantUnits =
                            selected.Quantity.Value / reactant.Quantity.Value;
                        availableUnits = Math.Min(availableUnits, reactantUnits);
                    }

                    if (availableUnits <= 0m)
                    {
                        return;
                    }

                    foreach (var selectedReactant in selectedReactants)
                    {
                        var requestedValue =
                            selectedReactant.Key.Quantity.Value * availableUnits;
                        transaction.Consume(
                            selectedReactant.Value,
                            new Quantity(
                                Math.Min(
                                    requestedValue,
                                    selectedReactant.Value.Quantity.Value),
                                selectedReactant.Key.Quantity.Unit));
                    }

                    foreach (var product in reaction.Products)
                    {
                        transaction.Add(
                            locationId,
                            new SubstanceBatch(
                                product.SubstanceId,
                                new Quantity(
                                    product.Quantity.Value * availableUnits,
                                    product.Quantity.Unit),
                                product.Phase,
                                productTemperature));
                    }

                    advanced = true;
                },
                "process:" + reaction.Id,
                tick,
                preparedEvent,
                events);
            return advanced;
        }
    }

    /// <summary>
    /// 热分解反应在本次推进中产生气体的事实。
    /// </summary>
    public sealed class GasGeneratedEvent : IDomainEvent
    {
        public GasGeneratedEvent(string reactionId, EntityId locationId)
        {
            ReactionId = reactionId;
            LocationId = locationId;
        }

        public string EventType => ChemistryEventTypes.GasGenerated;

        public string ReactionId { get; }

        public EntityId LocationId { get; }
    }

    /// <summary>
    /// 热分解反应已消耗反应物并更新产物数量的事实。
    /// </summary>
    public sealed class ReactionAdvancedEvent : IDomainEvent
    {
        public ReactionAdvancedEvent(string reactionId, EntityId locationId)
        {
            ReactionId = reactionId;
            LocationId = locationId;
        }

        public string EventType => ChemistryEventTypes.ReactionAdvanced;

        public string ReactionId { get; }

        public EntityId LocationId { get; }
    }
}
