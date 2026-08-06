using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Csv;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class EditableCsvDocumentTests
    {
        [Test]
        public void RoundTripPreservesUnknownColumnsOrderAndEscapedValues()
        {
            const string content =
                "实体ID,显示名称,自定义列\r\n"
                + "试管,\"试管,大型\",\"第一行\n第二行\"\r\n";

            var document = EditableCsvDocument.Parse("实验对象.csv", content);
            var serialized = document.ToCsv();
            var reparsed = EditableCsvDocument.Parse("实验对象.csv", serialized);

            CollectionAssert.AreEqual(
                new[] { "实体ID", "显示名称", "自定义列" },
                reparsed.Headers);
            Assert.That(reparsed.Rows.Single()["显示名称"], Is.EqualTo("试管,大型"));
            Assert.That(
                reparsed.Rows.Single()["自定义列"],
                Is.EqualTo("第一行\n第二行"));
        }

        [Test]
        public void EnsureColumnAddsColumnWithoutChangingExistingCells()
        {
            var document = EditableCsvDocument.Parse(
                "实验对象.csv",
                "实体ID,显示名称\r\n试管,大试管\r\n");

            document.EnsureColumn("参数.端口.出口.兼容组");

            CollectionAssert.AreEqual(
                new[] { "实体ID", "显示名称", "参数.端口.出口.兼容组" },
                document.Headers);
            Assert.That(document.Rows.Single()["实体ID"], Is.EqualTo("试管"));
            Assert.That(
                document.Rows.Single()["参数.端口.出口.兼容组"],
                Is.Empty);
        }

        [Test]
        public void SaveRefusesToOverwriteExternalChange()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-EditableCsv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "实验对象.csv");
            try
            {
                File.WriteAllText(
                    path,
                    "实体ID,显示名称\r\n试管,试管\r\n",
                    new UTF8Encoding(false));
                var document = EditableCsvDocument.Load(path);
                document.Rows.Single()["显示名称"] = "大试管";

                File.AppendAllText(path, "烧杯,烧杯\r\n", new UTF8Encoding(false));

                Assert.That(document.HasExternalChanges(), Is.True);
                Assert.Throws<InvalidOperationException>(() => document.SaveAtomic());
                StringAssert.Contains("烧杯,烧杯", File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void DetectsSameLengthExternalChangeWithoutPersistedFingerprint()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-EditableCsv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "实验对象.csv");
            try
            {
                File.WriteAllText(
                    path,
                    "实体ID,显示名称\r\n试管,试管\r\n",
                    new UTF8Encoding(false));
                var document = EditableCsvDocument.Load(path);

                // “试管”和“烧杯”的 UTF-8 长度相同，用于覆盖长度检测的盲区。
                File.WriteAllText(
                    path,
                    "实体ID,显示名称\r\n烧杯,烧杯\r\n",
                    new UTF8Encoding(false));

                Assert.That(document.HasExternalChanges(), Is.True);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void RestoreSupportsUndoWithoutChangingSavedBaseline()
        {
            var document = EditableCsvDocument.Parse(
                "实验对象.csv",
                "实体ID,显示名称\r\n试管,试管\r\n");
            var original = document.ToCsv();
            document.Rows.Single()["显示名称"] = "大试管";

            Assert.That(document.IsModified, Is.True);
            document.Restore(original);

            Assert.That(document.IsModified, Is.False);
            Assert.That(document.Rows.Single()["显示名称"], Is.EqualTo("试管"));
        }

        [Test]
        public void SaveWritesUtf8WithoutBomAndCanBeReloaded()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab-EditableCsv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "实验对象.csv");
            try
            {
                File.WriteAllText(
                    path,
                    "实体ID,显示名称\r\n试管,试管\r\n",
                    new UTF8Encoding(false));
                var document = EditableCsvDocument.Load(path);
                document.Rows.Single()["显示名称"] = "大试管";

                document.SaveAtomic();

                var bytes = File.ReadAllBytes(path);
                Assert.That(
                    bytes.Take(3).ToArray(),
                    Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
                Assert.That(
                    EditableCsvDocument.Load(path).Rows.Single()["显示名称"],
                    Is.EqualTo("大试管"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
