using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.UnityAdapters.Courses
{
    public sealed class CourseSceneAssembly
    {
        public CourseSceneAssembly(
            GameObject experimentRoot,
            CourseEntityViewRegistry courseViews)
        {
            ExperimentRoot = experimentRoot;
            CourseViews = courseViews ??
                throw new ArgumentNullException(nameof(courseViews));
        }

        public GameObject ExperimentRoot { get; }
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

            if (string.IsNullOrWhiteSpace(
                    course.ExperimentPrefabResourceId))
            {
                throw new InvalidOperationException(
                    $"课程“{course.CourseId}”没有配置实验预制体资源。");
            }

            var experimentPrefab = resources.Require<GameObject>(
                course.ExperimentPrefabResourceId);
            var experimentRoot = UnityEngine.Object.Instantiate(
                experimentPrefab,
                parent);
            experimentRoot.name = course.CourseId;
            var courseViews = new CourseEntityViewRegistry();
            var layouts = course.SceneLayouts.ToDictionary(
                value => value.EntityId,
                StringComparer.Ordinal);
            var expectedEntityIds = new HashSet<string>(
                course.Entities.Select(value => value.EntityId),
                StringComparer.Ordinal);

            try
            {
                foreach (var view in experimentRoot
                             .GetComponentsInChildren<CourseEntityView>(true))
                {
                    var entityId = view.EntityId;
                    if (string.IsNullOrWhiteSpace(entityId))
                    {
                        throw new InvalidOperationException(
                            $"实验预制体“{experimentPrefab.name}”包含未填写实体 ID 的 CourseEntityView。");
                    }

                    if (!expectedEntityIds.Contains(entityId))
                    {
                        throw new InvalidOperationException(
                            $"实验预制体包含课程未声明的实体视图“{entityId}”。");
                    }

                    if (courseViews.TryGet(entityId, out _))
                    {
                        throw new InvalidOperationException(
                            $"实验预制体包含重复实体视图“{entityId}”。");
                    }

                    if (layouts.TryGetValue(entityId, out var layout))
                    {
                        var localPosition = new Vector3(
                            (float)layout.PositionX,
                            (float)layout.PositionY,
                            (float)layout.PositionZ);
                        var localRotation = Quaternion.Euler(
                            (float)layout.RotationX,
                            (float)layout.RotationY,
                            (float)layout.RotationZ);
                        view.transform.SetPositionAndRotation(
                            experimentRoot.transform.TransformPoint(
                                localPosition),
                            experimentRoot.transform.rotation * localRotation);
                    }

                    courseViews.Register(view);
                }

                var missing = expectedEntityIds
                    .Where(value => !courseViews.TryGet(value, out _))
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidOperationException(
                        "实验预制体缺少实体视图：" + string.Join("、", missing));
                }
            }
            catch
            {
                DestroyExperimentRoot(experimentRoot);
                throw;
            }

            return new CourseSceneAssembly(
                experimentRoot,
                courseViews);
        }

        private static void DestroyExperimentRoot(GameObject experimentRoot)
        {
            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(experimentRoot);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(experimentRoot);
            }
        }
    }
}
