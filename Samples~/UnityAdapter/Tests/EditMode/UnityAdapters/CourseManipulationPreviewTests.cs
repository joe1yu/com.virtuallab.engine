using NUnit.Framework;
using UnityEngine;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Engine.Tests.UnityAdapters
{
    public sealed class CourseManipulationPreviewTests
    {
        private GameObject _entityObject;

        [TearDown]
        public void 清理对象()
        {
            if (_entityObject != null)
            {
                Object.DestroyImmediate(_entityObject);
            }
        }

        [Test]
        public void 取消预览会恢复抓取前姿态()
        {
            var view = CreateView();
            var originalPosition = new Vector3(1f, 2f, 3f);
            var originalRotation = Quaternion.Euler(10f, 20f, 30f);
            view.transform.SetPositionAndRotation(
                originalPosition,
                originalRotation);
            var preview = new CourseManipulationPreview();

            preview.Begin(view);
            preview.MoveTo(new Vector3(8f, 9f, 10f));
            preview.RotateAround(Vector3.up, 45f, Vector3.right, 15f);
            preview.Cancel();

            Assert.That(
                Vector3.Distance(view.transform.position, originalPosition),
                Is.LessThan(0.0001f));
            Assert.That(
                Quaternion.Angle(view.transform.rotation, originalRotation),
                Is.LessThan(0.001f));
            Assert.That(preview.IsActive, Is.False);
        }

        [Test]
        public void 提交预览会保留获准后的姿态()
        {
            var view = CreateView();
            var committedPosition = new Vector3(4f, 5f, 6f);
            var preview = new CourseManipulationPreview();

            preview.Begin(view);
            preview.MoveTo(committedPosition);
            preview.Commit();

            Assert.That(
                Vector3.Distance(view.transform.position, committedPosition),
                Is.LessThan(0.0001f));
            Assert.That(preview.IsActive, Is.False);
        }

        private CourseEntityView CreateView()
        {
            _entityObject = new GameObject("预览测试对象");
            var view = _entityObject.AddComponent<CourseEntityView>();
            view.Configure("预览测试对象");
            return view;
        }
    }
}
