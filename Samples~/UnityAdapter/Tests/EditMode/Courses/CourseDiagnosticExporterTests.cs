using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Drafts;
using VirtualLab.Unity.Authoring.Recipes;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseDiagnosticExporterTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-课程诊断-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void 导出六份只读中文诊断且不包含版本或哈希()
        {
            var compilation = Compile();
            var target = Path.Combine(_directory, "Generated", "Diagnostics");

            new CourseDiagnosticExporter().Export(target, compilation);

            var names = Directory.GetFiles(target)
                .Select(Path.GetFileName)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            Assert.That(names, Is.EqualTo(new[]
            {
                "动作策略.csv",
                "来源链.csv",
                "条件组.csv",
                "状态变化.csv",
                "编译报告.txt",
                "表现状态.csv"
            }.OrderBy(value => value, StringComparer.Ordinal)));
            foreach (var path in Directory.GetFiles(target))
            {
                var content = File.ReadAllText(path);
                Assert.That(
                    content.Split('\n')[0].TrimEnd('\r'),
                    Is.EqualTo("只读诊断，不是课程编译输入"));
                Assert.That(content, Does.Not.Contain("ContentHash"));
                Assert.That(content, Does.Not.Contain("内容哈希"));
                Assert.That(content, Does.Not.Contain("CourseVersion"));
                Assert.That(content, Does.Not.Contain("SchemaVersion"));
                Assert.That(content, Does.Not.Contain("Addressables"));
            }
        }

        [Test]
        public void 读取作者目录时忽略嵌套诊断目录()
        {
            var created = new CourseAuthoringDraftCreator().Create(
                _directory,
                "诊断读取测试",
                "诊断读取测试",
                string.Empty,
                "环境.prefab");
            var authoring = created.AuthoringDirectory;
            var nested = Path.Combine(
                authoring,
                "Generated",
                "Diagnostics");
            Directory.CreateDirectory(nested);
            File.WriteAllText(
                Path.Combine(nested, "旧动作策略.csv"),
                "不应被读取\n");

            var result = new CourseAuthoringDraftReader().Read(
                CourseBlueprintSource.FromDirectory(authoring));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Draft.Course.CourseId,
                Is.EqualTo("诊断读取测试"));
        }

        private static CourseBlueprintCompilationResult Compile()
        {
            var result = new CourseBlueprintCompiler().Compile(
                CourseBlueprintTestFactory.Blueprint(
                    CourseBlueprintTestFactory.Object(
                        "试管", new[] { "可夹持" })),
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());
            Assert.That(result.IsSuccess, Is.True);
            return result;
        }
    }
}
