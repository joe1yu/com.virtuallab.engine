using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class PrefabContractValidatorTests
    {
        [Test]
        public void 新学科能力可通过契约目录扩展而无需修改检查器()
        {
            var prefab = new GameObject("物理实验器材");
            try
            {
                var view = prefab.AddComponent<CourseEntityView>();
                view.Configure("测力计");
                var catalog = new PrefabContractRequirementCatalog(new[]
                {
                    new PrefabContractRequirement(
                        "可观察受力",
                        PrefabContractRequirementKind.SemanticAnchor,
                        nameof(SemanticAnchorKind.ObservationFocus),
                        "物理.需要读数观察点",
                        "测力对象必须包含读数观察点。")
                });
                var contract = new CoursePrefabContractDefinition(
                    "测力计",
                    new[] { "可观察受力" },
                    Array.Empty<string>());

                var diagnostics = PrefabContractValidator.Validate(
                    view,
                    contract,
                    catalog);

                Assert.That(
                    diagnostics.Select(value => value.Code),
                    Does.Contain("物理.需要读数观察点"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

    }
}
