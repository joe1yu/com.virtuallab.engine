using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Recipes;

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
            var authoring = Path.Combine(_directory, "Authoring");
            Directory.CreateDirectory(authoring);
            File.WriteAllText(
                Path.Combine(authoring, "课程.csv"),
                "课程ID,显示名称,学科配方包,实验Prefab\n"
                + "诊断读取测试,诊断读取测试,,环境.prefab\n");
            File.WriteAllText(
                Path.Combine(authoring, "实验对象.csv"),
                "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                + "试管,试管,可夹持,0|0|0,0|0|0\n");
            var nested = Path.Combine(
                authoring,
                "Generated",
                "Diagnostics");
            Directory.CreateDirectory(nested);
            File.WriteAllText(
                Path.Combine(nested, "旧动作策略.csv"),
                "不应被读取\n");

            var result = new CourseBlueprintReader().Read(
                CourseBlueprintSource.FromDirectory(authoring));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Blueprint.Course.CourseId,
                Is.EqualTo("诊断读取测试"));
        }

        private static CourseBlueprintCompilationResult Compile()
        {
            var result = new CourseBlueprintCompiler().Compile(
                new CourseBlueprintSource(new[]
                {
                    new CourseBlueprintFile(
                        "课程.csv",
                        "课程ID,显示名称,学科配方包,实验Prefab\n"
                        + "诊断导出测试,诊断导出测试,,环境.prefab\n"),
                    new CourseBlueprintFile(
                        "实验对象.csv",
                        "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                        + "试管,试管,可夹持,0|0|0,0|0|0\n")
                }),
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());
            Assert.That(result.IsSuccess, Is.True);
            return result;
        }
    }
}
