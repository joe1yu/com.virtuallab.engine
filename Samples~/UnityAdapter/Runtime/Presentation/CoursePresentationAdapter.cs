using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    /// <summary>
    /// 将课程资产中的强类型表现配置转换为纯 C# 反应定义。
    /// 保留效果组顺序，并在边界处完成协议枚举和参数类型转换。
    /// </summary>
    public static class CoursePresentationAdapter
    {
        public static PresentationReactionEngine CreateReactionEngine(
            CoursePresentationDefinition course,
            PresentationEffectCatalog catalog)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var groups = course.Groups.ToDictionary(
                value => value.GroupId,
                StringComparer.Ordinal);
            var effects = course.Effects.ToDictionary(
                value => value.EffectId,
                StringComparer.Ordinal);
            var rules = new List<PresentationRuleDefinition>();
            foreach (var rule in course.Rules)
            {
                if (!groups.TryGetValue(
                        rule.PresentationGroupId,
                        out var group))
                {
                    throw new InvalidOperationException(
                        $"表现规则“{rule.RuleId}”引用了不存在的效果组“" +
                        rule.PresentationGroupId +
                        "”。");
                }

                foreach (var effectId in group.EffectIds)
                {
                    if (!effects.ContainsKey(effectId))
                    {
                        throw new InvalidOperationException(
                            $"表现组“{group.GroupId}”引用了不存在的效果“" +
                            effectId +
                            "”。");
                    }
                }

                rules.Add(new PresentationRuleDefinition(
                    rule.RuleId,
                    ParseTriggerKind(rule),
                    rule.TriggerValue,
                    group.EffectIds,
                    rule.SourceEntityId,
                    rule.TargetEntityId));
            }

            var definitions = course.Effects.Select(effect =>
                ConvertEffect(effect, catalog));
            return new PresentationReactionEngine(rules, definitions);
        }

        private static PresentationEffectDefinition ConvertEffect(
            CoursePresentationEffectDefinition effect,
            PresentationEffectCatalog catalog)
        {
            var descriptor = catalog.RequireByProtocolId(
                effect.ProtocolId);

            var target = new PresentationTargetSelector(
                ConvertEntitySelector(effect.Target.EntityKind),
                effect.Target.EntityValue,
                ConvertLocation(effect.Target.LocationKind),
                effect.Target.LocationId);
            var bindings = effect.ParameterBindings.Select(binding =>
                new PresentationParameterBinding(
                    binding.Name,
                    binding.Source ==
                    CoursePresentationParameterSource.Constant
                        ? PresentationParameterSource.Constant
                        : PresentationParameterSource.SignalPayload,
                    binding.ConstantValue == null
                        ? null
                        : ConvertValue(
                            effect.EffectId,
                            binding.Name,
                            binding.ConstantValue),
                    binding.PayloadKey));

            return new PresentationEffectDefinition(
                effect.EffectId,
                effect.ProtocolId,
                target,
                descriptor.Channel,
                effect.Priority,
                ConvertLifecycle(effect.Lifecycle),
                bindings);
        }

        private static PresentationTriggerKind ParseTriggerKind(
            CoursePresentationRuleDefinition rule)
        {
            return rule.TriggerKind switch
            {
                CoursePresentationTriggerKind.CourseInitialized =>
                    PresentationTriggerKind.CourseInitialized,
                CoursePresentationTriggerKind.ActionAvailabilityChanged =>
                    PresentationTriggerKind.ActionAvailabilityChanged,
                CoursePresentationTriggerKind.ActionAccepted =>
                    PresentationTriggerKind.ActionAccepted,
                CoursePresentationTriggerKind.ActionRejected =>
                    PresentationTriggerKind.ActionRejected,
                CoursePresentationTriggerKind.DomainEvent =>
                    PresentationTriggerKind.DomainEvent,
                CoursePresentationTriggerKind.StateEntered =>
                    PresentationTriggerKind.StateEntered,
                CoursePresentationTriggerKind.StateActive =>
                    PresentationTriggerKind.StateActive,
                CoursePresentationTriggerKind.StateExited =>
                    PresentationTriggerKind.StateExited,
                _ => throw new InvalidOperationException(
                    $"表现规则“{rule.RuleId}”使用了未注册的触发类型“" +
                    rule.TriggerKind +
                    "”。")
            };
        }

        private static PresentationEntitySelectorKind ConvertEntitySelector(
            CoursePresentationEntitySelectorKind value) =>
            value switch
            {
                CoursePresentationEntitySelectorKind.SignalSubject =>
                    PresentationEntitySelectorKind.SignalSubject,
                CoursePresentationEntitySelectorKind.ActionSource =>
                    PresentationEntitySelectorKind.ActionSource,
                CoursePresentationEntitySelectorKind.ActionTarget =>
                    PresentationEntitySelectorKind.ActionTarget,
                CoursePresentationEntitySelectorKind.PayloadField =>
                    PresentationEntitySelectorKind.PayloadField,
                CoursePresentationEntitySelectorKind.FixedEntity =>
                    PresentationEntitySelectorKind.FixedEntity,
                CoursePresentationEntitySelectorKind.Global =>
                    PresentationEntitySelectorKind.Global,
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        private static PresentationLocationKind ConvertLocation(
            CoursePresentationLocationKind value) =>
            value switch
            {
                CoursePresentationLocationKind.EntityRoot =>
                    PresentationLocationKind.EntityRoot,
                CoursePresentationLocationKind.PresentationSlot =>
                    PresentationLocationKind.PresentationSlot,
                CoursePresentationLocationKind.SemanticAnchor =>
                    PresentationLocationKind.SemanticAnchor,
                CoursePresentationLocationKind.GlobalReceiver =>
                    PresentationLocationKind.GlobalReceiver,
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        private static PresentationEffectLifecycle ConvertLifecycle(
            CoursePresentationLifecycle value) =>
            value switch
            {
                CoursePresentationLifecycle.OneShot =>
                    PresentationEffectLifecycle.OneShot,
                CoursePresentationLifecycle.WhileActive =>
                    PresentationEffectLifecycle.WhileActive,
                CoursePresentationLifecycle.UntilReplaced =>
                    PresentationEffectLifecycle.UntilReplaced,
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        private static PresentationValue ConvertValue(
            string effectId,
            string parameterName,
            StructuredValue value)
        {
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"表现效果“{effectId}”的参数“{parameterName}”为空。");
            }

            return value.Kind switch
            {
                StructuredValueKind.Null => PresentationValue.Null(),
                StructuredValueKind.Boolean =>
                    PresentationValue.FromBoolean(value.Boolean),
                StructuredValueKind.Number =>
                    PresentationValue.FromNumber(value.Number),
                StructuredValueKind.Text =>
                    PresentationValue.FromText(value.Text),
                _ => throw new InvalidOperationException(
                    $"表现效果“{effectId}”的参数“{parameterName}”" +
                    "不能使用文本列表。")
            };
        }

    }
}
