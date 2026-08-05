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
        public const string StateOperationGroup = "教学.命名状态操作";
    }

    /// <summary>
    /// 教学配置协议集中定义在此处，课程表不依赖散落的字符串。
    /// </summary>
    public static class TeachingConfigurationKeys
    {
        public const string EntityId = CourseConfigurationKeys.Mutation.EntityId;
        public const string StateId = "教学状态";
    }

    /// <summary>
    /// 教学配置可调用的状态操作。
    /// </summary>
    public static class TeachingConfiguredStateOperationIds
    {
        public const string AddState = "添加教学状态";
        public const string RemoveState = "移除教学状态";
    }

    /// <summary>
    /// 显式安装命名教学状态；最小课程内核不解释任何教学语义。
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
                TeachingWorldStateTypeIds.NamedStates,
                world => new TeachingStateCollection(world.ContainsEntity));
            context.RegisterWorldStateCodec(new TeachingStateCourseCodec());
            context.RegisterStateOperations(
                TeachingProtocolIds.StateOperationGroup,
                RegisterStateOperations);

            foreach (var reader in TeachingCourseRegistrations.CreateFactReaders())
            {
                context.RegisterFactReader(reader);
            }
        }

        private static void RegisterStateOperations(
            ConfiguredStateOperationRegistry registry)
        {
            registry.Register(new AddTeachingStateOperation());
            registry.Register(new RemoveTeachingStateOperation());
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
                new TeachingStateFactReader(
                    TeachingStructuredFactFields.来源对象教学状态,
                    context => context.Request.SourceEntityId),
                new TeachingStateFactReader(
                    TeachingStructuredFactFields.目标对象教学状态,
                    context => context.Request.TargetEntityId)
            };
        }

        private sealed class TeachingStateFactReader : IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public TeachingStateFactReader(
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
                    context.World.RequireTeachingStates().StatesOf(entityId));
            }
        }
    }
}
