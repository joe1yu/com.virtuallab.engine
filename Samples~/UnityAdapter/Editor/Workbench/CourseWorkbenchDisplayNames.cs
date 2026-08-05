using System;
using System.Collections.Generic;
using System.IO;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Unity.Authoring.Workbench
{
    /// <summary>
    /// 课程工作台的中文展示词典。这里只改变界面文案，不修改 CSV 列名、
    /// 配方协议或生成资产中的稳定 ID。
    /// </summary>
    internal static class CourseWorkbenchDisplayNames
    {
        private static readonly IReadOnlyDictionary<string, string> Parameters =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["端口.出口.兼容组"] = "出口连接标签",
                ["端口.入口.兼容组"] = "入口连接标签",
                ["作用组.倾倒"] = "倾倒配对标签",
                ["作用组.加热"] = "加热配对标签",
                ["作用组.被点燃"] = "被点燃配对标签",
                ["作用组.点火源"] = "点火配对标签",
                ["作用组.集气"] = "集气配对标签",
                ["作用组.放置"] = "放置配对标签",
                ["作用组.覆盖"] = "覆盖配对标签",
                ["作用组.产物"] = "产物放入标签",
                ["作用组.定位"] = "定位配对标签"
            };

        private static readonly IReadOnlyDictionary<string, string> Features =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [InteractionCapabilityIds.Container] = "可容纳物质",
                [InteractionCapabilityIds.Clampable] = "固定不移动",
                [InteractionCapabilityIds.Connector] = "可作为连接发起端",
                [InteractionCapabilityIds.Connector] = "可作为连接接收端",
                ["可放置源"] = "可被放置",
                ["可放置目标"] = "可承放对象",
                [InteractionCapabilityIds.Coverable] = "可用于覆盖",
                [InteractionCapabilityIds.Coverable] = "可被覆盖",
                [InteractionCapabilityIds.PositionableSource] = "可调整位置",
                [InteractionCapabilityIds.PositionableTarget] = "可作为定位参照",
                ["反应产物来源"] = "可放入反应产物",
                ["反应产物容器"] = "可接收反应产物",
                ["气体来源"] = "可提供气体",
                ["可点燃"] = "可作为点火工具"
            };

        private static readonly IReadOnlyDictionary<string, string> ReferenceFiles =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["交互规则.csv"] = "交互限制",
                ["学科过程.csv"] = "学科操作",
                ["实验流程.csv"] = "课程步骤",
                ["表现覆盖.csv"] = "画面表现",
                ["教学评价.csv"] = "教学评价",
                ["验收场景.csv"] = "验收检查",
                ["高级覆盖.csv"] = "高级配置"
            };

        private static readonly IReadOnlyDictionary<string, string> ReferenceRoles =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["来源"] = "作为操作来源",
                ["目标"] = "作为操作目标",
                ["主体"] = "作为判断对象",
                ["要求主体"] = "作为限制对象",
                ["触发来源"] = "作为触发来源",
                ["触发目标"] = "作为触发目标",
                ["对象或状态"] = "作为表现对象",
                ["对象"] = "作为检查对象",
                ["来源实体"] = "作为来源对象",
                ["目标实体"] = "作为目标对象"
            };

        public static string Field(string csvColumn) => csvColumn switch
        {
            "实体ID" => "对象 ID",
            "初始位置" => "初始位置",
            "初始旋转" => "初始朝向",
            _ => csvColumn ?? string.Empty
        };

        public static string Parameter(string parameterName) =>
            Lookup(Parameters, parameterName);

        public static string Feature(string featureId) =>
            Lookup(Features, featureId);

        public static string FactField(string fieldId) =>
            fieldId?.Trim() ?? string.Empty;

        public static string Operator(string operatorId) =>
            operatorId?.Trim() ?? string.Empty;

        public static string ReferenceFile(string fileName) =>
            ReferenceFiles.TryGetValue(fileName ?? string.Empty, out var displayName)
                ? displayName
                : Path.GetFileNameWithoutExtension(fileName ?? string.Empty);

        public static string ReferenceRole(string columnName) =>
            ReferenceRoles.TryGetValue(columnName ?? string.Empty, out var displayName)
                ? displayName
                : $"位于“{columnName}”字段";

        /// <summary>
        /// 使用配方中的局部键作为界面名称。点号前内容只是配置分类，界面仅展示
        /// 最后一段，新增学科操作或表现时无需修改工作台代码。
        /// </summary>
        public static string ConfiguredItem(string localKey)
        {
            var value = (localKey ?? string.Empty).Trim();
            var separator = value.LastIndexOf('.');
            return separator >= 0 && separator + 1 < value.Length
                ? value.Substring(separator + 1)
                : value;
        }

        public static string Trigger(string triggerKind) => triggerKind switch
        {
            "CourseInitialized" => "课程初始化",
            "ActionAvailabilityChanged" => "可操作状态变化",
            "ActionAccepted" => "操作成功",
            "ActionRejected" => "操作被拒绝",
            "DomainEvent" => "实验事件发生",
            "StateEntered" => "状态开始",
            "StateActive" => "状态持续",
            "StateExited" => "状态结束",
            _ => triggerKind ?? string.Empty
        };

        private static string Lookup(
            IReadOnlyDictionary<string, string> names,
            string stableId) =>
            stableId != null && names.TryGetValue(stableId, out var displayName)
                ? displayName
                : stableId ?? string.Empty;

    }
}
