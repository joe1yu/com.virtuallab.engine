using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;

namespace VirtualLab.Teaching.Courses
{
    /// <summary>
    /// 教学模块拥有的命名状态事实。事实返回对象当前具有的状态名称集合，
    /// 课程用“包含”比较具体状态，不再用无语义的数字进度编码。
    /// </summary>
    public static class TeachingStructuredFactFields
    {
        public static readonly StructuredFactField 来源对象教学状态 =
            New("来源对象教学状态");
        public static readonly StructuredFactField 目标对象教学状态 =
            New("目标对象教学状态");

        public static IReadOnlyList<StructuredFactField> All { get; } =
            Array.AsReadOnly(new[]
            {
                来源对象教学状态,
                目标对象教学状态
            });

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }
}
