using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;

namespace VirtualLab.Interaction.Courses
{
    /// <summary>
    /// 交互模块拥有的持有、拿取与连接事实协议。
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
        public static readonly StructuredFactField 来源对象连接对象 =
            New("来源对象连接对象");
        public static readonly StructuredFactField 来源对象连接网络 =
            New("来源对象连接网络");
        public static readonly StructuredFactField 来源对象所在容器 =
            New("来源对象所在容器");
        public static readonly StructuredFactField 来源对象连接网络中由操作者持有的对象 =
            New("来源对象连接网络中由操作者持有的对象");
        public static readonly StructuredFactField 来源对象固定对象 =
            New("来源对象固定对象");
        public static readonly StructuredFactField 来源对象覆盖物 =
            New("来源对象覆盖物");
        public static readonly StructuredFactField 目标对象覆盖物 =
            New("目标对象覆盖物");
        public static readonly StructuredFactField 目标对象所在容器覆盖物 =
            New("目标对象所在容器覆盖物");

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
                连接标签相匹配,
                来源对象连接对象,
                来源对象连接网络,
                来源对象所在容器,
                来源对象连接网络中由操作者持有的对象,
                来源对象固定对象,
                来源对象覆盖物,
                目标对象覆盖物,
                目标对象所在容器覆盖物
            });

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }
}
