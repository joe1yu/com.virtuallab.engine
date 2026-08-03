using System;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Normalized;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class NormalizedCourseValidatorTests
    {
        [Test]
        public void 动作规则状态变化事件表现状态效果和评价都必须有来源()
        {
            var model = new NormalizedCourseModel(
                "来源测试",
                entities: new[]
                {
                    Item(
                        "实体",
                        "试管",
                        new NormalizedEntityDefinition("试管"),
                        true)
                },
                actions: new[]
                {
                    Item(
                        "动作策略",
                        "动作",
                        new NormalizedActionDefinition(
                            "策略.抓取",
                            "抓取",
                            "试管",
                            string.Empty,
                            Array.Empty<string>(),
                            string.Empty,
                            string.Empty),
                        false)
                },
                rules: new[]
                {
                    Item(
                        "规则",
                        "规则",
                        new NormalizedRuleDefinition(
                            "规则.存在",
                            "来源对象存在",
                            "等于",
                            "true",
                            string.Empty),
                        false)
                },
                stateChanges: new[]
                {
                    Item(
                        "状态变化",
                        "状态变化",
                        new NormalizedStateChangeDefinition(
                            "状态变化.持有",
                            "设置关系"),
                        false)
                },
                domainEvents: new[]
                {
                    Item(
                        "领域事件",
                        "事件",
                        new NormalizedDomainEventDefinition(
                            "事件.抓取",
                            "抓取完成"),
                        false)
                },
                presentationStates: new[]
                {
                    Item(
                        "表现状态",
                        "状态",
                        new NormalizedPresentationStateDefinition(
                            "状态.被持有",
                            "试管",
                            Array.Empty<string>()),
                        false)
                },
                presentationEffects: new[]
                {
                    Item(
                        "表现效果",
                        "效果",
                        new NormalizedPresentationEffectDefinition(
                            "效果.跟随",
                            "transform.follow"),
                        false)
                },
                evaluations: new[]
                {
                    Item(
                        "教学评价",
                        "评价",
                        new NormalizedEvaluationDefinition(
                            "评价.抓取",
                            Array.Empty<string>()),
                        false)
                },
                knownOperationIds: new[] { "设置关系" },
                knownPresentationProtocolIds: new[] { "transform.follow" });

            var result = new NormalizedCourseValidator().Validate(model);
            var missing = result.Diagnostics
                .Where(value => value.Code == "normalized.provenance.missing")
                .ToArray();

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(missing, Has.Length.EqualTo(7));
            Assert.That(missing.All(value => value.Provenance != null), Is.True);
        }

        [Test]
        public void 生成身份由五个结构化分量确定且不使用内容哈希()
        {
            var first = new GeneratedItemIdentity(
                "通用.抓取",
                "大试管",
                "学生",
                "动作策略",
                "抓取");
            var same = new GeneratedItemIdentity(
                "通用.抓取",
                "大试管",
                "学生",
                "动作策略",
                "抓取");
            var changed = new GeneratedItemIdentity(
                "通用.抓取",
                "大试管",
                "学生",
                "动作策略",
                "释放");

            Assert.That(first.GeneratedItemId, Is.EqualTo(same.GeneratedItemId));
            Assert.That(first.GeneratedItemId, Is.Not.EqualTo(changed.GeneratedItemId));
            Assert.That(first.GeneratedItemId, Does.Contain("通用.抓取"));
            Assert.That(first.GeneratedItemId, Does.Contain("大试管"));
            Assert.That(first.GeneratedItemId, Does.Contain("动作策略"));
        }

        [Test]
        public void 引用重复单位操作表现协议和不可读组ID统一返回诊断()
        {
            var model = new NormalizedCourseModel(
                "错误课程",
                entities: new[]
                {
                    Item("实体", "甲", new NormalizedEntityDefinition("试管"), true),
                    Item("实体", "乙", new NormalizedEntityDefinition("试管"), true)
                },
                actions: new[]
                {
                    Item(
                        "动作策略",
                        "动作",
                        new NormalizedActionDefinition(
                            "策略.错误",
                            "抓取",
                            "不存在实体",
                            string.Empty,
                            new[] { "规则.不存在" },
                            "result-group-1",
                            "presentation-group-1"),
                        true)
                },
                rules: new[]
                {
                    Item(
                        "规则",
                        "规则",
                        new NormalizedRuleDefinition(
                            "规则.单位",
                            "来源对象温度",
                            "小于",
                            "50",
                            "未知单位"),
                        true)
                },
                stateChanges: new[]
                {
                    Item(
                        "状态变化",
                        "变化",
                        new NormalizedStateChangeDefinition(
                            "变化.错误",
                            "domain.unknown"),
                        true)
                },
                actionResultGroups: new[]
                {
                    Item(
                        "动作结果组",
                        "结果组",
                        new NormalizedActionResultGroupDefinition(
                            "result-group-1",
                            new[] { "变化.不存在" },
                            new[] { "事件.不存在" }),
                        true)
                },
                presentationEffects: new[]
                {
                    Item(
                        "表现效果",
                        "效果",
                        new NormalizedPresentationEffectDefinition(
                            "效果.未知",
                            "presentation.unknown"),
                        true)
                },
                presentationGroups: new[]
                {
                    Item(
                        "表现组",
                        "表现组",
                        new NormalizedPresentationGroupDefinition(
                            "presentation-group-1",
                            new[] { "效果.不存在" }),
                        true)
                },
                knownUnitIds: new[] { "摄氏度" },
                knownOperationIds: new[] { "设置关系" },
                knownPresentationProtocolIds: new[] { "transform.follow" });

            var result = new NormalizedCourseValidator().Validate(model);
            var codes = result.Diagnostics.Select(value => value.Code);

            Assert.That(codes, Does.Contain("normalized.id.duplicate"));
            Assert.That(codes, Does.Contain("normalized.reference.entity-missing"));
            Assert.That(codes, Does.Contain("normalized.reference.rule-missing"));
            Assert.That(codes, Does.Contain("normalized.reference.mutation-missing"));
            Assert.That(codes, Does.Contain("normalized.reference.event-missing"));
            Assert.That(codes, Does.Contain("normalized.reference.effect-missing"));
            Assert.That(codes, Does.Contain("normalized.unit.unknown"));
            Assert.That(codes, Does.Contain("normalized.operation.unknown"));
            Assert.That(codes, Does.Contain("normalized.presentation-protocol.unknown"));
            Assert.That(codes, Does.Contain("normalized.group-id.unreadable"));
        }

        [Test]
        public void 相同输入顺序不同产生相同的规范化排序()
        {
            var firstEntity = Item(
                "实体",
                "乙",
                new NormalizedEntityDefinition("乙对象"),
                true);
            var secondEntity = Item(
                "实体",
                "甲",
                new NormalizedEntityDefinition("甲对象"),
                true);

            var normal = new NormalizedCourseModel(
                "确定性课程",
                entities: new[] { firstEntity, secondEntity });
            var reversed = new NormalizedCourseModel(
                "确定性课程",
                entities: new[] { secondEntity, firstEntity });

            Assert.That(
                normal.Entities.Select(value => value.GeneratedItemId),
                Is.EqualTo(reversed.Entities.Select(value =>
                    value.GeneratedItemId)));
            Assert.That(
                normal.ProvenanceByGeneratedItemId.Keys,
                Is.EqualTo(reversed.ProvenanceByGeneratedItemId.Keys));
            Assert.That(
                new NormalizedCourseValidator().Validate(normal).IsSuccess,
                Is.True);
        }

        private static NormalizedItem<T> Item<T>(
            string generatedType,
            string localKey,
            T definition,
            bool withSource)
            where T : INormalizedDefinition
        {
            var identity = new GeneratedItemIdentity(
                "通用.测试",
                "试管",
                string.Empty,
                generatedType,
                localKey);
            return new NormalizedItem<T>(
                identity,
                definition,
                withSource
                    ? new[] { Source(localKey) }
                    : Array.Empty<ConfigurationSource>());
        }

        private static ConfigurationSource Source(string id) =>
            new ConfigurationSource(
                ConfigurationLayer.Platform,
                "平台通用",
                "配方.csv",
                2,
                1,
                id);
    }
}
