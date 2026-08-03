using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 内核对语义动作的裁决结果；表现层可据此查表，但内核不描述动画。
    /// </summary>
    public sealed class SemanticActionOutcome
    {
        private SemanticActionOutcome(
            string commandId,
            bool accepted,
            IEnumerable<string> rejectionReasonIds,
            IEnumerable<KeyValuePair<string, StructuredValue>> facts)
        {
            CommandId = CourseContractGuard.Required(commandId, "命令 ID");
            Accepted = accepted;
            RejectionReasonIds = CourseContractGuard.CopyStrings(
                rejectionReasonIds,
                $"命令“{CommandId}”的拒绝原因 ID");
            Facts = CopyFacts(facts);

            if (accepted && RejectionReasonIds.Count > 0)
            {
                throw new ArgumentException(
                    $"已接受的命令“{CommandId}”不能包含拒绝原因。",
                    nameof(rejectionReasonIds));
            }
        }

        public string CommandId { get; }

        public bool Accepted { get; }

        public IReadOnlyList<string> RejectionReasonIds { get; }

        public IReadOnlyDictionary<string, StructuredValue> Facts { get; }

        public static SemanticActionOutcome Accept(
            string commandId,
            IEnumerable<KeyValuePair<string, StructuredValue>> facts)
        {
            return new SemanticActionOutcome(
                commandId,
                true,
                Array.Empty<string>(),
                facts);
        }

        public static SemanticActionOutcome Reject(
            string commandId,
            IEnumerable<string> rejectionReasonIds,
            IEnumerable<KeyValuePair<string, StructuredValue>> facts)
        {
            return new SemanticActionOutcome(
                commandId,
                false,
                rejectionReasonIds,
                facts);
        }

        private static IReadOnlyDictionary<string, StructuredValue> CopyFacts(
            IEnumerable<KeyValuePair<string, StructuredValue>> facts)
        {
            if (facts == null)
            {
                throw new ArgumentNullException(
                    nameof(facts),
                    "动作结果事实集合不能为空。");
            }

            var copy = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var pair in facts)
            {
                var key = CourseContractGuard.Required(
                    pair.Key,
                    "动作结果事实名");
                if (pair.Value == null || !copy.TryAdd(key, pair.Value))
                {
                    throw new ArgumentException(
                        $"动作结果事实“{key}”为空或重复。",
                        nameof(facts));
                }
            }

            return new ReadOnlyDictionary<string, StructuredValue>(copy);
        }
    }
}
