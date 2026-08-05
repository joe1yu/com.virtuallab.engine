using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;

namespace VirtualLab.Spatial.Courses
{
    /// <summary>
    /// 空间运行时模块的稳定标识。
    /// </summary>
    public static class SpatialModuleIds
    {
        public const string Spatial = "空间";
    }

    /// <summary>
    /// 显式注册空间事实，最小课程内核不解释物理采集参数。
    /// </summary>
    public sealed class SpatialCourseRuntimeModule : ICourseRuntimeModule
    {
        private static readonly CourseModuleManifest ModuleManifest =
            new CourseModuleManifest(
                SpatialModuleIds.Spatial,
                "空间事实",
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
            foreach (var reader in SpatialCourseRegistrations
                .CreateFactReaders())
            {
                context.RegisterFactReader(reader);
            }
        }
    }

    public static class SpatialCourseRegistrations
    {
        public static CourseRuntimeModuleScope CreateModuleScope() =>
            CourseRuntimeModuleScope.Create(
                new CoreCourseRuntimeModule(),
                new SpatialCourseRuntimeModule());

        public static IReadOnlyList<IStructuredFactReader> CreateFactReaders()
        {
            return new IStructuredFactReader[]
            {
                new RequestParameterFactReader(
                    SpatialStructuredFactFields.对象正在接触,
                    SpatialRequestParameterKeys.IsContacting,
                    StructuredValue.FromBoolean(false)),
                new RequestParameterFactReader(
                    SpatialStructuredFactFields.对象间距离,
                    SpatialRequestParameterKeys.DistanceMeters,
                    StructuredValue.FromNumber(double.MaxValue))
            };
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
    }
}
