using System;
using System.Linq;
using VirtualLab.Domain;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry
{
    public sealed class CombustionProcess : IProcessHandler
    {
        private readonly EntityId _locationId;
        private readonly ChemicalReactionDefinition _reaction;

        public CombustionProcess(
            EntityId locationId,
            ChemicalReactionDefinition reaction,
            string fuelSubstanceId,
            string oxygenSubstanceId,
            ReactionRate rate)
        {
            if (string.IsNullOrWhiteSpace(locationId.Value))
            {
                throw new ArgumentException("A process location ID cannot be blank.", nameof(locationId));
            }

            if (reaction == null)
            {
                throw new ArgumentNullException(nameof(reaction));
            }

            FuelSubstanceId = RequireReactant(reaction, fuelSubstanceId, nameof(fuelSubstanceId));
            OxygenSubstanceId = RequireReactant(reaction, oxygenSubstanceId, nameof(oxygenSubstanceId));
            if (string.Equals(FuelSubstanceId, OxygenSubstanceId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Fuel and oxygen must be different reactants.");
            }

            if (rate.ReactionUnitsPerTick <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(rate), "A combustion rate must be valid.");
            }

            _locationId = locationId;
            _reaction = reaction;
            Rate = rate;
        }

        public string FuelSubstanceId { get; }

        public string OxygenSubstanceId { get; }

        public ReactionRate Rate { get; }

        public bool IsIgnited { get; private set; }

        public void Ignite()
        {
            IsIgnited = true;
        }

        public void Extinguish()
        {
            IsIgnited = false;
        }

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

            var temperature = world.Matter.TryGetTemperature(
                _locationId,
                FuelSubstanceId,
                out var fuelTemperature)
                ? fuelTemperature
                : new Temperature(20m);
            ReactionProgress.Advance(
                world.Matter,
                _locationId,
                _reaction,
                Rate,
                IsIgnited,
                null,
                temperature,
                tick,
                events);
        }

        private static string RequireReactant(
            ChemicalReactionDefinition reaction,
            string substanceId,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(substanceId))
            {
                throw new ArgumentException("A combustion substance ID cannot be blank.", parameterName);
            }

            var normalizedId = substanceId.Trim();
            if (!reaction.Reactants.Any(
                    term => string.Equals(
                        term.SubstanceId,
                        normalizedId,
                        StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "A combustion substance must be a configured reactant.",
                    parameterName);
            }

            return normalizedId;
        }
    }
}
