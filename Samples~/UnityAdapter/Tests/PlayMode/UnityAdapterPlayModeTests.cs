using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.Engine.Tests.UnityAdapters
{
    public sealed class UnityAdapterPlayModeTests
    {
        [UnityTest]
        public IEnumerator 课程空间端口可采集并恢复物体姿态和刚体速度()
        {
            var entityObject = new GameObject("课程实体");
            try
            {
                var view = entityObject.AddComponent<CourseEntityView>();
                view.Configure("器材.试管");
                var body = entityObject.AddComponent<Rigidbody>();
                body.useGravity = false;
                entityObject.transform.localPosition = new Vector3(1, 2, 3);
                entityObject.transform.localEulerAngles =
                    new Vector3(10, 20, 30);
                var registry = new CourseEntityViewRegistry();
                registry.Register(view);
                var port = new UnityCourseSpatialStatePort(registry);
                var snapshot = port.Capture();

                entityObject.transform.localPosition =
                    new Vector3(8, 9, 10);
                entityObject.transform.rotation = Quaternion.identity;
                body.velocity = Vector3.one;
                body.angularVelocity = Vector3.one;
                port.Restore(snapshot);
                yield return null;

                Assert.That(
                    entityObject.transform.localPosition.x,
                    Is.EqualTo(1).Within(0.001));
                Assert.That(
                    entityObject.transform.localPosition.y,
                    Is.EqualTo(2).Within(0.001));
                Assert.That(
                    entityObject.transform.localPosition.z,
                    Is.EqualTo(3).Within(0.001));
                Assert.That(
                    Quaternion.Angle(
                        entityObject.transform.localRotation,
                        Quaternion.Euler(10, 20, 30)),
                    Is.LessThan(0.01));
                Assert.That(body.velocity, Is.EqualTo(Vector3.zero));
                Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                Object.Destroy(entityObject);
            }
        }
    }
}
