using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.UnityAdapters.Courses
{
    public sealed class CourseAssetResourceResolver :
        IPresentationResourceResolver
    {
        private readonly IReadOnlyDictionary<string, UnityEngine.Object>
            _resources;

        public CourseAssetResourceResolver(CompiledCourseAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            _resources = asset.ResourceBindings.ToDictionary(
                value => value.Key,
                SelectResource,
                StringComparer.Ordinal);
        }

        public bool TryResolve(
            string resourceId,
            out UnityEngine.Object resource)
        {
            resource = null;
            return !string.IsNullOrWhiteSpace(resourceId) &&
                   _resources.TryGetValue(resourceId.Trim(), out resource) &&
                   resource != null;
        }

        private static UnityEngine.Object SelectResource(
            CourseResourceBinding binding)
        {
            if (binding.Prefab != null)
            {
                return binding.Prefab;
            }

            if (binding.Material != null)
            {
                return binding.Material;
            }

            if (binding.AudioClip != null)
            {
                return binding.AudioClip;
            }

            return binding.PresentationResource;
        }
    }

    public sealed class CourseSceneAssembly
    {
        public CourseSceneAssembly(
            GameObject environment,
            CourseEntityViewRegistry courseViews)
        {
            Environment = environment;
            CourseViews = courseViews ??
                throw new ArgumentNullException(nameof(courseViews));
        }

        public GameObject Environment { get; }
        public CourseEntityViewRegistry CourseViews { get; }
    }

    public sealed class CourseSceneAssembler
    {
        public CourseSceneAssembly Assemble(
            CompiledCourseAsset asset,
            CompiledCourseDefinition course,
            Transform parent = null)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var bindings = asset.ResourceBindings.ToDictionary(
                value => value.Key,
                StringComparer.Ordinal);
            var courseViews = new CourseEntityViewRegistry();
            var environment = asset.EnvironmentPrefab == null
                ? null
                : UnityEngine.Object.Instantiate(
                    asset.EnvironmentPrefab,
                    parent);
            var layouts = course.SceneLayouts.ToDictionary(
                value => value.EntityId,
                StringComparer.Ordinal);

            foreach (var entity in course.Entities)
            {
                if (!bindings.TryGetValue(
                        entity.PrefabReference,
                        out var binding) ||
                    binding.Prefab == null)
                {
                    throw new InvalidOperationException(
                        $"实体“{entity.EntityId}”无法解析 Prefab 资源“" +
                        entity.PrefabReference +
                        "”。");
                }

                var instance = UnityEngine.Object.Instantiate(
                    binding.Prefab,
                    parent);
                instance.name = entity.EntityId;
                var view = instance.GetComponent<CourseEntityView>();
                if (view == null)
                {
                    throw new InvalidOperationException(
                        $"Prefab“{entity.PrefabReference}”缺少 CourseEntityView。");
                }

                view.Configure(entity.EntityId);
                if (layouts.TryGetValue(entity.EntityId, out var layout))
                {
                    instance.transform.localPosition = new Vector3(
                        (float)layout.PositionX,
                        (float)layout.PositionY,
                        (float)layout.PositionZ);
                    instance.transform.localEulerAngles = new Vector3(
                        (float)layout.RotationX,
                        (float)layout.RotationY,
                        (float)layout.RotationZ);
                }

                courseViews.Register(view);
            }

            return new CourseSceneAssembly(
                environment,
                courseViews);
        }
    }
}
