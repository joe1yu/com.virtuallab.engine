using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Normalized
{
    public sealed class NormalizedCourseValidationResult
    {
        internal NormalizedCourseValidationResult(
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Diagnostics = diagnostics.ToArray();
        }

        public bool IsSuccess => Diagnostics.All(value =>
            value.Severity != CourseDiagnosticSeverity.Error);
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// 在运行时定义构造之前校验规范化引用和注册表，统一返回作者可理解的诊断。
    /// </summary>
    public sealed class NormalizedCourseValidator
    {
        public NormalizedCourseValidationResult Validate(
            NormalizedCourseModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            var diagnostics = new List<CourseCompilationDiagnostic>();
            var items = model.AllItems()
                .OrderBy(value => value.GeneratedItemId, StringComparer.Ordinal)
                .ToArray();
            ValidateIdentities(items, diagnostics);
            ValidateProvenance(items, diagnostics);

            var entityIds = Ids(model.Entities);
            var ruleIds = Ids(model.Rules);
            var mutationIds = Ids(model.StateChanges);
            var eventIds = Ids(model.DomainEvents);
            var resultGroupIds = Ids(model.ActionResultGroups);
            var effectIds = Ids(model.PresentationEffects);
            var presentationGroupIds = Ids(model.PresentationGroups);
            var knownUnits = new HashSet<string>(
                model.KnownUnitIds,
                StringComparer.Ordinal);
            var knownOperations = new HashSet<string>(
                model.KnownOperationIds,
                StringComparer.Ordinal);
            var knownProtocols = new HashSet<string>(
                model.KnownPresentationProtocolIds,
                StringComparer.Ordinal);

            foreach (var item in model.Ports)
            {
                RequireReference(
                    item.Definition.EntityId,
                    entityIds,
                    "normalized.reference.entity-missing",
                    "端口所属实体",
                    item,
                    diagnostics);
            }

            foreach (var item in model.Actions)
            {
                var action = item.Definition;
                RequireReference(
                    action.SourceEntityId,
                    entityIds,
                    "normalized.reference.entity-missing",
                    "来源实体",
                    item,
                    diagnostics);
                OptionalReference(
                    action.TargetEntityId,
                    entityIds,
                    "normalized.reference.entity-missing",
                    "目标实体",
                    item,
                    diagnostics);
                RequireReferences(
                    action.RuleIds,
                    ruleIds,
                    "normalized.reference.rule-missing",
                    "规则",
                    item,
                    diagnostics);
                OptionalReference(
                    action.ResultGroupId,
                    resultGroupIds,
                    "normalized.reference.result-group-missing",
                    "动作结果组",
                    item,
                    diagnostics);
                OptionalReference(
                    action.PresentationGroupId,
                    presentationGroupIds,
                    "normalized.reference.presentation-group-missing",
                    "表现组",
                    item,
                    diagnostics);
            }

            foreach (var item in model.Rules)
            {
                var unitId = item.Definition.UnitId;
                if (!string.IsNullOrWhiteSpace(unitId)
                    && !knownUnits.Contains(unitId))
                {
                    diagnostics.Add(Diagnostic(
                        "normalized.unit.unknown",
                        item,
                        $"规则“{item.DefinitionId}”使用了未注册单位“{unitId}”。",
                        "在平台或学科包注册单位，或改用已有单位 ID。"));
                }
            }

            foreach (var item in model.StateChanges)
            {
                var operationId = item.Definition.OperationId;
                if (!knownOperations.Contains(operationId))
                {
                    diagnostics.Add(Diagnostic(
                        "normalized.operation.unknown",
                        item,
                        $"状态变化“{item.DefinitionId}”使用了未注册操作“{operationId}”。",
                        "使用显式注册的状态操作协议。"));
                }
            }

            foreach (var item in model.ActionResultGroups)
            {
                RequireReadableGroupId(item, diagnostics);
                RequireReferences(
                    item.Definition.MutationIds,
                    mutationIds,
                    "normalized.reference.mutation-missing",
                    "状态变化",
                    item,
                    diagnostics);
                RequireReferences(
                    item.Definition.EventIds,
                    eventIds,
                    "normalized.reference.event-missing",
                    "领域事件",
                    item,
                    diagnostics);
            }

            foreach (var item in model.PresentationStates)
            {
                RequireReference(
                    item.Definition.SubjectEntityId,
                    entityIds,
                    "normalized.reference.entity-missing",
                    "表现状态主体",
                    item,
                    diagnostics);
                RequireReferences(
                    item.Definition.RuleIds,
                    ruleIds,
                    "normalized.reference.rule-missing",
                    "表现状态规则",
                    item,
                    diagnostics);
                OptionalReference(
                    item.Definition.ContextTargetEntityId,
                    entityIds,
                    "normalized.reference.entity-missing",
                    "表现状态目标实体",
                    item,
                    diagnostics);
            }

            foreach (var item in model.PresentationEffects)
            {
                var protocolId = item.Definition.ProtocolId;
                if (!knownProtocols.Contains(protocolId))
                {
                    diagnostics.Add(Diagnostic(
                        "normalized.presentation-protocol.unknown",
                        item,
                        $"表现效果“{item.DefinitionId}”使用了未注册协议“{protocolId}”。",
                        "在统一表现目录注册协议和参数契约。"));
                }
            }

            foreach (var item in model.PresentationGroups)
            {
                RequireReadableGroupId(item, diagnostics);
                RequireReferences(
                    item.Definition.EffectIds,
                    effectIds,
                    "normalized.reference.effect-missing",
                    "表现效果",
                    item,
                    diagnostics);
            }

            foreach (var item in model.Evaluations)
            {
                RequireReferences(
                    item.Definition.ConditionRuleIds,
                    ruleIds,
                    "normalized.reference.rule-missing",
                    "评价条件规则",
                    item,
                    diagnostics);
                foreach (var condition in item.Definition.Conditions)
                {
                    OptionalReference(
                        condition.SubjectEntityId,
                        entityIds,
                        "normalized.reference.entity-missing",
                        "评价条件主体",
                        item,
                        diagnostics);
                }
            }

            foreach (var item in model.TeachingGoals)
            {
                ValidateEvaluationConditions(
                    item.Definition.Conditions,
                    ruleIds,
                    entityIds,
                    item,
                    diagnostics);
            }

            foreach (var item in model.TeachingRisks)
            {
                ValidateEvaluationConditions(
                    item.Definition.Conditions,
                    ruleIds,
                    entityIds,
                    item,
                    diagnostics);
            }

            foreach (var item in model.AcceptanceScenarios)
            {
                foreach (var step in item.Definition.Steps)
                {
                    if (step.ActionRequest != null)
                    {
                        RequireReference(
                            step.ActionRequest.SourceEntityId,
                            entityIds,
                            "normalized.reference.entity-missing",
                            "验收动作来源",
                            item,
                            diagnostics);
                        OptionalReference(
                            step.ActionRequest.TargetEntityId,
                            entityIds,
                            "normalized.reference.entity-missing",
                            "验收动作目标",
                            item,
                            diagnostics);
                    }

                    foreach (var assertion in step.Assertions)
                    {
                        OptionalReference(
                            assertion.ObjectId,
                            entityIds,
                            "normalized.reference.entity-missing",
                            "验收断言对象",
                            item,
                            diagnostics);
                        if (!string.IsNullOrWhiteSpace(assertion.UnitId)
                            && !knownUnits.Contains(assertion.UnitId))
                        {
                            diagnostics.Add(Diagnostic(
                                "normalized.unit.unknown",
                                item,
                                $"验收断言使用了未注册单位“{assertion.UnitId}”。",
                                "在平台或学科包注册单位，或改用已有单位 ID。"));
                        }
                    }
                }
            }

            foreach (var item in model.PrefabContracts)
            {
                RequireReference(
                    item.Definition.EntityId,
                    entityIds,
                    "normalized.reference.entity-missing",
                    "实体视图契约实体",
                    item,
                    diagnostics);
            }

            var ordered = diagnostics
                .OrderBy(value => value.FileName, StringComparer.Ordinal)
                .ThenBy(value => value.Line)
                .ThenBy(value => value.ConfigurationId, StringComparer.Ordinal)
                .ThenBy(value => value.Code, StringComparer.Ordinal)
                .ToArray();
            return new NormalizedCourseValidationResult(ordered);
        }

        private static void ValidateEvaluationConditions(
            IEnumerable<NormalizedEvaluationConditionDefinition> conditions,
            ISet<string> ruleIds,
            ISet<string> entityIds,
            INormalizedItem item,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var condition in conditions)
            {
                RequireReference(
                    condition.RuleId,
                    ruleIds,
                    "normalized.reference.rule-missing",
                    "评价条件规则",
                    item,
                    diagnostics);
                OptionalReference(
                    condition.SubjectEntityId,
                    entityIds,
                    "normalized.reference.entity-missing",
                    "评价条件主体",
                    item,
                    diagnostics);
            }
        }

        private static void ValidateIdentities(
            IReadOnlyList<INormalizedItem> items,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var item in items.Where(value =>
                         string.IsNullOrWhiteSpace(value.DefinitionId)))
            {
                diagnostics.Add(Diagnostic(
                    "normalized.id.missing",
                    item,
                    "规范化定义 ID 不能为空。",
                    "使用稳定且可读的中文定义 ID。"));
            }

            foreach (var duplicate in items
                         .Where(value => !string.IsNullOrWhiteSpace(
                             value.DefinitionId))
                         .GroupBy(
                             value => value.DefinitionId,
                             StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "normalized.id.duplicate",
                    duplicate.First(),
                    $"规范化定义 ID“{duplicate.Key}”重复。",
                    "调整配方局部键或课程配置 ID，保证生成定义全局唯一。"));
            }

            foreach (var duplicate in items
                         .GroupBy(
                             value => value.GeneratedItemId,
                             StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "normalized.generated-id.duplicate",
                    duplicate.First(),
                    $"结构化生成身份“{duplicate.Key}”重复。",
                    "检查配方 ID、来源/目标实体、定义类型和操作名称。"));
            }
        }

        private static void ValidateProvenance(
            IEnumerable<INormalizedItem> items,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var item in items)
            {
                if (item.Provenance == null
                    || item.Provenance.Sources.Count == 0)
                {
                    diagnostics.Add(Diagnostic(
                        "normalized.provenance.missing",
                        item,
                        $"生成项“{item.DefinitionId}”没有配置来源。",
                        "同时记录课程蓝图行和匹配配方行。"));
                }
                else if (!string.Equals(
                             item.GeneratedItemId,
                             item.Provenance.GeneratedItemId,
                             StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "normalized.provenance.identity-mismatch",
                        item,
                        $"生成项“{item.DefinitionId}”的来源链身份不一致。",
                        "使用同一个 GeneratedItemIdentity 创建定义和来源链。"));
                }
            }
        }

        private static HashSet<string> Ids<T>(
            IEnumerable<NormalizedItem<T>> items)
            where T : INormalizedDefinition =>
            new HashSet<string>(
                items.Select(value => value.DefinitionId),
                StringComparer.Ordinal);

        private static void RequireReference(
            string reference,
            ISet<string> available,
            string code,
            string displayName,
            INormalizedItem item,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(reference)
                && available.Contains(reference))
            {
                return;
            }

            diagnostics.Add(Diagnostic(
                code,
                item,
                $"{displayName}“{reference}”不存在。",
                $"引用当前规范化课程中已生成的{displayName} ID。"));
        }

        private static void OptionalReference(
            string reference,
            ISet<string> available,
            string code,
            string displayName,
            INormalizedItem item,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return;
            }

            RequireReference(
                reference,
                available,
                code,
                displayName,
                item,
                diagnostics);
        }

        private static void RequireReferences(
            IEnumerable<string> references,
            ISet<string> available,
            string code,
            string displayName,
            INormalizedItem item,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var reference in references)
            {
                RequireReference(
                    reference,
                    available,
                    code,
                    displayName,
                    item,
                    diagnostics);
            }
        }

        private static void RequireReadableGroupId(
            INormalizedItem item,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (item.DefinitionId.Any(character =>
                    character >= '\u4e00' && character <= '\u9fff'))
            {
                return;
            }

            diagnostics.Add(Diagnostic(
                "normalized.group-id.unreadable",
                item,
                $"组 ID“{item.DefinitionId}”缺少可读中文含义。",
                "配置表使用自然中文组标识；稳定运行协议由编译器统一生成。"));
        }

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            INormalizedItem item,
            string reason,
            string suggestion)
        {
            var provenance = item.Provenance;
            var source = provenance?.Sources.FirstOrDefault();
            return new CourseCompilationDiagnostic(
                code,
                source?.FileName ?? "规范化模型",
                source?.Line ?? 1,
                source?.Column ?? 1,
                string.Empty,
                source?.ConfigurationId ?? item.DefinitionId,
                reason,
                suggestion,
                CourseDiagnosticSeverity.Error,
                provenance,
                new CourseDiagnosticTarget(
                    source?.FileName ?? "规范化模型",
                    source?.ConfigurationId ?? item.DefinitionId,
                    string.Empty,
                    DiagnosticAction(code)));
        }

        private static string DiagnosticAction(string code)
        {
            switch (code)
            {
                case "normalized.reference.entity-missing":
                    return CourseDiagnosticActionIds.SelectEntity;
                case "normalized.operation.unknown":
                    return CourseDiagnosticActionIds.SelectOperation;
                case "normalized.unit.unknown":
                    return CourseDiagnosticActionIds.SelectUnit;
                case "normalized.presentation-protocol.unknown":
                    return CourseDiagnosticActionIds.SelectPresentation;
                default:
                    return CourseDiagnosticActionIds.LocateConfiguration;
            }
        }
    }
}
