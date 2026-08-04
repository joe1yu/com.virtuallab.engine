using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseTemplateCreatorTests
    {
        [Test]
        public void CreatesOnlyRequiredTablesAndStandardDirectories()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-CourseTemplate-" + Guid.NewGuid().ToString("N"));
            try
            {
                var result = new CourseTemplateCreator().Create(
                    root,
                    "气体体积测量",
                    "气体体积测量实验",
                    "化学基础",
                    "Assets/课程环境.prefab");

                Assert.That(Directory.Exists(result.CourseDirectory), Is.True);
                Assert.That(
                    Directory.Exists(Path.Combine(result.CourseDirectory, "Prefabs")),
                    Is.False);
                Assert.That(
                    Directory.Exists(Path.Combine(result.CourseDirectory, "Generated")),
                    Is.True);
                CollectionAssert.AreEqual(
                    new[] { "实验对象.csv", "课程.csv" },
                    Directory.GetFiles(result.AuthoringDirectory, "*.csv")
                        .Select(Path.GetFileName)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray());

                var documents = CourseDocumentSet.Load(result.AuthoringDirectory);
                var course = documents.GetRequiredDocument("课程.csv")
                    .Rows.Single();
                Assert.That(course["课程ID"], Is.EqualTo("气体体积测量"));
                Assert.That(course["显示名称"], Is.EqualTo("气体体积测量实验"));
                Assert.That(course["学科配方包"], Is.EqualTo("化学基础"));
                Assert.That(course["操作者实体ID"], Is.EqualTo("学生"));
                Assert.That(
                    course["实验Prefab"],
                    Is.EqualTo("Assets/课程环境.prefab"));
                Assert.That(
                    documents.GetRequiredDocument("实验对象.csv").Headers,
                    Does.Not.Contain("Prefab"));
                Assert.That(
                    documents.GetRequiredDocument("实验对象.csv").Rows,
                    Is.Empty);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void RefusesToOverwriteExistingCourseDirectory()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-CourseTemplate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "已有课程"));
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new CourseTemplateCreator().Create(
                        root,
                        "已有课程",
                        "已有课程",
                        string.Empty,
                        string.Empty));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

    }
}
