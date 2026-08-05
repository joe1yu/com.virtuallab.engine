namespace VirtualLab.Spatial.Courses
{
    /// <summary>
    /// 空间采集端与课程模块共享的请求参数键。
    /// 具体物理引擎只负责计算数值，不拥有课程协议名称。
    /// </summary>
    public static class SpatialRequestParameterKeys
    {
        public const string DistanceMeters = "空间距离米";
        public const string IsContacting = "空间接触";
        public const string PortAligned = "端口是否对齐";
        public const string OutletAligned = "出口是否对准目标入口";
        public const string TiltAngleDegrees = "倾角度数";
    }
}
