using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;

namespace VirtualLab.Teaching.Courses
{
    /// <summary>
    /// 教学运行时模块使用的稳定协议标识。
    /// </summary>
    public static class TeachingProtocolIds
    {
        public const string Module = "教学";
        public const string MilestoneOperationGroup = "教学.课程里程碑操作";
    }

    /// <summary>
    /// 教学配置协议集中定义在此处，课程表不依赖散落的字符串。
    /// </summary>
    public static class TeachingConfigurationKeys
    {
        public const string EntityId = CourseConfigurationKeys.Mutation.EntityId;
        public const string MilestoneId = "课程里程碑";
        public const string ScalarKey = CourseConfigurationKeys.Mutation.StateKey;
        public const string ExpectedValue =
            CourseConfigurationKeys.Mutation.ExpectedValue;
    }

    /// <summary>
    /// 教学配置可调用的课程里程碑操作。
    /// </summary>
    public static class TeachingConfiguredMilestoneOperationIds
    {
        public const string RecordMilestone = "记录课程里程碑";
        public const string RecordMilestoneWhenScalarEquals =
            "条件记录课程里程碑";

        public static IReadOnlyList<string> All { get; } = new[]
        {
            RecordMilestone,
            RecordMilestoneWhenScalarEquals
        };
    }

    /// <summary>
    /// 显式安装课程里程碑；最小课程内核不解释任何教学语义。
    /// </summary>
    public sealed class TeachingCourseRuntimeModule : ICourseRuntimeModule
    {
        private static readonly CourseModuleManifest ModuleManifest =
            new CourseModuleManifest(
                TeachingProtocolIds.Module,
                "实验教学",
                new Version(1, 0, 0),
                new[]
                {
                    new CourseModuleDependency(
                        CourseModuleIds.Core,
                        new Version(1, 0, 0))
                });

        public CourseModuleManifest Manifest => ModuleManifest;

        public void Register(CourseModuleRegistrationContext context)
        {
            context.RegisterWorldState(
                TeachingWorldStateTypeIds.CourseMilestones,
                world => new TeachingMilestoneCollection(world.ContainsEntity));
            context.RegisterWorldStateCodec(new TeachingMilestoneCourseCodec());
            context.RegisterStateOperations(
                TeachingProtocolIds.MilestoneOperationGroup,
                RegisterStateOperations);

            foreach (var reader in TeachingCourseRegistrations.CreateFactReaders())
            {
                context.RegisterFactReader(reader);
            }
        }

        private static void RegisterStateOperations(
            ConfiguredStateOperationRegistry registry)
        {
            registry.Register(new RecordCourseMilestoneOperation());
            registry.Register(new ConditionalRecordCourseMilestoneOperation());
        }
    }

    public static class TeachingCourseRegistrations
    {
        public static CourseRuntimeModuleScope CreateModuleScope() =>
            CourseRuntimeModuleScope.Create(
                new CoreCourseRuntimeModule(),
                new TeachingCourseRuntimeModule());

        public static IReadOnlyList<IStructuredFactReader> CreateFactReaders()
        {
            return new IStructuredFactReader[]
            {
                new TeachingMilestoneFactReader(
                    TeachingStructuredFactFields.来源对象课程里程碑,
                    context => context.Request.SourceEntityId),
                new TeachingMilestoneFactReader(
                    TeachingStructuredFactFields.目标对象课程里程碑,
                    context => context.Request.TargetEntityId)
            };
        }

        private sealed class TeachingMilestoneFactReader : IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public TeachingMilestoneFactReader(
                StructuredFactField field,
                Func<StructuredRuleContext, string> idSelector)
            {
                Field = field;
                _idSelector = idSelector;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                var entityId = _idSelector(context);
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    return StructuredValue.FromTextList(Array.Empty<string>());
                }

                return StructuredValue.FromTextList(
                    context.World
                        .RequireCourseMilestones()
                        .MilestonesOf(entityId));
            }
        }
    }
}
