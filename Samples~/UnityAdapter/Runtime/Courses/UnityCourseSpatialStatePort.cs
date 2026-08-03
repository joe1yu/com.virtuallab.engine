using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;

namespace VirtualLab.UnityAdapters.Courses
{
    /// <summary>
    /// Unity 场景姿态与设备无关课程快照之间的适配器。
    /// </summary>
    public sealed class UnityCourseSpatialStatePort : ICourseSpatialStatePort
    {
        private readonly CourseEntityViewRegistry _views;

        public UnityCourseSpatialStatePort(CourseEntityViewRegistry views)
        {
            _views = views ?? throw new ArgumentNullException(nameof(views));
        }

        public IReadOnlyList<CourseSpatialPoseState> Capture()
        {
            return _views.Views.Values
                .Where(value => value != null)
                .OrderBy(value => value.EntityId, StringComparer.Ordinal)
                .Select(value =>
                {
                    var position = value.transform.localPosition;
                    var rotation = value.transform.localEulerAngles;
                    return new CourseSpatialPoseState(
                        value.EntityId,
                        position.x,
                        position.y,
                        position.z,
                        rotation.x,
                        rotation.y,
                        rotation.z);
                })
                .ToArray();
        }

        public void Restore(IEnumerable<CourseSpatialPoseState> poses)
        {
            foreach (var pose in poses
                         ?? throw new ArgumentNullException(nameof(poses)))
            {
                if (pose == null
                    || !_views.TryGet(pose.EntityId, out var view))
                {
                    throw new InvalidOperationException(
                        $"空间快照引用了场景中不存在的实体“{pose?.EntityId}”。");
                }

                view.transform.localPosition = new Vector3(
                    (float)pose.PositionX,
                    (float)pose.PositionY,
                    (float)pose.PositionZ);
                view.transform.localEulerAngles = new Vector3(
                    (float)pose.RotationX,
                    (float)pose.RotationY,
                    (float)pose.RotationZ);
                var body = view.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = view.transform.position;
                    body.rotation = view.transform.rotation;
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            UnityEngine.Physics.SyncTransforms();
        }
    }
}
