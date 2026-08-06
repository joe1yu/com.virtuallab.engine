using System;
using System.Linq;
using UnityEngine;

namespace VirtualLab.UnityAdapters.Courses
{
    /// <summary>
    /// 集中维护运行时课程资源目录，避免场景和学科代码分别硬编码加载路径。
    /// </summary>
    public static class CourseRuntimeResourcePaths
    {
        public const string CompiledCourses = "VirtualLab/编译课程";
        public const string CourseResources = "VirtualLab/课程资源";
        public const string ConfiguredCourseResources = "课程资源";
    }

    /// <summary>
    /// 根据稳定课程 ID 从运行时资源目录解析编译资产。
    /// 场景只保存课程 ID，不再直接序列化生成资产引用。
    /// </summary>
    public static class CompiledCourseAssetCatalog
    {
        public static CompiledCourseAsset Require(string courseId)
        {
            if (string.IsNullOrWhiteSpace(courseId))
            {
                throw new InvalidOperationException("场景未配置课程 ID。");
            }

            var normalizedId = courseId.Trim();
            var matches = Resources
                .LoadAll<CompiledCourseAsset>(
                    CourseRuntimeResourcePaths.CompiledCourses)
                .Where(value =>
                    value != null
                    && string.Equals(
                        value.CourseId,
                        normalizedId,
                        StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"运行时资源目录中未找到课程“{normalizedId}”，请重新编译课程资产。");
            }

            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"运行时资源目录中存在重复课程“{normalizedId}”。");
            }

            return matches[0];
        }
    }
}
