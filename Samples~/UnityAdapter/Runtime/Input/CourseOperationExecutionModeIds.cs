using System.Collections.Generic;

namespace VirtualLab.UnityAdapters.Input
{
    /// <summary>
    /// Unity 层支持的稳定操作执行方式。它们只描述输入与表现节奏，
    /// 不包含倾倒、加热等学科语义。
    /// </summary>
    public static class CourseOperationExecutionModeIds
    {
        public const string Immediate = "即时执行";
        public const string ContinuousInput = "持续输入";
        public const string DirectManipulation = "直接操纵";

        public static IReadOnlyList<string> All { get; } = new[]
        {
            Immediate,
            ContinuousInput,
            DirectManipulation
        };
    }
}
