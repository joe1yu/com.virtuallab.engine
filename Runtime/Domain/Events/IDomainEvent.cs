namespace VirtualLab.Domain.Events
{
    /// <summary>
    /// 引擎内置领域事件的稳定名称，供事件生产者与消费者共同引用。
    /// </summary>
    public static class DomainEventTypes
    {
        // 实体与空间关系变化。
        public const string EntityGrabbed = "实体.已抓取";
        public const string EntityReleased = "实体.已释放";
        public const string ConnectionEstablished = "连接.已建立";
        public const string ConnectionRemoved = "连接.已断开";
        public const string ContainmentChanged = "容纳关系.已变更";
        public const string SealConfirmed = "密封.已确认";
        public const string ObservationAvailable = "观察.可用";

        // 与具体学科无关的物质库存变化。
        public const string SubstanceTransferred = "物质.已转移";
    }

    /// <summary>
    /// 所有可进入统一事件流的领域事件都必须提供稳定的中文事件类型。
    /// </summary>
    public interface IDomainEvent
    {
        string EventType { get; }
    }
}
