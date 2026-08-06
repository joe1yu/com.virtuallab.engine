using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Application.Courses
{
    public enum StructuredValueKind
    {
        Null,
        Boolean,
        Number,
        Text,
        TextList
    }

    /// <summary>
    /// 配表参数使用的受限值类型，避免把任意对象带入规则内核。
    /// </summary>
    public sealed class StructuredValue
    {
        public StructuredValue(
            StructuredValueKind kind,
            bool boolean,
            double number,
            string text,
            IReadOnlyList<string> textList)
        {
            if (!Enum.IsDefined(typeof(StructuredValueKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(number),
                    "结构化数值必须是有限数。");
            }

            Kind = kind;
            Boolean = boolean;
            Number = number;
            Text = text;
            TextList = textList?.ToArray()
                ?? throw new ArgumentNullException(nameof(textList));
        }

        public StructuredValueKind Kind { get; }

        public bool Boolean { get; }

        public double Number { get; }

        public string Text { get; }

        public IReadOnlyList<string> TextList { get; }

        public static StructuredValue Null()
        {
            return new StructuredValue(
                StructuredValueKind.Null,
                false,
                0d,
                null,
                Array.Empty<string>());
        }

        public static StructuredValue FromBoolean(bool value)
        {
            return new StructuredValue(
                StructuredValueKind.Boolean,
                value,
                0d,
                null,
                Array.Empty<string>());
        }

        public static StructuredValue FromNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "结构化数值必须是有限数。");
            }

            return new StructuredValue(
                StructuredValueKind.Number,
                false,
                value,
                null,
                Array.Empty<string>());
        }

        public static StructuredValue FromText(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    "结构化文本不能为 null；请显式使用 Null。");
            }

            return new StructuredValue(
                StructuredValueKind.Text,
                false,
                0d,
                value,
                Array.Empty<string>());
        }

        public static StructuredValue FromTextList(
            IEnumerable<string> values)
        {
            var copy = CourseContractGuard.CopyStrings(
                values,
                "结构化文本列表");
            return new StructuredValue(
                StructuredValueKind.TextList,
                false,
                0d,
                null,
                copy);
        }
    }

    /// <summary>
    /// 输入适配器提交给内核的设备无关语义动作。
    /// </summary>
    public sealed class SemanticActionRequest
    {
        public SemanticActionRequest(
            string commandId,
            string actionId,
            string operationInstanceId,
            SemanticActionPhase phase,
            double occurredAtSeconds,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId,
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters)
        {
            CommandId = CourseContractGuard.Required(commandId, "命令 ID");
            ActionId = CourseContractGuard.Required(actionId, "动作 ID");
            OperationInstanceId = CourseContractGuard.Required(
                operationInstanceId,
                $"命令“{CommandId}”的操作实例 ID");
            if (!Enum.IsDefined(typeof(SemanticActionPhase), phase))
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            if (double.IsNaN(occurredAtSeconds)
                || double.IsInfinity(occurredAtSeconds)
                || occurredAtSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(occurredAtSeconds),
                    occurredAtSeconds,
                    "语义操作发生时刻必须是有限且非负的秒数。");
            }

            Phase = phase;
            OccurredAtSeconds = occurredAtSeconds;
            ActorEntityId = CourseContractGuard.Required(
                actorEntityId,
                $"命令“{CommandId}”的操作者实体 ID");
            SourceEntityId = CourseContractGuard.Required(
                sourceEntityId,
                $"命令“{CommandId}”的来源实体 ID");
            TargetEntityId = CourseContractGuard.Optional(targetEntityId);
            Parameters = CopyParameters(parameters, CommandId);
        }

        public string CommandId { get; }

        public string ActionId { get; }

        public string OperationInstanceId { get; }

        public SemanticActionPhase Phase { get; }

        public double OccurredAtSeconds { get; }

        public string ActorEntityId { get; }

        public string SourceEntityId { get; }

        public string TargetEntityId { get; }

        public IReadOnlyDictionary<string, StructuredValue> Parameters { get; }

        private static IReadOnlyDictionary<string, StructuredValue> CopyParameters(
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters,
            string commandId)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(
                    nameof(parameters),
                    $"命令“{commandId}”的参数集合不能为空。");
            }

            var copy = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var pair in parameters)
            {
                var key = CourseContractGuard.Required(
                    pair.Key,
                    $"命令“{commandId}”的参数名");
                if (pair.Value == null)
                {
                    throw new ArgumentException(
                        $"命令“{commandId}”的参数“{key}”不能为空。",
                        nameof(parameters));
                }

                if (!copy.TryAdd(key, pair.Value))
                {
                    throw new ArgumentException(
                        $"命令“{commandId}”包含重复参数“{key}”。",
                        nameof(parameters));
                }
            }

            return new ReadOnlyDictionary<string, StructuredValue>(copy);
        }
    }
}
