using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Catalogs;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseAuthoringCatalogTests
    {
        [Test]
        public void 模块顺序不同仍按显示顺序和稳定标识生成同一目录()
        {
            var chemistry = Provider(
                "化学基础",
                categories: new[]
                {
                    Category("操作工具", 20),
                    Category("容器与反应器皿", 10)
                },
                components: new[] { Component("容器") },
                templates: new[]
                {
                    Template("试管", "容器与反应器皿", "容器")
                });
            var platform = Provider(
                "平台通用",
                categories: new[] { Category("系统角色", 0) });

            var first = CourseAuthoringCatalog.Create(
                new[] { chemistry, platform });
            var second = CourseAuthoringCatalog.Create(
                new[] { platform, chemistry });

            Assert.That(first.IsValid, Is.True);
            Assert.That(second.IsValid, Is.True);
            Assert.That(
                first.Categories.Select(value => value.CategoryId),
                Is.EqualTo(new[] { "系统角色", "容器与反应器皿", "操作工具" }));
            Assert.That(
                first.Categories.Select(value => value.CategoryId),
                Is.EqualTo(second.Categories.Select(value => value.CategoryId)));
            Assert.That(
                first.Templates.Select(value => value.TemplateId),
                Is.EqualTo(second.Templates.Select(value => value.TemplateId)));
        }

        [Test]
        public void 模板引用不存在组件时返回包含原因和修复建议的诊断()
        {
            var result = CourseAuthoringCatalog.Create(new[]
            {
                Provider(
                    "化学基础",
                    categories: new[] { Category("容器与反应器皿", 10) },
                    templates: new[]
                    {
                        Template("试管", "容器与反应器皿", "不存在的组件")
                    })
            });

            Assert.That(result.IsValid, Is.False);
            var diagnostic = result.Diagnostics.Single(value =>
                value.Code == "authoring.template.component-missing");
            Assert.That(diagnostic.ConfigurationId, Is.EqualTo("试管"));
            Assert.That(diagnostic.Reason, Does.Contain("不存在的组件"));
            Assert.That(diagnostic.Suggestion, Does.Contain("注册组件"));
        }

        [Test]
        public void 跨模块重复稳定标识会被拒绝且诊断顺序确定()
        {
            var result = CourseAuthoringCatalog.Create(new[]
            {
                Provider(
                    "学科乙",
                    categories: new[] { Category("容器", 10) },
                    operations: new[] { Operation("覆盖") }),
                Provider(
                    "学科甲",
                    categories: new[] { Category("容器", 20) },
                    operations: new[] { Operation("覆盖") })
            });

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Is.EqualTo(new[]
                {
                    "authoring.category.id-duplicate",
                    "authoring.operation.id-duplicate"
                }));
        }

        [Test]
        public void 模板引用不存在类别和操作时分别给出诊断()
        {
            var result = CourseAuthoringCatalog.Create(new[]
            {
                Provider(
                    "化学基础",
                    components: new[] { Component("容器") },
                    templates: new[]
                    {
                        new AuthoringItemTemplateDescriptor(
                            "试管",
                            "容器与反应器皿",
                            "试管",
                            "盛装少量物质并进行反应。",
                            10,
                            new[] { "容器" },
                            Array.Empty<KeyValuePair<string, string>>(),
                            new[] { "加热" },
                            Array.Empty<string>(),
                            Array.Empty<string>())
                    })
            });

            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Is.EqualTo(new[]
                {
                    "authoring.template.category-missing",
                    "authoring.template.operation-missing"
                }));
        }

        private static StubProvider Provider(
            string packageId,
            IEnumerable<AuthoringCategoryDescriptor> categories = null,
            IEnumerable<AuthoringComponentDescriptor> components = null,
            IEnumerable<AuthoringItemTemplateDescriptor> templates = null,
            IEnumerable<AuthoringOperationDescriptor> operations = null)
        {
            return new StubProvider(new CourseAuthoringModuleDescriptor(
                packageId,
                categories,
                components,
                templates,
                operations,
                Array.Empty<AuthoringProcessDescriptor>(),
                Array.Empty<AuthoringOptionDescriptor>()));
        }

        private static AuthoringCategoryDescriptor Category(
            string categoryId,
            int order)
        {
            return new AuthoringCategoryDescriptor(
                categoryId,
                categoryId,
                categoryId + "说明",
                order);
        }

        private static AuthoringComponentDescriptor Component(string componentId)
        {
            return new AuthoringComponentDescriptor(
                componentId,
                componentId,
                componentId + "说明",
                Array.Empty<AuthoringParameterDescriptor>(),
                Array.Empty<AuthoringPortDescriptor>());
        }

        private static AuthoringItemTemplateDescriptor Template(
            string templateId,
            string categoryId,
            params string[] componentIds)
        {
            return new AuthoringItemTemplateDescriptor(
                templateId,
                categoryId,
                templateId,
                templateId + "说明",
                10,
                componentIds,
                Array.Empty<KeyValuePair<string, string>>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static AuthoringOperationDescriptor Operation(string operationId)
        {
            return new AuthoringOperationDescriptor(
                operationId,
                operationId,
                operationId + "说明",
                AuthoringOperationLifecycle.Instant,
                "即时",
                string.Empty,
                string.Empty,
                operationId,
                string.Empty);
        }

        private sealed class StubProvider : ICourseAuthoringCatalogProvider
        {
            private readonly CourseAuthoringModuleDescriptor _module;

            public StubProvider(CourseAuthoringModuleDescriptor module)
            {
                _module = module;
            }

            public string PackageId => _module.PackageId;

            public CourseAuthoringModuleDescriptor Load()
            {
                return _module;
            }
        }
    }
}
