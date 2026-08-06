using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Chemistry.Authoring;
using VirtualLab.Chemistry.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Catalogs;

namespace VirtualLab.Chemistry.Tests.Authoring
{
    public sealed class ChemistryCourseAuthoringCatalogTests
    {
        private static readonly string[] ExpectedCategories =
        {
            "容器与反应器皿",
            "测量仪器",
            "加热与点火设备",
            "支撑与夹持器材",
            "连接与密封器材",
            "操作工具",
            "药品与实验材料",
            "安全、清洁与废弃设施"
        };

        [Test]
        public void 化学目录提供八类用品且不包含Unity资源引用()
        {
            var module = new ChemistryCourseAuthoringProvider().Load();

            Assert.That(
                module.Categories.Select(value => value.CategoryId),
                Is.EqualTo(ExpectedCategories));
            Assert.That(
                module.Templates,
                Has.Some.Property("TemplateId").EqualTo("试管"));
            Assert.That(
                module.Templates,
                Has.Some.Property("TemplateId").EqualTo("玻璃片"));
            Assert.That(
                module.Templates
                    .SelectMany(value => value.DefaultParameters.Keys)
                    .Any(value =>
                        value.IndexOf("Prefab", StringComparison.OrdinalIgnoreCase)
                            >= 0
                        || value.Contains("预制体")
                        || value.Contains("材质")
                        || value.Contains("音频")),
                Is.False);
        }

        [Test]
        public void 两块玻璃片使用同一模板并通过类型选择器参与覆盖操作()
        {
            var template = new ChemistryCourseAuthoringProvider().Load()
                .Templates.Single(value => value.TemplateId == "玻璃片");

            Assert.That(template.OperationIds, Does.Contain("覆盖"));
            Assert.That(template.ComponentIds, Does.Contain("可覆盖来源"));
        }

        [Test]
        public void 固定用品不声明可抓取而试管夹保留调节能力()
        {
            var templates = new ChemistryCourseAuthoringProvider().Load()
                .Templates.ToDictionary(
                    value => value.TemplateId,
                    StringComparer.Ordinal);

            foreach (var templateId in new[]
                     {
                         "铁架台", "试管架", "升降台", "水槽", "废液缸"
                     })
            {
                Assert.That(
                    templates[templateId].ComponentIds,
                    Does.Not.Contain("可抓取"),
                    templateId);
            }

            Assert.That(
                templates["铁架台试管夹"].ComponentIds,
                Is.SupersetOf(new[]
                {
                    "可上下调节", "可旋转调节", "可夹持"
                }));
        }

        [Test]
        public void 目录加载器拒绝英文生命周期并给出中文修复建议()
        {
            var source = Path.Combine(
                ChemistrySamplePaths.Root,
                "Editor",
                "Authoring",
                "Catalogs");
            var temporary = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab.Chemistry.Catalogs." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                foreach (var file in Directory.GetFiles(source, "*.csv"))
                {
                    File.Copy(file, Path.Combine(temporary, Path.GetFileName(file)));
                }

                var operationsPath = Path.Combine(temporary, "抽象操作.csv");
                File.WriteAllText(
                    operationsPath,
                    File.ReadAllText(operationsPath)
                        .Replace(",即时,", ",Instant,"));

                var result = new CourseAuthoringCatalogCsvLoader().Read(
                    "化学",
                    temporary);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(
                    result.Diagnostics.Any(value =>
                        value.FileName == "抽象操作.csv"
                        && value.Reason.Contains("Instant")
                        && value.Suggestion.Contains("即时")),
                    Is.True);
            }
            finally
            {
                Directory.Delete(temporary, true);
            }
        }
    }
}
