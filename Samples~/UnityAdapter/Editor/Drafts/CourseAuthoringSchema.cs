using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Unity.Authoring.Drafts
{
    /// <summary>
    /// 新课程草稿只接受这十张职责单一的表。
    /// </summary>
    public static class CourseAuthoringTableNames
    {
        public const string Course = "课程.csv";
        public const string Objects = "实验对象.csv";
        public const string Components = "组件.csv";
        public const string InitialRelations = "初始关系.csv";
        public const string OperationOverrides = "操作特例.csv";
        public const string Processes = "过程.csv";
        public const string Teaching = "教学.csv";
        public const string TeachingConditions = "教学条件.csv";
        public const string Presentation = "表现.csv";
        public const string AcceptanceScenarios = "验收场景.csv";
    }

    /// <summary>
    /// 集中保存草稿协议的所有列名，读取器和后续写入器不得复制字符串常量。
    /// </summary>
    public static class CourseAuthoringColumns
    {
        public static class Course
        {
            public const string Id = "课程标识";
            public const string DisplayName = "显示名称";
            public const string DisciplinePackage = "学科配方包";
            public const string ActorEntityId = "操作者实体标识";
            public const string ExperimentPrefab = "实验预制体";
        }

        public static class Object
        {
            public const string EntityId = "实体标识";
            public const string DisplayName = "显示名称";
            public const string EntityType = "实体类型";
            public const string Roles = "角色列表";
            public const string Tags = "标签列表";
            public const string InitialPosition = "初始位置";
            public const string InitialRotation = "初始旋转";
        }

        public static class Component
        {
            public const string Id = "组件标识";
            public const string SubjectSelectorKind = "主体选择方式";
            public const string SubjectSelectorValue = "主体选择值";
            public const string ComponentType = "组件类型";
            public const string Parameters = "参数";
        }

        public static class InitialRelation
        {
            public const string Id = "关系标识";
            public const string RelationType = "关系类型";
            public const string SourceEntity = "来源实体";
            public const string TargetEntity = "目标实体";
            public const string SourcePortId = "来源端口标识";
            public const string TargetPortId = "目标端口标识";
        }

        public static class OperationOverride
        {
            public const string Id = "特例标识";
            public const string Handling = "处理方式";
            public const string Operation = "操作";
            public const string SourceSelectorKind = "来源选择方式";
            public const string SourceSelectorValue = "来源选择值";
            public const string TargetSelectorKind = "目标选择方式";
            public const string TargetSelectorValue = "目标选择值";
            public const string Order = "顺序";
            public const string SubjectSelectorKind = "主体选择方式";
            public const string SubjectSelectorValue = "主体选择值";
            public const string Fact = "事实";
            public const string Comparison = "比较";
            public const string Value = "值";
            public const string Unit = "单位";
            public const string RejectionMessage = "拒绝文案";
            public const string ConsequenceTemplate = "后果模板";
        }

        public static class Process
        {
            public const string Id = "过程标识";
            public const string ProcessType = "过程类型";
            public const string SubjectSelectorKind = "主体选择方式";
            public const string SubjectSelectorValue = "主体选择值";
            public const string SourceSelectorKind = "来源选择方式";
            public const string SourceSelectorValue = "来源选择值";
            public const string TargetSelectorKind = "目标选择方式";
            public const string TargetSelectorValue = "目标选择值";
            public const string Parameters = "参数";
        }

        public static class Teaching
        {
            public const string Id = "教学项标识";
            public const string Type = "类型";
            public const string DisplayName = "显示名称";
            public const string TriggerType = "触发类型";
            public const string TriggerValue = "触发值";
            public const string ScoreDelta = "分值变化";
            public const string Prompt = "提示文案";
            public const string ErrorSeverity = "错误严重度";
            public const string Continuation = "发生后如何继续";
            public const string AffectedGoals = "受影响目标";
        }

        public static class TeachingCondition
        {
            public const string TeachingItemId = "教学项标识";
            public const string Order = "顺序";
            public const string SubjectSelectorKind = "主体选择方式";
            public const string SubjectSelectorValue = "主体选择值";
            public const string Fact = "事实";
            public const string Comparison = "比较";
            public const string Value = "值";
            public const string Unit = "单位";
        }

        public static class Presentation
        {
            public const string Id = "表现标识";
            public const string TriggerType = "触发类型";
            public const string TriggerValue = "触发值";
            public const string TriggerSource = "触发来源";
            public const string TriggerTarget = "触发目标";
            public const string SubjectSelectorKind = "主体选择方式";
            public const string SubjectSelectorValue = "主体选择值";
            public const string Signal = "表现信号";
            public const string Location = "作用位置";
            public const string LocationId = "位置标识";
            public const string Parameters = "参数";
        }

        public static class Acceptance
        {
            public const string ScenarioId = "场景标识";
            public const string Order = "顺序";
            public const string RecordType = "记录类型";
            public const string Operation = "操作";
            public const string Source = "来源";
            public const string Target = "目标";
            public const string ParameterName = "参数名";
            public const string ParameterValue = "参数值";
            public const string AssertionType = "断言类型";
            public const string Object = "对象";
            public const string Fact = "事实";
            public const string Comparison = "比较";
            public const string ExpectedValue = "期望值";
            public const string Unit = "单位";
        }
    }

    /// <summary>
    /// 表现表使用的自然语言触发类型。读取、验证和编译必须复用这些常量，
    /// 避免各层分别维护协议文字。
    /// </summary>
    public static class CoursePresentationTriggerNames
    {
        public const string CourseInitialized = "课程初始化";
        public const string ActionAccepted = "动作成功";
        public const string ActionRejected = "动作拒绝";
        public const string DomainEvent = "领域事件";
        public const string StateEntered = "状态进入";
        public const string StateActive = "状态持续";
        public const string StateExited = "状态退出";
        public const string ActionAvailabilityChanged = "可用性变化";

        public static IReadOnlyList<string> All { get; } =
            new ReadOnlyCollection<string>(new[]
            {
                CourseInitialized,
                ActionAccepted,
                ActionRejected,
                DomainEvent,
                StateEntered,
                StateActive,
                StateExited,
                ActionAvailabilityChanged
            });

        public static bool SupportsActionEntities(string value) =>
            string.Equals(value, ActionAccepted, StringComparison.Ordinal)
            || string.Equals(value, ActionRejected, StringComparison.Ordinal)
            || string.Equals(
                value,
                ActionAvailabilityChanged,
                StringComparison.Ordinal);
    }

    public sealed class CourseAuthoringTableSchema
    {
        public CourseAuthoringTableSchema(
            string fileName,
            string identityColumn,
            IEnumerable<string> columns)
        {
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            IdentityColumn = identityColumn
                ?? throw new ArgumentNullException(nameof(identityColumn));
            Columns = new ReadOnlyCollection<string>(
                (columns ?? throw new ArgumentNullException(nameof(columns)))
                .ToArray());
        }

        public string FileName { get; }
        public string IdentityColumn { get; }
        public IReadOnlyList<string> Columns { get; }
    }

    public static class CourseAuthoringSchema
    {
        public static IReadOnlyList<CourseAuthoringTableSchema> Tables { get; } =
            new ReadOnlyCollection<CourseAuthoringTableSchema>(new[]
            {
                Table(CourseAuthoringTableNames.Course, CourseAuthoringColumns.Course.Id,
                    CourseAuthoringColumns.Course.Id,
                    CourseAuthoringColumns.Course.DisplayName,
                    CourseAuthoringColumns.Course.DisciplinePackage,
                    CourseAuthoringColumns.Course.ActorEntityId,
                    CourseAuthoringColumns.Course.ExperimentPrefab),
                Table(CourseAuthoringTableNames.Objects, CourseAuthoringColumns.Object.EntityId,
                    CourseAuthoringColumns.Object.EntityId,
                    CourseAuthoringColumns.Object.DisplayName,
                    CourseAuthoringColumns.Object.EntityType,
                    CourseAuthoringColumns.Object.Roles,
                    CourseAuthoringColumns.Object.Tags,
                    CourseAuthoringColumns.Object.InitialPosition,
                    CourseAuthoringColumns.Object.InitialRotation),
                Table(CourseAuthoringTableNames.Components, CourseAuthoringColumns.Component.Id,
                    CourseAuthoringColumns.Component.Id,
                    CourseAuthoringColumns.Component.SubjectSelectorKind,
                    CourseAuthoringColumns.Component.SubjectSelectorValue,
                    CourseAuthoringColumns.Component.ComponentType,
                    CourseAuthoringColumns.Component.Parameters),
                Table(CourseAuthoringTableNames.InitialRelations, CourseAuthoringColumns.InitialRelation.Id,
                    CourseAuthoringColumns.InitialRelation.Id,
                    CourseAuthoringColumns.InitialRelation.RelationType,
                    CourseAuthoringColumns.InitialRelation.SourceEntity,
                    CourseAuthoringColumns.InitialRelation.TargetEntity,
                    CourseAuthoringColumns.InitialRelation.SourcePortId,
                    CourseAuthoringColumns.InitialRelation.TargetPortId),
                Table(CourseAuthoringTableNames.OperationOverrides, CourseAuthoringColumns.OperationOverride.Id,
                    CourseAuthoringColumns.OperationOverride.Id,
                    CourseAuthoringColumns.OperationOverride.Handling,
                    CourseAuthoringColumns.OperationOverride.Operation,
                    CourseAuthoringColumns.OperationOverride.SourceSelectorKind,
                    CourseAuthoringColumns.OperationOverride.SourceSelectorValue,
                    CourseAuthoringColumns.OperationOverride.TargetSelectorKind,
                    CourseAuthoringColumns.OperationOverride.TargetSelectorValue,
                    CourseAuthoringColumns.OperationOverride.Order,
                    CourseAuthoringColumns.OperationOverride.SubjectSelectorKind,
                    CourseAuthoringColumns.OperationOverride.SubjectSelectorValue,
                    CourseAuthoringColumns.OperationOverride.Fact,
                    CourseAuthoringColumns.OperationOverride.Comparison,
                    CourseAuthoringColumns.OperationOverride.Value,
                    CourseAuthoringColumns.OperationOverride.Unit,
                    CourseAuthoringColumns.OperationOverride.RejectionMessage,
                    CourseAuthoringColumns.OperationOverride.ConsequenceTemplate),
                Table(CourseAuthoringTableNames.Processes, CourseAuthoringColumns.Process.Id,
                    CourseAuthoringColumns.Process.Id,
                    CourseAuthoringColumns.Process.ProcessType,
                    CourseAuthoringColumns.Process.SubjectSelectorKind,
                    CourseAuthoringColumns.Process.SubjectSelectorValue,
                    CourseAuthoringColumns.Process.SourceSelectorKind,
                    CourseAuthoringColumns.Process.SourceSelectorValue,
                    CourseAuthoringColumns.Process.TargetSelectorKind,
                    CourseAuthoringColumns.Process.TargetSelectorValue,
                    CourseAuthoringColumns.Process.Parameters),
                Table(CourseAuthoringTableNames.Teaching, CourseAuthoringColumns.Teaching.Id,
                    CourseAuthoringColumns.Teaching.Id,
                    CourseAuthoringColumns.Teaching.Type,
                    CourseAuthoringColumns.Teaching.DisplayName,
                    CourseAuthoringColumns.Teaching.TriggerType,
                    CourseAuthoringColumns.Teaching.TriggerValue,
                    CourseAuthoringColumns.Teaching.ScoreDelta,
                    CourseAuthoringColumns.Teaching.Prompt,
                    CourseAuthoringColumns.Teaching.ErrorSeverity,
                    CourseAuthoringColumns.Teaching.Continuation,
                    CourseAuthoringColumns.Teaching.AffectedGoals),
                Table(CourseAuthoringTableNames.TeachingConditions, CourseAuthoringColumns.TeachingCondition.TeachingItemId,
                    CourseAuthoringColumns.TeachingCondition.TeachingItemId,
                    CourseAuthoringColumns.TeachingCondition.Order,
                    CourseAuthoringColumns.TeachingCondition.SubjectSelectorKind,
                    CourseAuthoringColumns.TeachingCondition.SubjectSelectorValue,
                    CourseAuthoringColumns.TeachingCondition.Fact,
                    CourseAuthoringColumns.TeachingCondition.Comparison,
                    CourseAuthoringColumns.TeachingCondition.Value,
                    CourseAuthoringColumns.TeachingCondition.Unit),
                Table(CourseAuthoringTableNames.Presentation, CourseAuthoringColumns.Presentation.Id,
                    CourseAuthoringColumns.Presentation.Id,
                    CourseAuthoringColumns.Presentation.TriggerType,
                    CourseAuthoringColumns.Presentation.TriggerValue,
                    CourseAuthoringColumns.Presentation.TriggerSource,
                    CourseAuthoringColumns.Presentation.TriggerTarget,
                    CourseAuthoringColumns.Presentation.SubjectSelectorKind,
                    CourseAuthoringColumns.Presentation.SubjectSelectorValue,
                    CourseAuthoringColumns.Presentation.Signal,
                    CourseAuthoringColumns.Presentation.Location,
                    CourseAuthoringColumns.Presentation.LocationId,
                    CourseAuthoringColumns.Presentation.Parameters),
                Table(CourseAuthoringTableNames.AcceptanceScenarios, CourseAuthoringColumns.Acceptance.ScenarioId,
                    CourseAuthoringColumns.Acceptance.ScenarioId,
                    CourseAuthoringColumns.Acceptance.Order,
                    CourseAuthoringColumns.Acceptance.RecordType,
                    CourseAuthoringColumns.Acceptance.Operation,
                    CourseAuthoringColumns.Acceptance.Source,
                    CourseAuthoringColumns.Acceptance.Target,
                    CourseAuthoringColumns.Acceptance.ParameterName,
                    CourseAuthoringColumns.Acceptance.ParameterValue,
                    CourseAuthoringColumns.Acceptance.AssertionType,
                    CourseAuthoringColumns.Acceptance.Object,
                    CourseAuthoringColumns.Acceptance.Fact,
                    CourseAuthoringColumns.Acceptance.Comparison,
                    CourseAuthoringColumns.Acceptance.ExpectedValue,
                    CourseAuthoringColumns.Acceptance.Unit)
            });

        public static IReadOnlyDictionary<string, CourseAuthoringTableSchema>
            ByFileName { get; } = new ReadOnlyDictionary<string, CourseAuthoringTableSchema>(
                Tables.ToDictionary(value => value.FileName, StringComparer.Ordinal));

        private static CourseAuthoringTableSchema Table(
            string fileName,
            string identityColumn,
            params string[] columns) =>
            new CourseAuthoringTableSchema(fileName, identityColumn, columns);
    }
}
