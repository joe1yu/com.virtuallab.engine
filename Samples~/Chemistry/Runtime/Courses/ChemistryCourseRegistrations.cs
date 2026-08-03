using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Matter;
using VirtualLab.Kernel;

namespace VirtualLab.Chemistry.Courses
{
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

            return CreateRuntimeDefinition(
                course.ConfiguredActions,
                course.ActionAssessments,
                course.Assessments.Sum(value => value.MaximumScore),
                matterTransferOperations,
                combustionOperations,
                heatingOperations,
                shakingOperations);
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
            var readers = CoreCourseRegistrations
                .CreateFactReaders()
                .Concat(CreateFactReaders())
                .ToArray();
            return new CourseRuntimeDefinition(
                readers,
                actions,
                actionAssessments,
                maximumScore,
                () =>
                {
                    var registry = new ConfiguredStateOperationRegistry();
                    matterTransferOperations?.RegisterWith(registry);
                    combustionOperations?.RegisterWith(registry);
                    heatingOperations?.RegisterWith(registry);
                    shakingOperations?.RegisterWith(registry);
                    return registry;
                },
                new ICourseEventProjector[]
                {
                    new ChemistryCourseEventProjector()
                });
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
                    "倾角度数",
                    StructuredValue.FromNumber(0d)),
                new RequestParameterFactReader(
                    ChemistryStructuredFactFields.倾倒口已对准,
                    "出口是否对准目标入口",
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
                var total = context.World.Matter.Entries
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
                var total = context.World.Matter.Entries
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
                var batch = context.World.Matter.Entries.FirstOrDefault(
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
