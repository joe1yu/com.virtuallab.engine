using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 通用课程运行能力的显式模块入口，只注册跨学科机制。
    /// </summary>
    public sealed class CoreCourseRuntimeModule : ICourseRuntimeModule
    {
        private static readonly CourseModuleManifest ModuleManifest =
            new CourseModuleManifest(
                CourseModuleIds.Core,
                "最小课程内核",
                new Version(1, 0, 0));

        public CourseModuleManifest Manifest => ModuleManifest;

        public void Register(CourseModuleRegistrationContext context)
        {
            foreach (var reader in CoreCourseRegistrations.CreateFactReaders())
            {
                context.RegisterFactReader(reader);
            }

            context.RegisterStateOperations(
                "内核.状态操作.通用",
                registry => registry.RegisterBuiltInOperations());
        }
    }

    /// <summary>
    /// 注册跨学科事实和通用状态操作，不隐式安装交互或学科协议。
    /// </summary>
    public static class CoreCourseRegistrations
    {
        private const string 对象正在接触Parameter = "空间接触";
        private const string 对象间距离Parameter = "空间距离米";

        public static ConfigDrivenCourseSession CreateSession(
            ExperimentWorld world,
            IEnumerable<ConfiguredActionDefinition> actions)
        {
            return new CourseRuntimeDefinition(
                    CreateModuleScope(),
                    actions,
                    Array.Empty<CourseActionAssessmentDefinition>(),
                    0)
                .CreateSession(world);
        }

        public static ConfigDrivenCourseSession CreateSession(
            ExperimentWorld world,
            CompiledCourseDefinition course)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var modules = CreateModuleScope();
            modules.ValidateRequiredModules(course.RequiredModuleIds);
            return new CourseRuntimeDefinition(
                    modules,
                    course.ConfiguredActions,
                    course.ActionAssessments,
                    course.Assessments.Sum(value => value.MaximumScore))
                .CreateSession(world);
        }

        public static CourseRuntimeFacade CreateRuntimeFacade(
            ExperimentWorld world,
            CompiledCourseDefinition course)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var modules = CreateModuleScope();
            modules.ValidateRequiredModules(course.RequiredModuleIds);
            var session = new CourseRuntimeDefinition(
                    modules,
                    course.ConfiguredActions,
                    course.ActionAssessments,
                    course.Assessments.Sum(value => value.MaximumScore))
                .CreateSession(world);
            return new CourseRuntimeFacade(
                world,
                session,
                modules.FactReaders,
                course.GoalRules);
        }

        public static CourseRuntimeModuleScope CreateModuleScope()
        {
            return CourseRuntimeModuleScope.Create(new CoreCourseRuntimeModule());
        }

        public static IReadOnlyList<IStructuredFactReader> CreateFactReaders()
        {
            return new IStructuredFactReader[]
            {
                new EntityExistsFactReader(
                    CoreStructuredFactFields.操作者存在,
                    context => context.Request.ActorEntityId),
                new EntityExistsFactReader(
                    CoreStructuredFactFields.来源对象存在,
                    context => context.Request.SourceEntityId),
                new EntityExistsFactReader(
                    CoreStructuredFactFields.目标对象存在,
                    context => context.Request.TargetEntityId),
                new CapabilityFactReader(
                    CoreStructuredFactFields.操作者能力,
                    context => context.Request.ActorEntityId),
                new CapabilityFactReader(
                    CoreStructuredFactFields.来源对象能力,
                    context => context.Request.SourceEntityId),
                new CapabilityFactReader(
                    CoreStructuredFactFields.目标对象能力,
                    context => context.Request.TargetEntityId),
                new ProgressFactReader(
                    TeachingStructuredFactFields.来源对象进度,
                    context => context.Request.SourceEntityId),
                new ProgressFactReader(
                    TeachingStructuredFactFields.目标对象进度,
                    context => context.Request.TargetEntityId),
                new RequestParameterFactReader(
                    SpatialStructuredFactFields.对象正在接触,
                    对象正在接触Parameter,
                    StructuredValue.FromBoolean(false)),
                new RequestParameterFactReader(
                    SpatialStructuredFactFields.对象间距离,
                    对象间距离Parameter,
                    StructuredValue.FromNumber(double.MaxValue))
            };
        }

        private static StructuredValue CapabilityIds(ExperimentEntity entity)
        {
            if (entity == null)
            {
                return StructuredValue.FromTextList(Array.Empty<string>());
            }

            var ids = entity.Capabilities
                .Select(value => value.CapabilityId)
                .OrderBy(value => value, StringComparer.Ordinal);
            return StructuredValue.FromTextList(ids);
        }

        private static ExperimentEntity FindEntity(
            StructuredRuleContext context,
            string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return null;
            }

            return context.World.TryGetEntity(
                new EntityId(entityId),
                out var entity)
                ? entity
                : null;
        }

        private sealed class EntityExistsFactReader : IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public EntityExistsFactReader(
                StructuredFactField field,
                Func<StructuredRuleContext, string> idSelector)
            {
                Field = field;
                _idSelector = idSelector;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return StructuredValue.FromBoolean(
                    FindEntity(context, _idSelector(context)) != null);
            }
        }

        private sealed class CapabilityFactReader : IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public CapabilityFactReader(
                StructuredFactField field,
                Func<StructuredRuleContext, string> idSelector)
            {
                Field = field;
                _idSelector = idSelector;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return CapabilityIds(
                    FindEntity(context, _idSelector(context)));
            }
        }

        private sealed class ProgressFactReader : IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public ProgressFactReader(
                StructuredFactField field,
                Func<StructuredRuleContext, string> idSelector)
            {
                Field = field;
                _idSelector = idSelector;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                var selectedId = _idSelector(context);
                if (string.IsNullOrWhiteSpace(selectedId))
                {
                    return StructuredValue.FromNumber(0d);
                }

                var key = selectedId + ".课程进度";
                return context.World.TryGetScalar(key, out var progress)
                    ? StructuredValue.FromNumber(progress.Value)
                    : StructuredValue.FromNumber(0d);
            }
        }

        private sealed class RequestParameterFactReader : IStructuredFactReader
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
    }
}
