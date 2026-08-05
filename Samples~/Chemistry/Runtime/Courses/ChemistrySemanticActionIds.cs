namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 化学学科动作协议，不包含鼠标、触控或 VR 手势。
    /// </summary>
    public static class ChemistrySemanticActionIds
    {
        public const string BeginPour = "开始倾倒";
        public const string EndPour = "结束倾倒";
        public const string Ignite = "点燃";
        public const string Extinguish = "熄灭";
        public const string BeginHeating = "开始加热";
        public const string EndHeating = "结束加热";
        public const string Shake = "振荡";
        public const string CollectGas = "收集气体";
        public const string PickUpSolidMatter = "取出固体";
    }
}
