using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 将通用课程会话与化学配置组合起来，并统一推进化学持续过程。
    /// 课程 ID、实体组合和实验阈值全部来自传入配置。
    /// </summary>
    public sealed class ChemistryCourseRuntime : ICourseProcessAdvancer
    {
        private readonly ExperimentWorld _world;
        private readonly MatterTransferOperations _matterTransfer;
        private readonly HeatingProcessOperations _heating;
        private readonly CombustionOperations _combustion;
        private readonly ShakingOperations _shaking;

        private ChemistryCourseRuntime(
            ExperimentWorld world,
            ConfigDrivenCourseSession session,
            MatterTransferOperations matterTransfer,
            HeatingProcessOperations heating,
            CombustionOperations combustion,
            ShakingOperations shaking)
        {
            _world = world;
            Session = session;
            _matterTransfer = matterTransfer;
            _heating = heating;
            _combustion = combustion;
            _shaking = shaking;
        }

        public ConfigDrivenCourseSession Session { get; }

        public CourseRuntimeFacade Facade { get; private set; }

        public static ChemistryCourseRuntime Create(
            ExperimentWorld world,
            CompiledCourseDefinition course,
            ChemistryRuntimeConfiguration configuration)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var issues = ChemistryConfigurationValidator.Validate(
                configuration,
                course.Entities.Select(value => value.EntityId));
            if (issues.Count > 0)
            {
                throw new ArgumentException(
                    issues[0].Code + ": " + issues[0].FieldPath,
                    nameof(configuration));
            }

            HydrateCourseCapabilities(world);
            ApplyConfiguration(world, configuration);
            var reactions = CreateReactions(configuration);
            var matterTransfer = new MatterTransferOperations();
            var heating = new HeatingProcessOperations(reactions);
            var combustion = new CombustionOperations(reactions);
            var shaking = new ShakingOperations(configuration.Reactions);
            var runtimeDefinition = ChemistryCourseRegistrations
                .CreateRuntimeDefinition(
                    course,
                    matterTransfer,
                    combustion,
                    heating,
                    shaking);
            var session = runtimeDefinition.CreateSession(world);
            var runtime = new ChemistryCourseRuntime(
                world,
                session,
                matterTransfer,
                heating,
                combustion,
                shaking);
            runtime.Facade = new CourseRuntimeFacade(
                world,
                session,
                runtimeDefinition.FactReaders,
                course.GoalRules,
                runtime);
            return runtime;
        }

        public void Advance(
            double elapsedSeconds,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            AdvanceProcesses(elapsedSeconds, tick, events);
        }

        public void AdvanceProcesses(
            double elapsedSeconds,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            _matterTransfer.Advance(_world, elapsedSeconds, tick, events);
            _heating.Advance(_world, elapsedSeconds, tick, events);
            _combustion.Advance(_world, elapsedSeconds, tick, events);
            _shaking.Advance(_world, tick, events);
        }

        private static void ApplyConfiguration(
            ExperimentWorld world,
            ChemistryRuntimeConfiguration configuration)
        {
            foreach (var binding in configuration.EntityCapabilities)
            {
                if (!world.TryGetEntity(
                        new EntityId(binding.EntityId),
                        out var entity))
                {
                    throw new ArgumentException(
                        $"化学能力绑定引用了未知实体“{binding.EntityId}”。",
                        nameof(configuration));
                }

                if (string.Equals(
                        binding.CapabilityId,
                        ChemistryCapabilityIds.Combustible,
                        StringComparison.Ordinal))
                {
                    if (!entity.HasCapability<CombustibleCapability>())
                    {
                        entity.AddCapability(new CombustibleCapability());
                    }

                    continue;
                }

                throw new ArgumentException(
                    $"未注册化学能力“{binding.CapabilityId}”。",
                    nameof(configuration));
            }

            foreach (var initial in configuration.InitialSubstances)
            {
                world.Matter.Add(
                    new EntityId(initial.EntityId),
                    new SubstanceBatch(
                        initial.SubstanceId,
                        new Quantity(
                            initial.QuantityValue,
                            initial.QuantityUnit),
                        initial.Phase,
                        new Temperature(initial.TemperatureValue)));
            }
        }

        private static void HydrateCourseCapabilities(ExperimentWorld world)
        {
            foreach (var entity in world.Entities)
            {
                var declarations = entity.Capabilities
                    .OfType<ConfiguredCapability>()
                    .ToArray();
                foreach (var declaration in declarations)
                {
                    IConfiguredCapability capability =
                        declaration.CapabilityId switch
                        {
                            ChemistryCapabilityIds.HeatSource =>
                                new Heat来源对象能力(
                                    declaration.NumberValue),
                            ChemistryCapabilityIds.Heatable =>
                                new HeatableCapability(),
                            ChemistryCapabilityIds.Pourable =>
                                new PourableCapability(),
                            ChemistryCapabilityIds.Ignitable =>
                                new IgnitableCapability(),
                            ChemistryCapabilityIds.Combustible =>
                                new CombustibleCapability(),
                            ChemistryCapabilityIds.Shakeable =>
                                new ShakeableCapability(),
                            _ => null
                        };
                    if (capability != null)
                    {
                        entity.PromoteCapability(capability);
                    }
                }
            }
        }

        private static IReadOnlyList<ChemicalReactionDefinition>
            CreateReactions(ChemistryRuntimeConfiguration configuration)
        {
            return configuration.Reactions
                .Select(value => new ChemicalReactionDefinition(
                    value.Id,
                    Terms(value.Reactants),
                    Terms(value.Products)))
                .ToArray();
        }

        private static IReadOnlyList<ReactionTerm> Terms(
            IEnumerable<ChemistryReactionTerm> values)
        {
            return values.Select(value => new ReactionTerm(
                    value.SubstanceId,
                    new Quantity(
                        value.QuantityValue,
                        value.QuantityUnit),
                    value.Phase,
                    value.GramsPerDeclaredUnit))
                .ToArray();
        }
    }
}
