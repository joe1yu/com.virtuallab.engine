using VirtualLab.Application.Courses;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 化学课程配置使用的 Key。按通用、倾倒、加热、燃烧和振荡职责分组，
    /// 课程配方协议变更时只需在此处统一调整。
    /// </summary>
    public static class ChemistryConfigurationKeys
    {
        public static class Artifacts
        {
            public const string RuntimeConfiguration = "化学运行配置";
        }

        public static class Common
        {
            public const string CommandId = "命令标识";
            public const string ReactionId = "反应标识";
            public const string SourceEntityId =
                CourseConfigurationKeys.Common.SourceEntityId;
            public const string TargetEntityId =
                CourseConfigurationKeys.Common.TargetEntityId;
            public const string SubstanceId =
                CourseConfigurationKeys.EventPayload.SubstanceId;
            public const string Quantity =
                CourseConfigurationKeys.EventPayload.Quantity;
            public const string MeasurementUnit = "计量单位";
            public const string StateKey =
                CourseConfigurationKeys.Mutation.StateKey;
        }

        public static class Pour
        {
            public const string CapacityPolicy = "容量处理策略";
            public const string TargetCapacity = "目标容量";
            public const string FlowMillilitresPerSecond = "流量毫升每秒";
            public const string RequestedFlowGramsPerSecond = "请求流量克每秒";
            public const string RequestedFlowMillilitresPerSecond =
                "请求流量毫升每秒";
            public const string CapacityInsufficientRejectionReason =
                "容量不足拒绝原因";
            public const string QuantityInsufficientRiskStateKey =
                "数量不足风险状态键";
            public const string QuantityInsufficientRejectionReason =
                "数量不足拒绝原因";
        }

        public static class Heating
        {
            public const string ReactionUnitsPerAdvance = "每次推进反应单位";
            public const string AmbientTemperatureCelsius = "环境温度摄氏度";
            public const string ThermalPower = "热功率";
            public const string Efficiency = "效率";
            public const string HeatCapacity = "热容";
            public const string HeatLossCoefficient = "散热系数";
            public const string ReactionThresholdCelsius = "反应阈值摄氏度";
            public const string RequiredContainedEntityId = "必需包含实体标识";
            public const string MissingContainedEntityRejectionReason =
                "缺少包含实体拒绝原因";
            public const string MissingContainedEntityRiskStateKey =
                "缺少包含实体风险状态键";
            public const string RequiredConnectedEntityId = "必需连接实体标识";
            public const string MissingConnectedEntityRejectionReason =
                "缺少连接实体拒绝原因";
            public const string MissingConnectedEntityRiskStateKey =
                "缺少连接实体风险状态键";
            public const string HeatSourceEntityId = "热源实体标识";
            public const string HeatSourceStateKey = "热源状态键";
            public const string HeatSourceNotIgnitedRejectionReason =
                "热源未点燃拒绝原因";
            public const string StopHeatingConnectionTriggersRisk =
                "停热连接触发风险";
            public const string RiskConnectionEntityId = "风险连接实体标识";
            public const string RiskConnectionPortId = "风险连接端口标识";
            public const string DetectAnyRiskEntityConnection =
                "检测风险实体任意连接";
            public const string RiskStateKeySuffix = "风险状态键后缀";
            public const string StopHeatingConnectionRejectionReason =
                "停热连接拒绝原因";
        }

        public static class Combustion
        {
            public const string FuelId = "燃料标识";
            public const string OxidizerId = "氧化剂标识";
            public const string MinimumIgnitionTemperatureCelsius =
                "最低点燃温度摄氏度";
            public const string MaximumIgnitionDistanceMeters =
                "最大点火距离米";
            public const string ReactionUnitsPerSecond = "每秒反应单位";
            public const string OxidizerSourceEntityId = "氧化剂来源实体标识";
            public const string IgnitionSourceInvalidRejectionReason =
                "点火源无效拒绝原因";
            public const string IgnitionDistanceRejectionReason =
                "点火距离过远拒绝原因";
            public const string NotCombustibleRejectionReason =
                "对象不可燃拒绝原因";
            public const string MissingOxidizerRejectionReason =
                "缺少氧化剂拒绝原因";
            public const string FuelTemperatureRejectionReason =
                "燃料温度不足拒绝原因";
            public const string 对象间距离Meters = "空间距离米";
            public const string SafeContainerEntityId = "安全容器实体标识";
            public const string MinimumSafeLiquidVolumeMillilitres =
                "最小安全液体体积毫升";
            public const string InsufficientLiquidRiskStateKey =
                "液体不足风险状态键";
            public const string InsufficientLiquidRejectionReason =
                "液体不足拒绝原因";
        }

        public static class Shaking
        {
            public const string Intensity = "强度";
            public const string DurationSeconds = "持续秒数";
            public const string MinimumIntensity = "最小强度";
            public const string MaximumIntensity = "最大强度";
            public const string MinimumDurationSeconds = "最短持续秒数";
            public const string MaximumDurationSeconds = "最长持续秒数";
            public const string NotShakeableRejectionReason =
                "不可振荡拒绝原因";
            public const string NotHeldRejectionReason = "未持有拒绝原因";
            public const string IntensityOutOfRangeRejectionReason =
                "强度越界拒绝原因";
            public const string DurationOutOfRangeRejectionReason =
                "持续时间越界拒绝原因";
            public const string MixingStateKeySuffix = "混合状态键后缀";
            public const string MixingIncrementFactor = "混合增量系数";
            public const string MaximumReactionUnits = "最大反应单位";
        }

        public static class EventPayload
        {
            public const string LocationEntityId = "位置实体标识";
            public const string Components = "组分";
            public const string ProcessStopped = "过程已停止";
        }
    }
}
