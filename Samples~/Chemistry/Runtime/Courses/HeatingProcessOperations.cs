using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 使用配置的热学参数推进温度，并在阈值满足后复用化学反应模型。
    /// </summary>
    public sealed class HeatingProcessOperations
    {
        public const string BeginOperationId = "开始加热过程";
        public const string EndOperationId = "结束加热过程";
        public const string HeatingProcessId = "加热过程";
        public const string ReactionUnitsPerAdvanceParameter =
            ChemistryConfigurationKeys.Heating.ReactionUnitsPerAdvance;

        private readonly IReadOnlyDictionary<string, ChemicalReactionDefinition>
            _reactions;

        public HeatingProcessOperations(
            IEnumerable<ChemicalReactionDefinition> reactions)
        {
            _reactions = reactions.ToDictionary(
                value => value.Id,
                StringComparer.Ordinal);
        }

        public void RegisterWith(ConfiguredStateOperationRegistry registry)
        {
            registry.Register(new BeginOperation(_reactions));
            registry.Register(new EndOperation());
        }

        public void Advance(
            ExperimentWorld world,
            double elapsedSeconds,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            if (!CombustionOperations.IsPositiveFinite(elapsedSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds));
            }

            foreach (var process in world.ActiveProcesses
                .Where(value => value.ProcessId == HeatingProcessId)
                .OrderBy(value => value.EntityId.Value, StringComparer.Ordinal)
                .ToArray())
            {
                var key = process.EntityId.Value + ".温度";
                var ambient = process.NumberParameters[ChemistryConfigurationKeys.Heating.AmbientTemperatureCelsius];
                var current = world.TryGetScalar(key, out var value)
                    ? value.Value
                    : ambient;
                if (world.TryGetScalar(key, out value)
                    && !value.Unit.Equals(
                        new WorldScalarUnit("摄氏度")))
                {
                    throw new InvalidOperationException(
                        "受热对象温度状态必须使用摄氏度。");
                }
                var power = process.NumberParameters[ChemistryConfigurationKeys.Heating.ThermalPower];
                var efficiency = process.NumberParameters[ChemistryConfigurationKeys.Heating.Efficiency];
                var heatCapacity = process.NumberParameters[ChemistryConfigurationKeys.Heating.HeatCapacity];
                var loss = process.NumberParameters[ChemistryConfigurationKeys.Heating.HeatLossCoefficient];
                var netPower =
                    power * efficiency - loss * (current - ambient);
                var next = current
                    + netPower * elapsedSeconds / heatCapacity;
                if (double.IsNaN(next) || double.IsInfinity(next))
                {
                    throw new InvalidOperationException(
                        "加热计算得到非有限温度。");
                }
                if (process.TextParameters.TryGetValue(
                    ChemistryConfigurationKeys.Common.ReactionId,
                    out var reactionId)
                    && next
                        >= process.NumberParameters[ChemistryConfigurationKeys.Heating.ReactionThresholdCelsius])
                {
                    ReactionProgress.Advance(
                        world.Matter,
                        process.EntityId,
                        _reactions[reactionId],
                        new ReactionRate(
                            (decimal)process.NumberParameters[
                                ReactionUnitsPerAdvanceParameter]),
                        true,
                        null,
                        new Temperature((decimal)next),
                        tick,
                        events);
                }

                // 反应及事件提交成功后再写温度，避免失败 Tick 留下半提交状态。
                world.SetScalar(
                    key,
                    next,
                    new WorldScalarUnit("摄氏度"),
                    null,
                    null);
            }
        }

        private sealed class BeginOperation : IConfiguredStateOperation
        {
            private readonly IReadOnlyDictionary<
                string,
                ChemicalReactionDefinition> _reactions;

            public BeginOperation(
                IReadOnlyDictionary<string, ChemicalReactionDefinition>
                    reactions)
            {
                _reactions = reactions;
            }

            public string OperationId => BeginOperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var source = new EntityId(request.SourceEntityId);
                var failures = new List<string>();
                if (!world.TryGetEntity(source, out var heated)
                    || !heated.HasCapability<HeatableCapability>())
                {
                    failures.Add("对象不可加热");
                }

                if (request.TargetEntityId == null
                    || !world.TryGetEntity(
                        new EntityId(request.TargetEntityId),
                        out var heater)
                    || !heater.HasCapability<Heat来源对象能力>())
                {
                    failures.Add("热源无效");
                }

                AddRequiredRelationFailure(
                    world,
                    source,
                    mutation,
                    InteractionRelationTypeIds.ContainedBy,
                    ChemistryConfigurationKeys.Heating.RequiredContainedEntityId,
                    ChemistryConfigurationKeys.Heating.MissingContainedEntityRejectionReason,
                    ChemistryConfigurationKeys.Heating.MissingContainedEntityRiskStateKey,
                    failures);
                AddRequiredRelationFailure(
                    world,
                    source,
                    mutation,
                    InteractionRelationTypeIds.Connection,
                    ChemistryConfigurationKeys.Heating.RequiredConnectedEntityId,
                    ChemistryConfigurationKeys.Heating.MissingConnectedEntityRejectionReason,
                    ChemistryConfigurationKeys.Heating.MissingConnectedEntityRiskStateKey,
                    failures);
                AddRequiredHeatSourceStateFailure(
                    world,
                    mutation,
                    failures);

                if (failures.Count > 0)
                {
                    throw new ConfiguredOperationRejectedException(failures);
                }

                var numbers = new Dictionary<string, double>(
                    StringComparer.Ordinal)
                {
                    [ChemistryConfigurationKeys.Heating.ThermalPower] =
                        CombustionOperations.Number(mutation, ChemistryConfigurationKeys.Heating.ThermalPower),
                    [ChemistryConfigurationKeys.Heating.Efficiency] =
                        CombustionOperations.Number(mutation, ChemistryConfigurationKeys.Heating.Efficiency),
                    [ChemistryConfigurationKeys.Heating.HeatCapacity] =
                        CombustionOperations.Number(mutation, ChemistryConfigurationKeys.Heating.HeatCapacity),
                    [ChemistryConfigurationKeys.Heating.HeatLossCoefficient] =
                        CombustionOperations.Number(mutation, ChemistryConfigurationKeys.Heating.HeatLossCoefficient),
                    [ChemistryConfigurationKeys.Heating.AmbientTemperatureCelsius] =
                        CombustionOperations.Number(
                            mutation,
                            ChemistryConfigurationKeys.Heating.AmbientTemperatureCelsius)
                };
                if (numbers[ChemistryConfigurationKeys.Heating.ThermalPower] < 0d
                    || numbers[ChemistryConfigurationKeys.Heating.Efficiency] < 0d
                    || numbers[ChemistryConfigurationKeys.Heating.HeatCapacity] <= 0d
                    || numbers[ChemistryConfigurationKeys.Heating.HeatLossCoefficient] < 0d)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(mutation),
                        "热学参数超出允许范围。");
                }

                var text = new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    [ChemistryConfigurationKeys.Heating.HeatSourceEntityId] = request.TargetEntityId
                };
                if (mutation.Parameters.ContainsKey(ChemistryConfigurationKeys.Common.ReactionId))
                {
                    var reactionId =
                        CombustionOperations.Text(mutation, ChemistryConfigurationKeys.Common.ReactionId);
                    if (!_reactions.ContainsKey(reactionId))
                    {
                        throw new ArgumentException(
                            $"受热反应“{reactionId}”未注册。");
                    }

                    text[ChemistryConfigurationKeys.Common.ReactionId] = reactionId;
                    numbers[ChemistryConfigurationKeys.Heating.ReactionThresholdCelsius] =
                        CombustionOperations.Number(
                            mutation,
                            ChemistryConfigurationKeys.Heating.ReactionThresholdCelsius);
                    numbers[ReactionUnitsPerAdvanceParameter] =
                        CombustionOperations.Number(
                            mutation,
                            ReactionUnitsPerAdvanceParameter);
                }

                world.StartProcess(
                    HeatingProcessId,
                    source,
                    text,
                    numbers);
                world.SetRelation(
                    new EntityRelation(
                        ChemistryRelationTypeIds.HeatedBy,
                        source,
                        new EntityId(request.TargetEntityId)));
            }
        }

        private sealed class EndOperation : IConfiguredStateOperation
        {
            public string OperationId => EndOperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var source = new EntityId(request.SourceEntityId);
                if (!world.TryGetProcess(
                        HeatingProcessId,
                        source,
                        out var process)
                    || request.TargetEntityId == null
                    || !string.Equals(
                        process.TextParameters[ChemistryConfigurationKeys.Heating.HeatSourceEntityId],
                        request.TargetEntityId,
                        StringComparison.Ordinal))
                {
                    throw new ConfiguredOperationRejectedException(
                        new[] { "指定热源未在加热该对象" });
                }

                var detectRisk = mutation.Parameters.TryGetValue(
                        ChemistryConfigurationKeys.Heating.StopHeatingConnectionTriggersRisk,
                        out var riskValue)
                    && riskValue.Kind == StructuredValueKind.Boolean
                    && riskValue.Boolean;
                var riskConnectionId = detectRisk
                    ? CombustionOperations.Text(
                        mutation,
                        ChemistryConfigurationKeys.Heating.RiskConnectionEntityId)
                    : null;
                var riskConnection = riskConnectionId == null
                    ? default
                    : new EntityId(riskConnectionId);
                var riskPortId = detectRisk
                    && mutation.Parameters.ContainsKey(ChemistryConfigurationKeys.Heating.RiskConnectionPortId)
                        ? CombustionOperations.Text(
                            mutation,
                            ChemistryConfigurationKeys.Heating.RiskConnectionPortId)
                        : null;
                var detectAnyRiskEntityConnection =
                    mutation.Parameters.TryGetValue(
                        ChemistryConfigurationKeys.Heating.DetectAnyRiskEntityConnection,
                        out var anyConnectionValue)
                    && anyConnectionValue.Kind
                        == StructuredValueKind.Boolean
                    && anyConnectionValue.Boolean;
                var hasRiskConnection = detectRisk
                    && world.Relations.Any(value =>
                    {
                        if (value.TypeId != InteractionRelationTypeIds.Connection)
                        {
                            return false;
                        }

                        if (detectAnyRiskEntityConnection)
                        {
                            var sourceMatches = value.Source == riskConnection
                                && (riskPortId == null
                                    || value.HasPortEndpoints
                                    && string.Equals(
                                        value.SourcePortId,
                                        riskPortId,
                                        StringComparison.Ordinal));
                            var targetMatches = value.Target == riskConnection
                                && (riskPortId == null
                                    || value.HasPortEndpoints
                                    && string.Equals(
                                        value.TargetPortId,
                                        riskPortId,
                                        StringComparison.Ordinal));
                            return sourceMatches || targetMatches;
                        }

                        return (value.Source == source
                                && value.Target == riskConnection)
                            || (value.Target == source
                                && value.Source == riskConnection);
                    });
                var riskStateKey = detectRisk
                    && mutation.Parameters.ContainsKey(ChemistryConfigurationKeys.Heating.RiskStateKeySuffix)
                        ? source.Value + CombustionOperations.Text(
                            mutation,
                            ChemistryConfigurationKeys.Heating.RiskStateKeySuffix)
                        : null;
                if (riskStateKey != null)
                {
                    world.SetScalar(
                        riskStateKey,
                        0d,
                        new WorldScalarUnit("布尔标记"),
                        0d,
                        1d);
                }

                if (hasRiskConnection)
                {
                    if (riskStateKey == null
                        && mutation.Parameters.ContainsKey(
                            ChemistryConfigurationKeys.Heating.StopHeatingConnectionRejectionReason))
                    {
                        throw new ConfiguredOperationRejectedException(
                            new[]
                            {
                                CombustionOperations.Text(
                                    mutation,
                                    ChemistryConfigurationKeys.Heating.StopHeatingConnectionRejectionReason)
                            });
                    }

                    world.SetScalar(
                        riskStateKey,
                        1d,
                        new WorldScalarUnit("布尔标记"),
                        0d,
                        1d);
                }

                world.StopProcess(HeatingProcessId, source);
                world.RemoveRelation(
                    new EntityRelation(
                        ChemistryRelationTypeIds.HeatedBy,
                        source,
                        new EntityId(
                            process.TextParameters[ChemistryConfigurationKeys.Heating.HeatSourceEntityId])));
            }
        }

        private static void AddRequiredRelationFailure(
            ExperimentWorld world,
            EntityId heatedEntity,
            ConfiguredMutationDefinition mutation,
            RelationTypeId typeId,
            string requiredEntityKey,
            string rejectionKey,
            string riskStateKeyParameter,
            ICollection<string> failures)
        {
            if (!mutation.Parameters.ContainsKey(requiredEntityKey))
            {
                return;
            }

            var required = new EntityId(
                CombustionOperations.Text(
                    mutation,
                    requiredEntityKey));
            var exists = world.Relations.Any(value =>
                value.TypeId == typeId
                && (typeId == InteractionRelationTypeIds.ContainedBy
                    ? value.Source == required
                        && value.Target == heatedEntity
                    : (value.Source == required
                            && value.Target == heatedEntity)
                        || (value.Target == required
                            && value.Source == heatedEntity)));
            var riskStateKey = mutation.Parameters.ContainsKey(
                    riskStateKeyParameter)
                ? CombustionOperations.Text(
                    mutation,
                    riskStateKeyParameter)
                : null;
            if (riskStateKey != null)
            {
                world.SetScalar(
                    riskStateKey,
                    exists ? 0d : 1d,
                    new WorldScalarUnit("布尔标记"),
                    0d,
                    1d);
            }

            if (!exists)
            {
                if (riskStateKey == null)
                {
                    failures.Add(
                        CombustionOperations.Text(
                            mutation,
                            rejectionKey));
                }
            }
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

            var key = CombustionOperations.Text(
                mutation,
                ChemistryConfigurationKeys.Heating.HeatSourceStateKey);
            if (!world.TryGetScalar(key, out var state)
                || state.Value <= 0d)
            {
                failures.Add(
                    CombustionOperations.Text(
                        mutation,
                        ChemistryConfigurationKeys.Heating.HeatSourceNotIgnitedRejectionReason));
            }
        }
    }
}
