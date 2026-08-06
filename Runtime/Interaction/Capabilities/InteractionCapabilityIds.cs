using System;
using System.Collections.Generic;

namespace VirtualLab.Interaction.Capabilities
{
    /// <summary>
    /// 交互端口协议使用的稳定标识。
    /// </summary>
    public static class InteractionPortIds
    {
        public const string Default = "默认端口";
    }

    /// <summary>
    /// 交互模块拥有的能力协议标识。
    /// 课程配置、能力实例和创作工具都应引用本目录，不能复制字符串。
    /// </summary>
    public static class InteractionCapabilityIds
    {
        public const string Grabbable = "可抓取";
        public const string Container = "容器";
        public const string Connector = "可连接";
        public const string Observable = "可观察";
        public const string Clampable = "可夹持";
        public const string Coverable = "可覆盖";
        public const string Breakable = "可破损";
        public const string PositionableSource = "可定位源";
        public const string PositionableTarget = "可定位目标";

        /// <summary>
        /// 交互模块声明的全部能力标识，供创作目录和冲突校验使用。
        /// </summary>
        public static IReadOnlyList<string> All { get; } =
            Array.AsReadOnly(new[]
            {
                Grabbable,
                Container,
                Connector,
                Observable,
                Clampable,
                Coverable,
                Breakable,
                PositionableSource,
                PositionableTarget
            });
    }
}
