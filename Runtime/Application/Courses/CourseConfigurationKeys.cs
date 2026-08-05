namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 平台配置协议使用的 Key。状态变化输入和事件载荷分组维护，修改协议时
    /// 可以集中检查生产者与消费者。
    /// </summary>
    public static class CourseConfigurationKeys
    {
        public static class Common
        {
            public const string SourceEntityId = "来源实体标识";
            public const string TargetEntityId = "目标实体标识";
            public const string Unit = "单位";
            public const string EventType = "事件类型";
        }

        public static class Mutation
        {
            public const string RelationTypeId = "关系类型";
            public const string SourceEntityId = Common.SourceEntityId;
            public const string SourceEntityReference = "来源实体引用";
            public const string TargetEntityId = Common.TargetEntityId;
            public const string TargetEntityReference = "目标实体引用";
            public const string SourcePortId = "来源端口标识";
            public const string TargetPortId = "目标端口标识";
            public const string StateKey = "状态键";
            public const string Value = "数值";
            public const string Unit = Common.Unit;
            public const string Minimum = "最小值";
            public const string Maximum = "最大值";
            public const string Increment = "增量";
            public const string ProcessId = "过程标识";
            public const string EntityId = "实体标识";
            public const string EventType = Common.EventType;
            public const string ExpectedValue = "期望值";
        }

        public static class EventPayload
        {
            public const string ActionId = "动作标识";
            public const string ActorEntityId = "操作者实体标识";
            public const string SourceEntityId = Common.SourceEntityId;
            public const string TargetEntityId = Common.TargetEntityId;
            public const string SubstanceId = "物质标识";
            public const string Quantity = "数量";
            public const string Unit = Common.Unit;
        }
    }
}
