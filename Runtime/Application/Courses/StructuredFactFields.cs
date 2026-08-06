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
}
