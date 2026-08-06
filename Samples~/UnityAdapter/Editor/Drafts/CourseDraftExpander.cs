using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Unity.Authoring.Drafts
{
    public sealed class CourseDraftExpansionResult
    {
        public CourseDraftExpansionResult(
            CourseBlueprint blueprint,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Blueprint = blueprint;
            Diagnostics = (diagnostics
                ?? throw new ArgumentNullException(nameof(diagnostics)))
                .ToArray();
        }

        public CourseBlueprint Blueprint { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
        public bool IsSuccess => Blueprint != null && Diagnostics.Count == 0;
    }

    /// <summary>
    /// 把作者维护的用品实例与课程差异展开为既有规范化编译器可消费的蓝图。
    /// 模板提供默认值，课程组件行只做增补或覆盖。
    /// </summary>
    public sealed class CourseDraftExpander
    {
        public CourseDraftExpansionResult Expand(
            CourseAuthoringDraft draft,
            CourseAuthoringCatalog catalog,
            RecipeCatalog recipeCatalog)
        {
            if (draft == null)
            {
                throw new ArgumentNullException(nameof(draft));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (recipeCatalog == null)
            {
                throw new ArgumentNullException(nameof(recipeCatalog));
            }

            var diagnostics = catalog.Diagnostics
                .Concat(recipeCatalog.Diagnostics)
                .Concat(RecipeAuthoringOperationContractValidator.Validate(
                    catalog,
                    recipeCatalog))
                .ToList();
            var objects = BuildObjects(draft, catalog, diagnostics);
            ApplyComponentOverrides(draft, catalog, objects, diagnostics);
            var interactions = BuildStandardInteractions(objects, catalog)
                .Concat(BuildOperationOverrides(
                    draft,
                    objects,
                    catalog,
                    diagnostics))
                .OrderBy(value => value.InteractionId, StringComparer.Ordinal)
                .ThenBy(value => value.Order)
                .ThenBy(value => value.Source.Line)
                .ToArray();

            if (diagnostics.Count > 0)
            {
                return new CourseDraftExpansionResult(null, diagnostics);
            }

            var blueprintObjects = objects.Values
                .OrderBy(value => value.Draft.EntityId, StringComparer.Ordinal)
                .Select(value => value.ToBlueprint())
                .ToArray();
            var blueprint = new CourseBlueprint(
                new CourseBlueprintCourse(
                    draft.Course.CourseId,
                    draft.Course.DisplayName,
                    Split(draft.Course.DisciplinePackageId),
                    draft.Course.ActorEntityId,
                    draft.Course.ExperimentPrefabPath,
                    draft.Course.Source),
                blueprintObjects,
                draft.InitialRelations.Select(value =>
                    new CourseInitialRelationBlueprint(
                        value.RelationId,
                        value.RelationType,
                        value.SourceEntityId,
                        value.TargetEntityId,
                        value.SourcePortId,
                        value.TargetPortId,
                        value.Source)),
                interactions,
                BuildProcesses(draft, objects, diagnostics),
                BuildTeaching(draft, objects, diagnostics),
                BuildPresentations(draft, objects, diagnostics),
                BuildAcceptance(draft),
                Array.Empty<CourseAdvancedOverrideBlueprint>());
            return diagnostics.Count == 0
                ? new CourseDraftExpansionResult(blueprint, diagnostics)
                : new CourseDraftExpansionResult(null, diagnostics);
        }

        private static Dictionary<string, ExpandedObject> BuildObjects(
            CourseAuthoringDraft draft,
            CourseAuthoringCatalog catalog,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var result = new Dictionary<string, ExpandedObject>(
                StringComparer.Ordinal);
            foreach (var item in draft.Objects)
            {
                AuthoringItemTemplateDescriptor template = null;
                if (item.EntityType.Length == 0)
                {
                    if (item.EntityId != draft.Course.ActorEntityId)
                    {
                        diagnostics.Add(Diagnostic(
                            "draft.template.missing",
                            item.Source,
                            $"实验对象“{item.EntityId}”没有选择实体类型。",
                            "从用品目录选择一个已注册模板。"));
                    }
                }
                else if (!catalog.TryGetTemplate(item.EntityType, out template))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.template.unknown",
                        item.Source,
                        $"实验对象“{item.EntityId}”使用了未注册实体类型“{item.EntityType}”。",
                        "从所选模块的用品目录重新选择实体类型。"));
                }

                var expanded = new ExpandedObject(item, template);
                if (template != null)
                {
                    foreach (var componentId in template.ComponentIds)
                    {
                        expanded.Features.Add(componentId);
                    }

                    foreach (var pair in template.DefaultParameters)
                    {
                        expanded.SetParameter(
                            ParameterDisplayName(
                                pair.Key,
                                template.ComponentIds,
                                catalog),
                            pair.Value,
                            item.Source);
                    }
                }

                result[item.EntityId] = expanded;
            }

            return result;
        }

        private static void ApplyComponentOverrides(
            CourseAuthoringDraft draft,
            CourseAuthoringCatalog catalog,
            IReadOnlyDictionary<string, ExpandedObject> objects,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var item in draft.Components)
            {
                if (!TrySelect(
                        item.SubjectSelectorKind,
                        item.SubjectSelectorValue,
                        objects.Values,
                        item.Source,
                        diagnostics,
                        out var selected))
                {
                    continue;
                }

                if (!catalog.TryGetComponent(
                        item.ComponentType,
                        out var component))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.component.unknown",
                        item.Source,
                        $"组件“{item.ComponentType}”未由模块注册。",
                        "从组件目录选择一个已注册组件。"));
                    continue;
                }

                var parameters = ParseParameters(
                    item.Parameters,
                    item.Source,
                    diagnostics);
                foreach (var selectedObject in selected)
                {
                    var expanded = selectedObject;
                    expanded.Features.Add(component.ComponentId);
                    foreach (var pair in parameters)
                    {
                        var descriptor = component.Parameters.FirstOrDefault(value =>
                            value.ParameterId == pair.Key
                            || value.DisplayName == pair.Key);
                        if (descriptor == null)
                        {
                            diagnostics.Add(Diagnostic(
                                "draft.component.parameter.unknown",
                                item.Source,
                                $"组件“{component.ComponentId}”没有参数“{pair.Key}”。",
                                "从该组件的参数表单选择参数。"));
                            continue;
                        }

                        expanded.SetParameter(
                            descriptor.DisplayName,
                            pair.Value,
                            item.Source);
                    }
                }
            }
        }

        private static IEnumerable<CourseInteractionRuleBlueprint>
            BuildStandardInteractions(
                IReadOnlyDictionary<string, ExpandedObject> objects,
                CourseAuthoringCatalog catalog)
        {
            foreach (var operation in catalog.Operations)
            {
                var capable = objects.Values
                    .Where(value => value.Template != null
                        && value.Template.OperationIds.Contains(
                            operation.OperationId,
                            StringComparer.Ordinal))
                    .OrderBy(value => value.Draft.EntityId, StringComparer.Ordinal)
                    .ToArray();
                var sources = capable.Where(value =>
                        value.Features.Contains("可" + operation.OperationId + "来源"))
                    .ToArray();
                var targets = capable.Where(value =>
                        value.Features.Contains("可被" + operation.OperationId)
                        || value.Features.Contains(
                            "可" + operation.OperationId + "目标"))
                    .ToArray();
                if (sources.Length > 0 && targets.Length > 0)
                {
                    foreach (var source in sources)
                    foreach (var target in targets.Where(value =>
                                 value.Draft.EntityId != source.Draft.EntityId))
                    {
                        yield return Interaction(
                            "标准." + operation.OperationId + "."
                            + source.Draft.EntityId + "." + target.Draft.EntityId,
                            "标准操作",
                            operation.OperationId,
                            source.Draft.EntityId,
                            target.Draft.EntityId,
                            0,
                            source.Draft.Source);
                    }

                    continue;
                }

                foreach (var source in capable)
                {
                    yield return Interaction(
                        "标准." + operation.OperationId + "." + source.Draft.EntityId,
                        "标准操作",
                        operation.OperationId,
                        source.Draft.EntityId,
                        string.Empty,
                        0,
                        source.Draft.Source);
                }
            }
        }

        private static IEnumerable<CourseInteractionRuleBlueprint>
            BuildOperationOverrides(
                CourseAuthoringDraft draft,
                IReadOnlyDictionary<string, ExpandedObject> objects,
                CourseAuthoringCatalog catalog,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var consequenceIds = new HashSet<string>(
                catalog.Options
                    .Where(value => value.Kind
                        == AuthoringOptionKind.ConsequenceTemplate)
                    .Select(value => value.OptionId),
                StringComparer.Ordinal);
            foreach (var item in draft.OperationOverrides)
            {
                if (!catalog.TryGetOperation(item.OperationId, out var operation))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.operation.unknown",
                        item.Source,
                        $"操作“{item.OperationId}”未由模块注册。",
                        "从抽象操作目录选择一个已注册操作。"));
                    continue;
                }

                if (item.ConsequenceTemplateId.Length > 0
                    && !consequenceIds.Contains(item.ConsequenceTemplateId))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.consequence-template.unknown",
                        item.Source,
                        $"后果模板“{item.ConsequenceTemplateId}”未由模块注册。",
                        "从后果模板选项中选择，不要填写状态操作协议。"));
                    continue;
                }

                if (!TrySelect(item.SourceSelectorKind, item.SourceSelectorValue,
                        objects.Values, item.Source, diagnostics, out var sources))
                {
                    continue;
                }

                if (!TrySelectOptional(
                        item.TargetSelectorKind,
                        item.TargetSelectorValue,
                        objects.Values,
                        item.Source,
                        diagnostics,
                        out var targets))
                {
                    continue;
                }

                if (!TrySelectOptional(
                        item.SubjectSelectorKind,
                        item.SubjectSelectorValue,
                        objects.Values,
                        item.Source,
                        diagnostics,
                        out var subjects))
                {
                    continue;
                }

                foreach (var source in sources)
                foreach (var target in targets)
                foreach (var subject in subjects)
                {
                    yield return Interaction(
                        string.Join(".", new[]
                        {
                            item.OverrideId,
                            source.Draft.EntityId,
                            target?.Draft.EntityId ?? string.Empty,
                            subject?.Draft.EntityId ?? string.Empty
                        }.Where(value => value.Length > 0)),
                        item.Handling,
                        DefaultActionCommand(operation),
                        source.Draft.EntityId,
                        target?.Draft.EntityId ?? string.Empty,
                        item.Order,
                        item.Source,
                        item.Fact,
                        item.Comparison,
                        item.Value,
                        item.Unit,
                        item.RejectionMessage,
                        item.ConsequenceTemplateId,
                        subject?.Draft.EntityId ?? string.Empty);
                }
            }
        }

        /// <summary>
        /// 课程特例约束一次操作的进入条件：持续和操纵操作约束开始阶段，
        /// 即时操作约束唯一的完成阶段。作者无需填写阶段协议。
        /// </summary>
        private static string DefaultActionCommand(
            AuthoringOperationDescriptor operation)
        {
            if (operation.Lifecycle == AuthoringOperationLifecycle.Instant)
            {
                return First(
                    operation.CompletionActionId,
                    operation.StartActionId,
                    operation.ObservationActionId);
            }

            return First(
                operation.StartActionId,
                operation.ObservationActionId,
                operation.CompletionActionId);
        }

        private static string First(params string[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;

        private static IEnumerable<CourseDisciplineProcessBlueprint>
            BuildProcesses(
                CourseAuthoringDraft draft,
                IReadOnlyDictionary<string, ExpandedObject> objects,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var item in draft.Processes)
            {
                if (!TrySelect(item.SubjectSelectorKind, item.SubjectSelectorValue,
                        objects.Values, item.Source, diagnostics, out var subjects))
                {
                    continue;
                }

                if (!TrySelectOptional(
                        item.SourceSelectorKind,
                        item.SourceSelectorValue,
                        objects.Values,
                        item.Source,
                        diagnostics,
                        out var sources)
                    || !TrySelectOptional(
                        item.TargetSelectorKind,
                        item.TargetSelectorValue,
                        objects.Values,
                        item.Source,
                        diagnostics,
                        out var targets))
                {
                    continue;
                }

                foreach (var subject in subjects)
                foreach (var source in sources)
                foreach (var target in targets)
                {
                    var subjectId = subject.Draft.EntityId;
                    var sourceId = source?.Draft.EntityId ?? string.Empty;
                    var targetId = target?.Draft.EntityId ?? string.Empty;
                    var values = Values(item.Source,
                        Pair("定义ID", string.Join(".", new[]
                        {
                            item.ProcessId,
                            subjectId,
                            sourceId,
                            targetId
                        }.Where(value => value.Length > 0))),
                        Pair("记录类型", "开始"),
                        Pair("学科配方", item.ProcessType),
                        Pair("主体", subjectId),
                        Pair("来源", sourceId),
                        Pair("目标", targetId),
                        Pair("启动交互", string.Empty),
                        Pair("停止交互", string.Empty));
                    var parameters = CourseBlueprint.ReadOnlyValues(
                        ParseParameters(item.Parameters, item.Source, diagnostics)
                             .Select(pair => new KeyValuePair<string, BlueprintValue>(
                                 "参数." + pair.Key,
                                 new BlueprintValue(
                                     "参数." + pair.Key,
                                     pair.Value,
                                     item.Source))));
                    yield return new CourseDisciplineProcessBlueprint(
                        values,
                        item.Source,
                        parameters);
                }
            }
        }

        private static IEnumerable<CourseTeachingEvaluationBlueprint>
            BuildTeaching(
                CourseAuthoringDraft draft,
                IReadOnlyDictionary<string, ExpandedObject> objects,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var item in draft.TeachingItems)
            {
                var conditions = draft.TeachingConditions
                    .Where(value => value.TeachingItemId == item.TeachingItemId)
                    .DefaultIfEmpty(null);
                foreach (var condition in conditions)
                {
                    if (!TrySelectOptional(
                            condition?.SubjectSelectorKind ?? string.Empty,
                            condition?.SubjectSelectorValue ?? string.Empty,
                            objects.Values,
                            condition?.Source ?? item.Source,
                            diagnostics,
                            out var subjects))
                    {
                        continue;
                    }

                    foreach (var subject in subjects)
                    {
                        var subjectId = subject?.Draft.EntityId ?? string.Empty;
                        var recordSource = condition?.Source ?? item.Source;
                        yield return new CourseTeachingEvaluationBlueprint(
                            Values(recordSource,
                            // 同一教学项的多条条件必须汇聚到同一目标或风险，
                            // 不能把条件主体拼进标识，否则“两个集气瓶均完成”会被拆成两个目标。
                            Pair("评价ID", TeachingDefinitionId(item)),
                            Pair("类型", item.Type),
                            Pair("显示名称", item.DisplayName),
                            Pair("触发类型", item.TriggerType),
                            Pair("触发值", item.TriggerValue),
                            Pair("顺序", condition?.Order.ToString(
                                CultureInfo.InvariantCulture) ?? "0"),
                            Pair("条件类型", string.Empty),
                            Pair("主体", subjectId),
                            Pair("字段", condition?.Fact ?? string.Empty),
                            Pair("比较", condition?.Comparison ?? string.Empty),
                            Pair("值", condition?.Value ?? string.Empty),
                            Pair("单位", condition?.Unit ?? string.Empty),
                            Pair("分值变化", item.ScoreDelta.ToString(
                                CultureInfo.InvariantCulture)),
                            Pair("提示文案", item.Prompt),
                            Pair("后果严重度", item.ErrorSeverity),
                            Pair("发生后如何继续", item.Continuation),
                            Pair("受影响目标", AffectedGoalIds(item.AffectedGoals))),
                            recordSource);
                    }
                }
            }
        }

        private static string TeachingDefinitionId(CourseDraftTeachingItem item) =>
            (item.Type == "目标" ? "目标." : item.Type == "风险" ? "风险." : string.Empty)
            + item.TeachingItemId;

        private static string AffectedGoalIds(string configured) =>
            string.Join(
                "|",
                (configured ?? string.Empty)
                    .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .Select(value => value == "整个实验" ? value : "目标." + value));

        private static IEnumerable<CoursePresentationOverrideBlueprint>
            BuildPresentations(
                CourseAuthoringDraft draft,
                IReadOnlyDictionary<string, ExpandedObject> objects,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var item in draft.Presentations)
            {
                if (!ValidatePresentationTriggerEntities(
                        item,
                        objects,
                        diagnostics))
                {
                    continue;
                }

                if (!TrySelect(item.SubjectSelectorKind, item.SubjectSelectorValue,
                        objects.Values, item.Source, diagnostics, out var subjects))
                {
                    continue;
                }

                foreach (var subject in subjects)
                {
                    var parameters = ParseParameters(
                            item.Parameters,
                            item.Source,
                            diagnostics)
                        .DefaultIfEmpty(new KeyValuePair<string, string>(
                            string.Empty,
                            string.Empty));
                    foreach (var parameter in parameters)
                    {
                        yield return new CoursePresentationOverrideBlueprint(
                            Values(item.Source,
                            Pair("覆盖ID", item.PresentationId + "." + subject.Draft.EntityId),
                            Pair("触发类型", item.TriggerType),
                            Pair("触发值", item.TriggerValue),
                            Pair("触发来源", item.TriggerSourceEntityId),
                            Pair("触发目标", item.TriggerTargetEntityId),
                            Pair("对象或状态", subject.Draft.EntityId),
                            Pair("表现原语", item.Signal),
                            Pair("作用位置", item.Location),
                            Pair("位置ID", item.LocationId),
                            Pair("参数名", parameter.Key),
                            Pair("参数类型", ParameterType(parameter.Value)),
                            Pair("参数值", parameter.Value)),
                            item.Source);
                    }
                }
            }
        }

        /// <summary>
        /// 触发来源和目标描述语义动作上下文，仅动作类触发能够使用。
        /// 这里显式校验实体引用，避免运行时把拼写错误默认为“任意对象”。
        /// </summary>
        private static bool ValidatePresentationTriggerEntities(
            CourseDraftPresentation item,
            IReadOnlyDictionary<string, ExpandedObject> objects,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var hasSource = !string.IsNullOrWhiteSpace(
                item.TriggerSourceEntityId);
            var hasTarget = !string.IsNullOrWhiteSpace(
                item.TriggerTargetEntityId);
            if (!hasSource && !hasTarget)
            {
                return true;
            }

            if (!CoursePresentationTriggerNames.SupportsActionEntities(
                    item.TriggerType))
            {
                var columnName = hasSource
                    ? CourseAuthoringColumns.Presentation.TriggerSource
                    : CourseAuthoringColumns.Presentation.TriggerTarget;
                diagnostics.Add(Diagnostic(
                    "draft.presentation-trigger.entity-not-applicable",
                    item.Source,
                    $"表现“{item.PresentationId}”的触发类型“{item.TriggerType}”没有动作来源或目标。",
                    "仅动作成功、动作拒绝或可用性变化可以填写触发来源和触发目标。",
                    columnName));
                return false;
            }

            var isValid = true;
            isValid &= ValidatePresentationTriggerEntity(
                item,
                item.TriggerSourceEntityId,
                CourseAuthoringColumns.Presentation.TriggerSource,
                objects,
                diagnostics);
            isValid &= ValidatePresentationTriggerEntity(
                item,
                item.TriggerTargetEntityId,
                CourseAuthoringColumns.Presentation.TriggerTarget,
                objects,
                diagnostics);
            return isValid;
        }

        private static bool ValidatePresentationTriggerEntity(
            CourseDraftPresentation item,
            string entityId,
            string columnName,
            IReadOnlyDictionary<string, ExpandedObject> objects,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(entityId)
                || objects.ContainsKey(entityId))
            {
                return true;
            }

            diagnostics.Add(Diagnostic(
                "draft.presentation-trigger.entity-missing",
                item.Source,
                $"表现“{item.PresentationId}”的{columnName}“{entityId}”不存在。",
                "从当前课程的实验对象中选择，或留空表示任意对象。",
                columnName));
            return false;
        }

        private static string ParameterType(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            if (string.Equals(value, "是", StringComparison.Ordinal)
                || string.Equals(value, "否", StringComparison.Ordinal))
            {
                return "布尔";
            }

            return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _)
                ? "数值"
                : "文本";
        }

        private static IEnumerable<CourseAcceptanceRecordBlueprint>
            BuildAcceptance(CourseAuthoringDraft draft) =>
            draft.AcceptanceRecords.Select(item =>
                new CourseAcceptanceRecordBlueprint(
                    Values(item.Source,
                        Pair("场景ID", item.ScenarioId),
                        Pair("顺序", item.Order.ToString(CultureInfo.InvariantCulture)),
                        Pair("记录类型", item.RecordType),
                        Pair("动作", item.OperationId),
                        Pair("来源", item.SourceEntityId),
                        Pair("目标", item.TargetEntityId),
                        Pair("参数名", item.ParameterName),
                        Pair("参数值", item.ParameterValue),
                        Pair("断言类型", item.AssertionType),
                        Pair("对象", item.ObjectId),
                        Pair("字段", item.Fact),
                        Pair("比较", item.Comparison),
                        Pair("期望值", item.ExpectedValue),
                        Pair("单位", item.Unit)),
                    item.Source));

        private static CourseInteractionRuleBlueprint Interaction(
            string id,
            string handling,
            string operation,
            string source,
            string target,
            int order,
            ConfigurationSource origin,
            string fact = "",
            string comparison = "",
            string expected = "",
            string unit = "",
            string rejection = "",
            string consequence = "",
            string requirementSubject = "") =>
            new CourseInteractionRuleBlueprint(
                Values(origin,
                    Pair("交互ID", id),
                    Pair("处理方式", handling),
                    Pair("动作", operation),
                    Pair("来源", source),
                    Pair("目标", target),
                    Pair("顺序", order.ToString(CultureInfo.InvariantCulture)),
                    Pair("要求类型", string.Empty),
                    Pair("要求主体", requirementSubject),
                    Pair("字段", fact),
                    Pair("比较", comparison),
                    Pair("值", expected),
                    Pair("单位", unit),
                    Pair("结果配方", string.Empty),
                    Pair("反馈配方", string.Empty),
                    Pair("拒绝文案", rejection),
                    Pair("后果模板", consequence)),
                origin);

        private static bool TrySelect(
            string kind,
            string value,
            IEnumerable<ExpandedObject> objects,
            ConfigurationSource source,
            ICollection<CourseCompilationDiagnostic> diagnostics,
            out ExpandedObject[] selected)
        {
            selected = Array.Empty<ExpandedObject>();
            if (!CourseSelector.TryParse(kind, value, out var selector))
            {
                diagnostics.Add(Diagnostic(
                    "draft.selector.invalid",
                    source,
                    $"选择方式“{kind}”或选择值“{value}”无效。",
                    "选择实体、类型、角色或标签，并填写非空选择值。"));
                return false;
            }

            selected = objects
                .Where(item => selector.Matches(
                    item.Draft.EntityId,
                    item.Draft.EntityType,
                    item.Roles,
                    item.Tags))
                .OrderBy(item => item.Draft.EntityId, StringComparer.Ordinal)
                .GroupBy(item => item.Draft.EntityId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            if (selected.Length > 0)
            {
                return true;
            }

            diagnostics.Add(Diagnostic(
                "draft.selector.empty",
                source,
                $"选择器“{kind}:{value}”没有匹配到实验对象。",
                "检查对象实体类型、角色、标签或实例标识。"));
            return false;
        }

        private static bool TrySelectOptional(
            string kind,
            string value,
            IEnumerable<ExpandedObject> objects,
            ConfigurationSource source,
            ICollection<CourseCompilationDiagnostic> diagnostics,
            out ExpandedObject[] selected)
        {
            var hasKind = !string.IsNullOrWhiteSpace(kind);
            var hasValue = !string.IsNullOrWhiteSpace(value);
            if (!hasKind && !hasValue)
            {
                selected = new ExpandedObject[] { null };
                return true;
            }

            return TrySelect(
                kind,
                value,
                objects,
                source,
                diagnostics,
                out selected);
        }

        private static IReadOnlyList<KeyValuePair<string, string>>
            ParseParameters(
                string configured,
                ConfigurationSource source,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var item in Split(configured))
            {
                var separator = item.IndexOf('=');
                if (separator <= 0)
                {
                    diagnostics.Add(Diagnostic(
                        "draft.parameters.invalid",
                        source,
                        $"参数项“{item}”不是“名称=值”格式。",
                        "使用分号分隔参数，并为每项填写名称和值。"));
                    continue;
                }

                result.Add(new KeyValuePair<string, string>(
                    item.Substring(0, separator).Trim(),
                    item.Substring(separator + 1).Trim()));
            }

            return result;
        }

        private static string ParameterDisplayName(
            string parameterId,
            IEnumerable<string> componentIds,
            CourseAuthoringCatalog catalog) =>
            componentIds
                .Select(id => catalog.TryGetComponent(id, out var component)
                    ? component
                    : null)
                .Where(value => value != null)
                .SelectMany(value => value.Parameters)
                .Where(value => value.ParameterId == parameterId)
                .Select(value => value.DisplayName)
                .FirstOrDefault() ?? parameterId;

        private static BlueprintVector3 Vector(string configured)
        {
            var parts = (configured ?? string.Empty).Split('|');
            return parts.Length == 3
                && double.TryParse(parts[0], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var y)
                && double.TryParse(parts[2], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var z)
                ? new BlueprintVector3(x, y, z)
                : new BlueprintVector3(0d, 0d, 0d);
        }

        private static IReadOnlyList<string> Split(string configured) =>
            CourseSelector.List(configured);

        private static KeyValuePair<string, string> Pair(
            string key,
            string value) => new KeyValuePair<string, string>(key, value ?? string.Empty);

        private static IReadOnlyDictionary<string, BlueprintValue> Values(
            ConfigurationSource source,
            params KeyValuePair<string, string>[] values) =>
            CourseBlueprint.ReadOnlyValues(values.Select(value =>
                new KeyValuePair<string, BlueprintValue>(
                    value.Key,
                    new BlueprintValue(value.Key, value.Value, source))));

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            ConfigurationSource source,
            string reason,
            string suggestion,
            string columnName = "") =>
            new CourseCompilationDiagnostic(
                code,
                source.FileName,
                source.Line,
                source.Column,
                columnName,
                source.ConfigurationId,
                reason,
                suggestion,
                CourseDiagnosticSeverity.Error,
                new ConfigurationProvenance(
                    source.ConfigurationId,
                    new[] { source }),
                new CourseDiagnosticTarget(
                    source.FileName,
                    source.ConfigurationId,
                    columnName,
                    string.IsNullOrWhiteSpace(columnName)
                        ? CourseDiagnosticActionIds.LocateConfiguration
                        : CourseDiagnosticActionIds.SelectPresentation));

        private sealed class ExpandedObject
        {
            public ExpandedObject(
                CourseDraftObject draft,
                AuthoringItemTemplateDescriptor template)
            {
                Draft = draft;
                Template = template;
                Features = new HashSet<string>(StringComparer.Ordinal);
                Parameters = new Dictionary<string, BlueprintValue>(
                    StringComparer.Ordinal);
                Roles = new HashSet<string>(
                    (template?.SuggestedRoles ?? Array.Empty<string>())
                    .Concat(Split(draft.Roles)),
                    StringComparer.Ordinal);
                Tags = new HashSet<string>(
                    (template?.SuggestedTags ?? Array.Empty<string>())
                    .Concat(Split(draft.Tags)),
                    StringComparer.Ordinal);
            }

            public CourseDraftObject Draft { get; }
            public AuthoringItemTemplateDescriptor Template { get; }
            public HashSet<string> Features { get; }
            public Dictionary<string, BlueprintValue> Parameters { get; }
            public HashSet<string> Roles { get; }
            public HashSet<string> Tags { get; }

            public void SetParameter(
                string displayName,
                string value,
                ConfigurationSource source)
            {
                var key = "参数." + displayName;
                Parameters[key] = new BlueprintValue(key, value, source);
            }

            public CourseObjectBlueprint ToBlueprint() =>
                new CourseObjectBlueprint(
                    Draft.EntityId,
                    Draft.DisplayName,
                    Features.OrderBy(value => value, StringComparer.Ordinal),
                    Vector(Draft.InitialPosition),
                    Vector(Draft.InitialRotation),
                    CourseBlueprint.ReadOnlyValues(Parameters),
                    Draft.Source,
                    Draft.EntityType,
                    Roles.OrderBy(value => value, StringComparer.Ordinal),
                    Tags.OrderBy(value => value, StringComparer.Ordinal));
        }
    }
}
