using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;

namespace VirtualLab.Spatial.Courses
{
    /// <summary>
    /// 空间模块拥有的接触与距离事实。
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
}
