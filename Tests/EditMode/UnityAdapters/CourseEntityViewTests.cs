using System;
using NUnit.Framework;
using UnityEngine;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.Engine.Tests.UnityAdapters
{
    public sealed class CourseEntityViewTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        [Test]
        public void 组合预制体只索引当前实体拥有的语义节点()
        {
            _root = new GameObject("父实体");
            var parent = _root.AddComponent<CourseEntityView>();
            AddAnchor(_root.transform, "父实体.观察点");

            var childObject = new GameObject("子实体");
            childObject.transform.SetParent(_root.transform);
            var child = childObject.AddComponent<CourseEntityView>();
            AddAnchor(childObject.transform, "子实体.观察点");

            parent.Configure("父实体");
            child.Configure("子实体");

            Assert.That(parent.Anchors.Count, Is.EqualTo(1));
            Assert.That(
                parent.TryGetAnchor("父实体.观察点", out _),
                Is.True);
            Assert.That(
                parent.TryGetAnchor("子实体.观察点", out _),
                Is.False);
        }

        [Test]
        public void 重复语义ID在建立索引时立即给出中文错误()
        {
            _root = new GameObject("重复锚点实体");
            var view = _root.AddComponent<CourseEntityView>();
            AddAnchor(_root.transform, "观察点");
            AddAnchor(_root.transform, "观察点");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                view.Configure("重复锚点实体"));

            Assert.That(exception.Message, Does.Contain("重复"));
            Assert.That(exception.Message, Does.Contain("观察点"));
        }

        [Test]
        public void 注册表使用单一只读视图并支持注销()
        {
            _root = new GameObject("注册实体");
            var view = _root.AddComponent<CourseEntityView>();
            view.Configure("注册实体");
            var registry = new CourseEntityViewRegistry();

            var first = registry.Views;
            registry.Register(view);
            var second = registry.Views;

            Assert.That(second, Is.SameAs(first));
            Assert.That(registry.TryGet("注册实体", out var found), Is.True);
            Assert.That(found, Is.SameAs(view));
            Assert.That(registry.Unregister(view), Is.True);
            Assert.That(registry.Count, Is.Zero);
        }

        private static void AddAnchor(Transform parent, string id)
        {
            var anchor = new GameObject(id);
            anchor.transform.SetParent(parent);
            anchor.AddComponent<SemanticAnchorMarker>().Configure(
                id,
                SemanticAnchorKind.ObservationFocus);
        }
    }
}
