using System;

namespace VirtualLab.Presentation
{
    public enum PresentationParameterSource
    {
        Constant,
        SignalPayload
    }

    /// <summary>
    /// 参数只允许固定值或信号载荷两种来源，不解释表达式或字符串模板。
    /// </summary>
    public sealed class PresentationParameterBinding
    {
        public PresentationParameterBinding(
            string name,
            PresentationParameterSource source,
            PresentationValue constantValue,
            string payloadKey)
        {
            Name = PresentationContractGuard.Required(name, "表现参数名");
            Source = source;
            ConstantValue = constantValue;
            PayloadKey = PresentationContractGuard.Optional(payloadKey);

            if (source == PresentationParameterSource.Constant &&
                constantValue == null)
            {
                throw new ArgumentException(
                    "固定值参数必须提供参数值。",
                    nameof(constantValue));
            }

            if (source == PresentationParameterSource.SignalPayload &&
                PayloadKey == null)
            {
                throw new ArgumentException(
                    "信号载荷参数必须提供载荷字段。",
                    nameof(payloadKey));
            }
        }

        public string Name { get; }

        public PresentationParameterSource Source { get; }

        public PresentationValue ConstantValue { get; }

        public string PayloadKey { get; }
    }
}
