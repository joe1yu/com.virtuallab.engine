using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class RecipeCatalogTests
    {
        [Test]
        public void 平台与学科包按稳定顺序合并且不受提供者顺序影响()
        {
            var platform = Provider(ValidPlatformPackage());
            var chemistry = Provider(new RecipePackage(
                "化学基础",
                RecipeLayer.Discipline,
                recipes: new[]
                {
                    Recipe(
                        "化学.安全抓取",
                        extends: "通用.抓取")
                }));
            var physics = Provider(new RecipePackage(
                "物理基础",
                RecipeLayer.Discipline,
                recipes: new[]
                {
                    Recipe(
                        "物理.测量抓取",
                        extends: "通用.抓取")
                }));

            var first = RecipeCatalog.Create(
                platform,
                new[] { chemistry, physics });
            var second = RecipeCatalog.Create(
                platform,
                new[] { physics, chemistry });

            Assert.That(first.IsValid, Is.True);
            Assert.That(second.IsValid, Is.True);
            Assert.That(
                first.Packages.Select(value => value.PackageId),
                Is.EqualTo(new[] { "平台通用", "化学基础", "物理基础" }));
            Assert.That(
                first.Recipes.Select(value => value.RecipeId),
                Is.EqualTo(second.Recipes.Select(value => value.RecipeId)));
        }

        [Test]
        public void 配方ID全局重复返回诊断()
        {
            var catalog = RecipeCatalog.Create(
                Provider(ValidPlatformPackage()),
                new[]
                {
                    Provider(new RecipePackage(
                        "化学基础",
                        RecipeLayer.Discipline,
                        recipes: new[]
                        {
                            Recipe(
                                "通用.抓取",
                                extends: "通用.观察")
                        }))
                });

            AssertCode(catalog, "recipe.id.duplicate");
        }

        [Test]
        public void 共享包层级只能是平台或学科()
        {
            var invalid = new RecipePackage(
                "伪装课程包",
                (RecipeLayer)99,
                recipes: new[] { Recipe("课程.专用") });

            var catalog = RecipeCatalog.Create(
                Provider(invalid),
                Array.Empty<IRecipePackageProvider>());

            AssertCode(catalog, "recipe.layer.invalid");
        }

        [Test]
        public void 学科配方必须显式扩展或替换已有配方()
        {
            var catalog = RecipeCatalog.Create(
                Provider(ValidPlatformPackage()),
                new[]
                {
                    Provider(new RecipePackage(
                        "化学基础",
                        RecipeLayer.Discipline,
                        recipes: new[] { Recipe("化学.孤立配方") }))
                });

            AssertCode(catalog, "recipe.discipline.relationship.missing");
        }

        [Test]
        public void 悬空关系循环和多重替换均被拒绝()
        {
            var package = new RecipePackage(
                "化学基础",
                RecipeLayer.Discipline,
                recipes: new[]
                {
                    Recipe("化学.甲", extends: "化学.乙"),
                    Recipe("化学.乙", extends: "化学.甲"),
                    Recipe("化学.悬空", replaces: "不存在"),
                    Recipe("化学.替换甲", replaces: "通用.抓取"),
                    Recipe("化学.替换乙", replaces: "通用.抓取")
                });
            var catalog = RecipeCatalog.Create(
                Provider(ValidPlatformPackage()),
                new[] { Provider(package) });

            AssertCode(catalog, "recipe.relationship.cycle");
            AssertCode(catalog, "recipe.relationship.target-missing");
            AssertCode(catalog, "recipe.replacement.conflict");
        }

        [Test]
        public void 共享配方不得引用课程实体或自由模板()
        {
            var package = new RecipePackage(
                "平台通用",
                RecipeLayer.Platform,
                recipes: new[]
                {
                    Recipe(
                        "通用.错误",
                        referencedEntities: new[] { "本课程试管" })
                },
                conditions: new[]
                {
                    new RecipeConditionDefinition(
                        "条件.错误绑定",
                        new[]
                        {
                            new RecipeValueBinding(
                                RecipeBindingKind.CurrentEntity,
                                "${当前实体}"),
                            new RecipeValueBinding(
                                (RecipeBindingKind)99,
                                "当前实体")
                        })
                });

            var catalog = RecipeCatalog.Create(
                Provider(package),
                Array.Empty<IRecipePackageProvider>());

            AssertCode(catalog, "recipe.course-entity.forbidden");
            AssertCode(catalog, "recipe.binding.template-forbidden");
            AssertCode(catalog, "recipe.binding.kind.invalid");
        }

        [Test]
        public void 参数类型单位范围默认值和必填性必须有效()
        {
            var package = new RecipePackage(
                "平台通用",
                RecipeLayer.Platform,
                recipes: new[] { Recipe("通用.错误参数") },
                parameters: new[]
                {
                    new RecipeParameterContract(
                        "通用.错误参数",
                        "容量",
                        RecipeParameterType.Number,
                        true,
                        "毫升",
                        100,
                        10,
                        "200",
                        false,
                        RecipeSafetyLevel.CourseMayTighten),
                    new RecipeParameterContract(
                        "通用.错误参数",
                        "是否开启",
                        RecipeParameterType.Boolean,
                        false,
                        "毫升",
                        null,
                        null,
                        "也许",
                        false,
                        RecipeSafetyLevel.CourseMayTighten),
                    new RecipeParameterContract(
                        "通用.错误参数",
                        "容量",
                        RecipeParameterType.Number,
                        false,
                        "毫升",
                        0,
                        100,
                        "50",
                        false,
                        RecipeSafetyLevel.CourseMayTighten)
                });

            var catalog = RecipeCatalog.Create(
                Provider(package),
                Array.Empty<IRecipePackageProvider>());

            AssertCode(catalog, "recipe.parameter.range.invalid");
            AssertCode(catalog, "recipe.parameter.default.invalid");
            AssertCode(catalog, "recipe.parameter.unit.invalid");
            AssertCode(catalog, "recipe.parameter.duplicate");
        }

        [Test]
        public void 配方操作引用的条件结果表现和状态操作必须存在()
        {
            var package = new RecipePackage(
                "平台通用",
                RecipeLayer.Platform,
                recipes: new[] { Recipe("通用.错误引用") },
                actions: new[]
                {
                    new RecipeActionDefinition(
                        "通用.错误引用",
                        "动作",
                        new[] { "条件.不存在" },
                        new[] { "结果.不存在" },
                        new[] { "表现.不存在" },
                        "错误动作",
                        SemanticActionLifecycle.Instant,
                        "即时执行",
                        SemanticActionPhase.Complete)
                },
                results: new[]
                {
                    new RecipeResultDefinition(
                        "结果.错误操作",
                        recipeId: "通用.错误引用",
                        operationIds: new[] { "操作.不存在" })
                });

            var catalog = RecipeCatalog.Create(
                Provider(package),
                Array.Empty<IRecipePackageProvider>());

            AssertCode(catalog, "recipe.reference.condition-missing");
            AssertCode(catalog, "recipe.reference.result-missing");
            AssertCode(catalog, "recipe.reference.presentation-missing");
            AssertCode(catalog, "recipe.reference.operation-missing");
        }

        [Test]
        public void 表现配方不能包含科学状态操作()
        {
            var package = new RecipePackage(
                "平台通用",
                RecipeLayer.Platform,
                recipes: new[] { Recipe("通用.错误表现") },
                presentations: new[]
                {
                    new RecipePresentationDefinition(
                        "表现.错误",
                        scientificOperationIds: new[] { "物质.增加" })
                });

            var catalog = RecipeCatalog.Create(
                Provider(package),
                Array.Empty<IRecipePackageProvider>());

            AssertCode(catalog, "recipe.presentation.science-operation-forbidden");
        }

        [Test]
        public void 不可弱化参数不能声明课程可删除()
        {
            var package = new RecipePackage(
                "平台通用",
                RecipeLayer.Platform,
                recipes: new[] { Recipe("通用.安全条件") },
                parameters: new[]
                {
                    new RecipeParameterContract(
                        "通用.安全条件",
                        "安全阈值",
                        RecipeParameterType.Number,
                        true,
                        "摄氏度",
                        0,
                        100,
                        "50",
                        true,
                        RecipeSafetyLevel.NonWeakenable)
                });

            var catalog = RecipeCatalog.Create(
                Provider(package),
                Array.Empty<IRecipePackageProvider>());

            AssertCode(catalog, "recipe.safety.deletion-forbidden");
        }

        [Test]
        public void 共享配方源统一拒绝未知文件重复文件和非法UTF8()
        {
            var source = new RecipePackageSource(new[]
            {
                new RecipePackageFile(
                    "配方.csv",
                    Encoding.UTF8.GetBytes("配方ID\n通用.抓取\n")),
                new RecipePackageFile(
                    "配方.csv",
                    Encoding.UTF8.GetBytes("配方ID\n通用.观察\n")),
                new RecipePackageFile(
                    "旧脚本.csv",
                    Encoding.UTF8.GetBytes("脚本\n自由表达式\n")),
                new RecipePackageFile(
                    "条件.csv",
                    new byte[] { 0xE5, 0xAE, 0x9E, 0xC3, 0x28 })
            });

            var result = source.ReadTables();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Tables.Keys, Does.Contain("配方.csv"));
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("recipe.file.duplicate"));
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("recipe.file.unsupported"));
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("csv.encoding.invalid"));
        }

        private static RecipePackage ValidPlatformPackage()
        {
            return new RecipePackage(
                "平台通用",
                RecipeLayer.Platform,
                recipes: new[]
                {
                    Recipe("通用.抓取"),
                    Recipe("通用.观察")
                });
        }

        private static RecipeDefinition Recipe(
            string id,
            string extends = "",
            string replaces = "",
            string[] referencedEntities = null)
        {
            return new RecipeDefinition(
                id,
                RecipeMatchKind.SingleEntity,
                "可抓取",
                extends,
                replaces,
                RecipeSafetyLevel.CourseMayTighten,
                referencedEntities ?? Array.Empty<string>());
        }

        private static IRecipePackageProvider Provider(RecipePackage package) =>
            new StubProvider(package);

        private static void AssertCode(RecipeCatalog catalog, string code)
        {
            Assert.That(catalog.IsValid, Is.False);
            Assert.That(
                catalog.Diagnostics.Select(value => value.Code),
                Does.Contain(code));
            Assert.That(
                catalog.Diagnostics
                    .Where(value => value.Code == code)
                    .All(value => !string.IsNullOrWhiteSpace(value.Reason)),
                Is.True);
        }

        private sealed class StubProvider : IRecipePackageProvider
        {
            private readonly RecipePackage _package;

            public StubProvider(RecipePackage package)
            {
                _package = package;
            }

            public string PackageId => _package.PackageId;
            public IReadOnlyList<string> RequiredRuntimeModuleIds { get; } =
                new[] { CourseModuleIds.Core };
            public IReadOnlyList<VirtualLab.Domain.Relations.RelationTypeId>
                RelationTypeIds { get; } =
                Array.Empty<VirtualLab.Domain.Relations.RelationTypeId>();
            public IReadOnlyList<string> RegisteredStateOperationIds { get; } =
                Array.Empty<string>();
            public RecipePackage Load() => _package;
        }
    }
}
