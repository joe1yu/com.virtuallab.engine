using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Drafts;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public sealed class CourseAuthoringSectionStatus
    {
        public CourseAuthoringSectionStatus(
            string displayName,
            IEnumerable<string> missingItems)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(
                nameof(displayName));
            MissingItems = (missingItems ?? Array.Empty<string>()).ToArray();
        }

        public string DisplayName { get; }
        public IReadOnlyList<string> MissingItems { get; }
        public bool IsComplete => MissingItems.Count == 0;
    }

    public sealed class CourseAuthoringCommandResult
    {
        private CourseAuthoringCommandResult(bool isSuccess, string message)
        {
            IsSuccess = isSuccess;
            Message = message ?? string.Empty;
        }

        public bool IsSuccess { get; }
        public string Message { get; }

        public static CourseAuthoringCommandResult Success() =>
            new CourseAuthoringCommandResult(true, string.Empty);

        public static CourseAuthoringCommandResult Failure(string message) =>
            new CourseAuthoringCommandResult(false, message);
    }

    public sealed class RiskFormValue
    {
        public RiskFormValue(
            string riskId,
            string severity,
            string continuation,
            IEnumerable<string> affectedTargets,
            string displayName = "",
            string prompt = "")
        {
            RiskId = riskId?.Trim() ?? string.Empty;
            Severity = severity?.Trim() ?? string.Empty;
            Continuation = continuation?.Trim() ?? string.Empty;
            AffectedTargets = (affectedTargets ?? Array.Empty<string>())
                .Select(value => value?.Trim() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? RiskId
                : displayName.Trim();
            Prompt = prompt?.Trim() ?? string.Empty;
        }

        public string RiskId { get; }
        public string Severity { get; }
        public string Continuation { get; }
        public IReadOnlyList<string> AffectedTargets { get; }
        public string DisplayName { get; }
        public string Prompt { get; }
    }

    public sealed class OperationOverrideFormValue
    {
        public OperationOverrideFormValue(
            string overrideId,
            string sourceSelectorKind,
            string sourceSelectorValue,
            string targetSelectorKind,
            string targetSelectorValue,
            string operationId,
            string handling,
            string fact = "",
            string comparison = "",
            string expectedValue = "",
            string unit = "",
            string conditionSubjectKind = "",
            string conditionSubjectValue = "",
            string rejectionMessage = "",
            string consequenceTemplateId = "",
            int order = 0)
        {
            OverrideId = overrideId?.Trim() ?? string.Empty;
            SourceSelectorKind = sourceSelectorKind?.Trim() ?? string.Empty;
            SourceSelectorValue = sourceSelectorValue?.Trim() ?? string.Empty;
            TargetSelectorKind = targetSelectorKind?.Trim() ?? string.Empty;
            TargetSelectorValue = targetSelectorValue?.Trim() ?? string.Empty;
            OperationId = operationId?.Trim() ?? string.Empty;
            Handling = handling?.Trim() ?? string.Empty;
            Fact = fact?.Trim() ?? string.Empty;
            Comparison = comparison?.Trim() ?? string.Empty;
            ExpectedValue = expectedValue?.Trim() ?? string.Empty;
            Unit = unit?.Trim() ?? string.Empty;
            ConditionSubjectKind = conditionSubjectKind?.Trim() ?? string.Empty;
            ConditionSubjectValue = conditionSubjectValue?.Trim() ?? string.Empty;
            RejectionMessage = rejectionMessage?.Trim() ?? string.Empty;
            ConsequenceTemplateId = consequenceTemplateId?.Trim() ?? string.Empty;
            Order = order;
        }

        public string OverrideId { get; }
        public string SourceSelectorKind { get; }
        public string SourceSelectorValue { get; }
        public string TargetSelectorKind { get; }
        public string TargetSelectorValue { get; }
        public string OperationId { get; }
        public string Handling { get; }
        public string Fact { get; }
        public string Comparison { get; }
        public string ExpectedValue { get; }
        public string Unit { get; }
        public string ConditionSubjectKind { get; }
        public string ConditionSubjectValue { get; }
        public string RejectionMessage { get; }
        public string ConsequenceTemplateId { get; }
        public int Order { get; }
    }

    /// <summary>
    /// 面向课程作者的六区工作流。状态按当前草稿即时计算，不保存第二份完成标记。
    /// </summary>
    public sealed class CourseAuthoringWorkflow
    {
        private CourseAuthoringReviewSummary _compiledReview;
        private static readonly string[] SectionNames =
        {
            "课程信息",
            "实验用品",
            "装置与初始状态",
            "操作与科学过程",
            "教学目标与错误后果",
            "检查与生成"
        };

        private static readonly string[] OperationFields =
        {
            "来源选择方式",
            "来源选择值",
            "目标选择方式",
            "目标选择值",
            "操作",
            "条件主体",
            "事实",
            "比较",
            "值",
            "单位",
            "处理方式",
            "拒绝文案",
            "后果模板"
        };

        private static readonly string[] ContinuationValues =
        {
            "可以继续",
            "纠正后继续",
            "更换样品或器材后继续",
            "重新开始",
            "无法继续"
        };

        private static readonly string[] SeverityValues =
        {
            "现象偏差",
            "实验风险",
            "安全事故"
        };

        public CourseAuthoringWorkflow(CourseAuthoringSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public CourseAuthoringSession Session { get; }
        public CourseDraftCourse Course => Session.Draft.Course;
        public string AuthoringDirectory => Session.AuthoringDirectory;
        public IReadOnlyList<CourseDraftObject> Objects => Session.Draft.Objects;
        public IReadOnlyList<CourseDraftInitialRelation> InitialRelations =>
            Session.Draft.InitialRelations;
        public IReadOnlyList<CourseDraftPresentation> Presentations =>
            Session.Draft.Presentations;
        public IReadOnlyList<CourseDraftAcceptanceRecord> AcceptanceRecords =>
            Session.Draft.AcceptanceRecords;
        public IReadOnlyList<string> VisibleOperationFields => OperationFields;
        public IReadOnlyList<string> ContinuationOptions => ContinuationValues;
        public IReadOnlyList<string> SeverityOptions => SeverityValues;
        public IReadOnlyList<AuthoringOperationDescriptor> Operations =>
            Session.Catalog.Operations;
        public IReadOnlyList<AuthoringProcessDescriptor> Processes =>
            Session.Catalog.Processes;
        public IReadOnlyList<AuthoringOptionDescriptor> ConsequenceTemplates =>
            Session.Catalog.Options
                .Where(value => value.Kind
                    == AuthoringOptionKind.ConsequenceTemplate)
                .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
                .ToArray();
        public IReadOnlyList<AuthoringOptionDescriptor> PresentationSignals =>
            Session.Catalog.Options
                .Where(value => value.Kind
                    == AuthoringOptionKind.PresentationSignal)
                .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
                .ToArray();
        public CourseAuthoringReviewSummary Review => _compiledReview
            ?? new CourseAuthoringReviewSummary(
                Objects.Count,
                Session.Draft.OperationOverrides.Count,
                Session.Draft.Processes.Count,
                null,
                null,
                null);
        public IReadOnlyList<CourseAuthoringSectionStatus> Sections =>
            BuildSections();

        public IReadOnlyList<AuthoringCategoryDescriptor> Categories =>
            Session.Catalog.Categories;

        public IReadOnlyList<AuthoringItemTemplateDescriptor> TemplatesIn(
            string categoryId) =>
            Session.Catalog.Templates
                .Where(value => value.CategoryId == categoryId)
                .OrderBy(value => value.DisplayOrder)
                .ThenBy(value => value.TemplateId, StringComparer.Ordinal)
                .ToArray();

        public IReadOnlyList<AuthoringOptionDescriptor> RelationTypes =>
            Session.Catalog.Options
                .Where(value => value.Kind == AuthoringOptionKind.RelationType)
                .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.OptionId, StringComparer.Ordinal)
                .ToArray();

        public IReadOnlyList<AuthoringPortDescriptor> PortsOf(string entityId)
        {
            var item = Session.Draft.Objects.SingleOrDefault(value =>
                value.EntityId == entityId);
            if (item == null
                || !Session.Catalog.TryGetTemplate(item.EntityType, out var template))
            {
                return Array.Empty<AuthoringPortDescriptor>();
            }

            return template.ComponentIds
                .Select(id => Session.Catalog.TryGetComponent(id, out var component)
                    ? component
                    : null)
                .Where(value => value != null)
                .SelectMany(value => value.Ports)
                .GroupBy(value => value.PortId, StringComparer.Ordinal)
                .Select(value => value.First())
                .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.PortId, StringComparer.Ordinal)
                .ToArray();
        }

        public IReadOnlyList<CourseDraftObject> AddSupplies(
            string categoryId,
            string templateId,
            int quantity)
        {
            if (quantity < 1 || quantity > 99)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    "单次添加数量必须在 1 到 99 之间。");
            }

            var category = Categories.SingleOrDefault(value =>
                value.CategoryId == categoryId
                || value.DisplayName == categoryId);
            if (category == null)
            {
                throw new ArgumentException(
                    $"用品类别“{categoryId}”未由模块注册。",
                    nameof(categoryId));
            }

            var template = Session.Catalog.Templates.SingleOrDefault(value =>
                (value.TemplateId == templateId
                 || value.DisplayName == templateId)
                && value.CategoryId == category.CategoryId);
            if (template == null)
            {
                throw new ArgumentException(
                    $"类别“{category.DisplayName}”中没有用品模板“{templateId}”。",
                    nameof(templateId));
            }

            var existing = new HashSet<string>(
                Session.Draft.Objects.Select(value => value.EntityId),
                StringComparer.Ordinal);
            var identities = new List<string>();
            if (quantity == 1 && existing.Add(template.DisplayName))
            {
                identities.Add(template.DisplayName);
            }
            else
            {
                for (var index = 1; identities.Count < quantity; index++)
                {
                    var candidate = template.DisplayName + ChineseNumber(index);
                    if (existing.Add(candidate))
                    {
                        identities.Add(candidate);
                    }
                }
            }

            var added = Session.AddSupplies(
                template.TemplateId,
                identities.Select(identity =>
                    new KeyValuePair<string, string>(identity, identity)));
            _compiledReview = null;
            return added;
        }

        public CourseAuthoringCommandResult TrySetRelation(
            string relationType,
            string sourceEntityId,
            string targetEntityId,
            string sourcePortId = "",
            string targetPortId = "")
        {
            var option = RelationTypes.SingleOrDefault(value =>
                value.OptionId == relationType
                || value.DisplayName == relationType);
            if (option == null)
            {
                return CourseAuthoringCommandResult.Failure(
                    $"关系类型“{relationType}”未由模块注册。");
            }

            var entityIds = new HashSet<string>(
                Session.Draft.Objects.Select(value => value.EntityId),
                StringComparer.Ordinal);
            if (!entityIds.Contains(sourceEntityId)
                || !entityIds.Contains(targetEntityId))
            {
                return CourseAuthoringCommandResult.Failure(
                    "关系来源和目标必须是当前课程中的实验对象。");
            }

            if (sourceEntityId == targetEntityId)
            {
                return CourseAuthoringCommandResult.Failure(
                    "关系来源和目标不能是同一个实验对象。");
            }

            if (!PortIsValid(sourceEntityId, sourcePortId)
                || !PortIsValid(targetEntityId, targetPortId))
            {
                return CourseAuthoringCommandResult.Failure(
                    "所选端口未由对应用品组件注册。");
            }

            if (Session.Draft.InitialRelations.Any(value =>
                    value.RelationType == option.OptionId
                    && value.SourceEntityId == sourceEntityId
                    && value.TargetEntityId == targetEntityId
                    && value.SourcePortId == sourcePortId
                    && value.TargetPortId == targetPortId))
            {
                return CourseAuthoringCommandResult.Failure(
                    "相同来源、目标和类型的关系已经存在。");
            }

            var relationId = string.Join(".", new[]
            {
                "关系",
                option.DisplayName,
                sourceEntityId,
                sourcePortId,
                targetEntityId,
                targetPortId
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
            Session.SetInitialRelation(new CourseDraftInitialRelation(
                relationId,
                option.OptionId,
                sourceEntityId,
                targetEntityId,
                sourcePortId,
                targetPortId,
                new ConfigurationSource(
                    ConfigurationLayer.Course,
                    Session.Draft.Course.CourseId,
                    CourseAuthoringTableNames.InitialRelations,
                    1,
                    1,
                    relationId)));
            _compiledReview = null;
            return CourseAuthoringCommandResult.Success();
        }

        public void SetOperationOverride(OperationOverrideFormValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!Session.Catalog.TryGetOperation(value.OperationId, out _))
            {
                throw new ArgumentException(
                    $"操作“{value.OperationId}”未由模块注册。",
                    nameof(value));
            }

            if (value.ConsequenceTemplateId.Length > 0
                && !Session.Catalog.Options.Any(option =>
                    option.Kind == AuthoringOptionKind.ConsequenceTemplate
                    && option.OptionId == value.ConsequenceTemplateId))
            {
                throw new ArgumentException(
                    $"后果模板“{value.ConsequenceTemplateId}”未由模块注册。",
                    nameof(value));
            }

            Session.SetOperationOverride(new CourseDraftOperationOverride(
                value.OverrideId,
                value.Handling,
                value.OperationId,
                value.SourceSelectorKind,
                value.SourceSelectorValue,
                value.TargetSelectorKind,
                value.TargetSelectorValue,
                value.Order,
                value.ConditionSubjectKind,
                value.ConditionSubjectValue,
                value.Fact,
                value.Comparison,
                value.ExpectedValue,
                value.Unit,
                value.RejectionMessage,
                value.ConsequenceTemplateId,
                Source(CourseAuthoringTableNames.OperationOverrides, value.OverrideId)));
            _compiledReview = null;
        }

        public void SetRisk(RiskFormValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!SeverityValues.Contains(value.Severity, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"错误严重度“{value.Severity}”不是可选值。",
                    nameof(value));
            }

            if (!ContinuationValues.Contains(
                    value.Continuation,
                    StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"继续方式“{value.Continuation}”不是可选值。",
                    nameof(value));
            }

            Session.SetTeachingItem(new CourseDraftTeachingItem(
                value.RiskId,
                "错误后果",
                value.DisplayName,
                "条件满足",
                value.RiskId,
                0,
                value.Prompt,
                value.Severity,
                value.Continuation,
                string.Join(";", value.AffectedTargets),
                Source(CourseAuthoringTableNames.Teaching, value.RiskId)));
            _compiledReview = null;
        }

        public void SetProcess(
            string processId,
            string processType,
            string subjectSelectorKind,
            string subjectSelectorValue,
            string parameters,
            string sourceSelectorKind = "",
            string sourceSelectorValue = "",
            string targetSelectorKind = "",
            string targetSelectorValue = "")
        {
            if (!Session.Catalog.TryGetProcess(processType, out _))
            {
                throw new ArgumentException(
                    $"科学过程“{processType}”未由模块注册。",
                    nameof(processType));
            }

            Session.SetProcess(new CourseDraftProcess(
                processId,
                processType,
                subjectSelectorKind,
                subjectSelectorValue,
                sourceSelectorKind,
                sourceSelectorValue,
                targetSelectorKind,
                targetSelectorValue,
                parameters,
                Source(CourseAuthoringTableNames.Processes, processId)));
            _compiledReview = null;
        }

        public void SetPresentation(
            string presentationId,
            string triggerType,
            string triggerValue,
            string subjectSelectorKind,
            string subjectSelectorValue,
            string signalId,
            string location,
            string locationId,
            string parameters)
        {
            if (!PresentationSignals.Any(value => value.OptionId == signalId))
            {
                throw new ArgumentException(
                    $"表现信号“{signalId}”未由模块注册。",
                    nameof(signalId));
            }

            Session.SetPresentation(new CourseDraftPresentation(
                presentationId,
                triggerType,
                triggerValue,
                subjectSelectorKind,
                subjectSelectorValue,
                signalId,
                location,
                locationId,
                parameters,
                Source(CourseAuthoringTableNames.Presentation, presentationId)));
            _compiledReview = null;
        }

        public void SetAcceptanceAction(
            string scenarioId,
            int order,
            string operationId,
            string sourceEntityId,
            string targetEntityId)
        {
            if (!Session.Catalog.TryGetOperation(operationId, out _))
            {
                throw new ArgumentException(
                    $"操作“{operationId}”未由模块注册。",
                    nameof(operationId));
            }

            var entityIds = new HashSet<string>(
                Objects.Select(value => value.EntityId),
                StringComparer.Ordinal);
            if (!entityIds.Contains(sourceEntityId)
                || (!string.IsNullOrWhiteSpace(targetEntityId)
                    && !entityIds.Contains(targetEntityId)))
            {
                throw new ArgumentException("验收动作引用了不存在的实验对象。");
            }

            Session.SetAcceptanceRecord(new CourseDraftAcceptanceRecord(
                scenarioId,
                order,
                "动作",
                operationId,
                sourceEntityId,
                targetEntityId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                Source(CourseAuthoringTableNames.AcceptanceScenarios, scenarioId)));
            _compiledReview = null;
        }

        public void SetCompiledReview(CourseAuthoringReviewSummary review)
        {
            _compiledReview = review ?? throw new ArgumentNullException(nameof(review));
        }

        private bool PortIsValid(string entityId, string portId) =>
            string.IsNullOrWhiteSpace(portId)
            || PortsOf(entityId).Any(value => value.PortId == portId);

        private ConfigurationSource Source(string fileName, string id) =>
            new ConfigurationSource(
                ConfigurationLayer.Course,
                Session.Draft.Course.CourseId,
                fileName,
                1,
                1,
                id ?? string.Empty);

        public CourseAuthoringSectionStatus Section(string displayName) =>
            Sections.Single(value => value.DisplayName == displayName);

        private IReadOnlyList<CourseAuthoringSectionStatus> BuildSections()
        {
            var draft = Session.Draft;
            var courseMissing = new List<string>();
            Missing(courseMissing, draft.Course.CourseId, "课程标识");
            Missing(courseMissing, draft.Course.DisplayName, "显示名称");
            Missing(courseMissing, draft.Course.DisciplinePackageId, "学科配方包");
            Missing(courseMissing, draft.Course.ActorEntityId, "操作者实体");
            Missing(courseMissing, draft.Course.ExperimentPrefabPath, "实验预制体路径");

            var supplies = draft.Objects.Where(value =>
                value.EntityId != draft.Course.ActorEntityId).ToArray();
            var supplyMissing = supplies.Length == 0
                ? new[] { "至少添加一种实验用品" }
                : Array.Empty<string>();
            var setupMissing = supplies.Any(value =>
                    string.IsNullOrWhiteSpace(value.InitialPosition)
                    || string.IsNullOrWhiteSpace(value.InitialRotation))
                ? new[] { "补全用品初始位置和旋转" }
                : Array.Empty<string>();
            var hasOperations = supplies.Any(value =>
                Session.Catalog.TryGetTemplate(value.EntityType, out var template)
                && template.OperationIds.Count > 0)
                || draft.OperationOverrides.Count > 0
                || draft.Processes.Count > 0;
            var operationMissing = hasOperations
                ? Array.Empty<string>()
                : new[] { "至少让一种用品具备操作，或配置一个科学过程" };
            var teachingMissing = draft.TeachingItems.Count == 0
                ? new[] { "至少配置一个教学目标或错误后果" }
                : Array.Empty<string>();
            var reviewMissing = draft.AcceptanceRecords.Count == 0
                ? new[] { "至少配置一个验收场景记录" }
                : Array.Empty<string>();

            var missingBySection = new IReadOnlyList<string>[]
            {
                courseMissing,
                supplyMissing,
                setupMissing,
                operationMissing,
                teachingMissing,
                reviewMissing
            };
            return SectionNames.Select((name, index) =>
                new CourseAuthoringSectionStatus(
                    name,
                    missingBySection[index])).ToArray();
        }

        private static void Missing(
            ICollection<string> target,
            string value,
            string displayName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                target.Add("填写" + displayName);
            }
        }

        private static string ChineseNumber(int value)
        {
            var digits = new[]
            {
                "零", "一", "二", "三", "四", "五", "六", "七", "八", "九"
            };
            if (value < 10) return digits[value];
            if (value < 20) return "十" + (value == 10 ? string.Empty : digits[value % 10]);
            return digits[value / 10] + "十"
                   + (value % 10 == 0 ? string.Empty : digits[value % 10]);
        }
    }
}
