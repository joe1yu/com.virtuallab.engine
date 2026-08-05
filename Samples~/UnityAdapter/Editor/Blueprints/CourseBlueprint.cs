using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Blueprints
{
    /// <summary>
    /// 表现覆盖可引用的跨课程语义主体。它们描述触发语义，不对应场景实体。
    /// </summary>
    public static class CoursePresentationSemanticSubjects
    {
        public const string AllActionRejections = "所有动作拒绝";
        public const string ActionTarget = "动作目标";
    }

    public readonly struct BlueprintVector3
    {
        public BlueprintVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public sealed class BlueprintValue
    {
        public BlueprintValue(
            string columnName,
            string rawValue,
            ConfigurationSource source)
        {
            ColumnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
            RawValue = rawValue ?? string.Empty;
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public string ColumnName { get; }
        public string RawValue { get; }
        public ConfigurationSource Source { get; }
    }

    public sealed class CourseBlueprintCourse
    {
        public CourseBlueprintCourse(
            string courseId,
            string displayName,
            IEnumerable<string> disciplinePackageIds,
            string actorEntityId,
            string experimentPrefab,
            ConfigurationSource source)
        {
            CourseId = courseId;
            DisplayName = displayName;
            DisciplinePackageIds = disciplinePackageIds.ToArray();
            ActorEntityId = actorEntityId;
            ExperimentPrefab = experimentPrefab;
            Source = source;
        }

        public string CourseId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> DisciplinePackageIds { get; }
        public string ActorEntityId { get; }
        public string ExperimentPrefab { get; }
        public ConfigurationSource Source { get; }
    }

    public sealed class CourseObjectBlueprint
    {
        public CourseObjectBlueprint(
            string entityId,
            string displayName,
            IEnumerable<string> featureIds,
            BlueprintVector3 initialPosition,
            BlueprintVector3 initialRotation,
            IReadOnlyDictionary<string, BlueprintValue> extensionValues,
            ConfigurationSource source,
            string entityType = "",
            IEnumerable<string> roleIds = null,
            IEnumerable<string> tagIds = null)
        {
            EntityId = entityId;
            DisplayName = displayName;
            FeatureIds = featureIds.ToArray();
            InitialPosition = initialPosition;
            InitialRotation = initialRotation;
            ExtensionValues = extensionValues;
            Source = source;
            EntityType = entityType ?? string.Empty;
            RoleIds = (roleIds ?? Array.Empty<string>()).ToArray();
            TagIds = (tagIds ?? Array.Empty<string>()).ToArray();
        }

        public string EntityId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> FeatureIds { get; }
        public BlueprintVector3 InitialPosition { get; }
        public BlueprintVector3 InitialRotation { get; }
        public IReadOnlyDictionary<string, BlueprintValue> ExtensionValues { get; }
        public ConfigurationSource Source { get; }
        public string EntityType { get; }
        public IReadOnlyList<string> RoleIds { get; }
        public IReadOnlyList<string> TagIds { get; }
    }

    /// <summary>
    /// 课程开始时已经成立的领域关系。关系类型直接使用模块注册的稳定中文标识，
    /// 不经过课程词典转换。
    /// </summary>
    public sealed class CourseInitialRelationBlueprint
    {
        public CourseInitialRelationBlueprint(
            string relationId,
            string relationTypeId,
            string sourceEntityId,
            string targetEntityId,
            string sourcePortId,
            string targetPortId,
            ConfigurationSource source)
        {
            RelationId = relationId;
            RelationTypeId = relationTypeId;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            SourcePortId = sourcePortId;
            TargetPortId = targetPortId;
            Source = source;
        }

        public string RelationId { get; }
        public string RelationTypeId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public string SourcePortId { get; }
        public string TargetPortId { get; }
        public ConfigurationSource Source { get; }
    }

    public abstract class CourseBlueprintRecord
    {
        protected CourseBlueprintRecord(
            IReadOnlyDictionary<string, BlueprintValue> values,
            ConfigurationSource source)
        {
            Values = values;
            Source = source;
        }

        public IReadOnlyDictionary<string, BlueprintValue> Values { get; }
        public ConfigurationSource Source { get; }

        protected string Value(string column) =>
            Values.TryGetValue(column, out var value)
                ? value.RawValue
                : string.Empty;
    }

    public sealed class CourseInteractionRuleBlueprint : CourseBlueprintRecord
    {
        public CourseInteractionRuleBlueprint(
            IReadOnlyDictionary<string, BlueprintValue> values,
            ConfigurationSource source)
            : base(values, source)
        {
        }

        public string InteractionId => Value("交互ID");
        public string HandlingMode => Value("处理方式");
        public string ActionId => Value("动作");
        public string SourceEntityId => Value("来源");
        public string TargetEntityId => Value("目标");
        public int Order => ParseInteger(Value("顺序"));
        public string RequirementType => Value("要求类型");
        public string RequirementSubject => Value("要求主体");
        public string Field => Value("字段");
        public string Comparison => Value("比较");
        public string ExpectedValue => Value("值");
        public string Unit => Value("单位");
        public string ResultRecipeId => Value("结果配方");
        public string FeedbackRecipeId => Value("反馈配方");
        public string RejectionMessage => Value("拒绝文案");
        public string ConsequenceTemplateId => Value("后果模板");

        private static int ParseInteger(string value) =>
            int.TryParse(value, out var result) ? result : 0;
    }

    public sealed class CourseDisciplineProcessBlueprint : CourseBlueprintRecord
    {
        public CourseDisciplineProcessBlueprint(
            IReadOnlyDictionary<string, BlueprintValue> values,
            ConfigurationSource source,
            IReadOnlyDictionary<string, BlueprintValue> parameters)
            : base(values, source)
        {
            Parameters = parameters;
        }

        public string DefinitionId => Value("定义ID");
        public string RecordType => Value("记录类型");
        public string DisciplineRecipeId => Value("学科配方");
        public string SubjectEntityId => Value("主体");
        public string SourceEntityId => Value("来源");
        public string TargetEntityId => Value("目标");
        public string StartInteractionId => Value("启动交互");
        public string StopInteractionId => Value("停止交互");
        public IReadOnlyDictionary<string, BlueprintValue> Parameters { get; }
    }

    public sealed class CourseTeachingEvaluationBlueprint : CourseBlueprintRecord
    {
        public CourseTeachingEvaluationBlueprint(
            IReadOnlyDictionary<string, BlueprintValue> values,
            ConfigurationSource source)
            : base(values, source)
        {
        }

        public string EvaluationId => Value("评价ID");
        public string EvaluationType => Value("类型");
        public string DisplayName => Value("显示名称");
        public string TriggerType => Value("触发类型");
        public string TriggerValue => Value("触发值");
        public int Order => int.TryParse(Value("顺序"), out var result) ? result : 0;
        public string ConditionType => Value("条件类型");
        public string SubjectEntityId => Value("主体");
        public string Field => Value("字段");
        public string Comparison => Value("比较");
        public string ExpectedValue => Value("值");
        public string Unit => Value("单位");
        public string ScoreDelta => Value("分值变化");
        public string PromptMessage => Value("提示文案");
        public string ConsequenceSeverity => Value("后果严重度");
        public string Recoverability => Value("可恢复性");
        public string Continuation => Value("发生后如何继续");
        public string BlockedGoalIds => Value("受阻目标");
    }

    public sealed class CoursePresentationOverrideBlueprint : CourseBlueprintRecord
    {
        public CoursePresentationOverrideBlueprint(
            IReadOnlyDictionary<string, BlueprintValue> values,
            ConfigurationSource source)
            : base(values, source)
        {
        }

        public string OverrideId => Value("覆盖ID");
        public string TriggerType => Value("触发类型");
        public string TriggerValue => Value("触发值");
        public string TriggerSourceEntityId => Value("触发来源");
        public string TriggerTargetEntityId => Value("触发目标");
        public string ObjectOrState => Value("对象或状态");
        public string PresentationPrimitive => Value("表现原语");
        public string TargetPosition => Value("作用位置");
        public string PositionId => Value("位置ID");
        public string ParameterName => Value("参数名");
        public string ParameterType => Value("参数类型");
        public string ParameterValue => Value("参数值");
    }

    public sealed class CourseAcceptanceRecordBlueprint : CourseBlueprintRecord
    {
        public CourseAcceptanceRecordBlueprint(
            IReadOnlyDictionary<string, BlueprintValue> values,
            ConfigurationSource source)
            : base(values, source)
        {
        }

        public string ScenarioId => Value("场景ID");
        public int Order => int.TryParse(Value("顺序"), out var result) ? result : 0;
        public string RecordType => Value("记录类型");
        public string ActionId => Value("动作");
        public string SourceEntityId => Value("来源");
        public string TargetEntityId => Value("目标");
        public string ParameterName => Value("参数名");
        public string ParameterValue => Value("参数值");
        public string AssertionType => Value("断言类型");
        public string ObjectId => Value("对象");
        public string Field => Value("字段");
        public string Comparison => Value("比较");
        public string ExpectedValue => Value("期望值");
        public string Unit => Value("单位");
    }

    public sealed class CourseAdvancedOverrideBlueprint : CourseBlueprintRecord
    {
        public CourseAdvancedOverrideBlueprint(
            IReadOnlyDictionary<string, BlueprintValue> values,
            ConfigurationSource source)
            : base(values, source)
        {
        }

        public string OverrideId => Value("覆盖ID");
        public string RecipeId => Value("配方ID");
        public string SourceEntityId => Value("来源实体");
        public string TargetEntityId => Value("目标实体");
        public string GeneratedItem => Value("生成项");
        public string Operation => Value("操作");
        public string Field => Value("字段");
        public string RawValue => Value("值");
    }

    public sealed class CourseBlueprint
    {
        public CourseBlueprint(
            CourseBlueprintCourse course,
            IEnumerable<CourseObjectBlueprint> objects,
            IEnumerable<CourseInitialRelationBlueprint> initialRelations,
            IEnumerable<CourseInteractionRuleBlueprint> interactionRules,
            IEnumerable<CourseDisciplineProcessBlueprint> disciplineProcesses,
            IEnumerable<CourseTeachingEvaluationBlueprint> teachingEvaluations,
            IEnumerable<CoursePresentationOverrideBlueprint> presentationOverrides,
            IEnumerable<CourseAcceptanceRecordBlueprint> acceptanceRecords,
            IEnumerable<CourseAdvancedOverrideBlueprint> advancedOverrides)
        {
            Course = course;
            Objects = objects.ToArray();
            InitialRelations = initialRelations.ToArray();
            InteractionRules = interactionRules.ToArray();
            DisciplineProcesses = disciplineProcesses.ToArray();
            TeachingEvaluations = teachingEvaluations.ToArray();
            PresentationOverrides = presentationOverrides.ToArray();
            AcceptanceRecords = acceptanceRecords.ToArray();
            AdvancedOverrides = advancedOverrides.ToArray();
        }

        public CourseBlueprintCourse Course { get; }
        public IReadOnlyList<CourseObjectBlueprint> Objects { get; }
        public IReadOnlyList<CourseInitialRelationBlueprint> InitialRelations
        {
            get;
        }
        public IReadOnlyList<CourseInteractionRuleBlueprint> InteractionRules { get; }
        public IReadOnlyList<CourseDisciplineProcessBlueprint> DisciplineProcesses { get; }
        public IReadOnlyList<CourseTeachingEvaluationBlueprint> TeachingEvaluations { get; }
        public IReadOnlyList<CoursePresentationOverrideBlueprint> PresentationOverrides { get; }
        public IReadOnlyList<CourseAcceptanceRecordBlueprint> AcceptanceRecords { get; }
        public IReadOnlyList<CourseAdvancedOverrideBlueprint> AdvancedOverrides { get; }

        internal static IReadOnlyDictionary<string, BlueprintValue> ReadOnlyValues(
            IEnumerable<KeyValuePair<string, BlueprintValue>> values)
        {
            var result = new Dictionary<string, BlueprintValue>(
                StringComparer.Ordinal);
            foreach (var pair in values)
            {
                // CSV 读取器已经报告重复表头；此处保留第一列，确保作者错误返回诊断而不是抛异常。
                if (!result.ContainsKey(pair.Key))
                {
                    result.Add(pair.Key, pair.Value);
                }
            }

            return new ReadOnlyDictionary<string, BlueprintValue>(result);
        }
    }
}
