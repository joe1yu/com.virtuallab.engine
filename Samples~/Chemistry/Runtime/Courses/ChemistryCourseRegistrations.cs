using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Interaction.Courses;
using VirtualLab.Kernel;
using VirtualLab.Measurement;
using VirtualLab.Spatial.Courses;
using VirtualLab.Teaching.Courses;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 化学模块的稳定标识集中定义，课程配置只引用这些公开协议。
    /// </summary>
    public static class ChemistryModuleIds
    {
        public const string Chemistry = "化学";
    }

    /// <summary>
    /// 化学事实、状态操作和事件投影的显式注册入口。
    /// </summary>
    public sealed class ChemistryCourseRuntimeModule : ICourseRuntimeModule
    {
        private static readonly CourseModuleManifest ModuleManifest =
            new CourseModuleManifest(
                ChemistryModuleIds.Chemistry,
                "化学实验",
                new Version(1, 0, 0),
                new[]
                {
                    new CourseModuleDependency(
                        CourseModuleIds.Core,
                        new Version(1, 0, 0)),
                    new CourseModuleDependency(
                        InteractionModuleIds.Interaction,
                        new Version(1, 0, 0)),
                    new CourseModuleDependency(
                        SpatialModuleIds.Spatial,
                        new Version(1, 0, 0))
                });

        private readonly MatterTransferOperations _matterTransferOperations;
        private readonly CombustionOperations _combustionOperations;
        private readonly HeatingProcessOperations _heatingOperations;
        private readonly ShakingOperations _shakingOperations;

        public ChemistryCourseRuntimeModule(
            MatterTransferOperations matterTransferOperations = null,
            CombustionOperations combustionOperations = null,
            HeatingProcessOperations heatingOperations = null,
            ShakingOperations shakingOperations = null)
        {
            _matterTransferOperations = matterTransferOperations;
            _combustionOperations = combustionOperations;
            _heatingOperations = heatingOperations;
            _shakingOperations = shakingOperations;
        }

        public CourseModuleManifest Manifest => ModuleManifest;

        public void Register(CourseModuleRegistrationContext context)
        {
            context.RegisterWorldState(
                MatterWorldStateTypeIds.Inventory,
                world => new MatterInventory(world.ContainsEntity));
            context.RegisterWorldStateCodec(
                new MatterInventoryCourseStateCodec());

            foreach (var schema in ChemistryRelationSchemas.All)
            {
                context.RegisterRelationSchema(schema);
            }

            foreach (var reader in ChemistryCourseRegistrations.CreateFactReaders())
            {
                context.RegisterFactReader(reader);
            }

            context.RegisterStateOperations(
                "化学.状态操作.课程",
                registry =>
                {
                    _matterTransferOperations?.RegisterWith(registry);
                    _combustionOperations?.RegisterWith(registry);
                    _heatingOperations?.RegisterWith(registry);
                    _shakingOperations?.RegisterWith(registry);
                });
            context.RegisterEventProjector(
                "化学.事件投影.课程",
                new ChemistryCourseEventProjector());
            context.RegisterProcessAdvancer(
                "化学.持续过程.课程",
                world => new ChemistryCourseProcessAdvancer(
                    world,
                    _matterTransferOperations,
                    _heatingOperations,
                    _combustionOperations,
                    _shakingOperations));
        }
    }

    /// <summary>
    /// 在跨学科内核之上注册化学事实与白名单状态操作。
    /// </summary>
    public static class ChemistryCourseRegistrations
    {
        public static ConfigDrivenCourseSession CreateSession(
            ExperimentWorld world,
            IEnumerable<ConfiguredActionDefinition> actions,
            MatterTransferOperations matterTransferOperations = null,
            CombustionOperations combustionOperations = null,
            HeatingProcessOperations heatingOperations = null,
            ShakingOperations shakingOperations = null)
        {
            return CreateRuntimeDefinition(
                    actions,
                    Array.Empty<CourseActionAssessmentDefinition>(),
                    0,
                    matterTransferOperations,
                    combustionOperations,
                    heatingOperations,
                    shakingOperations)
                .CreateSession(world);
        }

        public static CourseRuntimeDefinition CreateRuntimeDefinition(
            CompiledCourseDefinition course,
            MatterTransferOperations matterTransferOperations = null,
            CombustionOperations combustionOperations = null,
            HeatingProcessOperations heatingOperations = null,
            ShakingOperations shakingOperations = null)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var modules = CreateModuleScope(
                matterTransferOperations,
                combustionOperations,
                heatingOperations,
                shakingOperations);
            modules.ValidateRequiredModules(course.RequiredModuleIds);
            return new CourseRuntimeDefinition(
                modules,
                course.ConfiguredActions,
                course.ActionAssessments,
                course.Assessments.Sum(value => value.MaximumScore));
        }

        public static CourseRuntimeDefinition CreateRuntimeDefinition(
            IEnumerable<ConfiguredActionDefinition> actions,
            IEnumerable<CourseActionAssessmentDefinition> actionAssessments,
            int maximumScore,
            MatterTransferOperations matterTransferOperations = null,
            CombustionOperations combustionOperations = null,
            HeatingProcessOperations heatingOperations = null,
            ShakingOperations shakingOperations = null)
        {
            return new CourseRuntimeDefinition(
                CreateModuleScope(
                    matterTransferOperations,
                    combustionOperations,
                    heatingOperations,
                    shakingOperations),
                actions,
                actionAssessments,
                maximumScore);
        }

        public static CourseRuntimeModuleScope CreateModuleScope(
            MatterTransferOperations matterTransferOperations = null,
            CombustionOperations combustionOperations = null,
            HeatingProcessOperations heatingOperations = null,
            ShakingOperations shakingOperations = null)
        {
            return CourseRuntimeModuleScope.Create(
                new CoreCourseRuntimeModule(),
                new InteractionCourseRuntimeModule(),
                new SpatialCourseRuntimeModule(),
                new TeachingCourseRuntimeModule(),
                new ChemistryCourseRuntimeModule(
                    matterTransferOperations,
                    combustionOperations,
                    heatingOperations,
                    shakingOperations));
        }

        public static IReadOnlyList<IStructuredFactReader>
            CreateFactReaders()
        {
            return new IStructuredFactReader[]
            {
                new 来源对象温度FactReader(),
                new 来源内容体积FactReader(),
                new SourcePhaseMassFactReader(
                    ChemistryStructuredFactFields.来源固体质量,
                    MatterPhase.Solid),
                new RequestParameterFactReader(
                    ChemistryStructuredFactFields.倾倒角度,
                    SpatialRequestParameterKeys.TiltAngleDegrees,
                    StructuredValue.FromNumber(0d)),
                new RequestParameterFactReader(
                    ChemistryStructuredFactFields.倾倒口已对准,
                    SpatialRequestParameterKeys.OutletAligned,
                    StructuredValue.FromBoolean(false)),
                new RequestParameterFactReader(
                    ChemistryStructuredFactFields.请求流量,
                    "请求流量毫升每秒",
                    StructuredValue.FromNumber(0d)),
                new 来源内容单位FactReader()
            };
        }

        private sealed class 来源对象温度FactReader :
            IStructuredFactReader
        {
            public StructuredFactField Field =>
                ChemistryStructuredFactFields.来源对象温度;

            public StructuredValue Read(StructuredRuleContext context)
            {
                var key = context.Request.SourceEntityId + ".温度";
                return context.World.TryGetScalar(key, out var temperature)
                    ? StructuredValue.FromNumber(temperature.Value)
                    : StructuredValue.FromNumber(20d);
            }
        }

        private sealed class 来源内容体积FactReader :
            IStructuredFactReader
        {
            public StructuredFactField Field =>
                ChemistryStructuredFactFields.来源内容体积;

            public StructuredValue Read(StructuredRuleContext context)
            {
                var source = new EntityId(context.Request.SourceEntityId);
                var total = context.World.RequireMatterInventory().Entries
                    .Where(value => value.LocationId == source
                        && value.Batch.Phase == MatterPhase.Liquid
                        && value.Batch.Quantity.Unit == Unit.Millilitre)
                    .Sum(value => value.Batch.Quantity.Value);
                return StructuredValue.FromNumber((double)total);
            }
        }

        private sealed class SourcePhaseMassFactReader :
            IStructuredFactReader
        {
            private readonly MatterPhase _phase;

            public SourcePhaseMassFactReader(
                StructuredFactField field,
                MatterPhase phase)
            {
                Field = field;
                _phase = phase;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                var source = new EntityId(context.Request.SourceEntityId);
                var total = context.World.RequireMatterInventory().Entries
                    .Where(value => value.LocationId == source
                        && value.Batch.Phase == _phase
                        && value.Batch.Quantity.Unit == Unit.Gram)
                    .Sum(value => value.Batch.Quantity.Value);
                return StructuredValue.FromNumber((double)total);
            }
        }

        private sealed class RequestParameterFactReader :
            IStructuredFactReader
        {
            private readonly string _parameterName;
            private readonly StructuredValue _defaultValue;

            public RequestParameterFactReader(
                StructuredFactField field,
                string parameterName,
                StructuredValue defaultValue)
            {
                Field = field;
                _parameterName = parameterName;
                _defaultValue = defaultValue;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return context.Request.Parameters.TryGetValue(
                    _parameterName,
                    out var value)
                    ? value
                    : _defaultValue;
            }
        }

        private sealed class 来源内容单位FactReader :
            IStructuredFactReader
        {
            public StructuredFactField Field =>
                ChemistryStructuredFactFields.来源内容单位;

            public StructuredValue Read(StructuredRuleContext context)
            {
                var source = new EntityId(context.Request.SourceEntityId);
                var batch = context.World.RequireMatterInventory().Entries.FirstOrDefault(
                    value => value.LocationId == source
                        && value.Batch.Phase == MatterPhase.Liquid
                        && value.Batch.Quantity.Value > 0m);
                return batch == null
                    ? StructuredValue.Null()
                    : StructuredValue.FromText(
                        batch.Batch.Quantity.Unit.ToString());
            }
        }
    }
}
