using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;

namespace VirtualLab.Teaching.Courses
{
    /// <summary>
    /// 教学模块拥有的临时课程进度事实。后续由具体教学状态替代时，
    /// 只修改教学模块及课程配置，不向最小内核增加教学词汇。
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
