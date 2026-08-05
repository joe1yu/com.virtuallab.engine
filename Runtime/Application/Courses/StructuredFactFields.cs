using System;
using System.Collections.Generic;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 最小课程内核拥有的实体存在性与通用能力事实。
    /// 这些字段只描述通用实体和能力机制，不解释具体交互或学科含义。
    /// </summary>
    public static class CoreStructuredFactFields
    {
        public static readonly StructuredFactField 操作者存在 = New("操作者存在");
        public static readonly StructuredFactField 来源对象存在 = New("来源对象存在");
        public static readonly StructuredFactField 目标对象存在 = New("目标对象存在");
        public static readonly StructuredFactField 操作者能力 = New("操作者能力");
        public static readonly StructuredFactField 来源对象能力 = New("来源对象能力");
        public static readonly StructuredFactField 目标对象能力 = New("目标对象能力");

        public static IReadOnlyList<StructuredFactField> All { get; } =
            Array.AsReadOnly(new[]
            {
                操作者存在,
                来源对象存在,
                目标对象存在,
                操作者能力,
                来源对象能力,
                目标对象能力
            });

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }

    /// <summary>
    /// 交互模块拥有的持有、拿取与连接事实。
    /// </summary>
    public static class InteractionStructuredFactFields
    {
        public static readonly StructuredFactField 来源对象持有者 =
            New("来源对象持有者");
        public static readonly StructuredFactField 来源对象已被操作者拿起 =
            New("来源对象已被操作者拿起");
        public static readonly StructuredFactField 目标对象已被操作者拿起 =
            New("目标对象已被操作者拿起");
        public static readonly StructuredFactField 来源连接点占用状态 =
            New("来源连接点占用状态");
        public static readonly StructuredFactField 目标连接点占用状态 =
            New("目标连接点占用状态");
        public static readonly StructuredFactField 来源连接标签 =
            New("来源连接标签");
        public static readonly StructuredFactField 目标连接标签 =
            New("目标连接标签");
        public static readonly StructuredFactField 连接标签相匹配 =
            New("连接标签相匹配");

        public static IReadOnlyList<StructuredFactField> All { get; } =
            Array.AsReadOnly(new[]
            {
                来源对象持有者,
                来源对象已被操作者拿起,
                目标对象已被操作者拿起,
                来源连接点占用状态,
                目标连接点占用状态,
                来源连接标签,
                目标连接标签,
                连接标签相匹配
            });

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }

    /// <summary>
    /// 空间适配模块拥有的接触与距离事实。
    /// </summary>
    public static class SpatialStructuredFactFields
    {
        public static readonly StructuredFactField 对象正在接触 =
            New("对象正在接触");
        public static readonly StructuredFactField 对象间距离 =
            New("对象间距离");

        public static IReadOnlyList<StructuredFactField> All { get; } =
            Array.AsReadOnly(new[]
            {
                对象正在接触,
                对象间距离
            });

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }

    /// <summary>
    /// 教学模块暂时拥有的课程进度事实。后续迁移会把统一进度标量
    /// 替换为模块声明的具体状态，届时同步删除这组字段。
    /// </summary>
    public static class TeachingStructuredFactFields
    {
        public static readonly StructuredFactField 来源对象进度 =
            New("来源对象进度");
        public static readonly StructuredFactField 目标对象进度 =
            New("目标对象进度");

        public static IReadOnlyList<StructuredFactField> All { get; } =
            Array.AsReadOnly(new[]
            {
                来源对象进度,
                目标对象进度
            });

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }
}
