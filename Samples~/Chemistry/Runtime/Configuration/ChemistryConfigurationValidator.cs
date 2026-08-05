using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Configuration
{
    public sealed class ChemistryConfigurationValidationIssue
    {
        public ChemistryConfigurationValidationIssue(
            string code,
            string fieldPath,
            string message)
        {
            Code = code;
            FieldPath = fieldPath;
            Message = message;
        }

        public string Code { get; }
        public string FieldPath { get; }
        public string Message { get; }
    }

    public static class ChemistryConfigurationValidator
    {
        public const decimal MassToleranceGrams = 0.000001m;
        private const decimal GramUnitTolerance = 0.000000000001m;
        private static readonly Regex StableId = new Regex(
            "^[a-z0-9\\u3400-\\u4DBF\\u4E00-\\u9FFF]+"
            + "(?:[._-][a-z0-9\\u3400-\\u4DBF\\u4E00-\\u9FFF]+)*$",
            RegexOptions.CultureInvariant);

        public static IReadOnlyList<ChemistryConfigurationValidationIssue> Validate(
            ChemistryRuntimeConfiguration configuration,
            IEnumerable<string> knownEntityIds = null)
        {
            var issues = new List<ChemistryConfigurationValidationIssue>();
            if (configuration == null)
            {
                Add(issues, "chemistry.configuration.required", string.Empty,
                    "A chemistry configuration is required.");
                return Sort(issues);
            }

            var substances = Ids(
                issues,
                configuration.Substances,
                value => value == null ? null : value.Id,
                "substances",
                "substance");
            Ids(
                issues,
                configuration.Reactions,
                value => value == null ? null : value.Id,
                "reactions",
                "reaction");
            var entities = knownEntityIds == null
                ? null
                : new HashSet<string>(knownEntityIds, StringComparer.Ordinal);

            ValidateSubstances(issues, configuration.Substances);
            ValidateReactions(issues, configuration.Reactions, substances);
            ValidateInitials(
                issues,
                configuration.InitialSubstances,
                substances,
                entities);
            ValidateCapabilityBindings(
                issues,
                configuration.EntityCapabilities,
                entities);
            return Sort(issues);
        }

        private static HashSet<string> Ids<T>(
            ICollection<ChemistryConfigurationValidationIssue> issues,
            IReadOnlyList<T> values,
            Func<T, string> id,
            string path,
            string kind)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                if (value == null)
                {
                    Add(issues, kind + ".required", Index(path, index),
                        "A " + kind + " is required.");
                    continue;
                }

                var identifier = id(value);
                if (!IsStableId(identifier))
                {
                    Add(issues, "id." + kind + ".invalid",
                        Index(path, index) + ".id",
                        "标识必须使用稳定的自然中文或小写协议格式。");
                }
                var normalized = (identifier ?? string.Empty).Trim();
                if (!result.Add(normalized))
                {
                    Add(issues, "id." + kind + ".duplicate",
                        Index(path, index) + ".id",
                        "Duplicate " + kind + " ID '" + normalized + "'.");
                }
            }

            return result;
        }

        private static void ValidateSubstances(
            ICollection<ChemistryConfigurationValidationIssue> issues,
            IReadOnlyList<ChemistrySubstanceDefinition> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                if (value == null)
                {
                    continue;
                }

                var path = Index("substances", index);
                if (string.IsNullOrWhiteSpace(value.DisplayName)
                    || !string.Equals(
                        value.DisplayName,
                        value.DisplayName.Trim(),
                        StringComparison.Ordinal))
                {
                    Add(issues, "text.required", path + ".displayName",
                        "A display name is required.");
                }
                if (!Enum.IsDefined(typeof(MatterPhase), value.Phase))
                {
                    Add(issues, "substance.phase.unknown", path + ".phase",
                        "The matter phase must be known.");
                }
                if (value.MolarMassValue <= 0m)
                {
                    Add(issues, "range.substance.molar-mass",
                        path + ".molarMassValue",
                        "Molar mass must be positive.");
                }
                if (!Enum.IsDefined(
                    typeof(ChemistryMolarMassUnit),
                    value.MolarMassUnit))
                {
                    Add(issues, "unit.substance.molar-mass",
                        path + ".molarMassUnit",
                        "The molar-mass unit must be known.");
                }
            }
        }

        private static void ValidateReactions(
            ICollection<ChemistryConfigurationValidationIssue> issues,
            IReadOnlyList<ChemistryReactionDefinition> values,
            ISet<string> substances)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                if (value == null)
                {
                    continue;
                }

                var path = Index("reactions", index);
                if (!Enum.IsDefined(
                    typeof(ChemistryReactionProcessKind),
                    value.ProcessKind))
                {
                    Add(issues, "reaction.process-kind.unknown",
                        path + ".processKind", "The process kind must be known.");
                }
                if (value.MinimumTemperatureCelsius < 0m)
                {
                    Add(issues, "range.reaction.minimum-temperature",
                        path + ".minimumTemperatureCelsius",
                        "The minimum temperature cannot be negative.");
                }
                if (value.ReactionUnitsPerTick <= 0m)
                {
                    Add(issues, "range.reaction.units-per-tick",
                        path + ".reactionUnitsPerTick",
                        "Reaction units per tick must be positive.");
                }
                if (value.ProcessKind ==
                        ChemistryReactionProcessKind.Mixing &&
                    value.RequiresIgnition)
                {
                    Add(
                        issues,
                        "reaction.mixing.ignition.unsupported",
                        path + ".requiresIgnition",
                        "A mixing reaction cannot require ignition.");
                }

                var reactantsValid = ValidateTerms(
                    issues,
                    value.Reactants,
                    path + ".reactants",
                    substances,
                    "reaction.reactants.empty");
                var productsValid = ValidateTerms(
                    issues,
                    value.Products,
                    path + ".products",
                    substances,
                    "reaction.products.empty");
                if (!reactantsValid || !productsValid)
                {
                    continue;
                }

                if (!TryMass(value.Reactants, out var input)
                    || !TryMass(value.Products, out var output))
                {
                    Add(issues, "reaction.mass.overflow",
                        path + ".reactants|products",
                        "Reaction mass exceeds the decimal range.");
                }
                else if (Math.Abs(input - output) > MassToleranceGrams)
                {
                    Add(issues, "reaction.mass.unbalanced",
                        path + ".reactants|products",
                        "Reaction mass is not conserved.");
                }
            }
        }

        private static bool ValidateTerms(
            ICollection<ChemistryConfigurationValidationIssue> issues,
            IReadOnlyList<ChemistryReactionTerm> values,
            string path,
            ISet<string> substances,
            string emptyCode)
        {
            if (values == null || values.Count == 0)
            {
                Add(issues, emptyCode, path, "Reaction terms are required.");
                return false;
            }

            var valid = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                var itemPath = Index(path, index);
                if (value == null)
                {
                    Add(issues, "reaction.term.required", itemPath,
                        "A reaction term is required.");
                    valid = false;
                    continue;
                }
                if (!substances.Contains(value.SubstanceId ?? string.Empty))
                {
                    Add(issues, "reference.substance.unknown",
                        itemPath + ".substanceId",
                        "The substance reference is unknown.");
                    valid = false;
                }
                if (!seen.Add(value.SubstanceId ?? string.Empty))
                {
                    Add(issues, "reaction.term.duplicate",
                        itemPath + ".substanceId",
                        "A substance can occur only once per side.");
                    valid = false;
                }
                if (value.QuantityValue <= 0m)
                {
                    Add(issues, "range.reaction.quantity",
                        itemPath + ".quantityValue",
                        "Reaction quantity must be positive.");
                    valid = false;
                }
                if (!Enum.IsDefined(typeof(Unit), value.QuantityUnit))
                {
                    Add(issues, "unit.reaction.quantity",
                        itemPath + ".quantityUnit",
                        "The quantity unit must be known.");
                    valid = false;
                }
                if (!Enum.IsDefined(typeof(MatterPhase), value.Phase))
                {
                    Add(issues, "reaction.phase.unknown",
                        itemPath + ".phase",
                        "The matter phase must be known.");
                    valid = false;
                }
                if (value.GramsPerDeclaredUnit <= 0m)
                {
                    Add(issues, "reaction.mass-input.required",
                        itemPath + ".gramsPerDeclaredUnit",
                        "A positive mass input is required.");
                    valid = false;
                }
                else if (value.QuantityUnit == Unit.Gram
                    && Math.Abs(value.GramsPerDeclaredUnit - 1m)
                        > GramUnitTolerance)
                {
                    Add(issues, "reaction.mass-input.gram-mismatch",
                        itemPath + ".gramsPerDeclaredUnit",
                        "Gram quantities must use one gram per declared unit.");
                    valid = false;
                }
            }

            return valid;
        }

        private static void ValidateInitials(
            ICollection<ChemistryConfigurationValidationIssue> issues,
            IReadOnlyList<ChemistryInitialSubstance> values,
            ISet<string> substances,
            ISet<string> entities)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                var path = Index("initialSubstances", index);
                if (value == null)
                {
                    Add(issues, "initial-substance.required", path,
                        "An initial substance is required.");
                    continue;
                }
                if (entities != null
                    && !entities.Contains(value.EntityId ?? string.Empty))
                {
                    Add(issues, "reference.entity.unknown",
                        path + ".entityId", "The entity reference is unknown.");
                }
                if (!substances.Contains(value.SubstanceId ?? string.Empty))
                {
                    Add(issues, "reference.substance.unknown",
                        path + ".substanceId",
                        "The substance reference is unknown.");
                }
                if (HasSurroundingWhitespace(value.EntityId))
                {
                    Add(issues, "id.reference.whitespace",
                        path + ".entityId",
                        "Entity references cannot contain surrounding whitespace.");
                }
                if (HasSurroundingWhitespace(value.SubstanceId))
                {
                    Add(issues, "id.reference.whitespace",
                        path + ".substanceId",
                        "Substance references cannot contain surrounding whitespace.");
                }
                if (value.QuantityValue < 0m)
                {
                    Add(issues, "range.initial-substance.quantity",
                        path + ".quantityValue",
                        "Initial quantity cannot be negative.");
                }
                if (!Enum.IsDefined(typeof(Unit), value.QuantityUnit))
                {
                    Add(issues, "unit.initial-substance.quantity",
                        path + ".quantityUnit",
                        "The quantity unit must be known.");
                }
                if (!Enum.IsDefined(typeof(MatterPhase), value.Phase))
                {
                    Add(issues, "initial-substance.phase.unknown",
                        path + ".phase",
                        "The matter phase must be known.");
                }
                if (!Enum.IsDefined(
                    typeof(ChemistryTemperatureUnit),
                    value.TemperatureUnit))
                {
                    Add(issues, "unit.initial-substance.temperature",
                        path + ".temperatureUnit",
                        "The temperature unit must be known.");
                }
            }
        }

        private static void ValidateCapabilityBindings(
            ICollection<ChemistryConfigurationValidationIssue> issues,
            IReadOnlyList<ChemistryEntityCapabilityBinding> values,
            ISet<string> entities)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                var path = Index("entityCapabilities", index);
                if (value == null)
                {
                    Add(
                        issues,
                        "chemistry.capability.required",
                        path,
                        "A chemistry entity capability binding is required.");
                    continue;
                }

                if (!IsStableId(value.EntityId))
                {
                    Add(
                        issues,
                        "id.reference.invalid",
                        path + ".entityId",
                        "The entity reference must be a stable lowercase identifier.");
                }
                if (entities != null
                    && !entities.Contains(value.EntityId ?? string.Empty))
                {
                    Add(
                        issues,
                        "reference.entity.unknown",
                        path + ".entityId",
                        "The entity reference is unknown.");
                }
                if (!string.Equals(
                        value.CapabilityId,
                        ChemistryCapabilityIds.Combustible,
                        StringComparison.Ordinal))
                {
                    Add(
                        issues,
                        "chemistry.capability.unknown",
                        path + ".capabilityId",
                        "The chemistry capability ID is not supported.");
                }

                var key = (value.EntityId ?? string.Empty)
                    + "\n"
                    + (value.CapabilityId ?? string.Empty);
                if (!seen.Add(key))
                {
                    Add(
                        issues,
                        "chemistry.capability.duplicate",
                        path,
                        "A chemistry capability can be bound only once per entity.");
                }
            }
        }

        private static bool TryMass(
            IEnumerable<ChemistryReactionTerm> terms,
            out decimal mass)
        {
            mass = 0m;
            try
            {
                checked
                {
                    foreach (var term in terms)
                    {
                        mass += term.QuantityValue * term.GramsPerDeclaredUnit;
                    }
                }
            }
            catch (OverflowException)
            {
                return false;
            }
            return true;
        }

        private static bool IsStableId(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && string.Equals(value, value.Trim(), StringComparison.Ordinal)
                && StableId.IsMatch(value);
        }

        private static bool HasSurroundingWhitespace(string value)
        {
            return value != null
                && !string.Equals(value, value.Trim(), StringComparison.Ordinal);
        }

        private static string Index(string path, int index)
        {
            return path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
        }

        private static void Add(
            ICollection<ChemistryConfigurationValidationIssue> issues,
            string code,
            string fieldPath,
            string message)
        {
            issues.Add(
                new ChemistryConfigurationValidationIssue(
                    code,
                    fieldPath,
                    message));
        }

        private static IReadOnlyList<ChemistryConfigurationValidationIssue> Sort(
            IEnumerable<ChemistryConfigurationValidationIssue> issues)
        {
            return new ReadOnlyCollection<ChemistryConfigurationValidationIssue>(
                issues
                    .OrderBy(value => value.Code, StringComparer.Ordinal)
                    .ThenBy(value => value.FieldPath, StringComparer.Ordinal)
                    .ThenBy(value => value.Message, StringComparer.Ordinal)
                    .ToList());
        }
    }
}
