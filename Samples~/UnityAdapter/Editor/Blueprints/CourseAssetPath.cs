using System;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.Unity.Authoring.Blueprints
{
    /// <summary>
    /// 解析课程配置中的可迁移资产路径。课程目录引用不会绑定到固定的 Assets 路径，
    /// 因而课程放入 Package Sample 或移动目录后仍可重新编译。
    /// </summary>
    public static class CourseAssetPath
    {
        public const string CourseDirectory = "课程目录";

        private const string CourseDirectoryPrefix = CourseDirectory + "/";

        public static string Resolve(string reference, string courseRoot)
        {
            var value = (reference ?? string.Empty).Trim().Replace('\\', '/');
            var runtimeResourcePrefix =
                CourseRuntimeResourcePaths.ConfiguredCourseResources + "/";
            if (value.StartsWith(
                    runtimeResourcePrefix,
                    StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(courseRoot))
                {
                    throw new ArgumentException(
                        "解析课程资源路径时必须提供课程根目录。",
                        nameof(courseRoot));
                }

                var runtimeRelative = value.Substring(
                    runtimeResourcePrefix.Length);
                ValidateRelative(reference, runtimeRelative);
                return courseRoot.Trim().TrimEnd('/', '\\').Replace('\\', '/')
                       + "/Resources/"
                       + CourseRuntimeResourcePaths.CourseResources
                       + "/"
                       + runtimeRelative;
            }

            if (!value.StartsWith(
                    CourseDirectoryPrefix,
                    StringComparison.Ordinal))
            {
                return value;
            }

            if (string.IsNullOrWhiteSpace(courseRoot))
            {
                throw new ArgumentException(
                    "解析课程目录资产路径时必须提供课程根目录。",
                    nameof(courseRoot));
            }

            var relative = value.Substring(CourseDirectoryPrefix.Length);
            ValidateRelative(reference, relative);

            return courseRoot.Trim().TrimEnd('/', '\\').Replace('\\', '/')
                   + "/"
                   + relative;
        }

        private static void ValidateRelative(
            string reference,
            string relative)
        {
            if (relative.Length == 0
                || relative.StartsWith("/", StringComparison.Ordinal)
                || relative == ".."
                || relative.StartsWith("../", StringComparison.Ordinal)
                || relative.Contains("/../"))
            {
                throw new ArgumentException(
                    $"课程资产路径“{reference}”不能离开课程目录。",
                    nameof(reference));
            }
        }
    }
}
