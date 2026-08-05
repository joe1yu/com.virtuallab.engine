using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;

namespace VirtualLab.Teaching.Courses
{
    /// <summary>
    /// 教学运行时模块的稳定标识。
    /// </summary>
    public static class TeachingModuleIds
    {
        public const string Teaching = "教学";
    }

    /// <summary>
    /// 教学模块当前使用的状态键。键的解释和读取都留在教学模块内。
    /// </summary>
    public static class TeachingStateKeys
    {
        private const string EntityProgressSuffix = ".课程进度";

        public static string EntityProgress(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                throw new ArgumentException(
                    "教学进度实体 ID 不能为空。",
                    nameof(entityId));
            }

            return entityId.Trim() + EntityProgressSuffix;
        }
    }

    /// <summary>
    /// 显式注册教学事实，最小课程内核不再默认安装课程进度语义。
    /// </summary>
    public sealed class TeachingCourseRuntimeModule : ICourseRuntimeModule
    {
        private static readonly CourseModuleManifest ModuleManifest =
            new CourseModuleManifest(
                TeachingModuleIds.Teaching,
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
            foreach (var reader in TeachingCourseRegistrations
                .CreateFactReaders())
            {
                context.RegisterFactReader(reader);
            }
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
                new ProgressFactReader(
                    TeachingStructuredFactFields.来源对象进度,
                    context => context.Request.SourceEntityId),
                new ProgressFactReader(
                    TeachingStructuredFactFields.目标对象进度,
                    context => context.Request.TargetEntityId)
            };
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

                return context.World.TryGetScalar(
                        TeachingStateKeys.EntityProgress(selectedId),
                        out var progress)
                    ? StructuredValue.FromNumber(progress.Value)
                    : StructuredValue.FromNumber(0d);
            }
        }
    }
}
