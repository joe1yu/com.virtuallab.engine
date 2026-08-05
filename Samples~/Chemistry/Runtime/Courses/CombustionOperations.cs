using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;
using VirtualLab.Measurement;
using VirtualLab.Spatial.Courses;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 管理点燃、熄灭和燃烧反应；不会发布火焰或粒子等表现事件。
    /// </summary>
    public sealed class CombustionOperations
    {
        public const string IgniteOperationId = "开始燃烧";
        public const string ExtinguishOperationId = "停止燃烧";
        public const string CombustionProcessId = "燃烧过程";

        private readonly IReadOnlyDictionary<string, ChemicalReactionDefinition>
            _reactions;

        public CombustionOperations(
            IEnumerable<ChemicalReactionDefinition> reactions)
        {
            _reactions = CopyReactions(reactions);
        }

        public void RegisterWith(ConfiguredStateOperationRegistry registry)
        {
            registry.Register(new IgniteOperation(_reactions));
            registry.Register(new ExtinguishOperation());
        }

        public void Advance(
            ExperimentWorld world,
            double elapsedSeconds,
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

            if (!IsPositiveFinite(elapsedSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds));
            }

            foreach (var process in world.ActiveProcesses
                .Where(value => value.ProcessId == CombustionProcessId)
                .OrderBy(value => value.EntityId.Value, StringComparer.Ordinal)
                .ToArray())
            {
                var reaction =
                    _reactions[process.TextParameters[ChemistryConfigurationKeys.Common.ReactionId]];
                var rate = (decimal)process.NumberParameters[
                    ChemistryConfigurationKeys.Combustion.ReactionUnitsPerSecond] * (decimal)elapsedSeconds;
                var temperature = FuelTemperature(
                    world,
                    process.EntityId,
                    process.TextParameters[ChemistryConfigurationKeys.Combustion.FuelId]);
                var advanced = ReactionProgress.Advance(
                    world.RequireMatterInventory(),
                    process.EntityId,
                    reaction,
                    new ReactionRate(rate),
                    true,
                    null,
                    temperature,
                    tick,
                    events);
                var canContinue = advanced
                    && reaction.Reactants.All(
                        reactant => world.RequireMatterInventory().Total(
                            process.EntityId,
                            reactant.SubstanceId,
                            reactant.Quantity.Unit).Value
                            > 0m);
                if (!canContinue)
                {
                    world.StopProcess(
                        CombustionProcessId,
                        process.EntityId);
                }
            }
        }

        private sealed class IgniteOperation : IConfiguredStateOperation
        {
            private readonly IReadOnlyDictionary<
                string,
                ChemicalReactionDefinition> _reactions;

            public IgniteOperation(
                IReadOnlyDictionary<string, ChemicalReactionDefinition>
                    reactions)
            {
                _reactions = reactions;
            }

            public string OperationId => IgniteOperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var reactionId = Text(mutation, ChemistryConfigurationKeys.Common.ReactionId);
                if (!_reactions.TryGetValue(reactionId, out var reaction))
                {
                    throw new ArgumentException(
                        $"燃烧反应“{reactionId}”未注册。");
                }

                var fuelId = Text(mutation, ChemistryConfigurationKeys.Combustion.FuelId);
                var oxidizerId = Text(mutation, ChemistryConfigurationKeys.Combustion.OxidizerId);
                var minimumTemperature = Number(
                    mutation,
                    ChemistryConfigurationKeys.Combustion.MinimumIgnitionTemperatureCelsius);
                var maximumDistance = Number(mutation, ChemistryConfigurationKeys.Combustion.MaximumIgnitionDistanceMeters);
                var rate = Number(mutation, ChemistryConfigurationKeys.Combustion.ReactionUnitsPerSecond);
                var source = new EntityId(request.SourceEntityId);
                var oxidizerSource = OptionalEntityId(
                    mutation,
                    ChemistryConfigurationKeys.Combustion.OxidizerSourceEntityId,
                    source);
                var failures = new List<string>();

                if (request.TargetEntityId == null
                    || !world.TryGetEntity(
                        new EntityId(request.TargetEntityId),
                        out var igniter)
                    || !igniter.HasCapability<Heat来源对象能力>())
                {
                    failures.Add(Text(mutation, ChemistryConfigurationKeys.Combustion.IgnitionSourceInvalidRejectionReason));
                }

                if (RequestNumber(
                        request,
                        SpatialRequestParameterKeys.DistanceMeters)
                    > maximumDistance)
                {
                    failures.Add(Text(mutation, ChemistryConfigurationKeys.Combustion.IgnitionDistanceRejectionReason));
                }

                if (!world.TryGetEntity(source, out var fuelEntity)
                    || !fuelEntity.HasCapability<CombustibleCapability>())
                {
                    failures.Add(Text(mutation, ChemistryConfigurationKeys.Combustion.NotCombustibleRejectionReason));
                }

                var oxidizerTerm = reaction.Reactants.FirstOrDefault(
                    value => value.SubstanceId == oxidizerId);
                if (oxidizerTerm == null
                    || TotalOrZero(world, oxidizerSource, oxidizerId)
                    < oxidizerTerm.Quantity.Value)
                {
                    failures.Add(Text(mutation, ChemistryConfigurationKeys.Combustion.MissingOxidizerRejectionReason));
                }

                if (FuelTemperature(world, source, fuelId).Celsius
                    < (decimal)minimumTemperature)
                {
                    failures.Add(Text(mutation, ChemistryConfigurationKeys.Combustion.FuelTemperatureRejectionReason));
                }

                AddRequiredHeatSourceStateFailure(
                    world,
                    mutation,
                    failures);
                AddSafetyLiquidFailure(
                    world,
                    mutation,
                    failures);

                if (failures.Count > 0)
                {
                    throw new ConfiguredOperationRejectedException(failures);
                }

                if (!IsPositiveFinite(rate))
                {
                    throw new ArgumentOutOfRangeException(ChemistryConfigurationKeys.Combustion.ReactionUnitsPerSecond);
                }

                if (oxidizerSource != source)
                {
                    world.RequireMatterInventory().Transfer(
                        oxidizerSource,
                        source,
                        oxidizerId,
                        oxidizerTerm.Quantity,
                        new SimulationTick(0),
                        SilentProcessEventCollector.Instance);
                }

                world.StartProcess(
                    CombustionProcessId,
                    source,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [ChemistryConfigurationKeys.Common.ReactionId] = reactionId,
                        [ChemistryConfigurationKeys.Combustion.FuelId] = fuelId,
                        [ChemistryConfigurationKeys.Combustion.OxidizerId] = oxidizerId
                    },
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        [ChemistryConfigurationKeys.Combustion.ReactionUnitsPerSecond] = rate
                    });
            }
        }

        private sealed class ExtinguishOperation :
            IConfiguredStateOperation
        {
            public string OperationId => ExtinguishOperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                world.StopProcess(
                    CombustionProcessId,
                    new EntityId(request.SourceEntityId));
            }
        }

        private static IReadOnlyDictionary<
            string,
            ChemicalReactionDefinition> CopyReactions(
                IEnumerable<ChemicalReactionDefinition> reactions)
        {
            if (reactions == null)
            {
                throw new ArgumentNullException(nameof(reactions));
            }

            return reactions.ToDictionary(
                value => value.Id,
                StringComparer.Ordinal);
        }

        private static decimal TotalOrZero(
            ExperimentWorld world,
            EntityId location,
            string substanceId)
        {
            try
            {
                return world.RequireMatterInventory().Total(location, substanceId).Value;
            }
            catch (InvalidOperationException)
            {
                return 0m;
            }
        }

        private static Temperature FuelTemperature(
            ExperimentWorld world,
            EntityId source,
            string fuelId)
        {
            if (world.TryGetScalar(
                    ChemistryWorldStateKeys.Temperature(source.Value),
                    out var configuredTemperature))
            {
                if (!configuredTemperature.Unit.Equals(
                        new WorldScalarUnit("摄氏度")))
                {
                    throw new InvalidOperationException(
                        "可燃烧温度状态必须使用摄氏度。");
                }

                return new Temperature(
                    (decimal)configuredTemperature.Value);
            }

            return world.RequireMatterInventory().TryGetTemperature(
                source,
                fuelId,
                out var matterTemperature)
                ? matterTemperature
                : new Temperature(20m);
        }

        internal static string Text(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ArgumentException($"配置参数“{key}”必须是文本。");
            }

            return value.Text.Trim();
        }

        internal static double Number(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Number)
            {
                throw new ArgumentException($"配置参数“{key}”必须是数值。");
            }

            return value.Number;
        }

        private static EntityId OptionalEntityId(
            ConfiguredMutationDefinition mutation,
            string key,
            EntityId fallback)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value))
            {
                return fallback;
            }

            if (value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ArgumentException($"配置参数“{key}”必须是文本。");
            }

            return new EntityId(value.Text.Trim());
        }

        private static void AddRequiredHeatSourceStateFailure(
            ExperimentWorld world,
            ConfiguredMutationDefinition mutation,
            ICollection<string> failures)
        {
            if (!mutation.Parameters.ContainsKey(ChemistryConfigurationKeys.Heating.HeatSourceStateKey))
            {
                return;
            }

            var key = Text(mutation, ChemistryConfigurationKeys.Heating.HeatSourceStateKey);
            if (!world.TryGetScalar(key, out var state)
                || state.Value <= 0d)
            {
                failures.Add(Text(mutation, ChemistryConfigurationKeys.Heating.HeatSourceNotIgnitedRejectionReason));
            }
        }

        private static void AddSafetyLiquidFailure(
            ExperimentWorld world,
            ConfiguredMutationDefinition mutation,
            ICollection<string> failures)
        {
            if (!mutation.Parameters.ContainsKey(ChemistryConfigurationKeys.Combustion.SafeContainerEntityId))
            {
                return;
            }

            var container = new EntityId(
                Text(mutation, ChemistryConfigurationKeys.Combustion.SafeContainerEntityId));
            var minimum = (decimal)Number(
                mutation,
                ChemistryConfigurationKeys.Combustion.MinimumSafeLiquidVolumeMillilitres);
            var actual = world.RequireMatterInventory().Entries
                .Where(value => value.LocationId == container
                    && value.Batch.Phase == MatterPhase.Liquid
                    && value.Batch.Quantity.Unit == Unit.Millilitre)
                .Sum(value => value.Batch.Quantity.Value);
            var riskStateKey = mutation.Parameters.ContainsKey(
                    ChemistryConfigurationKeys.Combustion.InsufficientLiquidRiskStateKey)
                ? Text(mutation, ChemistryConfigurationKeys.Combustion.InsufficientLiquidRiskStateKey)
                : null;
            if (riskStateKey != null)
            {
                world.SetScalar(
                    riskStateKey,
                    actual < minimum ? 1d : 0d,
                    new WorldScalarUnit("布尔标记"),
                    0d,
                    1d);
            }

            if (actual < minimum)
            {
                if (riskStateKey == null)
                {
                    failures.Add(Text(mutation, ChemistryConfigurationKeys.Combustion.InsufficientLiquidRejectionReason));
                }
            }
        }

        internal static double RequestNumber(
            SemanticActionRequest request,
            string key)
        {
            if (!request.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Number)
            {
                throw new ArgumentException($"动作参数“{key}”必须是数值。");
            }

            return value.Number;
        }

        internal static bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value > 0d;
        }
    }
}
