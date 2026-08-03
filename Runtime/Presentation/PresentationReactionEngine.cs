using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Presentation
{
    /// <summary>
    /// 把动作结果、领域事件或权威状态信号转换为标准表现命令。
    /// 该类型不访问 GameObject，也不写回实验世界。
    /// </summary>
    public sealed class PresentationReactionEngine
    {
        private readonly IReadOnlyList<PresentationRuleDefinition> _rules;
        private readonly IReadOnlyDictionary<
            string,
            PresentationEffectDefinition> _effects;

        public PresentationReactionEngine(
            IEnumerable<PresentationRuleDefinition> rules,
            IEnumerable<PresentationEffectDefinition> effects)
        {
            _rules = CopyRules(rules);
            _effects = CopyEffects(effects);
            ValidateReferences(_rules, _effects);
        }

        public IReadOnlyList<PresentationEffectCommand> React(
            PresentationSignal signal)
        {
            if (signal == null)
            {
                throw new ArgumentNullException(nameof(signal));
            }

            var commands = new List<PresentationEffectCommand>();
            foreach (var rule in _rules)
            {
                if (!rule.Matches(signal))
                {
                    continue;
                }

                foreach (var effectDefinitionId in rule.EffectDefinitionIds)
                {
                    var effect = _effects[effectDefinitionId];
                    commands.Add(new PresentationEffectCommand(
                        rule.RuleId + ":" + effect.DefinitionId,
                        effect.EffectId,
                        ResolveTarget(effect.Target, signal),
                        new PresentationSignalContextReference(
                            signal.SubjectEntityId,
                            signal.ActionSourceEntityId,
                            signal.ActionTargetEntityId),
                        effect.Channel,
                        effect.Priority,
                        effect.Lifecycle,
                        ResolveBindings(
                            effect.ParameterBindings,
                            signal)));
                }
            }

            return new ReadOnlyCollection<PresentationEffectCommand>(commands);
        }

        private static IReadOnlyList<PresentationRuleDefinition> CopyRules(
            IEnumerable<PresentationRuleDefinition> rules)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            var copy = rules.ToArray();
            if (copy.Any(value => value == null))
            {
                throw new ArgumentException(
                    "表现规则不能包含空值。",
                    nameof(rules));
            }

            EnsureUnique(
                copy.Select(value => value.RuleId),
                "表现规则 ID",
                nameof(rules));
            return new ReadOnlyCollection<PresentationRuleDefinition>(copy);
        }

        private static IReadOnlyDictionary<
            string,
            PresentationEffectDefinition> CopyEffects(
            IEnumerable<PresentationEffectDefinition> effects)
        {
            if (effects == null)
            {
                throw new ArgumentNullException(nameof(effects));
            }

            var copy = new Dictionary<
                string,
                PresentationEffectDefinition>(StringComparer.Ordinal);
            foreach (var effect in effects)
            {
                if (effect == null ||
                    !copy.TryAdd(effect.DefinitionId, effect))
                {
                    throw new ArgumentException(
                        "表现效果定义不能包含空值或重复 ID。",
                        nameof(effects));
                }
            }

            return new ReadOnlyDictionary<
                string,
                PresentationEffectDefinition>(copy);
        }

        private static void ValidateReferences(
            IEnumerable<PresentationRuleDefinition> rules,
            IReadOnlyDictionary<string, PresentationEffectDefinition> effects)
        {
            foreach (var rule in rules)
            {
                foreach (var effectId in rule.EffectDefinitionIds)
                {
                    if (!effects.ContainsKey(effectId))
                    {
                        throw new ArgumentException(
                            $"表现规则“{rule.RuleId}”引用了不存在的效果定义“" +
                            effectId +
                            "”。");
                    }
                }
            }
        }

        private static IEnumerable<KeyValuePair<string, PresentationValue>>
            ResolveBindings(
                IEnumerable<PresentationParameterBinding> bindings,
                PresentationSignal signal)
        {
            foreach (var binding in bindings)
            {
                yield return new KeyValuePair<string, PresentationValue>(
                    binding.Name,
                    ResolveBinding(binding, signal));
            }
        }

        private static PresentationValue ResolveBinding(
            PresentationParameterBinding binding,
            PresentationSignal signal)
        {
            if (binding.Source == PresentationParameterSource.Constant)
            {
                return binding.ConstantValue;
            }

            if (!signal.Payload.TryGetValue(
                    binding.PayloadKey,
                    out var value))
            {
                throw new InvalidOperationException(
                    $"信号“{signal.SignalId}”缺少载荷字段“" +
                    binding.PayloadKey +
                    "”。");
            }

            return value;
        }

        private static PresentationTargetReference ResolveTarget(
            PresentationTargetSelector selector,
            PresentationSignal signal)
        {
            string entityId;
            switch (selector.EntityKind)
            {
                case PresentationEntitySelectorKind.SignalSubject:
                    entityId = signal.SubjectEntityId;
                    break;
                case PresentationEntitySelectorKind.ActionSource:
                    entityId = signal.ActionSourceEntityId;
                    break;
                case PresentationEntitySelectorKind.ActionTarget:
                    entityId = signal.ActionTargetEntityId;
                    break;
                case PresentationEntitySelectorKind.PayloadField:
                    entityId = RequirePayloadText(
                        signal,
                        selector.EntityValue);
                    break;
                case PresentationEntitySelectorKind.FixedEntity:
                    entityId = selector.EntityValue;
                    break;
                case PresentationEntitySelectorKind.Global:
                    entityId = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return new PresentationTargetReference(
                entityId,
                selector.LocationKind,
                selector.LocationId);
        }

        private static string RequirePayloadText(
            PresentationSignal signal,
            string key)
        {
            if (!signal.Payload.TryGetValue(key, out var value) ||
                value.Kind != PresentationValueKind.Text ||
                string.IsNullOrWhiteSpace(value.Text))
            {
                throw new InvalidOperationException(
                    $"信号“{signal.SignalId}”的载荷字段“{key}”必须是非空文本。");
            }

            return value.Text.Trim();
        }

        private static void EnsureUnique(
            IEnumerable<string> values,
            string context,
            string parameterName)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (values.Any(value => !set.Add(value)))
            {
                throw new ArgumentException(
                    context + "不能重复。",
                    parameterName);
            }
        }
    }
}
