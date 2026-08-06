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
                    context => context.Request.TargetEntityId)
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
    }
}
