using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using VirtualLab.Domain.Matter;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Configuration
{
    public sealed class ChemistryConfigurationCodec
    {
        private const int MaximumPayloadCharacters = 4 * 1024 * 1024;

        private static readonly JsonSerializerSettings Settings =
            new JsonSerializerSettings
            {
                ContractResolver =
                    new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                Formatting = Formatting.None,
                MaxDepth = 64,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Include,
                TypeNameHandling = TypeNameHandling.None
            };

        /// <summary>
        /// 为本地课程资产编码化学配置正文。
        /// </summary>
        public string EncodeCourseConfiguration(
            ChemistryRuntimeConfiguration configuration,
            IEnumerable<string> knownEntityIds = null)
        {
            var issues = ChemistryConfigurationValidator.Validate(
                configuration,
                knownEntityIds);
            if (issues.Count > 0)
            {
                throw new ArgumentException(
                    issues[0].Code + ": " + issues[0].FieldPath,
                    nameof(configuration));
            }

            return EncodePayload(configuration);
        }

        public ChemistryRuntimeConfiguration DecodeCourseConfiguration(
            string payload,
            IEnumerable<string> knownEntityIds = null)
        {
            if (!TryDecodeCourseConfiguration(
                    payload,
                    knownEntityIds,
                    out var configuration,
                    out var errorCode))
            {
                throw new ArgumentException(
                    errorCode,
                    nameof(payload));
            }

            return configuration;
        }

        public bool TryDecodeCourseConfiguration(
            string payload,
            IEnumerable<string> knownEntityIds,
            out ChemistryRuntimeConfiguration configuration,
            out string errorCode)
        {
            configuration = null;
            errorCode = null;
            if (payload == null)
            {
                errorCode = "chemistry.course.payload.required";
                return false;
            }

            if (payload.Length > MaximumPayloadCharacters)
            {
                errorCode = "chemistry.course.payload.too-large";
                return false;
            }

            if (!TryParsePayload(
                    payload.Trim(),
                    out configuration,
                    out errorCode))
            {
                return false;
            }

            var issues = ChemistryConfigurationValidator.Validate(
                configuration,
                knownEntityIds);
            if (issues.Count == 0)
            {
                return true;
            }

            configuration = null;
            errorCode = issues[0].Code;
            return false;
        }

        private static bool TryParsePayload(
            string payload,
            out ChemistryRuntimeConfiguration configuration,
            out string errorCode)
        {
            configuration = null;
            errorCode = null;
            try
            {
                JObject root;
                using (var text = new StringReader(payload))
                using (var reader = new JsonTextReader(text)
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 64
                })
                {
                    root = JToken.Load(
                        reader,
                        new JsonLoadSettings
                        {
                            CommentHandling = CommentHandling.Load,
                            DuplicatePropertyNameHandling =
                                DuplicatePropertyNameHandling.Error
                        }) as JObject;
                    if (reader.Read())
                    {
                        errorCode = "chemistry.course.json.invalid";
                        return false;
                    }
                }

                if (root == null
                    || root.DescendantsAndSelf().Any(
                        token => token.Type == JTokenType.Comment
                            || token.Type == JTokenType.Null))
                {
                    errorCode = "chemistry.course.json.invalid";
                    return false;
                }

                var transport = root.ToObject<ConfigurationTransport>(
                    JsonSerializer.Create(Settings));
                if (transport == null
                    || transport.Substances == null
                    || transport.Reactions == null
                    || transport.InitialSubstances == null
                    || transport.EntityCapabilities == null)
                {
                    errorCode = "chemistry.course.definition.invalid";
                    return false;
                }

                configuration = FromTransport(transport);
                var issues =
                    ChemistryConfigurationValidator.Validate(configuration);
                if (issues.Count == 0)
                {
                    return true;
                }

                configuration = null;
                errorCode = "chemistry.course.definition.invalid";
                return false;
            }
            catch (Exception error) when (
                error is JsonException
                || error is ArgumentException
                || error is FormatException
                || error is OverflowException
                || error is InvalidOperationException)
            {
                configuration = null;
                errorCode = "chemistry.course.definition.invalid";
                return false;
            }
        }

        private static string EncodePayload(
            ChemistryRuntimeConfiguration configuration)
        {
            return JsonConvert.SerializeObject(
                ToTransport(configuration),
                Settings);
        }

        private static ConfigurationTransport ToTransport(
            ChemistryRuntimeConfiguration configuration)
        {
            return new ConfigurationTransport
            {
                Substances = configuration.Substances
                    .OrderBy(value => value.Id, StringComparer.Ordinal)
                    .Select(value => new SubstanceTransport
                    {
                        Id = value.Id,
                        DisplayName = value.DisplayName,
                        Phase = value.Phase,
                        MolarMassValue = value.MolarMassValue,
                        MolarMassUnit = value.MolarMassUnit
                    })
                    .ToList(),
                Reactions = configuration.Reactions
                    .OrderBy(value => value.Id, StringComparer.Ordinal)
                    .Select(value => new ReactionTransport
                    {
                        Id = value.Id,
                        Reactants = Terms(value.Reactants),
                        Products = Terms(value.Products),
                        ProcessKind = value.ProcessKind,
                        MinimumTemperatureCelsius =
                            value.MinimumTemperatureCelsius,
                        ReactionUnitsPerTick = value.ReactionUnitsPerTick,
                        RequiresIgnition = value.RequiresIgnition
                    })
                    .ToList(),
                InitialSubstances = configuration.InitialSubstances
                    .OrderBy(value => value.EntityId, StringComparer.Ordinal)
                    .ThenBy(value => value.SubstanceId, StringComparer.Ordinal)
                    .ThenBy(value => value.QuantityUnit)
                    .ThenBy(value => value.QuantityValue)
                    .ThenBy(value => value.Phase)
                    .ThenBy(value => value.TemperatureUnit)
                    .ThenBy(value => value.TemperatureValue)
                    .Select(value => new InitialTransport
                    {
                        EntityId = value.EntityId,
                        SubstanceId = value.SubstanceId,
                        QuantityValue = value.QuantityValue,
                        QuantityUnit = value.QuantityUnit,
                        Phase = value.Phase,
                        TemperatureValue = value.TemperatureValue,
                        TemperatureUnit = value.TemperatureUnit
                    })
                    .ToList(),
                EntityCapabilities = configuration.EntityCapabilities
                    .OrderBy(value => value.EntityId, StringComparer.Ordinal)
                    .ThenBy(value => value.CapabilityId, StringComparer.Ordinal)
                    .Select(value => new EntityCapabilityTransport
                    {
                        EntityId = value.EntityId,
                        CapabilityId = value.CapabilityId
                    })
                    .ToList()
            };
        }

        private static List<TermTransport> Terms(
            IEnumerable<ChemistryReactionTerm> values)
        {
            return values
                .OrderBy(value => value.SubstanceId, StringComparer.Ordinal)
                .ThenBy(value => value.QuantityUnit)
                .ThenBy(value => value.QuantityValue)
                .ThenBy(value => value.Phase)
                .ThenBy(value => value.GramsPerDeclaredUnit)
                .Select(value => new TermTransport
                {
                    SubstanceId = value.SubstanceId,
                    QuantityValue = value.QuantityValue,
                    QuantityUnit = value.QuantityUnit,
                    Phase = value.Phase,
                    GramsPerDeclaredUnit = value.GramsPerDeclaredUnit
                })
                .ToList();
        }

        private static ChemistryRuntimeConfiguration FromTransport(
            ConfigurationTransport value)
        {
            return new ChemistryRuntimeConfiguration(
                value.Substances.Select(item =>
                    new ChemistrySubstanceDefinition(
                        item.Id,
                        item.DisplayName,
                        item.Phase,
                        item.MolarMassValue,
                        item.MolarMassUnit)).ToList(),
                value.Reactions.Select(item =>
                    new ChemistryReactionDefinition(
                        item.Id,
                        FromTerms(item.Reactants),
                        FromTerms(item.Products),
                        item.ProcessKind,
                        item.MinimumTemperatureCelsius,
                        item.ReactionUnitsPerTick,
                        item.RequiresIgnition)).ToList(),
                value.InitialSubstances.Select(item =>
                    new ChemistryInitialSubstance(
                        item.EntityId,
                        item.SubstanceId,
                        item.QuantityValue,
                        item.QuantityUnit,
                        item.Phase,
                        item.TemperatureValue,
                        item.TemperatureUnit)).ToList(),
                value.EntityCapabilities.Select(item =>
                    new ChemistryEntityCapabilityBinding(
                        item.EntityId,
                        item.CapabilityId)).ToList());
        }

        private static List<ChemistryReactionTerm> FromTerms(
            IEnumerable<TermTransport> values)
        {
            return values.Select(item =>
                new ChemistryReactionTerm(
                    item.SubstanceId,
                    item.QuantityValue,
                    item.QuantityUnit,
                    item.Phase,
                    item.GramsPerDeclaredUnit)).ToList();
        }

        private sealed class ConfigurationTransport
        {
            [JsonProperty("substances", Required = Required.Always)]
            public List<SubstanceTransport> Substances { get; set; }

            [JsonProperty("reactions", Required = Required.Always)]
            public List<ReactionTransport> Reactions { get; set; }

            [JsonProperty("initialSubstances", Required = Required.Always)]
            public List<InitialTransport> InitialSubstances { get; set; }

            [JsonProperty("entityCapabilities", Required = Required.Always)]
            public List<EntityCapabilityTransport> EntityCapabilities { get; set; }
        }

        private sealed class SubstanceTransport
        {
            [JsonProperty("id", Required = Required.Always)]
            public string Id { get; set; }
            [JsonProperty("displayName", Required = Required.Always)]
            public string DisplayName { get; set; }
            [JsonProperty("phase", Required = Required.Always)]
            public MatterPhase Phase { get; set; }
            [JsonProperty("molarMassValue", Required = Required.Always)]
            public decimal MolarMassValue { get; set; }
            [JsonProperty("molarMassUnit", Required = Required.Always)]
            public ChemistryMolarMassUnit MolarMassUnit { get; set; }
        }

        private sealed class ReactionTransport
        {
            [JsonProperty("id", Required = Required.Always)]
            public string Id { get; set; }
            [JsonProperty("reactants", Required = Required.Always)]
            public List<TermTransport> Reactants { get; set; }
            [JsonProperty("products", Required = Required.Always)]
            public List<TermTransport> Products { get; set; }
            [JsonProperty("processKind", Required = Required.Always)]
            public ChemistryReactionProcessKind ProcessKind { get; set; }
            [JsonProperty("minimumTemperatureCelsius", Required = Required.Always)]
            public decimal MinimumTemperatureCelsius { get; set; }
            [JsonProperty("reactionUnitsPerTick", Required = Required.Always)]
            public decimal ReactionUnitsPerTick { get; set; }
            [JsonProperty("requiresIgnition", Required = Required.Always)]
            public bool RequiresIgnition { get; set; }
        }

        private sealed class TermTransport
        {
            [JsonProperty("substanceId", Required = Required.Always)]
            public string SubstanceId { get; set; }
            [JsonProperty("quantityValue", Required = Required.Always)]
            public decimal QuantityValue { get; set; }
            [JsonProperty("quantityUnit", Required = Required.Always)]
            public Unit QuantityUnit { get; set; }
            [JsonProperty("phase", Required = Required.Always)]
            public MatterPhase Phase { get; set; }
            [JsonProperty("gramsPerDeclaredUnit", Required = Required.Always)]
            public decimal GramsPerDeclaredUnit { get; set; }
        }

        private sealed class InitialTransport
        {
            [JsonProperty("entityId", Required = Required.Always)]
            public string EntityId { get; set; }
            [JsonProperty("substanceId", Required = Required.Always)]
            public string SubstanceId { get; set; }
            [JsonProperty("quantityValue", Required = Required.Always)]
            public decimal QuantityValue { get; set; }
            [JsonProperty("quantityUnit", Required = Required.Always)]
            public Unit QuantityUnit { get; set; }
            [JsonProperty("phase", Required = Required.Always)]
            public MatterPhase Phase { get; set; }
            [JsonProperty("temperatureValue", Required = Required.Always)]
            public decimal TemperatureValue { get; set; }
            [JsonProperty("temperatureUnit", Required = Required.Always)]
            public ChemistryTemperatureUnit TemperatureUnit { get; set; }
        }

        private sealed class EntityCapabilityTransport
        {
            [JsonProperty("entityId", Required = Required.Always)]
            public string EntityId { get; set; }

            [JsonProperty("capabilityId", Required = Required.Always)]
            public string CapabilityId { get; set; }
        }
    }
}
