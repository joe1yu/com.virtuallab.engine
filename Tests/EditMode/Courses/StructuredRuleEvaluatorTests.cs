using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Interaction.Courses;
using VirtualLab.Spatial.Courses;
using VirtualLab.Teaching.Courses;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class StructuredRuleEvaluatorTests
    {
        private static readonly StructuredFactField NumericField =
            new StructuredFactField("测试数值事实");

        [Test]
        public void 规则收集全部失败原因并选择首个主要原因()
        {
            var evaluator = new StructuredRuleEvaluator(
                new IStructuredFactReader[]
                {
                    new StubFactReader(
                        CoreStructuredFactFields.来源对象能力,
                        StructuredValue.FromTextList(new[] { "可倾倒" })),
                    new StubFactReader(
                        InteractionStructuredFactFields.来源对象持有者,
                        StructuredValue.FromText("学生"))
                });
            var decision = evaluator.Evaluate(
                new[]
                {
                    Requirement(
                        20,
                        InteractionStructuredFactFields.来源对象持有者,
                        StructuredRuleOperator.为空,
                        StructuredValue.Null(),
                        "实体已被持有"),
                    Requirement(
                        10,
                        CoreStructuredFactFields.来源对象能力,
                        StructuredRuleOperator.包含,
                        StructuredValue.FromText("可抓取"),
                        "实体不可抓取")
                },
                Context());

            Assert.That(decision.IsAccepted, Is.False);
            Assert.That(
                decision.PrimaryRejectionCode,
                Is.EqualTo("实体不可抓取"));
            Assert.That(
                decision.RejectionCodes,
                Is.EqualTo(new[] { "实体不可抓取", "实体已被持有" }));
        }

        [Test]
        public void 数值运算符使用类型化事实而不是反射属性()
        {
            var evaluator = new StructuredRuleEvaluator(
                new IStructuredFactReader[]
                {
                    new StubFactReader(
                        NumericField,
                        StructuredValue.FromNumber(25d)),
                    new StubFactReader(
                        SpatialStructuredFactFields.对象间距离,
                        StructuredValue.FromNumber(0.05d))
                });

            var accepted = evaluator.Evaluate(
                new[]
                {
                    Requirement(
                        10,
                        NumericField,
                        StructuredRuleOperator.小于等于,
                        StructuredValue.FromNumber(30d),
                        "温度过高"),
                    Requirement(
                        20,
                        SpatialStructuredFactFields.对象间距离,
                        StructuredRuleOperator.小于,
                        StructuredValue.FromNumber(0.1d),
                        "距离过远")
                },
                Context());

            Assert.That(accepted.IsAccepted, Is.True);
            Assert.That(accepted.RejectionCodes, Is.Empty);
        }

        [Test]
        public void 文本集合同时支持包含和不包含判断()
        {
            var field = new StructuredFactField("测试状态集合");
            var evaluator = new StructuredRuleEvaluator(new[]
            {
                new StubFactReader(
                    field,
                    StructuredValue.FromTextList(new[] { "瓶盖已打开" }))
            });

            var decision = evaluator.Evaluate(
                new[]
                {
                    Requirement(
                        10,
                        field,
                        StructuredRuleOperator.包含,
                        StructuredValue.FromText("瓶盖已打开"),
                        "瓶盖尚未打开"),
                    Requirement(
                        20,
                        field,
                        StructuredRuleOperator.不包含,
                        StructuredValue.FromText("瓶盖已关闭"),
                        "瓶盖仍然关闭")
                },
                Context());

            Assert.That(decision.IsAccepted, Is.True);
        }

        private static StructuredRuleDefinition Requirement(
            int order,
            StructuredFactField field,
            StructuredRuleOperator ruleOperator,
            StructuredValue expected,
            string rejectionCode)
        {
            return new StructuredRuleDefinition(
                $"规则.{order}",
                order,
                field,
                ruleOperator,
                expected,
                rejectionCode);
        }

        private static StructuredRuleContext Context()
        {
            return new StructuredRuleContext(
                new SemanticActionRequest(
                    "命令.规则测试",
                    "抓取",
                    "操作.规则测试",
                    SemanticActionPhase.Complete,
                    0d,
                    "学生",
                    "器材.试管",
                    null,
                    Array.Empty<KeyValuePair<string, StructuredValue>>()),
                new ExperimentWorld());
        }

        private sealed class StubFactReader : IStructuredFactReader
        {
            private readonly StructuredValue _value;

            public StubFactReader(
                StructuredFactField field,
                StructuredValue value)
            {
                Field = field;
                _value = value;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return _value;
            }
        }
    }
}
