using VirtualLab.Application.Courses;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 化学课程可配置规则使用的稳定事实协议。
    /// </summary>
    public static class ChemistryStructuredFactFields
    {
        public static StructuredFactField 来源对象温度 =>
            New("来源对象温度");
        public static StructuredFactField 来源内容体积 =>
            New("来源内容体积");
        public static StructuredFactField 来源固体质量 =>
            New("来源固体质量");
        public static StructuredFactField 来源内容单位 =>
            New("来源内容单位");
        public static StructuredFactField 倾倒角度 =>
            New("倾倒角度");
        public static StructuredFactField 倾倒口已对准 =>
            New("倾倒口已对准");
        public static StructuredFactField 请求流量 =>
            New("请求流量");

        private static StructuredFactField New(string id) =>
            new StructuredFactField(id);
    }
}
