using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Presentation
{
    /// <summary>
    /// 纯 C# 表现规则。规则只认识语义信号和标准效果，不依赖 Unity 对象。
    /// </summary>
    public sealed class PresentationRuleDefinition
    {
        public PresentationRuleDefinition(
            string ruleId,
            PresentationTriggerKind triggerKind,
            string triggerValue,
            IEnumerable<string> effectDefinitionIds,
            string sourceEntityId = null,
            string targetEntityId = null)
        {
            RuleId = PresentationContractGuard.Required(
                ruleId,
                "表现规则 ID");
            TriggerKind = triggerKind;
            TriggerValue = PresentationContractGuard.Required(
                triggerValue,
                $"表现规则“{RuleId}”的触发值");
            EffectDefinitionIds = CopyUniqueIds(
                effectDefinitionIds,
                $"表现规则“{RuleId}”的效果定义");
            SourceEntityId =
                PresentationContractGuard.Optional(sourceEntityId);
            TargetEntityId =
                PresentationContractGuard.Optional(targetEntityId);
        }

        public string RuleId { get; }

        public PresentationTriggerKind TriggerKind { get; }

        public string TriggerValue { get; }

        public IReadOnlyList<string> EffectDefinitionIds { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }

        public bool Matches(PresentationSignal signal)
        {
            if (signal == null)
            {
                throw new ArgumentNullException(nameof(signal));
            }

            return TriggerKind == signal.TriggerKind
                   && string.Equals(
                       TriggerValue,
                       signal.SignalId,
                       StringComparison.Ordinal)
                   && (SourceEntityId == null
                       || string.Equals(
                           SourceEntityId,
                           signal.ActionSourceEntityId,
                           StringComparison.Ordinal))
                   && (TargetEntityId == null
                       || string.Equals(
                           TargetEntityId,
                           signal.ActionTargetEntityId,
                           StringComparison.Ordinal));
        }

        private static IReadOnlyList<string> CopyUniqueIds(
            IEnumerable<string> values,
            string context)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var copy = values
                .Select(value => PresentationContractGuard.Required(
                    value,
                    context + " ID"))
                .ToArray();
            if (copy.Length == 0)
            {
                throw new ArgumentException(
                    context + "不能为空。",
                    nameof(values));
            }

            if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            {
                throw new ArgumentException(
                    context + "包含重复 ID。",
                    nameof(values));
            }

            return new ReadOnlyCollection<string>(copy);
        }
    }

    /// <summary>
    /// 标准效果命令模板。定义 ID 供课程配表引用，效果 ID 供通用执行器识别。
    /// </summary>
    public sealed class PresentationEffectDefinition
    {
        public PresentationEffectDefinition(
            string definitionId,
            string effectId,
            PresentationTargetSelector target,
            string channel,
            int priority,
            PresentationEffectLifecycle lifecycle,
            IEnumerable<PresentationParameterBinding> parameterBindings)
        {
            DefinitionId = PresentationContractGuard.Required(
                definitionId,
                "表现效果定义 ID");
            EffectId = PresentationContractGuard.Required(
                effectId,
                $"表现效果定义“{DefinitionId}”的效果 ID");
            Target = target ??
                throw new ArgumentNullException(nameof(target));
            Channel = PresentationContractGuard.Required(
                channel,
                $"表现效果定义“{DefinitionId}”的通道");
            Priority = priority;
            Lifecycle = lifecycle;
            ParameterBindings = CopyBindings(
                parameterBindings,
                DefinitionId);
        }

        public string DefinitionId { get; }

        public string EffectId { get; }

        public PresentationTargetSelector Target { get; }

        public string Channel { get; }

        public int Priority { get; }

        public PresentationEffectLifecycle Lifecycle { get; }

        public IReadOnlyList<PresentationParameterBinding>
            ParameterBindings { get; }

        private static IReadOnlyList<PresentationParameterBinding>
            CopyBindings(
                IEnumerable<PresentationParameterBinding> bindings,
                string definitionId)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            var copy = bindings.ToArray();
            if (copy.Any(value => value == null))
            {
                throw new ArgumentException(
                    $"表现效果定义“{definitionId}”的参数绑定不能包含空项。",
                    nameof(bindings));
            }

            if (copy.Select(value => value.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != copy.Length)
            {
                throw new ArgumentException(
                    $"表现效果定义“{definitionId}”包含重复参数绑定。",
                    nameof(bindings));
            }

            return new ReadOnlyCollection<PresentationParameterBinding>(
                copy);
        }
    }
}
