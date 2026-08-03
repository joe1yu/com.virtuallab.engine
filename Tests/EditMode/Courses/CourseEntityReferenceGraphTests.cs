using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseEntityReferenceGraphTests
    {
        [Test]
        public void RenameUpdatesExplicitReferencesButDoesNotReplaceMessages()
        {
            var directory = CreateCourseDirectory();
            try
            {
                Write(
                    directory,
                    "实验对象.csv",
                    "实体ID,显示名称,Prefab,特征列表,初始位置,初始旋转\r\n"
                    + "试管,试管,,可抓取,0|0|0,0|0|0\r\n"
                    + "烧杯,烧杯,,,0|0|0,0|0|0\r\n");
                Write(
                    directory,
                    "交互规则.csv",
                    "交互ID,来源,目标,拒绝文案\r\n"
                    + "禁止抓取,试管,烧杯,试管不能抓取\r\n");
                Write(
                    directory,
                    "表现覆盖.csv",
                    "覆盖ID,触发来源,触发目标,对象或状态,参数值\r\n"
                    + "提示,试管,烧杯,试管,试管正在变化\r\n");
                var documents = CourseDocumentSet.Load(directory);
                var graph = new CourseEntityReferenceGraph();

                var changed = graph.Rename(documents, "试管", "大试管");

                Assert.That(changed.Count, Is.EqualTo(4));
                Assert.That(
                    documents.GetRequiredDocument("实验对象.csv")
                        .Rows.First()["实体ID"],
                    Is.EqualTo("大试管"));
                var interaction = documents.GetRequiredDocument("交互规则.csv")
                    .Rows.Single();
                Assert.That(interaction["来源"], Is.EqualTo("大试管"));
                Assert.That(interaction["拒绝文案"], Is.EqualTo("试管不能抓取"));
                var presentation = documents.GetRequiredDocument("表现覆盖.csv")
                    .Rows.Single();
                Assert.That(presentation["触发来源"], Is.EqualTo("大试管"));
                Assert.That(presentation["对象或状态"], Is.EqualTo("大试管"));
                Assert.That(presentation["参数值"], Is.EqualTo("试管正在变化"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RenameUpdatesExperimentFlowEntityReferences()
        {
            var directory = CreateCourseDirectory();
            try
            {
                Write(
                    directory,
                    "实验对象.csv",
                    "实体ID,显示名称\r\n试管,试管\r\n烧杯,烧杯\r\n");
                Write(
                    directory,
                    "实验流程.csv",
                    "流程ID,步骤ID,记录类型,来源,目标,主体,提示文案\r\n"
                    + "制取,投料,操作,试管,烧杯,,请拿起试管\r\n"
                    + "制取,完成,断言,,,试管,试管状态正确\r\n");
                var documents = CourseDocumentSet.Load(directory);

                var changed = new CourseEntityReferenceGraph().Rename(
                    documents,
                    "试管",
                    "大试管");

                Assert.That(changed.Count, Is.EqualTo(3));
                var flow = documents.GetRequiredDocument("实验流程.csv");
                Assert.That(flow.Rows.First()["来源"], Is.EqualTo("大试管"));
                Assert.That(flow.Rows.Last()["主体"], Is.EqualTo("大试管"));
                Assert.That(flow.Rows.First()["提示文案"], Is.EqualTo("请拿起试管"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SnapshotRestoresAllChangedCourseTablesTogether()
        {
            var directory = CreateCourseDirectory();
            try
            {
                Write(
                    directory,
                    "实验对象.csv",
                    "实体ID,显示名称\r\n试管,试管\r\n烧杯,烧杯\r\n");
                Write(
                    directory,
                    "验收场景.csv",
                    "场景ID,来源,目标\r\n连接,试管,烧杯\r\n");
                var documents = CourseDocumentSet.Load(directory);
                var before = documents.CaptureSnapshot();

                new CourseEntityReferenceGraph().Rename(
                    documents,
                    "试管",
                    "大试管");
                documents.Restore(before);

                Assert.That(
                    documents.GetRequiredDocument("实验对象.csv")
                        .Rows.First()["实体ID"],
                    Is.EqualTo("试管"));
                Assert.That(
                    documents.GetRequiredDocument("验收场景.csv")
                        .Rows.Single()["来源"],
                    Is.EqualTo("试管"));
                Assert.That(documents.IsModified, Is.False);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ExternalReferencesCanBlockObjectDeletion()
        {
            var directory = CreateCourseDirectory();
            try
            {
                Write(
                    directory,
                    "实验对象.csv",
                    "实体ID,显示名称\r\n试管,试管\r\n");
                Write(
                    directory,
                    "学科过程.csv",
                    "配置ID,来源,目标\r\n加热,试管,\r\n");
                var documents = CourseDocumentSet.Load(directory);

                var references = new CourseEntityReferenceGraph()
                    .FindExternalReferences(documents, "试管");

                Assert.That(references.Count, Is.EqualTo(1));
                Assert.That(references.Single().FileName, Is.EqualTo("学科过程.csv"));
                Assert.That(references.Single().ColumnName, Is.EqualTo("来源"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SaveModifiedPersistsCrossTableRenameTogether()
        {
            var directory = CreateCourseDirectory();
            try
            {
                Write(
                    directory,
                    "实验对象.csv",
                    "实体ID,显示名称\r\n试管,试管\r\n");
                Write(
                    directory,
                    "验收场景.csv",
                    "场景ID,来源,目标\r\n抓取,试管,\r\n");
                var documents = CourseDocumentSet.Load(directory);
                new CourseEntityReferenceGraph().Rename(
                    documents,
                    "试管",
                    "大试管");

                documents.SaveModified();

                var reloaded = CourseDocumentSet.Load(directory);
                Assert.That(reloaded.IsModified, Is.False);
                Assert.That(
                    reloaded.GetRequiredDocument("实验对象.csv")
                        .Rows.Single()["实体ID"],
                    Is.EqualTo("大试管"));
                Assert.That(
                    reloaded.GetRequiredDocument("验收场景.csv")
                        .Rows.Single()["来源"],
                    Is.EqualTo("大试管"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ExternalChangeInAnyTableBlocksAllWritesBeforeSaveStarts()
        {
            var directory = CreateCourseDirectory();
            try
            {
                Write(
                    directory,
                    "实验对象.csv",
                    "实体ID,显示名称\r\n试管,试管\r\n");
                Write(
                    directory,
                    "验收场景.csv",
                    "场景ID,来源,目标\r\n抓取,试管,\r\n");
                var documents = CourseDocumentSet.Load(directory);
                new CourseEntityReferenceGraph().Rename(
                    documents,
                    "试管",
                    "大试管");
                Write(
                    directory,
                    "验收场景.csv",
                    "场景ID,来源,目标\r\n观察,试管,\r\n");

                Assert.Throws<InvalidOperationException>(
                    () => documents.SaveModified());

                StringAssert.Contains(
                    "试管,试管",
                    File.ReadAllText(Path.Combine(directory, "实验对象.csv")));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateCourseDirectory()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-CourseDocuments-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void Write(
            string directory,
            string fileName,
            string content) =>
            File.WriteAllText(
                Path.Combine(directory, fileName),
                content,
                new UTF8Encoding(false));
    }
}
