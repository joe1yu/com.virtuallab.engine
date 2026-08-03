using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 仅通过已注册事实读取器求值，禁止反射课程对象的任意属性。
    /// </summary>
    public sealed class StructuredRuleEvaluator
    {
        private readonly IReadOnlyDictionary<
            StructuredFactField,
            IStructuredFactReader> _readers;

        public StructuredRuleEvaluator(
            IEnumerable<IStructuredFactReader> readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            var copy = new Dictionary<
                StructuredFactField,
                IStructuredFactReader>();
            foreach (var reader in readers)
            {
                if (reader == null)
                {
                    throw new ArgumentException(
                        "事实读取器集合不能包含空项。",
                        nameof(readers));
                }

                if (!copy.TryAdd(reader.Field, reader))
                {
                    throw new ArgumentException(
                        $"字段“{reader.Field}”注册了多个事实读取器。",
                        nameof(readers));
                }
            }

            _readers = copy;
        }

        public StructuredRuleDecision Evaluate(
            IEnumerable<StructuredRuleDefinition> rules,
            StructuredRuleContext context)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var ruleList = rules.ToArray();
            if (ruleList.Any(value => value == null))
            {
                throw new ArgumentException(
                    "规则集合不能包含空项。",
                    nameof(rules));
            }

            var failures = new List<string>();
            foreach (var rule in ruleList
                .OrderBy(value => value.Order)
                .ThenBy(value => value.RuleId, StringComparer.Ordinal))
            {
                if (!_readers.TryGetValue(rule.Field, out var reader))
                {
                    throw new InvalidOperationException(
                        $"字段“{rule.Field}”没有注册事实读取器。");
                }

                var actual = reader.Read(context)
                    ?? throw new InvalidOperationException(
                        $"字段“{rule.Field}”的事实读取器返回了空值。");
                if (!Matches(actual, rule.Operator, rule.ExpectedValue))
                {
                    failures.Add(rule.RejectionCode);
                }
            }

            return new StructuredRuleDecision(failures);
        }

        private static bool Matches(
            StructuredValue actual,
            StructuredRuleOperator ruleOperator,
            StructuredValue expected)
        {
            switch (ruleOperator)
            {
                case StructuredRuleOperator.等于:
                    return AreEqual(actual, expected);
                case StructuredRuleOperator.不等于:
                    return !AreEqual(actual, expected);
                case StructuredRuleOperator.包含:
                    RequireKind(
                        actual,
                        StructuredValueKind.TextList,
                        ruleOperator);
                    RequireKind(
                        expected,
                        StructuredValueKind.Text,
                        ruleOperator);
                    return actual.TextList.Contains(
                        expected.Text,
                        StringComparer.Ordinal);
                case StructuredRuleOperator.为空:
                    return IsEmpty(actual);
                case StructuredRuleOperator.不为空:
                    return !IsEmpty(actual);
                case StructuredRuleOperator.小于:
                    return CompareNumbers(actual, expected) < 0;
                case StructuredRuleOperator.小于等于:
                    return CompareNumbers(actual, expected) <= 0;
                case StructuredRuleOperator.大于:
                    return CompareNumbers(actual, expected) > 0;
                case StructuredRuleOperator.大于等于:
                    return CompareNumbers(actual, expected) >= 0;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(ruleOperator),
                        ruleOperator,
                        "未注册的结构化规则运算符。");
            }
        }

        private static bool AreEqual(
            StructuredValue left,
            StructuredValue right)
        {
            if (left.Kind != right.Kind)
            {
                return false;
            }

            switch (left.Kind)
            {
                case StructuredValueKind.Null:
                    return true;
                case StructuredValueKind.Boolean:
                    return left.Boolean == right.Boolean;
                case StructuredValueKind.Number:
                    return left.Number.Equals(right.Number);
                case StructuredValueKind.Text:
                    return string.Equals(
                        left.Text,
                        right.Text,
                        StringComparison.Ordinal);
                case StructuredValueKind.TextList:
                    return left.TextList.SequenceEqual(
                        right.TextList,
                        StringComparer.Ordinal);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static bool IsEmpty(StructuredValue value)
        {
            switch (value.Kind)
            {
                case StructuredValueKind.Null:
                    return true;
                case StructuredValueKind.Text:
                    return value.Text.Length == 0;
                case StructuredValueKind.TextList:
                    return value.TextList.Count == 0;
                default:
                    return false;
            }
        }

        private static int CompareNumbers(
            StructuredValue actual,
            StructuredValue expected)
        {
            RequireKind(
                actual,
                StructuredValueKind.Number,
                StructuredRuleOperator.小于);
            RequireKind(
                expected,
                StructuredValueKind.Number,
                StructuredRuleOperator.小于);
            return actual.Number.CompareTo(expected.Number);
        }

        private static void RequireKind(
            StructuredValue value,
            StructuredValueKind required,
            StructuredRuleOperator ruleOperator)
        {
            if (value.Kind != required)
            {
                throw new InvalidOperationException(
                    $"运算符“{ruleOperator}”要求“{required}”值，"
                    + $"实际为“{value.Kind}”。");
            }
        }
    }
}
