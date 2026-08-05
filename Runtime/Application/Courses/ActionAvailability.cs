using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 语义动作在当前课程状态下的可操作性分类。
    /// </summary>
    public enum ActionAvailabilityKind
    {
        Unsupported,
        TemporarilyBlocked,
        Disabled,
        Allowed
    }

    /// <summary>
    /// 规则引擎对一次设备无关语义动作的只读裁决结果。
    /// 表现层只消费该结果，不读取动作策略或规则配置。
    /// </summary>
    public sealed class ActionAvailability
    {
        public ActionAvailability(
            string actionId,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId,
            ActionAvailabilityKind kind,
            string rejectionCode,
            string messageId,
            IEnumerable<string> rejectionCodes,
            SemanticActionExecution execution)
        {
            if (!Enum.IsDefined(typeof(ActionAvailabilityKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            ActionId = CourseContractGuard.Required(actionId, "动作 ID");
            ActorEntityId = CourseContractGuard.Required(
                actorEntityId,
                "操作者实体 ID");
            SourceEntityId = CourseContractGuard.Required(
                sourceEntityId,
                "来源实体 ID");
            TargetEntityId = CourseContractGuard.Optional(targetEntityId);
            Kind = kind;
            RejectionCode = CourseContractGuard.Optional(rejectionCode);
            MessageId = CourseContractGuard.Optional(messageId);
            RejectionCodes = CopyRejectionCodes(rejectionCodes);
            Execution = execution;

            if (IsAllowed &&
                (RejectionCode != null || RejectionCodes.Count > 0))
            {
                throw new ArgumentException(
                    "允许执行的动作不能包含拒绝原因。",
                    nameof(rejectionCodes));
            }

            if (IsAllowed && Execution == null)
            {
                throw new ArgumentException(
                    "允许执行的动作必须包含设备无关执行计划。",
                    nameof(execution));
            }

            if (!IsAllowed && Execution != null)
            {
                throw new ArgumentException(
                    "不可执行的动作不能包含执行计划。",
                    nameof(execution));
            }

            if (!IsAllowed && RejectionCode == null)
            {
                throw new ArgumentException(
                    "不可执行的动作必须包含主要拒绝代码。",
                    nameof(rejectionCode));
            }
        }

        public string ActionId { get; }

        public string ActorEntityId { get; }

        public string SourceEntityId { get; }

        public string TargetEntityId { get; }

        public bool IsAllowed => Kind == ActionAvailabilityKind.Allowed;

        public ActionAvailabilityKind Kind { get; }

        public string RejectionCode { get; }

        public string MessageId { get; }

        public IReadOnlyList<string> RejectionCodes { get; }

        public SemanticActionExecution Execution { get; }

        private static IReadOnlyList<string> CopyRejectionCodes(
            IEnumerable<string> rejectionCodes)
        {
            if (rejectionCodes == null)
            {
                throw new ArgumentNullException(nameof(rejectionCodes));
            }

            var copy = new List<string>();
            foreach (var code in rejectionCodes)
            {
                var normalized = CourseContractGuard.Required(
                    code,
                    "拒绝代码");
                if (!copy.Contains(normalized))
                {
                    copy.Add(normalized);
                }
            }

            return new ReadOnlyCollection<string>(copy);
        }
    }
}
