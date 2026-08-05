using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Drafts;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseAuthoringDraftCreatorTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-CourseDraft-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
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
        public void 新建课程一次创建十张职责表并可立即加入试管()
        {
            var created = new CourseAuthoringDraftCreator().Create(
                _root,
                "演示课程",
                "演示课程",
                "化学基础",
                "课程资源/演示.prefab",
                "学生");
            var session = CourseAuthoringSession.Load(
                created.AuthoringDirectory,
                Catalog());
            session.AddSupply("试管", "大试管", "大试管");
            session.Save();

            Assert.That(
                Directory.GetFiles(created.AuthoringDirectory, "*.csv"),
                Has.Length.EqualTo(CourseAuthoringSchema.Tables.Count));
            Assert.That(
                File.ReadAllText(Path.Combine(
                    created.AuthoringDirectory,
                    CourseAuthoringTableNames.Objects)),
                Does.Contain("大试管,大试管,试管"));
            Assert.That(
                session.Draft.Objects.Single(value => value.EntityId == "学生")
                    .EntityType,
                Is.Empty);
        }

        [Test]
        public void 已存在课程目录不会被新建服务覆盖()
        {
            var path = Path.Combine(_root, "演示课程");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "保留.txt"), "原内容");

            Assert.Throws<InvalidOperationException>(() =>
                new CourseAuthoringDraftCreator().Create(
                    _root,
                    "演示课程",
                    "演示课程",
                    "化学基础",
                    "课程资源/演示.prefab"));
            Assert.That(
                File.ReadAllText(Path.Combine(path, "保留.txt")),
                Is.EqualTo("原内容"));
        }

        private static CourseAuthoringCatalog Catalog() =>
            CourseAuthoringCatalog.Create(new[]
            {
                new Provider(new CourseAuthoringModuleDescriptor(
                    "化学基础",
                    new[]
                    {
                        new AuthoringCategoryDescriptor(
                            "容器", "容器", string.Empty, 1)
                    },
                    Array.Empty<AuthoringComponentDescriptor>(),
                    new[]
                    {
                        new AuthoringItemTemplateDescriptor(
                            "试管", "容器", "试管", string.Empty, 1,
                            Array.Empty<string>(),
                            Array.Empty<KeyValuePair<string, string>>(),
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            Array.Empty<string>())
                    },
                    Array.Empty<AuthoringOperationDescriptor>(),
                    Array.Empty<AuthoringProcessDescriptor>(),
                    Array.Empty<AuthoringOptionDescriptor>()))
            });

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
