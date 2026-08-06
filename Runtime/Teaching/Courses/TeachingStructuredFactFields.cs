using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;

namespace VirtualLab.Teaching.Courses
{
    /// <summary>
    /// 教学模块拥有的课程里程碑事实。事实返回对象已经完成的里程碑集合，
    /// 仅用于表达不能由当前权威世界状态持续推导的历史事实。
    /// </summary>
    public static class TeachingStructuredFactFields
    {
        public static readonly StructuredFactField 来源对象课程里程碑 =
            New("来源对象课程里程碑");
        public static readonly StructuredFactField 目标对象课程里程碑 =
            New("目标对象课程里程碑");

        public static IReadOnlyList<StructuredFactField> All { get; } =
            Array.AsReadOnly(new[]
            {
                来源对象课程里程碑,
                目标对象课程里程碑
            });

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }
}
