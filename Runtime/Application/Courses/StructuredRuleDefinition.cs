using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Domain;

namespace VirtualLab.Application.Courses
{
    public readonly struct StructuredFactField :
        IEquatable<StructuredFactField>
    {
        public StructuredFactField(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("事实字段 ID 不能为空。", nameof(id));
            }

            Id = id.Trim();
        }

        public string Id { get; }

        public static StructuredFactField 操作者存在 => New("操作者存在");
        public static StructuredFactField 来源对象存在 => New("来源对象存在");
        public static StructuredFactField 目标对象存在 => New("目标对象存在");
        public static StructuredFactField 操作者能力 => New("操作者能力");
        public static StructuredFactField 来源对象能力 => New("来源对象能力");
        public static StructuredFactField 目标对象能力 => New("目标对象能力");
        public static StructuredFactField 来源对象持有者 => New("来源对象持有者");
        public static StructuredFactField 来源对象已被操作者拿起 =>
            New("来源对象已被操作者拿起");
        public static StructuredFactField 目标对象已被操作者拿起 =>
            New("目标对象已被操作者拿起");
        public static StructuredFactField 来源对象进度 => New("来源对象进度");
        public static StructuredFactField 目标对象进度 => New("目标对象进度");
        public static StructuredFactField 来源连接点占用状态 =>
            New("来源连接点占用状态");
        public static StructuredFactField 目标连接点占用状态 =>
            New("目标连接点占用状态");
        public static StructuredFactField 来源连接标签 =>
            New("来源连接标签");
        public static StructuredFactField 目标连接标签 =>
            New("目标连接标签");
        public static StructuredFactField 连接标签相匹配 =>
            New("连接标签相匹配");
        public static StructuredFactField 对象正在接触 => New("对象正在接触");
        public static StructuredFactField 对象间距离 => New("对象间距离");

        public bool Equals(StructuredFactField other) =>
            string.Equals(Id, other.Id, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is StructuredFactField other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Id ?? string.Empty);

        public override string ToString() => Id ?? string.Empty;

        public static bool operator ==(
            StructuredFactField left,
            StructuredFactField right) => left.Equals(right);

        public static bool operator !=(
            StructuredFactField left,
            StructuredFactField right) => !left.Equals(right);

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }

    public enum StructuredRuleOperator
    {
        等于,
        不等于,
        包含,
        为空,
        不为空,
        小于,
        小于等于,
        大于,
        大于等于
    }

    /// <summary>
    /// 配表编译后的单条要求。字段和运算符均为白名单枚举。
    /// </summary>
    public sealed class StructuredRuleDefinition
    {
        public StructuredRuleDefinition(
            string ruleId,
            int order,
            StructuredFactField field,
            StructuredRuleOperator @operator,
            StructuredValue expectedValue,
            string rejectionCode)
        {
            RuleId = CourseContractGuard.Required(ruleId, "规则 ID");
            if (order < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(order),
                    order,
                    $"规则“{RuleId}”的顺序不能小于 0。");
            }

            Order = order;
            Field = field;
            Operator = @operator;
            ExpectedValue = expectedValue
                ?? throw new ArgumentNullException(
                    nameof(expectedValue),
                    $"规则“{RuleId}”的期望值不能为空。");
            RejectionCode = CourseContractGuard.Required(
                rejectionCode,
                $"规则“{RuleId}”的拒绝原因");
        }

        public string RuleId { get; }

        public int Order { get; }

        public StructuredFactField Field { get; }

        public StructuredRuleOperator Operator { get; }

        public StructuredValue ExpectedValue { get; }

        public string RejectionCode { get; }
    }

    public sealed class StructuredRuleContext
    {
        public StructuredRuleContext(
            SemanticActionRequest request,
            ExperimentWorld world)
        {
            Request = request
                ?? throw new ArgumentNullException(nameof(request));
            World = world
                ?? throw new ArgumentNullException(nameof(world));
        }

        public SemanticActionRequest Request { get; }

        public ExperimentWorld World { get; }
    }

    public interface IStructuredFactReader
    {
        StructuredFactField Field { get; }

        StructuredValue Read(StructuredRuleContext context);
    }

    public sealed class StructuredRuleDecision
    {
        internal StructuredRuleDecision(IEnumerable<string> rejectionCodes)
        {
            var copy = new List<string>(rejectionCodes);
            RejectionCodes = new ReadOnlyCollection<string>(copy);
            IsAccepted = copy.Count == 0;
            PrimaryRejectionCode = IsAccepted ? null : copy[0];
        }

        public bool IsAccepted { get; }

        public string PrimaryRejectionCode { get; }

        public IReadOnlyList<string> RejectionCodes { get; }
    }
}
