using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Configuration
{
    public static class ChemistryCapabilityIds
    {
        public const string HeatSource = "热源";
        public const string Heatable = "可加热";
        public const string Pourable = "可倾倒";
        public const string Ignitable = "可点燃";
        public const string Combustible = "可燃烧";
        public const string Shakeable = "可振荡";
    }

    public enum ChemistryMolarMassUnit
    {
        GramPerMole
    }

    public enum ChemistryReactionProcessKind
    {
        ThermalDecomposition,
        Combustion,
        Mixing
    }

    public enum ChemistryTemperatureUnit
    {
        Celsius
    }

    public sealed class ChemistryRuntimeConfiguration
    {
        public ChemistryRuntimeConfiguration(
            IList<ChemistrySubstanceDefinition> substances,
            IList<ChemistryReactionDefinition> reactions,
            IList<ChemistryInitialSubstance> initialSubstances)
            : this(
                substances,
                reactions,
                initialSubstances,
                new List<ChemistryEntityCapabilityBinding>())
        {
        }

        public ChemistryRuntimeConfiguration(
            IList<ChemistrySubstanceDefinition> substances,
            IList<ChemistryReactionDefinition> reactions,
            IList<ChemistryInitialSubstance> initialSubstances,
            IList<ChemistryEntityCapabilityBinding> entityCapabilities)
        {
            Substances = ReadOnly(substances);
            Reactions = ReadOnly(reactions);
            InitialSubstances = ReadOnly(initialSubstances);
            EntityCapabilities = ReadOnly(entityCapabilities);
        }

        public IReadOnlyList<ChemistrySubstanceDefinition> Substances { get; }
        public IReadOnlyList<ChemistryReactionDefinition> Reactions { get; }
        public IReadOnlyList<ChemistryInitialSubstance> InitialSubstances { get; }
        public IReadOnlyList<ChemistryEntityCapabilityBinding>
            EntityCapabilities { get; }

        private static IReadOnlyList<T> ReadOnly<T>(IList<T> values)
        {
            return new ReadOnlyCollection<T>(
                new List<T>(
                    values ?? throw new ArgumentNullException(nameof(values))));
        }
    }

    public sealed class ChemistryEntityCapabilityBinding
    {
        public ChemistryEntityCapabilityBinding(
            string entityId,
            string capabilityId)
        {
            EntityId = entityId;
            CapabilityId = capabilityId;
        }

        public string EntityId { get; }
        public string CapabilityId { get; }
    }

    public sealed class ChemistrySubstanceDefinition
    {
        public ChemistrySubstanceDefinition(
            string id,
            string displayName,
            MatterPhase phase,
            decimal molarMassValue,
            ChemistryMolarMassUnit molarMassUnit)
        {
            Id = id;
            DisplayName = displayName;
            Phase = phase;
            MolarMassValue = molarMassValue;
            MolarMassUnit = molarMassUnit;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public MatterPhase Phase { get; }
        public decimal MolarMassValue { get; }
        public ChemistryMolarMassUnit MolarMassUnit { get; }
    }

    public sealed class ChemistryReactionTerm
    {
        public ChemistryReactionTerm(
            string substanceId,
            decimal quantityValue,
            Unit quantityUnit,
            MatterPhase phase,
            decimal gramsPerDeclaredUnit)
        {
            SubstanceId = substanceId;
            QuantityValue = quantityValue;
            QuantityUnit = quantityUnit;
            Phase = phase;
            GramsPerDeclaredUnit = gramsPerDeclaredUnit;
        }

        public string SubstanceId { get; }
        public decimal QuantityValue { get; }
        public Unit QuantityUnit { get; }
        public MatterPhase Phase { get; }
        public decimal GramsPerDeclaredUnit { get; }
    }

    public sealed class ChemistryReactionDefinition
    {
        public ChemistryReactionDefinition(
            string id,
            IList<ChemistryReactionTerm> reactants,
            IList<ChemistryReactionTerm> products,
            ChemistryReactionProcessKind processKind,
            decimal minimumTemperatureCelsius,
            decimal reactionUnitsPerTick,
            bool requiresIgnition)
        {
            Id = id;
            Reactants = new ReadOnlyCollection<ChemistryReactionTerm>(
                new List<ChemistryReactionTerm>(
                    reactants ?? throw new ArgumentNullException(nameof(reactants))));
            Products = new ReadOnlyCollection<ChemistryReactionTerm>(
                new List<ChemistryReactionTerm>(
                    products ?? throw new ArgumentNullException(nameof(products))));
            ProcessKind = processKind;
            MinimumTemperatureCelsius = minimumTemperatureCelsius;
            ReactionUnitsPerTick = reactionUnitsPerTick;
            RequiresIgnition = requiresIgnition;
        }

        public string Id { get; }
        public IReadOnlyList<ChemistryReactionTerm> Reactants { get; }
        public IReadOnlyList<ChemistryReactionTerm> Products { get; }
        public ChemistryReactionProcessKind ProcessKind { get; }
        public decimal MinimumTemperatureCelsius { get; }
        public decimal ReactionUnitsPerTick { get; }
        public bool RequiresIgnition { get; }
    }

    public sealed class ChemistryInitialSubstance
    {
        public ChemistryInitialSubstance(
            string entityId,
            string substanceId,
            decimal quantityValue,
            Unit quantityUnit,
            MatterPhase phase,
            decimal temperatureValue,
            ChemistryTemperatureUnit temperatureUnit)
        {
            EntityId = entityId;
            SubstanceId = substanceId;
            QuantityValue = quantityValue;
            QuantityUnit = quantityUnit;
            Phase = phase;
            TemperatureValue = temperatureValue;
            TemperatureUnit = temperatureUnit;
        }

        public string EntityId { get; }
        public string SubstanceId { get; }
        public decimal QuantityValue { get; }
        public Unit QuantityUnit { get; }
        public MatterPhase Phase { get; }
        public decimal TemperatureValue { get; }
        public ChemistryTemperatureUnit TemperatureUnit { get; }
    }
}
