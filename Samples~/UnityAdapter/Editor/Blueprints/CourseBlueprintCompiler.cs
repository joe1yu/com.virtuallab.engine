using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain.Relations;
using VirtualLab.Presentation;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Unity.Authoring.Blueprints
{
    public enum CourseBlueprintCompilationStage
    {
        Read,
        BlueprintValidation,
        RecipeCatalog,
        Expansion,
        Override,
        NormalizedValidation,
        PrefabContract,
        Asset
    }

    public sealed class CourseCompilationSummary
    {
        public CourseCompilationSummary(
            int objectCount,
            int platformRecipeCount,
            int disciplineRecipeCount,
            int automaticInteractionCount,
            int specialInteractionCount,
            int overrideCount,
            int warningCount,
            int errorCount)
        {
            ObjectCount = objectCount;
            PlatformRecipeCount = platformRecipeCount;
            DisciplineRecipeCount = disciplineRecipeCount;
            AutomaticInteractionCount = automaticInteractionCount;
            SpecialInteractionCount = specialInteractionCount;
            OverrideCount = overrideCount;
            WarningCount = warningCount;
            ErrorCount = errorCount;
        }

        public int ObjectCount { get; }
        public int PlatformRecipeCount { get; }
        public int DisciplineRecipeCount { get; }
        public int AutomaticInteractionCount { get; }
        public int SpecialInteractionCount { get; }
        public int OverrideCount { get; }
        public int WarningCount { get; }
        public int ErrorCount { get; }
    }

    public sealed class CourseBlueprintCompilationResult
    {
        internal CourseBlueprintCompilationResult(
            CourseBlueprint blueprint,
            RecipeCatalog catalog,
            NormalizedCourseModel normalized,
            CompiledCourseDefinition domain,
            CoursePresentationDefinition presentation,
            IEnumerable<NormalizedItem<GeneratedCourseArtifact>>
                generatedArtifacts,
            CourseCompilationSummary summary,
            IReadOnlyDictionary<string, ConfigurationProvenance> provenance,
            IEnumerable<CourseBlueprintCompilationStage> stages,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Blueprint = blueprint;
            Catalog = catalog;
            Normalized = normalized;
            Domain = domain;
            Presentation = presentation;
            GeneratedArtifacts = (generatedArtifacts
                                  ?? Array.Empty<
                                      NormalizedItem<GeneratedCourseArtifact>>())
                .ToArray();
            Summary = summary;
            ProvenanceByGeneratedItemId = provenance
                ?? new ReadOnlyDictionary<string, ConfigurationProvenance>(
                    new Dictionary<string, ConfigurationProvenance>());
            Stages = (stages
                      ?? Array.Empty<CourseBlueprintCompilationStage>())
                .ToArray();
            Diagnostics = (diagnostics
                           ?? Array.Empty<CourseCompilationDiagnostic>())
                .OrderBy(value => value.FileName, StringComparer.Ordinal)
                .ThenBy(value => value.Line)
                .ThenBy(value => value.Code, StringComparer.Ordinal)
                .ToArray();
        }

        public bool IsSuccess =>
            Domain != null
            && Presentation != null
            && Diagnostics.All(value =>
                value.Severity != CourseDiagnosticSeverity.Error);

        public CourseBlueprint Blueprint { get; }
        public RecipeCatalog Catalog { get; }
        public NormalizedCourseModel Normalized { get; }
        public CompiledCourseDefinition Domain { get; }
        public CoursePresentationDefinition Presentation { get; }
        public IReadOnlyList<NormalizedItem<GeneratedCourseArtifact>>
            GeneratedArtifacts { get; }
        public CourseCompilationSummary Summary { get; }
        public IReadOnlyDictionary<string, ConfigurationProvenance>
            ProvenanceByGeneratedItemId { get; }
        public IReadOnlyList<CourseBlueprintCompilationStage> Stages { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// 蓝图生产编译的唯一编排入口。每个阶段只消费上一个阶段的内存快照，
    /// 任一错误都会终止后续阶段，避免校验输入与生成输入发生漂移。
    /// </summary>
    public sealed class CourseBlueprintCompiler
    {
        private static readonly CourseBlueprintCompilationStage[] StageOrder =
        {
            CourseBlueprintCompilationStage.Read,
            CourseBlueprintCompilationStage.BlueprintValidation,
            CourseBlueprintCompilationStage.RecipeCatalog,
            CourseBlueprintCompilationStage.Expansion,
            CourseBlueprintCompilationStage.Override,
            CourseBlueprintCompilationStage.NormalizedValidation,
            CourseBlueprintCompilationStage.PrefabContract,
            CourseBlueprintCompilationStage.Asset
        };

        public CourseBlueprintCompilationResult Compile(
            CourseBlueprintSource source,
            IRecipePackageProvider platform,
            IReadOnlyList<IRecipePackageProvider> disciplines)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (platform == null)
            {
                throw new ArgumentNullException(nameof(platform));
            }

            if (disciplines == null)
            {
                throw new ArgumentNullException(nameof(disciplines));
            }

            var stages = new List<CourseBlueprintCompilationStage>();
            var diagnostics = new List<CourseCompilationDiagnostic>();
            CourseBlueprint blueprint = null;
            RecipeCatalog catalog = null;
            NormalizedCourseModel normalized = null;
            CompiledCourseDefinition domain = null;
            CoursePresentationDefinition presentation = null;

            var read = new CourseBlueprintReader().Read(source);
            stages.Add(CourseBlueprintCompilationStage.Read);
            diagnostics.AddRange(read.Diagnostics);
            blueprint = read.Blueprint;
            if (HasErrors(diagnostics))
            {
                return Result();
            }

            ValidateBlueprintPackages(
                blueprint,
                platform,
                disciplines,
                diagnostics);
            stages.Add(CourseBlueprintCompilationStage.BlueprintValidation);
            if (HasErrors(diagnostics))
            {
                return Result();
            }

            catalog = RecipeCatalog.Create(platform, disciplines);
            stages.Add(CourseBlueprintCompilationStage.RecipeCatalog);
            diagnostics.AddRange(catalog.Diagnostics);
            if (HasErrors(diagnostics))
            {
                return Result();
            }

            var expansion = new RecipeExpander().Expand(blueprint, catalog);
            stages.Add(CourseBlueprintCompilationStage.Expansion);
            diagnostics.AddRange(expansion.Diagnostics);
            normalized = expansion.Model;
            if (HasErrors(diagnostics))
            {
                return Result();
            }

            var overridden = new CourseOverrideApplier().Apply(
                blueprint,
                catalog,
                normalized);
            stages.Add(CourseBlueprintCompilationStage.Override);
            diagnostics.AddRange(overridden.Diagnostics);
            normalized = overridden.Model;
            if (HasErrors(diagnostics))
            {
                return Result();
            }

            var normalizedValidation =
                new NormalizedCourseValidator().Validate(normalized);
            stages.Add(CourseBlueprintCompilationStage.NormalizedValidation);
            diagnostics.AddRange(normalizedValidation.Diagnostics);
            if (HasErrors(diagnostics))
            {
                return Result();
            }

            ValidatePrefabContracts(normalized, diagnostics);
            stages.Add(CourseBlueprintCompilationStage.PrefabContract);
            if (HasErrors(diagnostics))
            {
                return Result();
            }

            try
            {
                domain = BuildDomain(
                    blueprint,
                    normalized,
                    RuntimeModuleIds(platform, disciplines),
                    diagnostics);
                presentation = BuildPresentation(normalized, diagnostics);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.asset.build-failed",
                    blueprint.Course.Source,
                    blueprint.Course.CourseId,
                    $"运行时配置构造失败：{exception.Message}",
                    "检查规则字段、动作结果、表现目标和结构化参数。"));
            }

            stages.Add(CourseBlueprintCompilationStage.Asset);
            if (HasErrors(diagnostics))
            {
                domain = null;
                presentation = null;
            }

            return Result();

            CourseBlueprintCompilationResult Result()
            {
                var summary = Summary(
                    blueprint,
                    catalog,
                    normalized,
                    diagnostics);
                return new CourseBlueprintCompilationResult(
                    blueprint,
                    catalog,
                    normalized,
                    domain,
                    presentation,
                    normalized?.GeneratedArtifacts,
                    summary,
                    normalized?.ProvenanceByGeneratedItemId,
                    stages,
                    diagnostics);
            }
        }

        private static void ValidateBlueprintPackages(
            CourseBlueprint blueprint,
            IRecipePackageProvider platform,
            IReadOnlyList<IRecipePackageProvider> disciplines,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var providerIds = disciplines
                .Where(value => value != null)
                .Select(value => value.PackageId)
                .ToArray();
            foreach (var duplicate in providerIds
                         .GroupBy(value => value, StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.discipline-provider.duplicate",
                    blueprint.Course.Source,
                    duplicate.Key,
                    $"学科配方包提供者“{duplicate.Key}”重复。",
                    "每个学科包只传入一个提供者。"));
            }

            foreach (var requested in blueprint.Course.DisciplinePackageIds)
            {
                if (!providerIds.Contains(requested, StringComparer.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.discipline-package.missing",
                        blueprint.Course.Source,
                        requested,
                        $"课程选择了未知的学科类型“{requested}”，系统不知道该使用哪套学科规则。",
                        "请把“学科类型”改为当前项目已经安装的类型，例如“化学基础”。"));
                }
            }

            foreach (var extra in providerIds.Where(value =>
                         !blueprint.Course.DisciplinePackageIds.Contains(
                             value,
                             StringComparer.Ordinal)))
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.discipline-package.unselected",
                    blueprint.Course.Source,
                    extra,
                    $"传入的学科配方包“{extra}”未被课程选择。",
                    "在课程.csv 声明该学科包，或从编译参数移除。"));
            }

            foreach (var provider in new[] { platform }.Concat(disciplines))
            {
                if (provider?.RequiredRuntimeModuleIds == null
                    || provider.RequiredRuntimeModuleIds.Count == 0
                    || provider.RequiredRuntimeModuleIds.Any(
                        string.IsNullOrWhiteSpace))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.runtime-module.missing",
                        blueprint.Course.Source,
                        provider?.PackageId ?? string.Empty,
                        $"配方包“{provider?.PackageId}”没有声明所需运行时模块。",
                        "由配方包提供者声明至少一个稳定的运行时模块标识。"));
                }

                if (provider?.RelationTypeIds == null)
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.relation-types.missing",
                        blueprint.Course.Source,
                        provider?.PackageId ?? string.Empty,
                        $"配方包“{provider?.PackageId}”没有声明关系类型集合。",
                        "没有关系类型时声明空集合，不能返回空引用。"));
                }
            }

            ValidateInitialRelationTypes(
                blueprint,
                new[] { platform }.Concat(disciplines),
                diagnostics);
        }

        private static void ValidateInitialRelationTypes(
            CourseBlueprint blueprint,
            IEnumerable<IRecipePackageProvider> providers,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var ownership = providers
                .Where(value => value?.RelationTypeIds != null)
                .SelectMany(provider => provider.RelationTypeIds.Select(typeId =>
                    new { provider.PackageId, TypeId = typeId }))
                .GroupBy(value => value.TypeId)
                .ToDictionary(value => value.Key, value => value.ToArray());
            foreach (var duplicate in ownership.Where(value =>
                         value.Value.Select(item => item.PackageId)
                             .Distinct(StringComparer.Ordinal)
                             .Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.relation-type.owner-conflict",
                    blueprint.Course.Source,
                    duplicate.Key.Value,
                    $"关系类型“{duplicate.Key}”被多个配方包声明："
                    + string.Join("、", duplicate.Value.Select(value =>
                        value.PackageId).Distinct(StringComparer.Ordinal)),
                    "每个关系类型只由一个平台或学科配方包拥有。"));
            }

            foreach (var relation in blueprint.InitialRelations)
            {
                if (!string.IsNullOrWhiteSpace(relation.RelationTypeId)
                    && ownership.ContainsKey(
                        new RelationTypeId(relation.RelationTypeId)))
                {
                    continue;
                }

                diagnostics.Add(Diagnostic(
                    "blueprint.initial-relation.type-unknown",
                    relation.Source,
                    relation.RelationId,
                    $"初始关系“{relation.RelationId}”使用了未注册的关系类型"
                    + $"“{relation.RelationTypeId}”。",
                    "使用当前平台或已选择学科包声明的稳定关系类型标识。"));
            }
        }

        private static IReadOnlyList<string> RuntimeModuleIds(
            IRecipePackageProvider platform,
            IEnumerable<IRecipePackageProvider> disciplines)
        {
            return new[] { platform }
                .Concat(disciplines)
                .SelectMany(value => value.RequiredRuntimeModuleIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static void ValidatePrefabContracts(
            NormalizedCourseModel normalized,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var duplicateContracts = normalized.PrefabContracts
                .GroupBy(
                    value => value.Definition.ContractId,
                    StringComparer.Ordinal)
                .Where(value => value.Count() > 1);
            foreach (var duplicate in duplicateContracts)
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.prefab-contract.duplicate",
                    duplicate.First().Provenance.Sources.First(),
                    duplicate.Key,
                    $"实体视图契约“{duplicate.Key}”重复。",
                    "每个实体、契约类型和标识只保留一项。"));
            }
        }

        private static CompiledCourseDefinition BuildDomain(
            CourseBlueprint blueprint,
            NormalizedCourseModel model,
            IEnumerable<string> requiredModuleIds,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var runtimeRules = BuildRules(model.Rules, diagnostics);
            var rulesById = runtimeRules.ToDictionary(
                value => value.RuleId,
                StringComparer.Ordinal);
            var mutations = model.StateChanges
                .Select(value => new ConfiguredMutationDefinition(
                    value.Definition.MutationId,
                    value.Definition.OperationId,
                    value.Definition.Parameters.Select(parameter =>
                        new KeyValuePair<string, StructuredValue>(
                            parameter.Key,
                            ParseStructuredValue(parameter.Value)))))
                .ToArray();
            var mutationsById = mutations.ToDictionary(
                value => value.MutationId,
                StringComparer.Ordinal);
            var events = model.DomainEvents
                .Select(value => new CourseDomainEventDefinition(
                    value.Definition.EventId,
                    value.Definition.EventType,
                    Array.Empty<KeyValuePair<string, StructuredValue>>()))
                .ToArray();
            var eventsById = events.ToDictionary(
                value => value.EventId,
                StringComparer.Ordinal);
            var resultGroups = model.ActionResultGroups
                .Select(value => new CourseActionResultGroupDefinition(
                    value.Definition.GroupId,
                    value.Definition.MutationIds,
                    value.Definition.EventIds))
                .ToArray();
            var resultGroupsById = model.ActionResultGroups.ToDictionary(
                value => value.Definition.GroupId,
                StringComparer.Ordinal);

            var configuredActions =
                new List<ConfiguredActionDefinition>();
            foreach (var item in model.Actions)
            {
                var action = item.Definition;
                var actionRules = action.RuleIds
                    .Where(rulesById.ContainsKey)
                    .Select(value => rulesById[value])
                    .ToArray();
                var actionMutations = new List<ConfiguredMutationDefinition>();
                if (!string.IsNullOrWhiteSpace(action.ResultGroupId)
                    && resultGroupsById.TryGetValue(
                        action.ResultGroupId,
                        out var group))
                {
                    actionMutations.AddRange(group.Definition.MutationIds
                        .Where(mutationsById.ContainsKey)
                        .Select(value => mutationsById[value]));
                    foreach (var eventId in group.Definition.EventIds.Where(
                                 eventsById.ContainsKey))
                    {
                        var domainEvent = eventsById[eventId];
                        actionMutations.Add(
                            new ConfiguredMutationDefinition(
                                "发出事件." + domainEvent.EventId,
                                ConfiguredStateOperationIds.EventEmit,
                                new[]
                                {
                                    new KeyValuePair<string, StructuredValue>(
                                        "事件类型",
                                        StructuredValue.FromText(
                                            domainEvent.EventType))
                                }));
                    }
                }

                configuredActions.Add(
                    ConfiguredActionDefinition.CreatePolicy(
                        action.PolicyId,
                        RuntimeActionId(action.ActionId),
                        action.OperationId,
                        action.Lifecycle,
                        action.ExecutionModeId,
                        action.Phase,
                        action.SourceEntityId,
                        action.TargetEntityId,
                        action.Priority,
                        action.PolicyEffect == "禁用"
                            ? ConfiguredActionPolicyEffect.Deny
                            : ConfiguredActionPolicyEffect.Allow,
                        EmptyToNull(action.MessageId),
                        actionRules,
                        actionMutations,
                        string.IsNullOrWhiteSpace(action.RejectionCode)
                            ? action.PolicyId + ".拒绝"
                            : action.RejectionCode));
            }

            var resources = new List<CourseResourceDefinition>
            {
                new CourseResourceDefinition(
                    CourseResourceIds.ExperimentPrefab,
                    blueprint.Course.ExperimentPrefab)
            };

            var entities = model.Entities.Select(value =>
                new CourseEntityDefinition(
                    value.Definition.EntityId,
                    RuntimeCapabilities(
                        value.Definition.FeatureIds))).ToArray();
            var actionPolicies = model.Actions.Select(value =>
                new ActionPolicyDefinition(
                    value.Definition.PolicyId,
                    RuntimeActionId(value.Definition.ActionId),
                    value.Definition.OperationId,
                    value.Definition.Lifecycle,
                    value.Definition.ExecutionModeId,
                    value.Definition.Phase,
                    value.Definition.SourceEntityId,
                    value.Definition.TargetEntityId,
                    value.Definition.RuleIds)).ToArray();
            var goals = model.TeachingGoals.Select(value =>
                new CourseGoalDefinition(
                    value.Definition.GoalId,
                    value.Definition.DisplayName)).ToArray();
            var goalRules = model.TeachingGoals.Select(value =>
                new CourseGoalRuleDefinition(
                    value.Definition.GoalId,
                    value.Definition.Conditions.Select(condition =>
                        new CourseConditionDefinition(
                            "条件." + value.Definition.GoalId + "."
                            + condition.RuleId,
                            blueprint.Course.ActorEntityId,
                            condition.SubjectEntityId,
                            null,
                            Array.Empty<
                                KeyValuePair<string, StructuredValue>>(),
                            rulesById.TryGetValue(
                                condition.RuleId,
                                out var rule)
                                ? new[] { rule }
                                : Array.Empty<StructuredRuleDefinition>()))))
                .ToArray();
            var assessments = model.TeachingScores.Count == 0
                ? Array.Empty<CourseAssessmentDefinition>()
                : new[]
                {
                    new CourseAssessmentDefinition(
                        "评分.课程总分",
                        "课程总分",
                        100)
                };
            var scoresByEvaluation = model.TeachingScores.ToDictionary(
                value => value.Definition.EvaluationId,
                value => value.Definition.ScoreDelta,
                StringComparer.Ordinal);
            var hintsByEvaluation = model.TeachingHints.ToDictionary(
                value => value.Definition.EvaluationId,
                value => value.Definition.Message,
                StringComparer.Ordinal);
            var configuredGoalIds = new HashSet<string>(
                model.TeachingGoals.Select(value => value.Definition.GoalId),
                StringComparer.Ordinal);
            var actionAssessments = model.TeachingRisks
                .Where(value =>
                    value.Definition.TriggerType == "动作拒绝"
                    || value.Definition.TriggerType == "领域事件")
                .Select(value =>
                {
                    var riskConditions = value.Definition.Conditions.Select(
                        condition => new CourseConditionDefinition(
                            "条件." + value.Definition.RiskId + "."
                            + condition.RuleId,
                            blueprint.Course.ActorEntityId,
                            condition.SubjectEntityId,
                            null,
                            Array.Empty<KeyValuePair<
                                string,
                                StructuredValue>>(),
                            rulesById.TryGetValue(
                                condition.RuleId,
                                out var riskRule)
                                ? new[] { riskRule }
                                : Array.Empty<StructuredRuleDefinition>()))
                        .ToArray();
                    var score = scoresByEvaluation.TryGetValue(
                        value.Definition.RiskId,
                        out var configuredScore)
                            ? configuredScore
                            : 0;
                    var prompt = hintsByEvaluation.TryGetValue(
                            value.Definition.RiskId,
                            out var configuredHint)
                        && !string.IsNullOrWhiteSpace(configuredHint)
                            ? configuredHint
                            : value.Definition.DisplayName;
                    var source = value.Provenance.Sources.First();
                    var severity = ConsequenceSeverity(
                        value.Definition.ConsequenceSeverity,
                        source,
                        value.Definition.RiskId,
                        diagnostics);
                    var recoverability = ConsequenceRecoverability(
                        value.Definition.Recoverability,
                        source,
                        value.Definition.RiskId,
                        diagnostics);
                    var blockedGoalIds = value.Definition.BlockedGoalIds
                        .ToArray();
                    foreach (var blockedGoalId in blockedGoalIds.Where(
                                 blockedGoalId =>
                                     !configuredGoalIds.Contains(blockedGoalId)))
                    {
                        diagnostics.Add(Diagnostic(
                            "blueprint.asset.risk-blocked-goal-missing",
                            source,
                            value.Definition.RiskId,
                            $"风险“{value.Definition.RiskId}”引用了不存在的受阻目标“{blockedGoalId}”。",
                            "在实验流程.csv 中填写已有目标步骤 ID。"));
                    }

                    if (recoverability
                            == CourseConsequenceRecoverability
                                .GoalPermanentlyBlocked
                        && blockedGoalIds.Length == 0)
                    {
                        diagnostics.Add(Diagnostic(
                            "blueprint.asset.risk-blocked-goal-required",
                            source,
                            value.Definition.RiskId,
                            $"风险“{value.Definition.RiskId}”声明永久阻断目标，但没有填写受阻目标。",
                            "填写一个或多个以半角分号分隔的目标步骤 ID。"));
                        return null;
                    }

                    if (value.Definition.TriggerType == "领域事件")
                    {
                        if (string.IsNullOrWhiteSpace(
                                value.Definition.TriggerValue))
                        {
                            diagnostics.Add(Diagnostic(
                                "blueprint.asset.risk-event-invalid",
                                value.Provenance.Sources.First(),
                                value.Definition.RiskId,
                                $"风险“{value.Definition.RiskId}”必须填写领域事件类型。",
                                "填写分段的自然中文事件类型。"));
                            return null;
                        }

                        return CourseActionAssessmentDefinition.ForDomainEvent(
                            "事件评价." + value.Definition.RiskId,
                            value.Definition.TriggerValue.Trim(),
                            value.Definition.RiskId,
                            score,
                            prompt,
                            riskConditions,
                            severity,
                            recoverability,
                            blockedGoalIds);
                    }

                    var trigger = value.Definition.TriggerValue.Split(
                        new[] { '|' },
                        2,
                        StringSplitOptions.None);
                    if (trigger.Length != 2
                        || string.IsNullOrWhiteSpace(trigger[0])
                        || string.IsNullOrWhiteSpace(trigger[1]))
                    {
                        diagnostics.Add(Diagnostic(
                            "blueprint.asset.risk-trigger-invalid",
                            value.Provenance.Sources.First(),
                            value.Definition.RiskId,
                            $"风险“{value.Definition.RiskId}”的动作拒绝触发值"
                            + "必须是“动作|拒绝原因”。",
                            "在实验流程.csv 的风险记录中填写语义动作和拒绝原因。"));
                        return null;
                    }

                    return new CourseActionAssessmentDefinition(
                        "动作评价." + value.Definition.RiskId,
                        RuntimeActionId(trigger[0].Trim()),
                        trigger[1].Trim(),
                        value.Definition.RiskId,
                        score,
                        prompt,
                        riskConditions,
                        severity: severity,
                        recoverability: recoverability,
                        blockedGoalIds: blockedGoalIds);
                })
                .Where(value => value != null)
                .ToArray();
            var ports = model.Ports.Select(value =>
                new CoursePortDefinition(
                    value.Definition.PortId,
                    value.Definition.EntityId,
                    value.Definition.CompatibilityGroup)).ToArray();
            var initialRelations = blueprint.InitialRelations.Select(value =>
                new CourseInitialRelationDefinition(
                    value.RelationId,
                    new RelationTypeId(value.RelationTypeId),
                    value.SourceEntityId,
                    value.TargetEntityId,
                    value.SourcePortId,
                    value.TargetPortId)).ToArray();
            var layouts = blueprint.Objects.Select(value =>
                new CourseSceneLayoutDefinition(
                    value.EntityId,
                    value.InitialPosition.X,
                    value.InitialPosition.Y,
                    value.InitialPosition.Z,
                    value.InitialRotation.X,
                    value.InitialRotation.Y,
                    value.InitialRotation.Z)).ToArray();
            var prefabContracts = model.Entities.Select(entity =>
                new CoursePrefabContractDefinition(
                    entity.Definition.EntityId,
                    RuntimeCapabilities(
                        entity.Definition.FeatureIds),
                    model.Ports
                        .Where(port => port.Definition.EntityId
                            == entity.Definition.EntityId)
                        .Select(port => port.Definition.PortId))).ToArray();

            return new CompiledCourseDefinition(
                blueprint.Course.CourseId,
                blueprint.Course.ActorEntityId,
                blueprint.Course.DisciplinePackageIds,
                requiredModuleIds,
                CourseResourceIds.ExperimentPrefab,
                entities,
                actionPolicies,
                goals,
                assessments,
                runtimeRules,
                configuredActions,
                resources,
                ports,
                initialRelations,
                mutations,
                events,
                Array.Empty<CourseContinuousProcessDefinition>(),
                resultGroups,
                goalRules,
                actionAssessments,
                layouts,
                prefabContracts);
        }

        private static CoursePresentationDefinition BuildPresentation(
            NormalizedCourseModel model,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var runtimeRules = BuildRules(model.Rules, diagnostics)
                .ToDictionary(value => value.RuleId, StringComparer.Ordinal);
            var effects = model.PresentationEffects.Select(value =>
            {
                var definition = value.Definition;
                var isGlobalSemanticSubject = string.Equals(
                    definition.SubjectId,
                    CoursePresentationSemanticSubjects.AllActionRejections,
                    StringComparison.Ordinal);
                var isActionTarget = string.Equals(
                    definition.SubjectId,
                    CoursePresentationSemanticSubjects.ActionTarget,
                    StringComparison.Ordinal);
                return new CoursePresentationEffectDefinition(
                    definition.EffectId,
                    definition.ProtocolId,
                    new CoursePresentationTargetDefinition(
                        isGlobalSemanticSubject
                            ? CoursePresentationEntitySelectorKind.Global
                            : isActionTarget
                                ? CoursePresentationEntitySelectorKind
                                    .ActionTarget
                            : string.IsNullOrWhiteSpace(definition.SubjectId)
                                ? CoursePresentationEntitySelectorKind.ActionSource
                                : CoursePresentationEntitySelectorKind.FixedEntity,
                        isGlobalSemanticSubject || isActionTarget
                            ? null
                            : definition.SubjectId,
                        definition.LocationKind
                            ?? CoursePresentationLocationKind.EntityRoot,
                        definition.LocationId),
                    definition.Lifecycle,
                    100,
                    definition.ParameterBindings);
            }).ToArray();

            var groups = model.PresentationGroups.Select(value =>
                new CoursePresentationGroupDefinition(
                    value.Definition.GroupId,
                    value.Definition.EffectIds)).ToList();
            var referencedEffects = new HashSet<string>(
                groups.SelectMany(value => value.EffectIds),
                StringComparer.Ordinal);
            foreach (var effect in effects.Where(value =>
                         !referencedEffects.Contains(value.EffectId)))
            {
                groups.Add(new CoursePresentationGroupDefinition(
                    "表现覆盖组." + effect.EffectId,
                    new[] { effect.EffectId }));
            }

            var rules = model.Actions
                .Where(value => !string.IsNullOrWhiteSpace(
                    value.Definition.PresentationGroupId))
                .Select(value => new CoursePresentationRuleDefinition(
                    "表现规则." + value.Definition.PolicyId,
                    value.Definition.PolicyEffect == "禁用"
                        ? CoursePresentationTriggerKind.ActionRejected
                        : CoursePresentationTriggerKind.ActionAccepted,
                    RuntimeActionId(value.Definition.ActionId),
                    value.Definition.PresentationGroupId,
                    value.Definition.SourceEntityId,
                    string.IsNullOrWhiteSpace(
                        value.Definition.TargetEntityId)
                        ? null
                        : value.Definition.TargetEntityId))
                .ToList();
            var groupByEffect = groups
                .SelectMany(group => group.EffectIds.Select(effectId =>
                    new { effectId, group.GroupId }))
                .GroupBy(value => value.effectId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.First().GroupId,
                    StringComparer.Ordinal);
            foreach (var effect in model.PresentationEffects.Where(value =>
                         value.Definition.TriggerKind.HasValue))
            {
                var definition = effect.Definition;
                if (!groupByEffect.TryGetValue(
                        definition.EffectId,
                        out var groupId))
                {
                    continue;
                }

                if (definition.TriggerKind ==
                        CoursePresentationTriggerKind.ActionRejected
                    && string.Equals(
                        definition.TriggerValue,
                        CoursePresentationSemanticSubjects
                            .AllActionRejections,
                        StringComparison.Ordinal))
                {
                    foreach (var actionId in model.Actions
                                 .Select(value => RuntimeActionId(
                                     value.Definition.ActionId))
                                 .Distinct(StringComparer.Ordinal)
                                 .OrderBy(value => value, StringComparer.Ordinal))
                    {
                        rules.Add(new CoursePresentationRuleDefinition(
                            $"表现触发规则.{definition.EffectId}.{actionId}",
                            definition.TriggerKind.Value,
                            actionId,
                            groupId,
                            NullIfEmpty(definition.TriggerSourceEntityId),
                            NullIfEmpty(definition.TriggerTargetEntityId)));
                    }

                    continue;
                }

                rules.Add(new CoursePresentationRuleDefinition(
                    "表现触发规则." + definition.EffectId,
                    definition.TriggerKind.Value,
                    RuntimePresentationTriggerValue(
                        definition.TriggerKind.Value,
                        definition.TriggerValue),
                    groupId,
                    NullIfEmpty(definition.TriggerSourceEntityId),
                    NullIfEmpty(definition.TriggerTargetEntityId)));
            }

            foreach (var effect in model.PresentationEffects.Where(value =>
                         !value.Definition.TriggerKind.HasValue
                         && !referencedEffects.Contains(
                             value.Definition.EffectId)))
            {
                var groupId = "表现覆盖组." + effect.Definition.EffectId;
                if (string.Equals(
                        effect.Definition.SubjectId,
                        CoursePresentationSemanticSubjects.AllActionRejections,
                        StringComparison.Ordinal))
                {
                    foreach (var actionId in model.Actions
                                 .Select(value => RuntimeActionId(
                                     value.Definition.ActionId))
                                 .Distinct(StringComparer.Ordinal)
                                 .OrderBy(value => value, StringComparer.Ordinal))
                    {
                        rules.Add(new CoursePresentationRuleDefinition(
                            $"表现覆盖规则.{effect.Definition.EffectId}.{actionId}",
                            CoursePresentationTriggerKind.ActionRejected,
                            actionId,
                            groupId));
                    }

                    continue;
                }

                rules.Add(new CoursePresentationRuleDefinition(
                    "表现覆盖规则." + effect.Definition.EffectId,
                    CoursePresentationTriggerKind.StateActive,
                    effect.Definition.SubjectId,
                    groupId));
            }

            var states = model.PresentationStates.Select(value =>
                new CoursePresentationStateDefinition(
                    value.Definition.StateId,
                    value.Definition.SubjectEntityId,
                    value.Definition.SubjectEntityId,
                    value.Definition.ContextTargetEntityId,
                    value.Definition.RuleIds
                        .Where(runtimeRules.ContainsKey)
                        .Select(ruleId => runtimeRules[ruleId]))).ToArray();
            var texts = model.Actions
                .Where(value =>
                    !string.IsNullOrWhiteSpace(value.Definition.MessageId)
                    && !string.IsNullOrWhiteSpace(
                        value.Definition.RejectionMessage))
                .Select(value => new CourseTextDefinition(
                    value.Definition.MessageId,
                    value.Definition.RejectionMessage))
                .Concat(model.TeachingHints.Select(value =>
                    new CourseTextDefinition(
                        "教学提示." + value.Definition.EvaluationId,
                        value.Definition.Message)))
                .GroupBy(value => value.TextId, StringComparer.Ordinal)
                .Select(value => value.First())
                .ToArray();

            return new CoursePresentationDefinition(
                rules,
                groups,
                effects,
                states,
                texts);
        }

        private static IReadOnlyList<StructuredRuleDefinition> BuildRules(
            IEnumerable<NormalizedItem<NormalizedRuleDefinition>> items,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var result = new List<StructuredRuleDefinition>();
            var order = 0;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Definition.FieldId))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.asset.rule-field-unknown",
                        item.Provenance.Sources.First(),
                        item.Definition.RuleId,
                        $"规则字段“{item.Definition.FieldId}”未注册。",
                        "使用 StructuredFactField 中登记的稳定字段协议。"));
                    continue;
                }

                var field = new StructuredFactField(
                    item.Definition.FieldId);

                if (!Enum.TryParse(
                        item.Definition.OperatorId,
                        true,
                        out StructuredRuleOperator ruleOperator))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.asset.rule-operator-unknown",
                        item.Provenance.Sources.First(),
                        item.Definition.RuleId,
                        $"规则操作符“{item.Definition.OperatorId}”未注册。",
                        "使用 Equal、NotEqual、IsEmpty 或数值比较协议。"));
                    continue;
                }

                result.Add(new StructuredRuleDefinition(
                    item.Definition.RuleId,
                    order++,
                    field,
                    ruleOperator,
                    ParseExpectedValue(
                        ruleOperator,
                        item.Definition.ExpectedValue),
                    item.Definition.RuleId + ".不满足"));
            }

            return result;
        }

        private static StructuredValue ParseExpectedValue(
            StructuredRuleOperator ruleOperator,
            string rawValue)
        {
            if (ruleOperator == StructuredRuleOperator.为空
                || ruleOperator == StructuredRuleOperator.不为空)
            {
                return StructuredValue.Null();
            }

            return ParseStructuredValue(rawValue);
        }

        private static StructuredValue ParseStructuredValue(string rawValue)
        {
            if (rawValue == "是"
                || string.Equals(
                    rawValue,
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                return StructuredValue.FromBoolean(true);
            }

            if (rawValue == "否"
                || string.Equals(
                    rawValue,
                    "false",
                    StringComparison.OrdinalIgnoreCase))
            {
                return StructuredValue.FromBoolean(false);
            }

            if (double.TryParse(
                    rawValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number)
                && !double.IsNaN(number)
                && !double.IsInfinity(number))
            {
                return StructuredValue.FromNumber(number);
            }

            return StructuredValue.FromText(rawValue ?? string.Empty);
        }

        private static string NullIfEmpty(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static CourseConsequenceSeverity ConsequenceSeverity(
            string configured,
            ConfigurationSource source,
            string riskId,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return CourseConsequenceSeverity.ExperimentRisk;
            }

            switch (configured.Trim())
            {
                case "提示":
                    return CourseConsequenceSeverity.Advisory;
                case "现象偏差":
                    return CourseConsequenceSeverity.PhenomenonDeviation;
                case "实验风险":
                    return CourseConsequenceSeverity.ExperimentRisk;
                case "器材或样品损坏":
                    return CourseConsequenceSeverity.EquipmentOrSampleDamage;
                case "安全事故":
                    return CourseConsequenceSeverity.SafetyIncident;
                default:
                    diagnostics.Add(Diagnostic(
                        "blueprint.asset.risk-severity-invalid",
                        source,
                        riskId,
                        $"风险“{riskId}”使用了未注册的后果严重度“{configured}”。",
                        "使用提示、现象偏差、实验风险、器材或样品损坏或安全事故。"));
                    return CourseConsequenceSeverity.ExperimentRisk;
            }
        }

        private static CourseConsequenceRecoverability
            ConsequenceRecoverability(
                string configured,
                ConfigurationSource source,
                string riskId,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return CourseConsequenceRecoverability.RecoverableByOperation;
            }

            switch (configured.Trim())
            {
                case "无需恢复":
                    return CourseConsequenceRecoverability.NoneRequired;
                case "可通过后续操作恢复":
                    return CourseConsequenceRecoverability
                        .RecoverableByOperation;
                case "需要更换器材或样品":
                    return CourseConsequenceRecoverability.ReplacementRequired;
                case "需要重新开始实验":
                    return CourseConsequenceRecoverability.RestartRequired;
                case "永久阻断指定目标":
                    return CourseConsequenceRecoverability
                        .GoalPermanentlyBlocked;
                default:
                    diagnostics.Add(Diagnostic(
                        "blueprint.asset.risk-recoverability-invalid",
                        source,
                        riskId,
                        $"风险“{riskId}”使用了未注册的可恢复性“{configured}”。",
                        "使用无需恢复、可通过后续操作恢复、需要更换器材或样品、需要重新开始实验或永久阻断指定目标。"));
                    return CourseConsequenceRecoverability
                        .RecoverableByOperation;
            }
        }

        private static string RuntimePresentationTriggerValue(
            CoursePresentationTriggerKind kind,
            string value)
        {
            if (kind == CoursePresentationTriggerKind.CourseInitialized)
            {
                return PresentationSignalIds.CourseInitialized;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("表现触发值不能为空。");
            }

            return kind == CoursePresentationTriggerKind.ActionAccepted
                   || kind == CoursePresentationTriggerKind.ActionRejected
                   || kind == CoursePresentationTriggerKind
                       .ActionAvailabilityChanged
                ? RuntimeActionId(value)
                : value.Trim();
        }

        private static string RuntimeActionId(string configured)
        {
            return configured?.Trim() ?? string.Empty;
        }

        private static IReadOnlyList<string> RuntimeCapabilities(
            IEnumerable<string> featureIds) =>
            (featureIds ?? Array.Empty<string>())
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        private static string EmptyToNull(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        private static bool HasErrors(
            IEnumerable<CourseCompilationDiagnostic> diagnostics) =>
            diagnostics.Any(value =>
                value.Severity == CourseDiagnosticSeverity.Error);

        private static CourseCompilationSummary Summary(
            CourseBlueprint blueprint,
            RecipeCatalog catalog,
            NormalizedCourseModel normalized,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            var values = diagnostics.ToArray();
            return new CourseCompilationSummary(
                blueprint?.Objects.Count ?? 0,
                catalog?.Packages
                    .Where(value => value.Layer == RecipeLayer.Platform)
                    .Sum(value => value.Recipes.Count) ?? 0,
                catalog?.Packages
                    .Where(value => value.Layer == RecipeLayer.Discipline)
                    .Sum(value => value.Recipes.Count) ?? 0,
                normalized?.Actions.Count ?? 0,
                blueprint?.InteractionRules.Count(value =>
                    value.HandlingMode == "新增特殊") ?? 0,
                (blueprint?.AdvancedOverrides.Count ?? 0)
                + (blueprint?.PresentationOverrides.Count ?? 0)
                + (blueprint?.InteractionRules.Count(value =>
                    value.HandlingMode == "禁用默认"
                    || value.HandlingMode == "收紧默认") ?? 0),
                values.Count(value =>
                    value.Severity == CourseDiagnosticSeverity.Warning),
                values.Count(value =>
                    value.Severity == CourseDiagnosticSeverity.Error));
        }

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            ConfigurationSource source,
            string configurationId,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                source?.FileName ?? "蓝图编译",
                source?.Line ?? 1,
                source?.Column ?? 1,
                string.Empty,
                configurationId ?? string.Empty,
                reason,
                suggestion);
    }
}
