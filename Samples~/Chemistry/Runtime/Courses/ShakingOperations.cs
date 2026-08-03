using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Processes;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 将振荡语义转换为混合程度，不保存表现振幅或动画曲线。
    /// </summary>
    public sealed class ShakingOperations
    {
        public const string OperationId = "记录振荡";
        public const string MixingProcessId = "混合过程";

        private readonly IReadOnlyDictionary<
            string,
            ChemistryReactionDefinition> _mixingReactions;

        public ShakingOperations()
            : this(Array.Empty<ChemistryReactionDefinition>())
        {
        }

        public ShakingOperations(
            IEnumerable<ChemistryReactionDefinition> reactions)
        {
            _mixingReactions = reactions
                .Where(value =>
                    value.ProcessKind ==
                    ChemistryReactionProcessKind.Mixing)
                .ToDictionary(value => value.Id, StringComparer.Ordinal);
        }

        public void RegisterWith(ConfiguredStateOperationRegistry registry)
        {
            registry.Register(new ApplyOperation(_mixingReactions));
        }

        public void Advance(
            ExperimentWorld world,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            var executor = new StoichiometricMixingReactionExecutor();
            foreach (var process in world.ActiveProcesses
                .Where(value => value.ProcessId == MixingProcessId)
                .OrderBy(value => value.EntityId.Value, StringComparer.Ordinal)
                .ToArray())
            {
                executor.Execute(
                    world.Matter,
                    process.EntityId,
                    _mixingReactions[
                        process.TextParameters[ChemistryConfigurationKeys.Common.ReactionId]],
                    (decimal)process.NumberParameters[ChemistryConfigurationKeys.Shaking.MaximumReactionUnits],
                    new Temperature(20m),
                    process.TextParameters[ChemistryConfigurationKeys.Common.CommandId],
                    tick,
                    events);
                world.StopProcess(MixingProcessId, process.EntityId);
            }
        }

        private sealed class ApplyOperation : IConfiguredStateOperation
        {
            private readonly IReadOnlyDictionary<
                string,
                ChemistryReactionDefinition> _mixingReactions;

            public ApplyOperation(
                IReadOnlyDictionary<
                    string,
                    ChemistryReactionDefinition> mixingReactions)
            {
                _mixingReactions = mixingReactions;
            }

            public string OperationId => ShakingOperations.OperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var source = new EntityId(request.SourceEntityId);
                var intensity = CombustionOperations.RequestNumber(
                    request,
                    ChemistryConfigurationKeys.Shaking.Intensity);
                var duration = CombustionOperations.RequestNumber(
                    request,
                    ChemistryConfigurationKeys.Shaking.DurationSeconds);
                var minimumIntensity =
                    CombustionOperations.Number(mutation, ChemistryConfigurationKeys.Shaking.MinimumIntensity);
                var maximumIntensity =
                    CombustionOperations.Number(mutation, ChemistryConfigurationKeys.Shaking.MaximumIntensity);
                var minimumDuration =
                    CombustionOperations.Number(mutation, ChemistryConfigurationKeys.Shaking.MinimumDurationSeconds);
                var maximumDuration =
                    CombustionOperations.Number(mutation, ChemistryConfigurationKeys.Shaking.MaximumDurationSeconds);
                var failures = new List<string>();

                if (!world.TryGetEntity(source, out var vessel)
                    || !vessel.HasCapability<ShakeableCapability>())
                {
                    failures.Add(
                        CombustionOperations.Text(
                            mutation,
                            ChemistryConfigurationKeys.Shaking.NotShakeableRejectionReason));
                }

                if (!world.Relations.Any(
                    value => value.Kind == RelationKind.由对象持有
                        && value.Source == source
                        && value.Target
                            == new EntityId(request.ActorEntityId)))
                {
                    failures.Add(
                        CombustionOperations.Text(
                            mutation,
                            ChemistryConfigurationKeys.Shaking.NotHeldRejectionReason));
                }

                if (intensity < minimumIntensity
                    || intensity > maximumIntensity)
                {
                    failures.Add(
                        CombustionOperations.Text(
                            mutation,
                            ChemistryConfigurationKeys.Shaking.IntensityOutOfRangeRejectionReason));
                }

                if (duration < minimumDuration
                    || duration > maximumDuration)
                {
                    failures.Add(
                        CombustionOperations.Text(
                            mutation,
                            ChemistryConfigurationKeys.Shaking.DurationOutOfRangeRejectionReason));
                }

                if (failures.Count > 0)
                {
                    throw new ConfiguredOperationRejectedException(failures);
                }

                var suffix = CombustionOperations.Text(
                    mutation,
                    ChemistryConfigurationKeys.Shaking.MixingStateKeySuffix);
                var coefficient = CombustionOperations.Number(
                    mutation,
                    ChemistryConfigurationKeys.Shaking.MixingIncrementFactor);
                world.AddScalar(
                    source.Value + suffix,
                    intensity * duration * coefficient,
                    new WorldScalarUnit("混合度"),
                    0d,
                    null);

                if (!mutation.Parameters.ContainsKey(ChemistryConfigurationKeys.Common.ReactionId))
                {
                    return;
                }

                var reactionId = CombustionOperations.Text(
                    mutation,
                    ChemistryConfigurationKeys.Common.ReactionId);
                if (!_mixingReactions.ContainsKey(reactionId))
                {
                    throw new ArgumentException(
                        $"振荡混合反应“{reactionId}”未注册。");
                }

                var maximumUnits = CombustionOperations.Number(
                    mutation,
                    ChemistryConfigurationKeys.Shaking.MaximumReactionUnits);
                if (!CombustionOperations.IsPositiveFinite(maximumUnits))
                {
                    throw new ArgumentOutOfRangeException(
                        ChemistryConfigurationKeys.Shaking.MaximumReactionUnits);
                }

                world.StartProcess(
                    MixingProcessId,
                    source,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [ChemistryConfigurationKeys.Common.ReactionId] = reactionId,
                        [ChemistryConfigurationKeys.Common.CommandId] = request.CommandId
                    },
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        [ChemistryConfigurationKeys.Shaking.MaximumReactionUnits] = maximumUnits
                    });
            }
        }
    }
}
