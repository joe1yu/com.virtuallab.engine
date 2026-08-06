using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Matter
{
    /// <summary>
    /// 化学模块使用的计量单位目录。单位标识集中在模块边界内，
    /// 测量内核只负责单位值的相等性和量值运算。
    /// </summary>
    public static class ChemistryUnits
    {
        public const string GramId = "克";
        public const string MillilitreId = "毫升";

        public static readonly Unit Gram = new Unit(GramId);
        public static readonly Unit Millilitre = new Unit(MillilitreId);

        /// <summary>
        /// 判断单位是否由当前倾倒模型支持。
        /// 其它模块仍可自由声明单位，但必须同时提供相应的过程模型。
        /// </summary>
        public static bool SupportsMatterTransfer(Unit unit) =>
            unit == Gram || unit == Millilitre;
    }
}
