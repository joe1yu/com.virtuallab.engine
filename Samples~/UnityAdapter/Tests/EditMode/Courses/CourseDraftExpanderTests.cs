using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Domain.Relations;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Drafts;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseDraftExpanderTests
    {
        [Test]
        public void 模板组件先展开再应用课程组件覆盖()
        {
            var catalog = Catalog(
                components: new[]
                {
                    new AuthoringComponentDescriptor(
                        "容器",
                        "容器",
                        string.Empty,
                        new[]
                        {
                            new AuthoringParameterDescriptor(
                                "容量毫升", "容量", string.Empty,
                                AuthoringParameterType.Number, false,
                                "毫升", 0, null, "50")
                        },
                        Array.Empty<AuthoringPortDescriptor>())
                },
                templates: new[]
                {
                    Template(
                        "试管",
                        new[] { "容器" },
                        new[]
                        {
                            new KeyValuePair<string, string>(
                                "容量毫升", "50")
                        })
                });
            var draft = Draft(
                new[] { Object("大试管", "试管") },
                new[]
                {
                    new CourseDraftComponent(
                        "覆盖.容量", "实体", "大试管", "容器", "容量=80",
                        Source("组件.csv", 2, "覆盖.容量"))
                });

            var result = Expand(draft, catalog);

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            var entity = result.Blueprint.Objects.Single();
            Assert.That(entity.FeatureIds, Does.Contain("容器"));
            Assert.That(
                entity.ExtensionValues["参数.容量"].RawValue,
                Is.EqualTo("80"));
            Assert.That(entity.EntityType, Is.EqualTo("试管"));
        }

        [Test]
        public void 类型选择器让任意玻璃片都能覆盖任意集气瓶()
        {
            var catalog = Catalog(
                components: new[]
                {
                    Component("可覆盖来源"),
                    Component("可被覆盖")
                },
                templates: new[]
                {
                    Template("玻璃片", new[] { "可覆盖来源" },
                        operations: new[] { "覆盖" }),
                    Template("集气瓶", new[] { "可被覆盖" },
                        operations: new[] { "覆盖" })
                },
                operations: new[]
                {
                    new AuthoringOperationDescriptor(
                        "覆盖", "覆盖", string.Empty,
                        AuthoringOperationLifecycle.Instant,
                        "放置到目标", string.Empty, string.Empty, "覆盖", string.Empty)
                });
            var draft = Draft(new[]
            {
                Object("玻璃片一", "玻璃片"),
                Object("玻璃片二", "玻璃片"),
                Object("集气瓶一", "集气瓶"),
                Object("集气瓶二", "集气瓶")
            });

            var result = Expand(draft, catalog);

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            var covers = result.Blueprint.InteractionRules
                .Where(value => value.ActionId == "覆盖")
                .Select(value => value.SourceEntityId + ">" + value.TargetEntityId)
                .ToArray();
            Assert.That(covers, Is.EquivalentTo(new[]
            {
                "玻璃片一>集气瓶一", "玻璃片一>集气瓶二",
                "玻璃片二>集气瓶一", "玻璃片二>集气瓶二"
            }));
        }

        [Test]
        public void 选择器空结果返回原配置行诊断()
        {
            var catalog = Catalog(
                components: new[] { Component("容器") },
                templates: new[] { Template("试管", Array.Empty<string>()) });
            var draft = Draft(
                new[] { Object("大试管", "试管") },
                new[]
                {
                    new CourseDraftComponent(
                        "覆盖.不存在", "类型", "烧杯", "容器", string.Empty,
                        Source("组件.csv", 8, "覆盖.不存在"))
                });

            var result = Expand(draft, catalog);

            Assert.That(result.IsSuccess, Is.False);
            var diagnostic = result.Diagnostics.Single(value =>
                value.Code == "draft.selector.empty");
            Assert.That(diagnostic.FileName, Is.EqualTo("组件.csv"));
            Assert.That(diagnostic.Line, Is.EqualTo(8));
        }

        [Test]
        public void 模板建议角色可以被选择器直接使用()
        {
            var catalog = Catalog(
                components: new[] { Component("可加热") },
                templates: new[]
                {
                    Template(
                        "试管",
                        Array.Empty<string>(),
                        suggestedRoles: new[] { "反应容器" })
                });
            var draft = Draft(
                new[] { Object("大试管", "试管") },
                new[]
                {
                    new CourseDraftComponent(
                        "补充.加热", "角色", "反应容器", "可加热", string.Empty,
                        Source("组件.csv", 3, "补充.加热"))
                });

            var result = Expand(draft, catalog);

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Blueprint.Objects.Single().FeatureIds,
                Does.Contain("可加热"));
        }

        [Test]
        public void 创作目录与共享配方的操作契约不一致时拒绝展开()
        {
            var operation = new AuthoringOperationDescriptor(
                "覆盖", "覆盖", string.Empty,
                AuthoringOperationLifecycle.Instant,
                "放置到目标", string.Empty, string.Empty, "覆盖", string.Empty);
            var catalog = Catalog(
                components: Array.Empty<AuthoringComponentDescriptor>(),
                templates: new[] { Template("玻璃片", Array.Empty<string>()) },
                operations: new[] { operation });
            var recipeCatalog = RecipeCatalogWith(new RecipeActionDefinition(
                "测试.覆盖",
                "覆盖",
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                "覆盖",
                SemanticActionLifecycle.Continuous,
                "错误执行方式",
                SemanticActionPhase.Complete,
                "错误动作"));

            var result = new CourseDraftExpander().Expand(
                Draft(new[] { Object("玻璃片", "玻璃片") }),
                catalog,
                recipeCatalog);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Select(value => value.Code),
                Is.SupersetOf(new[]
                {
                    "authoring.operation.lifecycle-mismatch",
                    "authoring.operation.execution-mode-mismatch",
                    "authoring.operation.phase-command-mismatch"
                }));
        }

        [Test]
        public void 未注册后果模板不能进入蓝图结果配方字段()
        {
            var operation = new AuthoringOperationDescriptor(
                "覆盖", "覆盖", string.Empty,
                AuthoringOperationLifecycle.Instant,
                "放置到目标", string.Empty, string.Empty, "覆盖", string.Empty);
            var catalog = Catalog(
                components: Array.Empty<AuthoringComponentDescriptor>(),
                templates: new[] { Template("玻璃片", Array.Empty<string>()) },
                operations: new[] { operation });
            var draft = Draft(
                new[] { Object("玻璃片", "玻璃片") },
                operationOverrides: new[]
                {
                    new CourseDraftOperationOverride(
                        "错误覆盖", "允许", "覆盖",
                        "实体", "玻璃片", string.Empty, string.Empty,
                        1, string.Empty, string.Empty,
                        string.Empty, string.Empty, string.Empty, string.Empty,
                        string.Empty, "任意状态操作",
                        Source("操作特例.csv", 4, "错误覆盖"))
                });

            var result = Expand(draft, catalog);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Select(value => value.Code),
                Does.Contain("draft.consequence-template.unknown"));
        }

        [Test]
        public void 过程来源和目标选择器按全部组合展开()
        {
            var catalog = Catalog(
                components: Array.Empty<AuthoringComponentDescriptor>(),
                templates: new[]
                {
                    Template("操作者", Array.Empty<string>()),
                    Template("来源", Array.Empty<string>()),
                    Template("目标", Array.Empty<string>())
                },
                processes: new[]
                {
                    new AuthoringProcessDescriptor(
                        "测试过程", "测试过程", string.Empty,
                        Array.Empty<AuthoringParameterDescriptor>())
                });
            var draft = Draft(
                new[]
                {
                    Object("学生", "操作者"),
                    Object("来源一", "来源"),
                    Object("来源二", "来源"),
                    Object("目标一", "目标"),
                    Object("目标二", "目标")
                },
                processes: new[]
                {
                    new CourseDraftProcess(
                        "过程", "测试过程",
                        "实体", "学生",
                        "类型", "来源",
                        "类型", "目标",
                        string.Empty,
                        Source("过程.csv", 2, "过程"))
                });

            var result = Expand(draft, catalog);

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Blueprint.DisciplineProcesses
                .Select(value => value.SourceEntityId + ">" + value.TargetEntityId),
                Is.EquivalentTo(new[]
                {
                    "来源一>目标一", "来源一>目标二",
                    "来源二>目标一", "来源二>目标二"
                }));
        }

        private static CourseAuthoringDraft Draft(
            IEnumerable<CourseDraftObject> objects,
            IEnumerable<CourseDraftComponent> components = null,
            IEnumerable<CourseDraftOperationOverride> operationOverrides = null,
            IEnumerable<CourseDraftProcess> processes = null) =>
            new CourseAuthoringDraft(
                new CourseDraftCourse(
                    "测试课程", "测试课程", "化学基础", "学生", "实验.prefab",
                    Source("课程.csv", 2, "测试课程")),
                objects,
                components ?? Array.Empty<CourseDraftComponent>(),
                Array.Empty<CourseDraftInitialRelation>(),
                operationOverrides ?? Array.Empty<CourseDraftOperationOverride>(),
                processes ?? Array.Empty<CourseDraftProcess>(),
                Array.Empty<CourseDraftTeachingItem>(),
                Array.Empty<CourseDraftTeachingCondition>(),
                Array.Empty<CourseDraftPresentation>(),
                Array.Empty<CourseDraftAcceptanceRecord>());

        private static CourseDraftObject Object(string id, string type) =>
            new CourseDraftObject(
                id, id, type, string.Empty, string.Empty,
                "0|0|0", "0|0|0", Source("实验对象.csv", 2, id));

        private static AuthoringItemTemplateDescriptor Template(
            string id,
            IEnumerable<string> components,
            IEnumerable<KeyValuePair<string, string>> parameters = null,
            IEnumerable<string> operations = null,
            IEnumerable<string> suggestedRoles = null) =>
            new AuthoringItemTemplateDescriptor(
                id,
                "测试类别",
                id,
                string.Empty,
                1,
                components,
                parameters ?? Array.Empty<KeyValuePair<string, string>>(),
                operations ?? Array.Empty<string>(),
                suggestedRoles ?? Array.Empty<string>(),
                Array.Empty<string>());

        private static CourseDraftExpansionResult Expand(
            CourseAuthoringDraft draft,
            CourseAuthoringCatalog catalog) =>
            new CourseDraftExpander().Expand(
                draft,
                catalog,
                RecipeCatalogWith());

        private static RecipeCatalog RecipeCatalogWith(
            params RecipeActionDefinition[] actions) =>
            RecipeCatalog.Create(
                new RecipeProvider(new RecipePackage(
                    "测试共享配方",
                    RecipeLayer.Platform,
                    recipes: new[]
                    {
                        new RecipeDefinition(
                            "测试.覆盖",
                            RecipeMatchKind.SingleEntity,
                            "测试特征",
                            string.Empty,
                            string.Empty,
                            RecipeSafetyLevel.CourseMayTighten,
                            Array.Empty<string>())
                    },
                    actions: actions)),
                Array.Empty<IRecipePackageProvider>());

        private static AuthoringComponentDescriptor Component(string id) =>
            new AuthoringComponentDescriptor(
                id, id, string.Empty,
                Array.Empty<AuthoringParameterDescriptor>(),
                Array.Empty<AuthoringPortDescriptor>());

        private static CourseAuthoringCatalog Catalog(
            IEnumerable<AuthoringComponentDescriptor> components,
            IEnumerable<AuthoringItemTemplateDescriptor> templates,
            IEnumerable<AuthoringOperationDescriptor> operations = null,
            IEnumerable<AuthoringProcessDescriptor> processes = null) =>
            CourseAuthoringCatalog.Create(new[]
            {
                new Provider(new CourseAuthoringModuleDescriptor(
                    "化学基础",
                    new[]
                    {
                        new AuthoringCategoryDescriptor(
                            "测试类别", "测试类别", string.Empty, 1)
                    },
                    components,
                    templates,
                    operations ?? Array.Empty<AuthoringOperationDescriptor>(),
                    processes ?? Array.Empty<AuthoringProcessDescriptor>(),
                    Array.Empty<AuthoringOptionDescriptor>()))
            });

        private static ConfigurationSource Source(
            string file,
            int line,
            string id) =>
            new ConfigurationSource(
                ConfigurationLayer.Course,
                "测试课程",
                file,
                line,
                1,
                id);

        private static string Diagnostics(CourseDraftExpansionResult result) =>
            string.Join("\n", result.Diagnostics.Select(value =>
                value.Code + ": " + value.Reason));

        private sealed class Provider : ICourseAuthoringCatalogProvider
        {
            private readonly CourseAuthoringModuleDescriptor _descriptor;

            public Provider(CourseAuthoringModuleDescriptor descriptor)
            {
                _descriptor = descriptor;
            }

            public string PackageId => _descriptor.PackageId;
            public CourseAuthoringModuleDescriptor Load() => _descriptor;
        }

        private sealed class RecipeProvider : IRecipePackageProvider
        {
            private readonly RecipePackage _package;

            public RecipeProvider(RecipePackage package)
            {
                _package = package;
            }

            public string PackageId => _package.PackageId;
            public IReadOnlyList<string> RequiredRuntimeModuleIds { get; } =
                Array.Empty<string>();
            public IReadOnlyList<RelationTypeId> RelationTypeIds { get; } =
                Array.Empty<RelationTypeId>();
            public IReadOnlyList<string> RegisteredStateOperationIds { get; } =
                Array.Empty<string>();
            public RecipePackage Load() => _package;
        }
    }
}
