using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseAuthoringWorkflowTests
    {
        private string _root;
        private CourseAuthoringWorkflow _workflow;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-Workflow-" + Guid.NewGuid().ToString("N"));
            var created = new CourseAuthoringDraftCreator().Create(
                _root,
                "测试课程",
                "测试课程",
                "化学基础",
                "课程资源/测试.prefab");
            _workflow = new CourseAuthoringWorkflow(
                CourseAuthoringSession.Load(
                    created.AuthoringDirectory,
                    Catalog()));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void 从类别选择模板和数量会创建唯一实体标识()
        {
            var added = _workflow.AddSupplies(
                "容器与反应器皿",
                "集气瓶",
                2);

            Assert.That(
                added.Select(value => value.EntityId),
                Is.EqualTo(new[] { "集气瓶一", "集气瓶二" }));
            Assert.That(
                added.Select(value => value.EntityType).Distinct(),
                Is.EqualTo(new[] { "集气瓶" }));
        }

        [Test]
        public void 初始关系只接受目录注册类型和现有实体端点()
        {
            _workflow.AddSupplies("容器与反应器皿", "集气瓶", 1);
            _workflow.AddSupplies("配套小件", "玻璃片", 1);

            var success = _workflow.TrySetRelation(
                "覆盖",
                "玻璃片",
                "集气瓶");
            var invalid = _workflow.TrySetRelation(
                "任意关系",
                "玻璃片",
                "不存在");

            Assert.That(success.IsSuccess, Is.True, success.Message);
            Assert.That(invalid.IsSuccess, Is.False);
            Assert.That(
                _workflow.Session.Draft.InitialRelations.Single().RelationType,
                Is.EqualTo("交互.关系.覆盖"));
        }

        private static CourseAuthoringCatalog Catalog() =>
            CourseAuthoringCatalog.Create(new[]
            {
                new Provider(new CourseAuthoringModuleDescriptor(
                    "化学基础",
                    new[]
                    {
                        new AuthoringCategoryDescriptor(
                            "容器与反应器皿",
                            "容器与反应器皿",
                            string.Empty,
                            1),
                        new AuthoringCategoryDescriptor(
                            "配套小件",
                            "配套小件",
                            string.Empty,
                            2)
                    },
                    Array.Empty<AuthoringComponentDescriptor>(),
                    new[]
                    {
                        Template("集气瓶", "容器与反应器皿"),
                        Template("玻璃片", "配套小件")
                    },
                    Array.Empty<AuthoringOperationDescriptor>(),
                    Array.Empty<AuthoringProcessDescriptor>(),
                    new[]
                    {
                        new AuthoringOptionDescriptor(
                            AuthoringOptionKind.RelationType,
                            "交互.关系.覆盖",
                            "覆盖",
                            "覆盖件盖住目标")
                    }))
            });

        private static AuthoringItemTemplateDescriptor Template(
            string id,
            string category) =>
            new AuthoringItemTemplateDescriptor(
                id,
                category,
                id,
                string.Empty,
                1,
                Array.Empty<string>(),
                Array.Empty<KeyValuePair<string, string>>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());

        private sealed class Provider : ICourseAuthoringCatalogProvider
        {
            private readonly CourseAuthoringModuleDescriptor _module;

            public Provider(CourseAuthoringModuleDescriptor module)
            {
                _module = module;
            }

            public string PackageId => _module.PackageId;
            public CourseAuthoringModuleDescriptor Load() => _module;
        }
    }
}
