using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Drafts;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseAuthoringSessionTests
    {
        private string _root;
        private CourseAuthoringSession _session;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-CourseSession-" + Guid.NewGuid().ToString("N"));
            var created = new CourseAuthoringDraftCreator().Create(
                _root,
                "测试课程",
                "测试课程",
                "化学基础",
                "课程资源/测试.prefab");
            _session = CourseAuthoringSession.Load(
                created.AuthoringDirectory,
                Catalog());
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
        public void 外部修改任一表时整次保存失败且不覆盖其它表()
        {
            _session.AddSupply("试管", "大试管", "大试管");
            var teachingPath = _session.PathOf(CourseAuthoringTableNames.Teaching);
            File.AppendAllText(teachingPath, Environment.NewLine);

            Assert.Throws<InvalidOperationException>(() => _session.Save());
            Assert.That(File.ReadAllText(teachingPath), Does.EndWith(
                Environment.NewLine + Environment.NewLine));
            Assert.That(
                File.ReadAllText(_session.PathOf(CourseAuthoringTableNames.Objects)),
                Does.Not.Contain("大试管"));
        }

        [Test]
        public void 用品修改支持撤销和重做且不产生第二份状态源()
        {
            _session.AddSupply("试管", "大试管", "大试管");

            Assert.That(_session.Undo(), Is.True);
            Assert.That(_session.Draft.Objects.Select(value => value.EntityId),
                Does.Not.Contain("大试管"));
            Assert.That(_session.Redo(), Is.True);
            Assert.That(_session.Draft.Objects.Select(value => value.EntityId),
                Does.Contain("大试管"));
        }

        [Test]
        public void 工作流固定返回六个面向作者的区域和具体缺失项()
        {
            var workflow = new CourseAuthoringWorkflow(_session);

            Assert.That(workflow.Sections.Select(value => value.DisplayName),
                Is.EqualTo(new[]
                {
                    "课程信息",
                    "实验用品",
                    "装置与初始状态",
                    "操作与科学过程",
                    "教学目标与错误后果",
                    "检查与生成"
                }));
            Assert.That(workflow.Section("课程信息").IsComplete, Is.True);
            Assert.That(workflow.Section("实验用品").MissingItems,
                Does.Contain("至少添加一种实验用品"));
        }

        [Test]
        public void 表现触发来源和目标保存后可以完整往返()
        {
            _session.AddSupply("试管", "药匙", "药匙");
            _session.AddSupply("试管", "试剂瓶", "试剂瓶");
            _session.SetPresentation(new CourseDraftPresentation(
                "舀取表现",
                "动作成功",
                "取出",
                "药匙",
                "试剂瓶",
                "实体",
                "试剂瓶",
                "隐藏渲染器",
                "表现插槽",
                "插槽.内容",
                string.Empty,
                new ConfigurationSource(
                    ConfigurationLayer.Course,
                    "测试课程",
                    CourseAuthoringTableNames.Presentation,
                    2,
                    1,
                    "舀取表现")));

            _session.Save();
            var reloaded = CourseAuthoringSession.Load(
                _session.AuthoringDirectory,
                Catalog());
            var presentation = reloaded.Draft.Presentations.Single();

            Assert.That(presentation.TriggerSourceEntityId,
                Is.EqualTo("药匙"));
            Assert.That(presentation.TriggerTargetEntityId,
                Is.EqualTo("试剂瓶"));
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
