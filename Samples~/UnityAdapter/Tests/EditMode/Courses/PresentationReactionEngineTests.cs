using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class PresentationReactionEngineTests
    {
        [Test]
        public void 结构化选择器和绑定从信号生成表现命令()
        {
            var signal = new PresentationSignal(
                "动作.倾倒",
                PresentationTriggerKind.ActionAccepted,
                "烧杯",
                "烧杯",
                "试管",
                new[]
                {
                    new KeyValuePair<string, PresentationValue>(
                        "液面比例",
                        PresentationValue.FromNumber(0.4))
                });
            var engine = new PresentationReactionEngine(
                new[]
                {
                    new PresentationRuleDefinition(
                        "规则.倾倒",
                        PresentationTriggerKind.ActionAccepted,
                        "动作.倾倒",
                        new[] { "效果.更新液面" })
                },
                new[]
                {
                    new PresentationEffectDefinition(
                        "效果.更新液面",
                        "liquid.set-level",
                        new PresentationTargetSelector(
                            PresentationEntitySelectorKind.ActionTarget,
                            null,
                            PresentationLocationKind.PresentationSlot,
                            "插槽.液体"),
                        "liquid.level",
                        10,
                        PresentationEffectLifecycle.UntilReplaced,
                        new[]
                        {
                            new PresentationParameterBinding(
                                "液面比例",
                                PresentationParameterSource.SignalPayload,
                                null,
                                "液面比例")
                        })
                });

            var command = engine.React(signal).Single();

            Assert.That(command.Target.EntityId, Is.EqualTo("试管"));
            Assert.That(
                command.Target.LocationKind,
                Is.EqualTo(PresentationLocationKind.PresentationSlot));
            Assert.That(command.Target.LocationId, Is.EqualTo("插槽.液体"));
            Assert.That(command.Parameters["液面比例"].Number, Is.EqualTo(0.4));
        }

        [Test]
        public void 同动作的表现规则只匹配配置的来源和目标实体()
        {
            var engine = new PresentationReactionEngine(
                new[]
                {
                    new PresentationRuleDefinition(
                        "规则.导气管连接集气瓶",
                        PresentationTriggerKind.ActionAccepted,
                        "连接",
                        new[] { "效果.吸附" },
                        "导气管",
                        "集气瓶")
                },
                new[]
                {
                    new PresentationEffectDefinition(
                        "效果.吸附",
                        "interaction.snap-to-anchor",
                        new PresentationTargetSelector(
                            PresentationEntitySelectorKind.ActionTarget,
                            null,
                            PresentationLocationKind.SemanticAnchor,
                            "集气瓶.入口"),
                        "transform.position",
                        10,
                        PresentationEffectLifecycle.UntilReplaced,
                        Array.Empty<PresentationParameterBinding>())
                });

            var unrelated = engine.React(new PresentationSignal(
                "连接",
                PresentationTriggerKind.ActionAccepted,
                "单孔橡皮塞",
                "单孔橡皮塞",
                "大试管",
                Array.Empty<KeyValuePair<string, PresentationValue>>()));
            var matched = engine.React(new PresentationSignal(
                "连接",
                PresentationTriggerKind.ActionAccepted,
                "导气管",
                "导气管",
                "集气瓶",
                Array.Empty<KeyValuePair<string, PresentationValue>>()));

            Assert.That(unrelated, Is.Empty);
            Assert.That(matched, Has.Count.EqualTo(1));
        }

        [TestCase(
            PresentationTriggerKind.ActionRejected,
            "实体温度过高",
            "ui.message")]
        [TestCase(
            PresentationTriggerKind.DomainEvent,
            "entity.ignited",
            "vfx.play")]
        [TestCase(
            PresentationTriggerKind.StateActive,
            "relation.held-by",
            "interaction.follow-anchor")]
        public void 表现规则从三类内核信号生成标准效果(
            PresentationTriggerKind triggerKind,
            string signalId,
            string expectedEffectId)
        {
            var effectDefinitionId = "效果定义." + expectedEffectId;
            var engine = new PresentationReactionEngine(
                new[]
                {
                    new PresentationRuleDefinition(
                        "表现规则." + signalId,
                        triggerKind,
                        signalId,
                        new[] { effectDefinitionId })
                },
                new[]
                {
                    new PresentationEffectDefinition(
                        effectDefinitionId,
                        expectedEffectId,
                        new PresentationTargetSelector(
                            PresentationEntitySelectorKind.SignalSubject,
                            null,
                            PresentationLocationKind.EntityRoot,
                            null),
                        "测试通道",
                        10,
                        PresentationEffectLifecycle.OneShot,
                        Array.Empty<PresentationParameterBinding>())
                });

            var commands = engine.React(
                new PresentationSignal(
                    signalId,
                    triggerKind,
                    "器材.试管",
                    null,
                    null,
                    Array.Empty<
                        KeyValuePair<string, PresentationValue>>()));

            Assert.That(commands.Single().EffectId, Is.EqualTo(expectedEffectId));
            Assert.That(
                commands.Single().Target.EntityId,
                Is.EqualTo("器材.试管"));
        }

        [Test]
        public void 配置参数覆盖信号载荷且不会反向修改输入()
        {
            var signalPayload = new Dictionary<string, PresentationValue>
            {
                ["持续秒数"] = PresentationValue.FromNumber(1d),
                ["锚点实体ID"] = PresentationValue.FromText("器材.铁架台")
            };
            var configuredParameters =
                new Dictionary<string, PresentationValue>
                {
                    ["持续秒数"] = PresentationValue.FromNumber(2d)
                };
            var engine = CreateEngine(
                "动作已接受",
                PresentationTriggerKind.ActionAccepted,
                "抓取",
                "效果.跟随",
                "interaction.follow-anchor",
                configuredParameters);

            var command = engine.React(
                new PresentationSignal(
                    "抓取",
                    PresentationTriggerKind.ActionAccepted,
                    "器材.试管",
                    "器材.试管",
                    null,
                    signalPayload)).Single();
            signalPayload["锚点实体ID"] =
                PresentationValue.FromText("器材.烧杯");
            configuredParameters["持续秒数"] =
                PresentationValue.FromNumber(99d);

            Assert.That(command.Parameters["持续秒数"].Number, Is.EqualTo(2d));
            Assert.That(command.Parameters.ContainsKey("锚点实体ID"), Is.False);
        }

        [Test]
        public void 结构化动作上下文可定位连接来源和目标锚点()
        {
            var engine = new PresentationReactionEngine(
                new[]
                {
                    new PresentationRuleDefinition(
                        "表现规则.连接",
                        PresentationTriggerKind.ActionAccepted,
                        "连接",
                        new[] { "效果.吸附" })
                },
                new[]
                {
                    new PresentationEffectDefinition(
                        "效果.吸附",
                        "interaction.snap-to-anchor",
                        new PresentationTargetSelector(
                            PresentationEntitySelectorKind.ActionTarget,
                            null,
                            PresentationLocationKind.SemanticAnchor,
                            "端口.进气"),
                        "transform.position",
                        0,
                        PresentationEffectLifecycle.UntilReplaced,
                        Array.Empty<PresentationParameterBinding>())
                });

            var command = engine.React(
                new PresentationSignal(
                    "连接",
                    PresentationTriggerKind.ActionAccepted,
                    "导气管",
                    "导气管",
                    "集气瓶一",
                    Array.Empty<
                        KeyValuePair<string, PresentationValue>>())).Single();

            Assert.That(command.Target.EntityId, Is.EqualTo("集气瓶一"));
            Assert.That(
                command.Target.LocationKind,
                Is.EqualTo(PresentationLocationKind.SemanticAnchor));
            Assert.That(command.Target.LocationId, Is.EqualTo("端口.进气"));
            Assert.That(
                command.SignalContext.ActionSourceEntityId,
                Is.EqualTo("导气管"));
        }

        [Test]
        public void 临时效果结束后恢复最新权威状态而非回放旧事件()
        {
            var arbiter = new PresentationChannelArbiter();
            var originalFollow = Command(
                "权威.跟随",
                "interaction.follow-anchor",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                "锚点.甲");
            var temporary = Command(
                "临时.归位",
                "interaction.snap-to-anchor",
                100,
                PresentationEffectLifecycle.WhileActive,
                "锚点.临时");
            var latestFollow = Command(
                "权威.跟随",
                "interaction.follow-anchor",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                "锚点.乙");

            arbiter.SynchronizeAuthoritativeState(new[] { originalFollow });
            Assert.That(
                arbiter.GetCurrent(originalFollow.Target, "transform.position")
                    .Parameters["锚点实体ID"].Text,
                Is.EqualTo("锚点.甲"));

            arbiter.BeginTemporary(temporary);
            arbiter.SynchronizeAuthoritativeState(new[] { latestFollow });
            Assert.That(
                arbiter.GetCurrent(
                    temporary.Target,
                    "transform.position").EffectId,
                Is.EqualTo("interaction.snap-to-anchor"));

            var resumed = arbiter.EndTemporary(
                temporary.CommandId,
                temporary.Target,
                temporary.Channel);

            Assert.That(resumed.Current.EffectId,
                Is.EqualTo("interaction.follow-anchor"));
            Assert.That(
                resumed.Current.Parameters["锚点实体ID"].Text,
                Is.EqualTo("锚点.乙"));
        }

        [Test]
        public void 持续至替换的效果结束后不会恢复已被替换的旧效果()
        {
            var arbiter = new PresentationChannelArbiter();
            var first = Command(
                "持续.甲",
                "interaction.follow-anchor",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                "锚点.甲");
            var second = Command(
                "持续.乙",
                "interaction.follow-anchor",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                "锚点.乙");

            arbiter.ReplaceTemporary(first);
            arbiter.ReplaceTemporary(second);
            var ended = arbiter.EndTemporary(
                second.CommandId,
                second.Target,
                second.Channel);

            Assert.That(ended.Current, Is.Null);
        }

        [Test]
        public void 课程表现配表可直接适配为反应引擎并保留效果顺序()
        {
            var course = new CoursePresentationDefinition(
                new[]
                {
                    new CoursePresentationRuleDefinition(
                        "表现规则.拿起",
                        CoursePresentationTriggerKind.ActionAccepted,
                        "抓取",
                        "表现组.拿起")
                },
                new[]
                {
                    new CoursePresentationGroupDefinition(
                        "表现组.拿起",
                        new[] { "效果.跟随", "效果.提示" })
                },
                new[]
                {
                    new CoursePresentationEffectDefinition(
                        "效果.跟随",
                        "interaction.follow-anchor",
                        new CoursePresentationTargetDefinition(
                            CoursePresentationEntitySelectorKind.ActionSource,
                            null,
                            CoursePresentationLocationKind.SemanticAnchor,
                            "锚点.抓取"),
                        CoursePresentationLifecycle.UntilReplaced,
                        20,
                        Array.Empty<
                            CoursePresentationParameterBindingDefinition>()),
                    new CoursePresentationEffectDefinition(
                        "效果.提示",
                        "ui.message",
                        new CoursePresentationTargetDefinition(
                            CoursePresentationEntitySelectorKind.Global,
                            null,
                            CoursePresentationLocationKind.GlobalReceiver,
                            null),
                        CoursePresentationLifecycle.OneShot,
                        0,
                        new[]
                        {
                            new CoursePresentationParameterBindingDefinition(
                                "资源ID",
                                CoursePresentationParameterSource.Constant,
                                StructuredValue.FromText("资源.提示音"),
                                null),
                            new CoursePresentationParameterBindingDefinition(
                                "文案",
                                CoursePresentationParameterSource.Constant,
                                StructuredValue.FromText("已拿起试管"),
                                null)
                        })
                },
                Array.Empty<CoursePresentationStateDefinition>(),
                new[]
                {
                    new CourseTextDefinition(
                        "文案.拿起成功",
                        "已拿起试管")
                });

            var commands = CoursePresentationAdapter
                .CreateReactionEngine(
                    course,
                    BuiltInPresentationEffectCatalog.Create())
                .React(new PresentationSignal(
                    "抓取",
                    PresentationTriggerKind.ActionAccepted,
                    "器材.试管",
                    "器材.试管",
                    null,
                    Array.Empty<
                        KeyValuePair<string, PresentationValue>>()));

            Assert.That(
                commands.Select(value => value.EffectId),
                Is.EqualTo(new[]
                {
                    "interaction.follow-anchor",
                    "ui.message"
                }));
            Assert.That(
                commands[0].Lifecycle,
                Is.EqualTo(PresentationEffectLifecycle.UntilReplaced));
            Assert.That(commands[0].Priority, Is.EqualTo(20));
            Assert.That(
                commands[1].Parameters["资源ID"].Text,
                Is.EqualTo("资源.提示音"));
            Assert.That(
                commands[1].Parameters["文案"].Text,
                Is.EqualTo("已拿起试管"));
        }

        private static PresentationReactionEngine CreateEngine(
            string ruleId,
            PresentationTriggerKind triggerKind,
            string triggerValue,
            string effectDefinitionId,
            string effectId,
            IReadOnlyDictionary<string, PresentationValue> parameters)
        {
            return new PresentationReactionEngine(
                new[]
                {
                    new PresentationRuleDefinition(
                        ruleId,
                        triggerKind,
                        triggerValue,
                        new[] { effectDefinitionId })
                },
                new[]
                {
                    new PresentationEffectDefinition(
                        effectDefinitionId,
                        effectId,
                        new PresentationTargetSelector(
                            PresentationEntitySelectorKind.SignalSubject,
                            null,
                            PresentationLocationKind.EntityRoot,
                            null),
                        "transform.position",
                        10,
                        PresentationEffectLifecycle.UntilReplaced,
                        parameters.Select(value =>
                            new PresentationParameterBinding(
                                value.Key,
                                PresentationParameterSource.Constant,
                                value.Value,
                                null)))
                });
        }

        private static PresentationEffectCommand Command(
            string commandId,
            string effectId,
            int priority,
            PresentationEffectLifecycle lifecycle,
            string anchorEntityId)
        {
            return new PresentationEffectCommand(
                commandId,
                effectId,
                new PresentationTargetReference(
                    "器材.试管",
                    PresentationLocationKind.EntityRoot,
                    null),
                new PresentationSignalContextReference(
                    "器材.试管",
                    "器材.试管",
                    null),
                "transform.position",
                priority,
                lifecycle,
                new[]
                {
                    new KeyValuePair<string, PresentationValue>(
                        "锚点实体ID",
                        PresentationValue.FromText(anchorEntityId))
                });
        }
    }
}
