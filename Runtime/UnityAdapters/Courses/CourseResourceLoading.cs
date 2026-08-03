using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.UnityAdapters.Courses
{
    /// <summary>
    /// 课程资源加载端口。路径由领域资源定义提供，具体加载技术可以替换为
    /// Resources、Addressables 或宿主项目自己的资源系统。
    /// 异步资源系统可在课程初始化前完成预加载，再由此端口返回缓存结果。
    /// </summary>
    public interface ICourseResourceLoader
    {
        bool TryLoad(
            string resourcePath,
            Type expectedType,
            out UnityEngine.Object resource);
    }

    /// <summary>
    /// 将配置中的课程资源路径转换为 Unity Resources 加载路径。
    /// </summary>
    public static class CourseResourcesPath
    {
        private const string ResourcesSegment = "/Resources/";

        public static string ToRuntimePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new ArgumentException(
                    "课程资源路径不能为空。",
                    nameof(configuredPath));
            }

            var value = configuredPath.Trim().Replace('\\', '/');
            var configuredPrefix =
                CourseRuntimeResourcePaths.ConfiguredCourseResources + "/";
            if (value.StartsWith(configuredPrefix, StringComparison.Ordinal))
            {
                value = CourseRuntimeResourcePaths.CourseResources + "/"
                        + value.Substring(configuredPrefix.Length);
            }
            else
            {
                var markerIndex = value.IndexOf(
                    ResourcesSegment,
                    StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    value = value.Substring(
                        markerIndex + ResourcesSegment.Length);
                }
            }

            if (value.StartsWith("/", StringComparison.Ordinal)
                || value == ".."
                || value.StartsWith("../", StringComparison.Ordinal)
                || value.Contains("/../"))
            {
                throw new ArgumentException(
                    $"课程资源路径“{configuredPath}”无效。",
                    nameof(configuredPath));
            }

            var extension = Path.GetExtension(value);
            if (!string.IsNullOrEmpty(extension))
            {
                value = value.Substring(0, value.Length - extension.Length);
            }

            return value;
        }
    }

    /// <summary>
    /// 当前默认的同步路径加载实现。后续接入 Addressables 时只需替换此端口，
    /// 课程资产、场景装配和表现资源解析无需修改。
    /// </summary>
    public sealed class ResourcesCourseResourceLoader : ICourseResourceLoader
    {
        public bool TryLoad(
            string resourcePath,
            Type expectedType,
            out UnityEngine.Object resource)
        {
            if (expectedType == null)
            {
                throw new ArgumentNullException(nameof(expectedType));
            }

            resource = Resources.Load(
                CourseResourcesPath.ToRuntimePath(resourcePath),
                expectedType);
            return resource != null;
        }
    }

    /// <summary>
    /// 按资源 ID 解析课程资源并缓存结果，供场景装配和表现层共同使用。
    /// </summary>
    public sealed class CourseRuntimeResourceResolver :
        IPresentationResourceResolver
    {
        private readonly Dictionary<string, CourseResourceDefinition>
            _definitions;
        private readonly ICourseResourceLoader _loader;
        private readonly Dictionary<string, UnityEngine.Object> _loaded =
            new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

        public CourseRuntimeResourceResolver(
            CompiledCourseDefinition course,
            ICourseResourceLoader loader)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _definitions = new Dictionary<string, CourseResourceDefinition>(
                StringComparer.Ordinal);
            foreach (var definition in course.Resources)
            {
                _definitions.Add(definition.ResourceId, definition);
            }
        }

        public bool TryResolve(
            string resourceId,
            out UnityEngine.Object resource)
        {
            resource = null;
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return false;
            }

            var normalizedId = resourceId.Trim();
            if (_loaded.TryGetValue(normalizedId, out resource))
            {
                return resource != null;
            }

            if (!_definitions.TryGetValue(normalizedId, out var definition))
            {
                return false;
            }

            var expectedType = CourseResourceTypes.For(definition.Kind);
            if (!_loader.TryLoad(
                    definition.AssetPath,
                    expectedType,
                    out resource)
                || resource == null
                || !expectedType.IsInstanceOfType(resource))
            {
                resource = null;
                return false;
            }

            _loaded.Add(normalizedId, resource);
            return true;
        }

        public T Require<T>(string resourceId) where T : UnityEngine.Object
        {
            if (TryResolve(resourceId, out var resource)
                && resource is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException(
                $"课程资源“{resourceId}”无法加载为“{typeof(T).Name}”。");
        }
    }

    internal static class CourseResourceTypes
    {
        public static Type For(CourseResourceKind kind)
        {
            switch (kind)
            {
                case CourseResourceKind.Prefab:
                    return typeof(GameObject);
                case CourseResourceKind.Material:
                    return typeof(Material);
                case CourseResourceKind.Audio:
                    return typeof(AudioClip);
                case CourseResourceKind.Presentation:
                    return typeof(UnityEngine.Object);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "未知课程资源类型。");
            }
        }
    }
}
