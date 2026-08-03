namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 跨学科通用动作协议。输入适配器只负责把设备输入翻译为这些语义。
    /// </summary>
    public static class CoreSemanticActionIds
    {
        public const string Grab = "抓取";
        public const string Release = "释放";
        public const string Place = "放置";
        public const string Take = "拿出";
        public const string Position = "定位";
        public const string Cover = "覆盖";
        public const string Uncover = "揭开";
        public const string Connect = "连接";
        public const string Disconnect = "断开";
        public const string Observe = "观察";
    }
}
