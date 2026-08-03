using System;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.UnityAdapters.Courses
{
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
            CompiledCourseDefinition course,
            CourseRuntimeResourceResolver resources,
            Transform parent = null)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            if (resources == null)
            {
                throw new ArgumentNullException(nameof(resources));
            }

            var courseViews = new CourseEntityViewRegistry();
            var environmentPrefab = string.IsNullOrWhiteSpace(
                course.EnvironmentResourceId)
                ? null
                : resources.Require<GameObject>(course.EnvironmentResourceId);
            var environment = environmentPrefab == null
                ? null
                : UnityEngine.Object.Instantiate(environmentPrefab, parent);
            var layouts = course.SceneLayouts.ToDictionary(
                value => value.EntityId,
                StringComparer.Ordinal);

            foreach (var entity in course.Entities)
            {
                var prefab = resources.Require<GameObject>(
                    entity.PrefabReference);
                var instance = UnityEngine.Object.Instantiate(
                    prefab,
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
