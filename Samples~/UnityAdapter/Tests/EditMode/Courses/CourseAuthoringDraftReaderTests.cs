using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Drafts;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseAuthoringDraftReaderTests
    {
        [Test]
        public void 新课程十表读取为职责分离的草稿()
        {
            var result = new CourseAuthoringDraftReader().Read(Source());

            Assert.That(result.IsSuccess, Is.True, Diagnostics(result));
            Assert.That(result.Draft.Course.CourseId, Is.EqualTo("演示课程"));
            Assert.That(
                result.Draft.Objects.Single().EntityType,
                Is.EqualTo("试管"));
            Assert.That(
                result.Draft.TeachingItems.Single().Continuation,
                Is.EqualTo("纠正后继续"));
            Assert.That(result.Draft.TeachingConditions.Count, Is.EqualTo(1));
            Assert.That(
                result.Draft.AcceptanceRecords.Select(value => value.Order),
                Is.EqualTo(new[] { 1, 2 }));
            Assert.That(
                result.Draft.OperationOverrides.Single().Source.FileName,
                Is.EqualTo(CourseAuthoringTableNames.OperationOverrides));
        }

        [Test]
        public void 十张表名与表头只由草稿模式集中提供()
        {
            Assert.That(CourseAuthoringSchema.Tables.Count, Is.EqualTo(10));
            Assert.That(
                CourseAuthoringSchema.ByFileName[
                    CourseAuthoringTableNames.AcceptanceScenarios].Columns,
                Is.EqualTo(new[]
                {
                    "场景标识", "顺序", "记录类型", "操作", "来源", "目标",
                    "参数名", "参数值", "断言类型", "对象", "事实", "比较",
                    "期望值", "单位"
                }));
        }

        [TestCase("交互规则.csv")]
        [TestCase("学科过程.csv")]
        [TestCase("实验流程.csv")]
        [TestCase("表现覆盖.csv")]
        public void 旧课程表不属于新草稿协议(string fileName)
        {
            var result = new CourseAuthoringDraftReader().Read(
                Source().Append(fileName, "旧列\n旧值\n"));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(
                result.Diagnostics.Single().Code,
                Is.EqualTo("draft.file.unsupported"));
            Assert.That(result.Diagnostics.Single().FileName,
                Is.EqualTo(fileName));
        }

        [Test]
        public void 旧标识表头不会转换成新草稿协议()
        {
            var result = new CourseAuthoringDraftReader().Read(
                Source().Replace(
                    CourseAuthoringTableNames.Course,
                    "课程ID,显示名称,学科配方包,操作者实体标识,实验预制体\n"
                    + "演示课程,演示课程,化学基础,学生,课程资源/演示实验.prefab\n"));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics.Any(value =>
                    value.Code == "draft.header.invalid"),
                Is.True);
        }

        private static CourseBlueprintSource Source() =>
            CourseBlueprintSource.FromDirectory(Path.GetFullPath(Path.Combine(
                "Packages",
                "com.virtuallab.engine",
                "Samples~",
                "UnityAdapter",
                "Tests",
                "EditMode",
                "Fixtures",
                "Courses",
                "新课程草稿")));

        private static string Diagnostics(CourseAuthoringDraftReadResult result) =>
            string.Join("\n", result.Diagnostics.Select(value =>
                $"{value.Code}: {value.Reason}"));
    }
}
