using System;
using System.Collections.Generic;
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
        public void 课程资产保存定义和文本产物()
        {
            var asset = ScriptableObject.CreateInstance<CompiledCourseAsset>();
            asset.SetData(
                "最小课程",
                CourseAssetDecoder.EncodeDomain(Definition()),
                "{}",
                new[]
                {
                    new CourseTextArtifactBinding(
                        "化学运行配置",
                        "{\"物质\":[]}")
                });

            Assert.That(asset.CourseId, Is.EqualTo("最小课程"));
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
        }

        [Test]
        public void 课程资产将领域表现和文本产物收敛为单一压缩载荷()
        {
            var domainJson = "{\"内容\":\"" + new string('中', 20000) + "\"}";
            var presentationJson =
                "{\"表现\":\"" + new string('现', 10000) + "\"}";
            var artifactContent = new string('产', 5000);
            var asset = ScriptableObject.CreateInstance<CompiledCourseAsset>();
            asset.SetData(
                "压缩课程",
                domainJson,
                presentationJson,
                new[]
                {
                    new CourseTextArtifactBinding("学科产物", artifactContent)
                });
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();

            var serialized = File.ReadAllText(AssetPath);
            var uncompressedBytes = System.Text.Encoding.UTF8.GetByteCount(
                domainJson + presentationJson + artifactContent);
            Assert.That(serialized, Does.Contain("compiledPayload:"));
            Assert.That(serialized, Does.Not.Contain("domainJson:"));
            Assert.That(serialized, Does.Not.Contain("presentationJson:"));
            Assert.That(serialized, Does.Not.Contain("textArtifacts:"));
            Assert.That(serialized, Does.Not.Contain("resourceBindings:"));
            Assert.That(serialized, Does.Not.Contain("environmentPrefab:"));
            Assert.That(
                new FileInfo(AssetPath).Length,
                Is.LessThan(uncompressedBytes / 2),
                "重复度高的课程配置应显著小于未压缩内容。");
            Assert.That(asset.DomainJson, Is.EqualTo(domainJson));
            Assert.That(asset.PresentationJson, Is.EqualTo(presentationJson));
            Assert.That(
                asset.RequireTextArtifact("学科产物"),
                Is.EqualTo(artifactContent));
        }

        [Test]
        public void 课程资产载荷损坏时要求重新生成而不是静默读取错误数据()
        {
            var asset = ScriptableObject.CreateInstance<CompiledCourseAsset>();
            asset.SetData(
                "损坏载荷课程",
                "{}",
                "{}");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("compiledPayload").stringValue =
                "不是有效的压缩载荷";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(
                () => _ = asset.DomainJson,
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("重新生成课程资产"));

            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void 固定路径重复生成保留GUID并更新载荷()
        {
            var generator = new CourseAssetGenerator();
            generator.GenerateOrUpdate(
                AssetPath,
                Definition(),
                "{}");
            var firstGuid = AssetDatabase.AssetPathToGUID(AssetPath);

            generator.GenerateOrUpdate(
                AssetPath,
                Definition(),
                "{\"表现\":\"新\"}");
            var asset = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                AssetPath);

            Assert.That(AssetDatabase.AssetPathToGUID(AssetPath), Is.EqualTo(firstGuid));
            Assert.That(asset.PresentationJson, Is.EqualTo("{\"表现\":\"新\"}"));
        }

        [Test]
        public void 编译错误不会覆盖上一次可用资产()
        {
            var generator = new CourseAssetGenerator();
            generator.GenerateOrUpdate(
                AssetPath,
                Definition(),
                "{\"状态\":\"可用\"}");
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
                    "{}"),
                Is.False);

            var asset = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                AssetPath);
            Assert.That(asset.PresentationJson, Is.EqualTo("{\"状态\":\"可用\"}"));
        }

        [Test]
        public void 蓝图编译阶段固定且成功结果包含运行时内容摘要和来源链()
        {
            var experimentPath = TestFolder + "/蓝图实验.prefab";
            SaveExperimentPrefab(experimentPath, "蓝图实验", "试管");

            var result = new CourseBlueprintCompiler().Compile(
                BlueprintSource(experimentPath),
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
            var experimentPath = TestFolder + "/原子实验.prefab";
            SaveExperimentPrefab(experimentPath, "原子实验", "试管");
            var compilation = new CourseBlueprintCompiler().Compile(
                BlueprintSource(experimentPath),
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

            Assert.That(AssetDatabase.DeleteAsset(experimentPath), Is.True);
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
                        new[] { "可抓取" })
                },
                Array.Empty<ActionPolicyDefinition>(),
                Array.Empty<CourseGoalDefinition>(),
                Array.Empty<CourseAssessmentDefinition>());
        }

        private static void SaveExperimentPrefab(
            string path,
            string name,
            string entityId)
        {
            var root = new GameObject(name);
            var entity = new GameObject(entityId);
            entity.transform.SetParent(root.transform, false);
            entity.AddComponent<BoxCollider>();
            entity.AddComponent<CourseEntityView>().Configure(entityId);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static CourseBlueprintSource BlueprintSource(
            string experimentPath) =>
            new CourseBlueprintSource(new[]
            {
                new CourseBlueprintFile(
                    "课程.csv",
                    "课程ID,显示名称,学科配方包,实验Prefab\n"
                    + $"蓝图生成测试,蓝图生成测试,测试学科,{experimentPath}\n"),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                    + "试管,试管,可夹持,0|0|0,0|0|0\n")
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

            public IReadOnlyList<string> RequiredRuntimeModuleIds { get; } =
                new[] { CourseModuleIds.Core };

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
