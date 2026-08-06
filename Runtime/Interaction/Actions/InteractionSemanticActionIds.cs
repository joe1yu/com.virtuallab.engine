using System;
using System.Collections.Generic;

namespace VirtualLab.Interaction.Actions
{
    /// <summary>
    /// 交互模块拥有的设备无关语义动作协议。
    /// 输入适配器只负责把鼠标、触控或 VR 输入翻译为这些动作。
    /// </summary>
    public static class InteractionSemanticActionIds
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

        /// <summary>
        /// 交互模块声明的全部动作标识，供注册与冲突校验使用。
        /// </summary>
        public static IReadOnlyList<string> All { get; } =
            Array.AsReadOnly(new[]
            {
                Grab,
                Release,
                Place,
                Take,
                Position,
                Cover,
                Uncover,
                Connect,
                Disconnect,
                Observe
            });
    }
}
