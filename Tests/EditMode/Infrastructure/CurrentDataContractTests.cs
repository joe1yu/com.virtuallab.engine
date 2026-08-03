using System.Linq;
using NUnit.Framework;
using VirtualLab.Application;
using VirtualLab.Infrastructure.Persistence;
using VirtualLab.Infrastructure.Reporting;

namespace VirtualLab.Engine.Tests.Infrastructure
{
    public sealed class CurrentDataContractTests
    {
        [Test]
        public void 当前存档契约不包含版本和内容指纹()
        {
            var forbidden = new[]
            {
                "SchemaVersion",
                "EngineVersion",
                "ConfigurationHash",
                "StateHash",
                "AdapterStateHash"
            };
            var archiveProperties = typeof(SessionArchive)
                .GetProperties()
                .Select(value => value.Name)
                .ToArray();

            foreach (var propertyName in forbidden)
            {
                Assert.That(archiveProperties, Does.Not.Contain(propertyName));
            }
            Assert.That(
                typeof(ExperimentSession).GetProperty("StateHash"),
                Is.Null,
                "领域会话不得保留仅供旧存档校验使用的科学状态指纹。");
        }

        [Test]
        public void 当前报告契约不包含版本和内容指纹()
        {
            var forbidden = new[]
            {
                "SchemaVersion",
                "EngineVersion",
                "ConfigurationHash",
                "StateHash"
            };
            var reportProperties = typeof(ExperimentReport)
                .GetProperties()
                .Select(value => value.Name)
                .ToArray();
            var metadataProperties = typeof(ReportMetadata)
                .GetProperties()
                .Select(value => value.Name)
                .ToArray();

            foreach (var propertyName in forbidden)
            {
                Assert.That(reportProperties, Does.Not.Contain(propertyName));
                Assert.That(metadataProperties, Does.Not.Contain(propertyName));
            }
        }
    }
}
