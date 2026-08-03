using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.Chemistry.Tests.EngineIntegration
{
    public sealed class ChemistryPrefabContractTests
    {
        [Test]
        public void 化学能力可推导预制体必须具备的组件锚点与插槽()
        {
            var prefab = new GameObject("完整实验器材");
            try
            {
                prefab.AddComponent<CourseEntityView>();
                prefab.AddComponent<BoxCollider>();
                AddAnchor(prefab, "端口.连接", SemanticAnchorKind.ConnectionPort);
                AddAnchor(prefab, "锚点.倾倒出口", SemanticAnchorKind.PourOutlet);
                AddAnchor(prefab, "锚点.受热", SemanticAnchorKind.HeatingPoint);
                AddAnchor(prefab, "锚点.点火", SemanticAnchorKind.IgnitionPoint);
                AddAnchor(prefab, "锚点.观察", SemanticAnchorKind.ObservationFocus);
                AddSlot(prefab, "插槽.液面", PresentationSlotKind.Liquid);
                AddSlot(prefab, "插槽.燃烧", PresentationSlotKind.Combustion);
                var contract = new CoursePrefabContractDefinition(
                    "预制体.完整器材",
                    new[]
                    {
                        "可抓取", "可连接", "可倾倒", "容器", "可加热",
                        "可点燃", "可观察"
                    },
                    new[] { "端口.连接" });

                Assert.That(
                    PrefabContractValidator.Validate(prefab, contract),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void 缺失化学视图项会返回可定位诊断码()
        {
            var prefab = new GameObject("不完整器材");
            try
            {
                var contract = new CoursePrefabContractDefinition(
                    "预制体.不完整器材",
                    new[] { "可抓取", "可倾倒", "容器" },
                    Array.Empty<string>());
                var codes = PrefabContractValidator.Validate(prefab, contract)
                    .Select(value => value.Code);

                Assert.That(codes, Does.Contain("预制体.需要碰撞体"));
                Assert.That(codes, Does.Contain("预制体.需要倾倒出口"));
                Assert.That(codes, Does.Contain("预制体.需要内容插槽"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        private static void AddAnchor(
            GameObject parent,
            string id,
            SemanticAnchorKind kind)
        {
            var value = new GameObject(id);
            value.transform.SetParent(parent.transform);
            value.AddComponent<SemanticAnchorMarker>().Configure(id, kind);
        }

        private static void AddSlot(
            GameObject parent,
            string id,
            PresentationSlotKind kind)
        {
            var value = new GameObject(id);
            value.transform.SetParent(parent.transform);
            value.AddComponent<PresentationSlotMarker>().Configure(id, kind);
        }
    }
}
