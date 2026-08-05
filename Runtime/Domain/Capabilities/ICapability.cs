namespace VirtualLab.Domain.Capabilities
{
    /// <summary>
    /// 实体能力的最小稳定契约。能力标识由声明该能力的模块拥有，
    /// 通用领域层只负责存储、判重和对外枚举，不解释标识含义。
    /// </summary>
    public interface ICapability
    {
        string CapabilityId { get; }
    }

    /// <summary>
    /// 由课程或学科包声明的稳定能力协议。通用领域层只保存协议 ID 与配置值，
    /// 不认识也不解释具体学科含义。
    /// </summary>
    public interface IConfiguredCapability : ICapability
    {
        decimal NumberValue { get; }
        string TextValue { get; }
    }

    public sealed class ConfiguredCapability : IConfiguredCapability
    {
        public ConfiguredCapability(
            string capabilityId,
            decimal numberValue = 0m,
            string textValue = null)
        {
            if (string.IsNullOrWhiteSpace(capabilityId))
            {
                throw new System.ArgumentException(
                    "能力协议 ID 不能为空。",
                    nameof(capabilityId));
            }

            CapabilityId = capabilityId.Trim();
            NumberValue = numberValue;
            TextValue = string.IsNullOrWhiteSpace(textValue)
                ? null
                : textValue.Trim();
        }

        public string CapabilityId { get; }
        public decimal NumberValue { get; }
        public string TextValue { get; }
    }
}
