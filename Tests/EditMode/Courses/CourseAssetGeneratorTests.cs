using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Generation;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.Unity.Authoring.Recipes;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseAssetGeneratorTests
    {
        private const string TestFolder = "Assets/VirtualLab.Tests/课程资产";
        private const string AssetPath = TestFolder + "/最小课程.asset";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/VirtualLab.Tests"))
            {
                AssetDatabase.CreateFolder("Assets", "VirtualLab.Tests");
            }

            if (!AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.CreateFolder(
                    "Assets/VirtualLab.Tests",
                    "课程资产");
            }
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void 课程资产保存定义和Unity资源直接引用且没有版本哈希()
        {
            var prefab = new GameObject("试管");
            var material = new Material(
                Shader.Find("Sprites/Default"));
            var asset = ScriptableObject.CreateInstance<CompiledCourseAsset>();
            asset.SetData(
                "最小课程",
                CourseAssetDecoder.EncodeDomain(Definition()),
                "{}",
                new[]
                {
                    new CourseResourceBinding(
                        "资源.试管",
                        prefab,
                        material,
                        null,
                        null)
                },
                new[]
                {
                    new CourseTextArtifactBinding(
                        "化学运行配置",
                        "{\"物质\":[]}")
                },
                prefab);

            Assert.That(asset.CourseId, Is.EqualTo("最小课程"));
            Assert.That(asset.ResourceBindings.Single().Prefab, Is.SameAs(prefab));
            Assert.That(asset.ResourceBindings.Single().Material, Is.SameAs(material));
            Assert.That(asset.EnvironmentPrefab, Is.SameAs(prefab));
            Assert.That(
                asset.RequireTextArtifact("化学运行配置"),
                Is.EqualTo("{\"物质\":[]}"));
            Assert.That(
                CourseAssetDecoder.DecodeDomain(asset).CourseId,
                Is.EqualTo("最小课程"));
            Assert.That(asset.GetType().GetProperty("ContentHash"), Is.Null);
            Assert.That(asset.GetType().GetProperty("SchemaVersion"), Is.Null);
            Assert.That(asset.GetType().GetProperty("CourseVersion"), Is.Null);

            UnityEngine.Object.DestroyImmediate(asset);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(prefab);
        }

        [Test]
        public void 固定路径重复生成保留GUID并删除失效绑定()
        {
            var generator = new CourseAssetGenerator();
            generator.GenerateOrUpdate(
                AssetPath,
                Definition(),
                "{}",
                new[]
                {
                    new CourseResourceBinding("资源.旧", null, null, null, null),
                    new CourseResourceBinding("资源.保留", null, null, null, null)
                },
                null);
            var firstGuid = AssetDatabase.AssetPathToGUID(AssetPath);

            generator.GenerateOrUpdate(
                AssetPath,
                Definition(),
                "{\"表现\":\"新\"}",
                new[]
                {
                    new CourseResourceBinding("资源.保留", null, null, null, null)
                },
                null);
            var asset = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                AssetPath);

            Assert.That(AssetDatabase.AssetPathToGUID(AssetPath), Is.EqualTo(firstGuid));
            Assert.That(asset.ResourceBindings.Select(value => value.Key),
                Is.EqualTo(new[] { "资源.保留" }));
            Assert.That(asset.PresentationJson, Is.EqualTo("{\"表现\":\"新\"}"));
        }

        [Test]
        public void 重复绑定或编译错误不会覆盖上一次可用资产()
        {
            var generator = new CourseAssetGenerator();
            generator.GenerateOrUpdate(
                AssetPath,
                Definition(),
                "{\"状态\":\"可用\"}",
                Array.Empty<CourseResourceBinding>(),
                null);

            Assert.Throws<ArgumentException>(() =>
                generator.GenerateOrUpdate(
                    AssetPath,
                    Definition(),
                    "{}",
                    new[]
                    {
                        new CourseResourceBinding("资源.重复", null, null, null, null),
                        new CourseResourceBinding("资源.重复", null, null, null, null)
                    },
                    null));
            var failedCompilation = new CourseCompilationResultForGeneration(
                new[]
                {
                    new CourseCompilationDiagnostic(
                        "course.test",
                        "课程.csv",
                        1,
                        1,
                        string.Empty,
                        "最小课程",
                        "模拟编译失败。",
                        "修复表格。")
                });
            Assert.That(
                generator.TryGenerateOrUpdate(
                    AssetPath,
                    failedCompilation,
                    "{}",
                    Array.Empty<CourseResourceBinding>(),
                    null),
                Is.False);

            var asset = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                AssetPath);
            Assert.That(asset.PresentationJson, Is.EqualTo("{\"状态\":\"可用\"}"));
        }

        [Test]
        public void 蓝图编译阶段固定且成功结果包含运行时内容摘要和来源链()
        {
            var environmentPath = TestFolder + "/蓝图实验室.prefab";
            var tubePath = TestFolder + "/蓝图试管.prefab";
            SaveBasicPrefab(environmentPath, "蓝图实验室");
            SaveBasicPrefab(tubePath, "蓝图试管");

            var result = new CourseBlueprintCompiler().Compile(
                BlueprintSource(environmentPath, tubePath),
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new TestDisciplineProvider("新学科产物")
                });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Stages, Is.EqualTo(new[]
            {
                CourseBlueprintCompilationStage.Read,
                CourseBlueprintCompilationStage.BlueprintValidation,
                CourseBlueprintCompilationStage.RecipeCatalog,
                CourseBlueprintCompilationStage.Expansion,
                CourseBlueprintCompilationStage.Override,
                CourseBlueprintCompilationStage.NormalizedValidation,
                CourseBlueprintCompilationStage.PrefabContract,
                CourseBlueprintCompilationStage.Asset
            }));
            Assert.That(result.Domain.CourseId, Is.EqualTo("蓝图生成测试"));
            Assert.That(result.Presentation, Is.Not.Null);
            Assert.That(
                result.GeneratedArtifacts.Single().Definition.Content,
                Is.EqualTo(System.Text.Encoding.UTF8.GetBytes("新学科产物")));
            Assert.That(result.Summary.ObjectCount, Is.EqualTo(1));
            Assert.That(result.Summary.PlatformRecipeCount,
                Is.GreaterThan(0));
            Assert.That(result.Summary.DisciplineRecipeCount,
                Is.EqualTo(1));
            Assert.That(result.Summary.ErrorCount, Is.EqualTo(0));
            Assert.That(result.ProvenanceByGeneratedItemId, Is.Not.Empty);
            Assert.That(
                result.GetType().GetProperty("ContentHash"),
                Is.Null);
            Assert.That(
                result.GetType().GetProperty("CourseVersion"),
                Is.Null);
            Assert.That(
                result.GetType().GetProperty("SchemaVersion"),
                Is.Null);
        }

        [Test]
        public void 蓝图生成原子更新课程资产和学科产物()
        {
            var environmentPath = TestFolder + "/原子实验室.prefab";
            var tubePath = TestFolder + "/原子试管.prefab";
            SaveBasicPrefab(environmentPath, "原子实验室");
            SaveBasicPrefab(tubePath, "原子试管");
            var compilation = new CourseBlueprintCompiler().Compile(
                BlueprintSource(environmentPath, tubePath),
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new TestDisciplineProvider("第一次可用内容")
                });
            var generatedDirectory = TestFolder + "/Generated";
            var generator = new CourseAssetGenerator();

            Assert.That(
                generator.TryGenerateFromBlueprint(
                    AssetPath,
                    generatedDirectory,
                    compilation,
                    out var diagnostics),
                Is.True);
            Assert.That(diagnostics, Is.Empty);
            Assert.That(
                Directory.GetFiles(
                    TestFolder,
                    "Generated.tmp-*.meta",
                    SearchOption.TopDirectoryOnly),
                Is.Empty,
                "原子生成结束后不应遗留临时目录的 Unity 元文件。");
            var artifactPath =
                Path.Combine(generatedDirectory, "测试学科产物.txt");
            Assert.That(
                File.ReadAllText(artifactPath),
                Is.EqualTo("第一次可用内容"));
            var generatedAsset = AssetDatabase
                .LoadAssetAtPath<CompiledCourseAsset>(AssetPath);
            Assert.That(
                generatedAsset.RequireTextArtifact("测试学科产物"),
                Is.EqualTo("第一次可用内容"));
            var previousDomain = generatedAsset.DomainJson;

            Assert.That(AssetDatabase.DeleteAsset(tubePath), Is.True);
            Assert.That(
                generator.TryGenerateFromBlueprint(
                    AssetPath,
                    generatedDirectory,
                    compilation,
                    out var failedDiagnostics),
                Is.False);
            Assert.That(
                failedDiagnostics.Select(value => value.Code),
                Does.Contain("course.asset.missing"));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(AssetPath)
                    .DomainJson,
                Is.EqualTo(previousDomain));
            Assert.That(
                File.ReadAllText(artifactPath),
                Is.EqualTo("第一次可用内容"));
        }

        private static CompiledCourseDefinition Definition()
        {
            return CompiledCourseDefinition.CreateBasic(
                "最小课程",
                new[]
                {
                    new CourseEntityDefinition(
                        "器材.试管",
                        "资源.试管",
                        new[] { "可抓取" })
                },
                Array.Empty<ActionPolicyDefinition>(),
                Array.Empty<CourseGoalDefinition>(),
                Array.Empty<CourseAssessmentDefinition>());
        }

        private static void SaveBasicPrefab(string path, string name)
        {
            var instance = new GameObject(name);
            instance.AddComponent<CourseEntityView>();
            PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityEngine.Object.DestroyImmediate(instance);
        }

        private static CourseBlueprintSource BlueprintSource(
            string environmentPath,
            string tubePath) =>
            new CourseBlueprintSource(new[]
            {
                new CourseBlueprintFile(
                    "课程.csv",
                    "课程ID,显示名称,学科配方包,环境Prefab\n"
                    + $"蓝图生成测试,蓝图生成测试,测试学科,{environmentPath}\n"),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,Prefab,特征列表,初始位置,初始旋转\n"
                    + $"试管,试管,{tubePath},可夹持,0|0|0,0|0|0\n")
            });

        private sealed class TestDisciplineProvider :
            IRecipePackageProvider
        {
            private readonly string _content;

            public TestDisciplineProvider(string content)
            {
                _content = content;
            }

            public string PackageId => "测试学科";

            public RecipePackage Load() =>
                new RecipePackage(
                    PackageId,
                    RecipeLayer.Discipline,
                    recipes: new[]
                    {
                        new RecipeDefinition(
                            "测试学科.扩展",
                            RecipeMatchKind.SingleEntity,
                            "测试学科特征",
                            "通用.抓取",
                            string.Empty,
                            RecipeSafetyLevel.CourseMayTighten,
                            Array.Empty<string>())
                    },
                    disciplineRecordCompiler:
                    new TestDisciplineRecordCompiler(_content));
        }

        private sealed class TestDisciplineRecordCompiler :
            IDisciplineRecordCompiler
        {
            private readonly string _content;

            public TestDisciplineRecordCompiler(string content)
            {
                _content = content;
            }

            public DisciplineRecordCompilationResult Compile(
                CourseBlueprint blueprint)
            {
                var identity = new GeneratedItemIdentity(
                    "测试学科.记录",
                    blueprint.Course.CourseId,
                    string.Empty,
                    "学科产物",
                    "测试学科产物");
                return new DisciplineRecordCompilationResult(
                    new[]
                    {
                        new NormalizedItem<GeneratedCourseArtifact>(
                            identity,
                            new GeneratedCourseArtifact(
                                "测试学科产物",
                                "测试学科产物.txt",
                                System.Text.Encoding.UTF8.GetBytes(_content)),
                            new[] { blueprint.Course.Source })
                    },
                    Array.Empty<CourseCompilationDiagnostic>());
            }
        }
    }
}
