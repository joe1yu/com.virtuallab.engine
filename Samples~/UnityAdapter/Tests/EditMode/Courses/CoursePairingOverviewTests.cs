using System;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CoursePairingOverviewTests
    {
        [Test]
        public void 按对方对象和配合方向归组编译后的双对象操作()
        {
            var actions = new[]
            {
                Action("开始倾倒", "试管", "烧杯"),
                Action("结束倾倒", "试管", "烧杯"),
                Action("开始加热", "试管", "酒精灯"),
                Action("接收产物", "药匙", "试管", "禁止"),
                Action("观察", "试管", string.Empty),
                Action("无关操作", "木炭", "酒精灯")
            };

            var result = CoursePairingOverview.Build(actions, "试管");

            Assert.That(result.Count, Is.EqualTo(3));
            var beaker = result.Single(value =>
                value.OtherEntityId == "烧杯");
            Assert.That(beaker.SelectedIsSource, Is.True);
            Assert.That(
                beaker.Operations.Select(value => value.OperationName),
                Is.EquivalentTo(new[] { "开始倾倒", "结束倾倒" }));

            var spoon = result.Single(value =>
                value.OtherEntityId == "药匙");
            Assert.That(spoon.SelectedIsSource, Is.False);
            Assert.That(spoon.Operations.Single().PolicyEffect, Is.EqualTo("禁止"));
        }

        private static NormalizedItem<NormalizedActionDefinition> Action(
            string operationName,
            string sourceEntityId,
            string targetEntityId,
            string policyEffect = "允许")
        {
            return new NormalizedItem<NormalizedActionDefinition>(
                new GeneratedItemIdentity(
                    "测试.配方",
                    sourceEntityId,
                    targetEntityId,
                    "动作策略",
                    operationName),
                new NormalizedActionDefinition(
                    "策略." + operationName,
                    "test.action",
                    operationName,
                    SemanticActionLifecycle.Instant,
                    "即时执行",
                    SemanticActionPhase.Complete,
                    sourceEntityId,
                    targetEntityId,
                    Array.Empty<string>(),
                    string.Empty,
                    string.Empty,
                    policyEffect: policyEffect),
                Array.Empty<
                    VirtualLab.Unity.Authoring.Diagnostics.ConfigurationSource>());
        }
    }
}
