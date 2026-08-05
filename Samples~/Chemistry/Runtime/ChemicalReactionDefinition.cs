using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Domain.Matter;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry
{
    public sealed class ReactionTerm
    {
        public ReactionTerm(
            string substanceId,
            Quantity quantity,
            MatterPhase phase,
            decimal gramsPerDeclaredUnit)
        {
            if (string.IsNullOrWhiteSpace(substanceId))
            {
                throw new ArgumentException("A reaction substance ID cannot be blank.", nameof(substanceId));
            }

            if (quantity.Value <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "A reaction quantity must be positive.");
            }

            if (!Enum.IsDefined(typeof(Unit), quantity.Unit))
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "A reaction quantity must use a known unit.");
            }

            if (!Enum.IsDefined(typeof(MatterPhase), phase))
            {
                throw new ArgumentOutOfRangeException(nameof(phase), "A reaction phase must be known.");
            }

            if (gramsPerDeclaredUnit <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gramsPerDeclaredUnit),
                    "A reaction term mass equivalence must be positive.");
            }

            if (quantity.Unit == Unit.Gram && gramsPerDeclaredUnit != 1m)
            {
                throw new ArgumentException(
                    "A quantity declared in grams must use exactly one gram per declared unit.",
                    nameof(gramsPerDeclaredUnit));
            }

            SubstanceId = substanceId.Trim();
            Quantity = quantity;
            Phase = phase;
            GramsPerDeclaredUnit = gramsPerDeclaredUnit;
        }

        public string SubstanceId { get; }

        public Quantity Quantity { get; }

        public MatterPhase Phase { get; }

        public decimal GramsPerDeclaredUnit { get; }

        public decimal MassGrams => Quantity.Value * GramsPerDeclaredUnit;
    }

    public sealed class ChemicalReactionDefinition
    {
        public const decimal DefaultMassToleranceGrams = 0.000001m;

        public ChemicalReactionDefinition(
            string id,
            IEnumerable<ReactionTerm> reactants,
            IEnumerable<ReactionTerm> products)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A reaction ID cannot be blank.", nameof(id));
            }

            Id = id.Trim();
            Reactants = CopyRequiredTerms(reactants, nameof(reactants));
            Products = CopyRequiredTerms(products, nameof(products));
            EnsureUniqueSubstances(Reactants, nameof(reactants));
            EnsureUniqueSubstances(Products, nameof(products));
            InputMassGrams = SumMass(Reactants);
            OutputMassGrams = SumMass(Products);
            MassToleranceGrams = DefaultMassToleranceGrams;
            if (Math.Abs(InputMassGrams - OutputMassGrams) > MassToleranceGrams)
            {
                throw new ArgumentException(
                    "A chemical reaction must conserve mass within its configured decimal tolerance.");
            }
        }

        public string Id { get; }

        public IReadOnlyList<ReactionTerm> Reactants { get; }

        public IReadOnlyList<ReactionTerm> Products { get; }

        public decimal InputMassGrams { get; }

        public decimal OutputMassGrams { get; }

        public decimal MassToleranceGrams { get; }

        private static IReadOnlyList<ReactionTerm> CopyRequiredTerms(
            IEnumerable<ReactionTerm> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var copied = new List<ReactionTerm>();
            foreach (var term in source)
            {
                if (term == null)
                {
                    throw new ArgumentException("A reaction cannot contain a null term.", parameterName);
                }

                copied.Add(term);
            }

            if (copied.Count == 0)
            {
                throw new ArgumentException("A reaction must contain at least one term.", parameterName);
            }

            return new ReadOnlyCollection<ReactionTerm>(copied);
        }

        private static void EnsureUniqueSubstances(
            IEnumerable<ReactionTerm> terms,
            string parameterName)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var term in terms)
            {
                if (!ids.Add(term.SubstanceId))
                {
                    throw new ArgumentException(
                        "A substance can appear only once on one side of a reaction.",
                        parameterName);
                }
            }
        }

        private static decimal SumMass(IEnumerable<ReactionTerm> terms)
        {
            var total = 0m;
            foreach (var term in terms)
            {
                total += term.MassGrams;
            }

            return total;
        }
    }

    public readonly struct ReactionRate
    {
        public ReactionRate(decimal reactionUnitsPerTick)
        {
            if (reactionUnitsPerTick <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reactionUnitsPerTick),
                    "Reaction units per simulation tick must be positive.");
            }

            ReactionUnitsPerTick = reactionUnitsPerTick;
        }

        public decimal ReactionUnitsPerTick { get; }
    }
}
