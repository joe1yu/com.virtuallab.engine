using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Normalized;

namespace VirtualLab.Unity.Authoring.Recipes
{
    public sealed class RecipeExpansionResult
    {
        internal RecipeExpansionResult(
            NormalizedCourseModel model,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Model = model;
            Diagnostics = diagnostics.ToArray();
        }

        public bool IsSuccess => Diagnostics.All(value =>
            value.Severity != CourseDiagnosticSeverity.Error);
        public NormalizedCourseModel Model { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// 把特征匹配结果和强类型绑定展开为规范化项，不执行字符串模板替换。
    /// </summary>
    public sealed class RecipeExpander
    {
        /// <summary>
        /// 校验创作目录与共享配方对同一抽象操作的解释一致。
        /// 只比较共享配方实际声明的阶段；目录可以先提供尚未被配方使用的操作。
        /// </summary>
        public static IReadOnlyList<CourseCompilationDiagnostic>
            ValidateAuthoringOperationContracts(
                CourseAuthoringCatalog authoringCatalog,
                RecipeCatalog recipeCatalog) =>
            RecipeAuthoringOperationContractValidator.Validate(
                authoringCatalog,
                recipeCatalog);

        public RecipeExpansionResult Expand(
            CourseBlueprint blueprint,
            RecipeCatalog catalog)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!catalog.IsValid)
            {
                return new RecipeExpansionResult(
                    new NormalizedCourseModel(blueprint.Course.CourseId),
                    catalog.Diagnostics);
            }

            var entities =
                new List<NormalizedItem<NormalizedEntityDefinition>>();
            var ports =
                new List<NormalizedItem<NormalizedPortDefinition>>();
            var actions =
                new List<NormalizedItem<NormalizedActionDefinition>>();
            var rules =
                new List<NormalizedItem<NormalizedRuleDefinition>>();
            var stateChanges =
                new List<NormalizedItem<NormalizedStateChangeDefinition>>();
            var events =
                new List<NormalizedItem<NormalizedDomainEventDefinition>>();
            var resultGroups =
                new List<NormalizedItem<NormalizedActionResultGroupDefinition>>();
            var presentationStates =
                new List<NormalizedItem<NormalizedPresentationStateDefinition>>();
            var presentationEffects =
                new List<NormalizedItem<NormalizedPresentationEffectDefinition>>();
            var presentationGroups =
                new List<NormalizedItem<NormalizedPresentationGroupDefinition>>();
            var evaluations =
                new List<NormalizedItem<NormalizedEvaluationDefinition>>();
            var teachingGoals =
                new List<NormalizedItem<NormalizedTeachingGoalDefinition>>();
            var teachingRisks =
                new List<NormalizedItem<NormalizedTeachingRiskDefinition>>();
            var teachingScores =
                new List<NormalizedItem<NormalizedTeachingScoreDefinition>>();
            var teachingHints =
                new List<NormalizedItem<NormalizedTeachingHintDefinition>>();
            var acceptanceScenarios =
                new List<NormalizedItem<NormalizedAcceptanceScenarioDefinition>>();
            var prefabContracts =
                new List<NormalizedItem<NormalizedPrefabContractDefinition>>();
            var generatedArtifacts =
                new List<NormalizedItem<GeneratedCourseArtifact>>();
            var disciplineMutationOverrides =
                new List<DisciplineMutationOverride>();
            var disciplineMutationAdditions =
                new List<DisciplineMutationAddition>();
            var diagnostics = new List<CourseCompilationDiagnostic>();

            var recipeActions = catalog.Packages
                .SelectMany(value => value.Actions)
                .ToLookup(value => value.RecipeId, StringComparer.Ordinal);
            var results = catalog.Packages
                .SelectMany(value => value.Results)
                .ToDictionary(value => value.ResultId, StringComparer.Ordinal);
            var operations = catalog.Packages
                .SelectMany(value => value.Operations)
                .ToDictionary(value => value.OperationId, StringComparer.Ordinal);
            var presentations = catalog.Packages
                .SelectMany(value => value.Presentations)
                .ToDictionary(
                    value => value.PresentationId,
                    StringComparer.Ordinal);
            var conditions = catalog.Packages
                .SelectMany(value => value.Conditions)
                .ToDictionary(value => value.ConditionId, StringComparer.Ordinal);
            var prefabByRecipe = catalog.Packages
                .SelectMany(value => value.PrefabContracts)
                .ToLookup(value => value.RecipeId, StringComparer.Ordinal);

            foreach (var courseObject in blueprint.Objects)
            {
                entities.Add(new NormalizedItem<NormalizedEntityDefinition>(
                    Identity(
                        "课程.实验对象",
                        courseObject.EntityId,
                        "实体",
                        courseObject.EntityId),
                    new NormalizedEntityDefinition(
                        courseObject.EntityId,
                        courseObject.FeatureIds),
                    new[] { courseObject.Source }));

                foreach (var recipe in catalog.EffectiveRecipes
                             .Where(value =>
                                 value.MatchKind == RecipeMatchKind.SingleEntity
                                 && courseObject.FeatureIds.Contains(
                                     value.MatchFeatureId,
                                     StringComparer.Ordinal))
                             .OrderBy(
                                 value => value.RecipeId,
                                 StringComparer.Ordinal))
                {
                    ExpandSingleEntityRecipe(
                        courseObject,
                        recipe,
                        recipeActions[recipe.RecipeId],
                        results,
                        operations,
                        presentations,
                        conditions,
                        prefabByRecipe[recipe.RecipeId],
                        actions,
                        rules,
                        stateChanges,
                        events,
                        resultGroups,
                        presentationStates,
                        presentationEffects,
                        presentationGroups,
                        prefabContracts,
                        diagnostics);
                }
            }

            foreach (var recipe in catalog.EffectiveRecipes
                         .Where(value =>
                             value.MatchKind == RecipeMatchKind.CompatiblePair
                             && value.CompatiblePair != null)
                         .OrderBy(value => value.RecipeId, StringComparer.Ordinal))
            {
                var contract = recipe.CompatiblePair;
                foreach (var sourceObject in blueprint.Objects.Where(value =>
                             value.FeatureIds.Contains(
                                 contract.SourceFeatureId,
                                 StringComparer.Ordinal)))
                {
                    foreach (var targetObject in blueprint.Objects.Where(value =>
                                 value.EntityId != sourceObject.EntityId
                                 && value.FeatureIds.Contains(
                                     contract.TargetFeatureId,
                                     StringComparer.Ordinal)))
                    {
                        if (!TryMatchPair(
                                sourceObject,
                                targetObject,
                                contract,
                                out var match))
                        {
                            continue;
                        }

                        ExpandCompatiblePairRecipe(
                            sourceObject,
                            targetObject,
                            match,
                            recipe,
                            recipeActions[recipe.RecipeId],
                            results,
                            operations,
                            presentations,
                            conditions,
                            ports,
                            actions,
                            rules,
                            stateChanges,
                            resultGroups,
                            presentationStates,
                            presentationEffects,
                            presentationGroups,
                            diagnostics);
                    }
                }
            }

            foreach (var package in catalog.Packages.Where(value =>
                         value.DisciplineRecordCompiler != null))
            {
                try
                {
                    var compiled = package.DisciplineRecordCompiler.Compile(
                        blueprint);
                    generatedArtifacts.AddRange(compiled.Artifacts);
                    disciplineMutationOverrides.AddRange(
                        compiled.MutationOverrides);
                    disciplineMutationAdditions.AddRange(
                        compiled.MutationAdditions);
                    diagnostics.AddRange(compiled.Diagnostics);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new CourseCompilationDiagnostic(
                        "recipe.discipline-record.compile-failed",
                        "学科过程.csv",
                        1,
                        1,
                        string.Empty,
                        package.PackageId,
                        $"学科记录编译失败：{exception.Message}",
                        "检查学科过程记录类型、参数、引用和单位。"));
                }
            }

            ApplyDisciplineMutationOverrides(
                stateChanges,
                disciplineMutationOverrides,
                diagnostics);
            ApplyDisciplineMutationAdditions(
                stateChanges,
                resultGroups,
                disciplineMutationAdditions,
                diagnostics);
            CompileTeachingEvaluations(
                blueprint.TeachingEvaluations,
                rules,
                evaluations,
                teachingGoals,
                teachingRisks,
                teachingScores,
                teachingHints,
                diagnostics);
            CompileAcceptanceScenarios(
                blueprint.AcceptanceRecords,
                blueprint.Course.ActorEntityId,
                actions,
                acceptanceScenarios,
                diagnostics);

            var model = new NormalizedCourseModel(
                blueprint.Course.CourseId,
                entities: entities,
                ports: ports,
                actions: actions,
                rules: rules,
                stateChanges: stateChanges,
                domainEvents: events,
                actionResultGroups: resultGroups,
                presentationStates: presentationStates,
                presentationEffects: presentationEffects,
                presentationGroups: presentationGroups,
                evaluations: evaluations,
                teachingGoals: teachingGoals,
                teachingRisks: teachingRisks,
                teachingScores: teachingScores,
                teachingHints: teachingHints,
                acceptanceScenarios: acceptanceScenarios,
                prefabContracts: prefabContracts,
                generatedArtifacts: generatedArtifacts,
                knownUnitIds: catalog.Packages
                    .SelectMany(value => value.Parameters)
                    .Select(value => value.Unit)
                    .Concat(blueprint.TeachingEvaluations.Select(value =>
                        value.Unit))
                    .Concat(blueprint.AcceptanceRecords.Select(value =>
                        value.Unit)),
                knownOperationIds: catalog.Packages
                    .SelectMany(value => value.Operations)
                    .Select(value => value.ProtocolOperationId)
                    .Concat(catalog.RegisteredStateOperationIds),
                knownPresentationProtocolIds: catalog.Packages
                    .SelectMany(value => value.Presentations)
                    .Select(value => value.ProtocolId));
            var validation = new NormalizedCourseValidator().Validate(model);
            diagnostics.AddRange(validation.Diagnostics);
            return new RecipeExpansionResult(model, diagnostics);
        }

        private static void ApplyDisciplineMutationOverrides(
            IList<NormalizedItem<NormalizedStateChangeDefinition>>
                stateChanges,
            IEnumerable<DisciplineMutationOverride> overrides,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var mutationOverride in overrides)
            {
                var matches = stateChanges
                    .Select((value, index) => new { value, index })
                    .Where(item =>
                        item.value.Identity.RecipeId
                        == mutationOverride.RecipeId
                        && item.value.Identity.SourceEntityId
                        == mutationOverride.SourceEntityId
                        && item.value.Identity.TargetEntityId
                        == mutationOverride.TargetEntityId
                        && item.value.Identity.GeneratedItemType == "状态变化"
                        && item.value.Identity.LocalKey
                        == mutationOverride.OperationLocalKey)
                    .ToArray();
                if (matches.Length != 1)
                {
                    var source = mutationOverride.Sources.FirstOrDefault();
                    diagnostics.Add(Diagnostic(
                        matches.Length == 0
                            ? "recipe.discipline-mutation.not-found"
                            : "recipe.discipline-mutation.ambiguous",
                        source,
                        mutationOverride.OperationLocalKey,
                        matches.Length == 0
                            ? $"过程参数未找到配方“{mutationOverride.RecipeId}”"
                              + $"为“{mutationOverride.SourceEntityId}”到"
                              + $"“{mutationOverride.TargetEntityId}”生成的操作"
                              + $"“{mutationOverride.OperationLocalKey}”。"
                            : $"过程参数定位到多个操作“{mutationOverride.OperationLocalKey}”。",
                        "检查配方 ID、来源、目标和配方内操作名称。"));
                    continue;
                }

                var match = matches[0];
                var parameters = new Dictionary<string, string>(
                    match.value.Definition.Parameters,
                    StringComparer.Ordinal);
                foreach (var parameter in mutationOverride.Parameters)
                {
                    parameters[parameter.Key] = parameter.Value;
                }

                var operationId =
                    string.IsNullOrWhiteSpace(
                        mutationOverride.ProtocolOperationId)
                        ? match.value.Definition.OperationId
                        : mutationOverride.ProtocolOperationId;
                stateChanges[match.index] =
                    new NormalizedItem<NormalizedStateChangeDefinition>(
                        match.value.Identity,
                        new NormalizedStateChangeDefinition(
                            match.value.Definition.MutationId,
                            operationId,
                            parameters),
                        match.value.Provenance.Sources
                            .Concat(mutationOverride.Sources),
                        "学科过程参数");
            }
        }

        private static void ApplyDisciplineMutationAdditions(
            ICollection<NormalizedItem<NormalizedStateChangeDefinition>>
                stateChanges,
            IList<NormalizedItem<NormalizedActionResultGroupDefinition>>
                resultGroups,
            IEnumerable<DisciplineMutationAddition> additions,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var addition in additions)
            {
                var matches = resultGroups
                    .Select((value, index) => new { value, index })
                    .Where(item =>
                        item.value.Identity.RecipeId == addition.RecipeId
                        && item.value.Identity.SourceEntityId
                        == addition.SourceEntityId
                        && item.value.Identity.TargetEntityId
                        == addition.TargetEntityId
                        && item.value.Identity.GeneratedItemType == "动作结果组"
                        && item.value.Identity.LocalKey
                        == addition.ResultLocalKey)
                    .ToArray();
                if (matches.Length != 1)
                {
                    var source = addition.Sources.FirstOrDefault();
                    diagnostics.Add(Diagnostic(
                        matches.Length == 0
                            ? "recipe.discipline-result.not-found"
                            : "recipe.discipline-result.ambiguous",
                        source,
                        addition.OperationLocalKey,
                        matches.Length == 0
                            ? $"过程操作未找到配方“{addition.RecipeId}”"
                              + $"为“{addition.SourceEntityId}”到"
                              + $"“{addition.TargetEntityId}”生成的结果"
                              + $"“{addition.ResultLocalKey}”。"
                            : $"过程操作定位到多个结果“{addition.ResultLocalKey}”。",
                        "检查配方 ID、来源、目标和动作结果局部键。"));
                    continue;
                }

                var identity = new GeneratedItemIdentity(
                    addition.RecipeId,
                    addition.SourceEntityId,
                    addition.TargetEntityId,
                    "状态变化",
                    addition.OperationLocalKey);
                if (stateChanges.Any(value =>
                        value.GeneratedItemId == identity.GeneratedItemId))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.discipline-mutation.duplicate",
                        addition.Sources.FirstOrDefault(),
                        addition.OperationLocalKey,
                        $"过程操作“{addition.OperationLocalKey}”重复生成。",
                        "为同一动作结果中的每个附加操作使用不同名称。"));
                    continue;
                }

                var entitySuffix = string.IsNullOrEmpty(
                    addition.TargetEntityId)
                    ? addition.SourceEntityId
                    : $"{addition.SourceEntityId}.{addition.TargetEntityId}";
                var mutationId =
                    $"状态变化.{Readable(addition.OperationLocalKey)}."
                    + entitySuffix;
                stateChanges.Add(
                    new NormalizedItem<NormalizedStateChangeDefinition>(
                        identity,
                        new NormalizedStateChangeDefinition(
                            mutationId,
                            addition.ProtocolOperationId,
                            addition.Parameters),
                        addition.Sources));

                var match = matches[0];
                resultGroups[match.index] =
                    new NormalizedItem<
                        NormalizedActionResultGroupDefinition>(
                        match.value.Identity,
                        new NormalizedActionResultGroupDefinition(
                            match.value.Definition.GroupId,
                            match.value.Definition.MutationIds
                                .Concat(new[] { mutationId }),
                            match.value.Definition.EventIds),
                        match.value.Provenance.Sources
                            .Concat(addition.Sources),
                        "追加学科过程操作");
            }
        }

        private static void CompileTeachingEvaluations(
            IReadOnlyList<CourseTeachingEvaluationBlueprint> records,
            ICollection<NormalizedItem<NormalizedRuleDefinition>> rules,
            ICollection<NormalizedItem<NormalizedEvaluationDefinition>>
                evaluations,
            ICollection<NormalizedItem<NormalizedTeachingGoalDefinition>>
                teachingGoals,
            ICollection<NormalizedItem<NormalizedTeachingRiskDefinition>>
                teachingRisks,
            ICollection<NormalizedItem<NormalizedTeachingScoreDefinition>>
                teachingScores,
            ICollection<NormalizedItem<NormalizedTeachingHintDefinition>>
                teachingHints,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var group in records
                         .GroupBy(
                             value => value.EvaluationId,
                             StringComparer.Ordinal)
                         .OrderBy(
                             value => value.Min(item => item.Order))
                         .ThenBy(value => value.Key, StringComparer.Ordinal))
            {
                var rows = group
                    .OrderBy(value => value.Source.Line)
                    .ToArray();
                var first = rows[0];
                if (!HasConsistentEvaluationMetadata(rows))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.evaluation.metadata-conflict",
                        first.Source,
                        first.EvaluationId,
                        $"评价“{first.EvaluationId}”的类型、显示名称、触发方式、顺序、分值或提示不一致。",
                        "相同评价 ID 的多行只填写不同条件，其余元数据保持完全一致。"));
                    continue;
                }

                if (!int.TryParse(
                        first.ScoreDelta,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var scoreDelta))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.evaluation.score-invalid",
                        first.Source,
                        first.EvaluationId,
                        $"评价“{first.EvaluationId}”的分值变化必须是整数。",
                        "填写 0、正整数或负整数。"));
                    continue;
                }

                var conditions =
                    new List<NormalizedEvaluationConditionDefinition>();
                foreach (var row in rows)
                {
                    var ruleId =
                        $"规则.教学评价.{row.EvaluationId}.{row.Source.Line}";
                    rules.Add(new NormalizedItem<NormalizedRuleDefinition>(
                        Identity(
                            "课程.教学评价",
                            row.SubjectEntityId,
                            "规则",
                            row.EvaluationId + "." + row.Source.Line),
                        new NormalizedRuleDefinition(
                            ruleId,
                            string.IsNullOrWhiteSpace(row.ConditionType)
                                ? Field(row.Field)
                                : row.ConditionType + "." + Field(row.Field),
                            Operator(row.Comparison),
                            row.ExpectedValue,
                            row.Unit),
                        new[] { row.Source }));
                    conditions.Add(
                        new NormalizedEvaluationConditionDefinition(
                            ruleId,
                            row.ConditionType,
                            row.SubjectEntityId));
                }

                evaluations.Add(
                    new NormalizedItem<NormalizedEvaluationDefinition>(
                        Identity(
                            "课程.教学评价",
                            first.EvaluationId,
                            "教学评价",
                            first.EvaluationId),
                        new NormalizedEvaluationDefinition(
                            first.EvaluationId,
                            first.EvaluationType,
                            first.DisplayName,
                            first.TriggerType,
                            first.TriggerValue,
                            first.Order,
                            conditions,
                            scoreDelta,
                            first.PromptMessage),
                        rows.Select(value => value.Source)));
                AddTeachingEvaluationProjections(
                    first,
                    conditions,
                    scoreDelta,
                    rows.Select(value => value.Source),
                    teachingGoals,
                    teachingRisks,
                    teachingScores,
                    teachingHints,
                    diagnostics);
            }
        }

        private static void AddTeachingEvaluationProjections(
            CourseTeachingEvaluationBlueprint first,
            IReadOnlyList<NormalizedEvaluationConditionDefinition> conditions,
            int scoreDelta,
            IEnumerable<ConfigurationSource> sources,
            ICollection<NormalizedItem<NormalizedTeachingGoalDefinition>>
                teachingGoals,
            ICollection<NormalizedItem<NormalizedTeachingRiskDefinition>>
                teachingRisks,
            ICollection<NormalizedItem<NormalizedTeachingScoreDefinition>>
                teachingScores,
            ICollection<NormalizedItem<NormalizedTeachingHintDefinition>>
                teachingHints,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var sourceArray = sources.ToArray();
            if (first.EvaluationType == "目标")
            {
                teachingGoals.Add(
                    new NormalizedItem<NormalizedTeachingGoalDefinition>(
                        Identity(
                            "课程.教学评价",
                            first.EvaluationId,
                            "教学目标",
                            first.EvaluationId),
                        new NormalizedTeachingGoalDefinition(
                            first.EvaluationId,
                            first.DisplayName,
                            first.TriggerType,
                            first.TriggerValue,
                            first.Order,
                            conditions),
                        sourceArray));
            }
            else if (first.EvaluationType == "风险")
            {
                teachingRisks.Add(
                    new NormalizedItem<NormalizedTeachingRiskDefinition>(
                        Identity(
                            "课程.教学评价",
                            first.EvaluationId,
                            "教学风险",
                            first.EvaluationId),
                        new NormalizedTeachingRiskDefinition(
                            first.EvaluationId,
                            first.DisplayName,
                            first.TriggerType,
                            first.TriggerValue,
                            first.Order,
                            conditions,
                            first.ConsequenceSeverity,
                            first.Continuation,
                            SplitGroups(first.AffectedTargetIds)),
                        sourceArray));
            }
            else if (first.EvaluationType != "评分"
                     && first.EvaluationType != "提示")
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.evaluation.type-unknown",
                    first.Source,
                    first.EvaluationId,
                    $"评价类型“{first.EvaluationType}”未注册。",
                    "使用目标、风险、评分或提示。"));
            }

            if (scoreDelta != 0 || first.EvaluationType == "评分")
            {
                teachingScores.Add(
                    new NormalizedItem<NormalizedTeachingScoreDefinition>(
                        Identity(
                            "课程.教学评价",
                            first.EvaluationId,
                            "教学评分",
                            first.EvaluationId),
                        new NormalizedTeachingScoreDefinition(
                            first.EvaluationId,
                            scoreDelta),
                        sourceArray));
            }

            var hint = string.IsNullOrWhiteSpace(first.PromptMessage)
                && first.EvaluationType == "提示"
                    ? first.DisplayName
                    : first.PromptMessage;
            if (!string.IsNullOrWhiteSpace(hint))
            {
                teachingHints.Add(
                    new NormalizedItem<NormalizedTeachingHintDefinition>(
                        Identity(
                            "课程.教学评价",
                            first.EvaluationId,
                            "教学提示",
                            first.EvaluationId),
                        new NormalizedTeachingHintDefinition(
                            first.EvaluationId,
                            hint),
                        sourceArray));
            }
        }

        private static bool HasConsistentEvaluationMetadata(
            IReadOnlyList<CourseTeachingEvaluationBlueprint> rows)
        {
            var first = rows[0];
            return rows.All(value =>
                string.Equals(
                    value.EvaluationType,
                    first.EvaluationType,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.DisplayName,
                    first.DisplayName,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.TriggerType,
                    first.TriggerType,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.TriggerValue,
                    first.TriggerValue,
                    StringComparison.Ordinal)
                && value.Order == first.Order
                && string.Equals(
                    value.ScoreDelta,
                    first.ScoreDelta,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.PromptMessage,
                    first.PromptMessage,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.ConsequenceSeverity,
                    first.ConsequenceSeverity,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.Continuation,
                    first.Continuation,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.AffectedTargetIds,
                    first.AffectedTargetIds,
                    StringComparison.Ordinal));
        }

        private static void CompileAcceptanceScenarios(
            IReadOnlyList<CourseAcceptanceRecordBlueprint> records,
            string actorEntityId,
            IReadOnlyCollection<NormalizedItem<NormalizedActionDefinition>>
                actions,
            ICollection<NormalizedItem<NormalizedAcceptanceScenarioDefinition>>
                scenarios,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var scenarioGroup in records
                         .GroupBy(value => value.ScenarioId, StringComparer.Ordinal)
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var steps = new List<NormalizedAcceptanceStepDefinition>();
                var scenarioValid = true;
                foreach (var stepGroup in scenarioGroup
                             .GroupBy(value => value.Order)
                             .OrderBy(value => value.Key))
                {
                    var rows = stepGroup
                        .OrderBy(value => value.Source.Line)
                        .ToArray();
                    var recordKinds = rows
                        .Select(value => value.RecordType)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (recordKinds.Length != 1
                        || (recordKinds[0] != "动作"
                            && recordKinds[0] != "断言"))
                    {
                        diagnostics.Add(Diagnostic(
                            "blueprint.acceptance.record-kind-conflict",
                            rows[0].Source,
                            scenarioGroup.Key,
                            $"验收场景“{scenarioGroup.Key}”的顺序 {stepGroup.Key} 混合了动作与断言，或使用了未知记录类型。",
                            "同一顺序只保留动作参数行，或只保留断言行。"));
                        scenarioValid = false;
                        continue;
                    }

                    if (recordKinds[0] == "动作")
                    {
                        if (!HasConsistentActionMetadata(rows))
                        {
                            diagnostics.Add(Diagnostic(
                                "blueprint.acceptance.action-metadata-conflict",
                                rows[0].Source,
                                scenarioGroup.Key,
                                $"验收场景“{scenarioGroup.Key}”的顺序 {stepGroup.Key} 包含不一致的动作、来源或目标。",
                                "同一动作的多行只填写不同参数。"));
                            scenarioValid = false;
                            continue;
                        }

                        var duplicateParameter = rows
                            .Where(value => !string.IsNullOrWhiteSpace(
                                value.ParameterName))
                            .GroupBy(
                                value => value.ParameterName,
                                StringComparer.Ordinal)
                            .FirstOrDefault(value => value.Count() > 1);
                        if (duplicateParameter != null)
                        {
                            diagnostics.Add(Diagnostic(
                                "blueprint.acceptance.parameter-duplicate",
                                duplicateParameter.Skip(1).First().Source,
                                scenarioGroup.Key,
                                $"验收动作参数“{duplicateParameter.Key}”重复。",
                                "同一动作的每个参数只保留一行。"));
                            scenarioValid = false;
                            continue;
                        }

                        var first = rows[0];
                        try
                        {
                            var actionId = ActionId(first.ActionId);
                            var phases = actions
                                .Where(value =>
                                    value.Definition.ActionId == actionId
                                    && value.Definition.SourceEntityId
                                    == first.SourceEntityId
                                    && value.Definition.TargetEntityId
                                    == first.TargetEntityId)
                                .Select(value => value.Definition.Phase)
                                .Distinct()
                                .ToArray();
                            if (phases.Length != 1)
                            {
                                throw new InvalidOperationException(
                                    $"验收动作“{first.ActionId}”没有唯一的操作阶段。");
                            }

                            var request = new SemanticActionRequest(
                                $"验收.{scenarioGroup.Key}.{stepGroup.Key}",
                                actionId,
                                $"验收操作.{scenarioGroup.Key}.{stepGroup.Key}",
                                phases[0],
                                stepGroup.Key,
                                actorEntityId,
                                first.SourceEntityId,
                                first.TargetEntityId,
                                rows
                                    .Where(value => !string.IsNullOrWhiteSpace(
                                        value.ParameterName))
                                    .Select(value =>
                                        new KeyValuePair<string, StructuredValue>(
                                            value.ParameterName,
                                            StructuredValueOf(
                                                value.ParameterValue))));
                            steps.Add(
                                new NormalizedAcceptanceStepDefinition(
                                    stepGroup.Key,
                                    request,
                                    Array.Empty<
                                        NormalizedAcceptanceAssertionDefinition>()));
                        }
                        catch (ArgumentException exception)
                        {
                            diagnostics.Add(Diagnostic(
                                "blueprint.acceptance.action-invalid",
                                first.Source,
                                scenarioGroup.Key,
                                $"验收动作无效：{exception.Message}",
                                "补全动作、来源实体和参数。"));
                            scenarioValid = false;
                        }
                    }
                    else
                    {
                        steps.Add(new NormalizedAcceptanceStepDefinition(
                            stepGroup.Key,
                            null,
                            rows.Select(value =>
                                new NormalizedAcceptanceAssertionDefinition(
                                    value.AssertionType,
                                    value.ObjectId,
                                    Field(value.Field),
                                    Operator(value.Comparison),
                                    value.ExpectedValue,
                                    value.Unit))));
                    }
                }

                if (!scenarioValid)
                {
                    continue;
                }

                var sources = scenarioGroup
                    .Select(value => value.Source)
                    .ToArray();
                scenarios.Add(
                    new NormalizedItem<
                        NormalizedAcceptanceScenarioDefinition>(
                        Identity(
                            "课程.验收场景",
                            scenarioGroup.Key,
                            "验收场景",
                            scenarioGroup.Key),
                        new NormalizedAcceptanceScenarioDefinition(
                            scenarioGroup.Key,
                            steps),
                        sources));
            }
        }

        private static bool HasConsistentActionMetadata(
            IReadOnlyList<CourseAcceptanceRecordBlueprint> rows)
        {
            var first = rows[0];
            return rows.All(value =>
                string.Equals(
                    value.ActionId,
                    first.ActionId,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.SourceEntityId,
                    first.SourceEntityId,
                    StringComparison.Ordinal)
                && string.Equals(
                    value.TargetEntityId,
                    first.TargetEntityId,
                    StringComparison.Ordinal));
        }

        private static StructuredValue StructuredValueOf(string rawValue)
        {
            if (string.Equals(rawValue, "是", StringComparison.Ordinal)
                || string.Equals(
                    rawValue,
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                return StructuredValue.FromBoolean(true);
            }

            if (string.Equals(rawValue, "否", StringComparison.Ordinal)
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

        private static string Operator(string value) =>
            value?.Trim() ?? string.Empty;

        private static string Field(string value) =>
            value?.Trim() ?? string.Empty;

        private static string ActionId(string configured)
        {
            return configured?.Trim() ?? string.Empty;
        }

        private static void ExpandSingleEntityRecipe(
            CourseObjectBlueprint courseObject,
            RecipeDefinition recipe,
            IEnumerable<RecipeActionDefinition> recipeActions,
            IReadOnlyDictionary<string, RecipeResultDefinition> results,
            IReadOnlyDictionary<string, RecipeOperationDefinition> operations,
            IReadOnlyDictionary<string, RecipePresentationDefinition> presentations,
            IReadOnlyDictionary<string, RecipeConditionDefinition> conditions,
            IEnumerable<RecipePrefabContract> recipePrefabContracts,
            ICollection<NormalizedItem<NormalizedActionDefinition>> actions,
            ICollection<NormalizedItem<NormalizedRuleDefinition>> rules,
            ICollection<NormalizedItem<NormalizedStateChangeDefinition>> stateChanges,
            ICollection<NormalizedItem<NormalizedDomainEventDefinition>> events,
            ICollection<NormalizedItem<NormalizedActionResultGroupDefinition>> resultGroups,
            ICollection<NormalizedItem<NormalizedPresentationStateDefinition>> presentationStates,
            ICollection<NormalizedItem<NormalizedPresentationEffectDefinition>> presentationEffects,
            ICollection<NormalizedItem<NormalizedPresentationGroupDefinition>> presentationGroups,
            ICollection<NormalizedItem<NormalizedPrefabContractDefinition>> prefabContracts,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var contract in recipePrefabContracts)
            {
                var contractId =
                    $"配方附加预制体要求.{Readable(contract.Identifier)}."
                    + courseObject.EntityId;
                prefabContracts.Add(
                    new NormalizedItem<NormalizedPrefabContractDefinition>(
                        Identity(
                            recipe.RecipeId,
                            courseObject.EntityId,
                            "配方附加预制体要求",
                            contract.Identifier),
                        new NormalizedPrefabContractDefinition(
                            contractId,
                            courseObject.EntityId,
                            contract.ContractKind,
                            contract.Identifier),
                        Sources(courseObject.Source, contract.Source, recipe.Source)));
            }

            foreach (var item in recipeActions.OrderBy(
                         value => value.OperationName,
                         StringComparer.Ordinal))
            {
                var ruleIds = new List<string>();
                foreach (var conditionId in item.ConditionIds)
                {
                    if (!conditions.TryGetValue(conditionId, out var condition))
                    {
                        continue;
                    }

                    var ruleId =
                        $"规则.{Readable(condition.ConditionId)}.{courseObject.EntityId}";
                    ruleIds.Add(ruleId);
                    rules.Add(new NormalizedItem<NormalizedRuleDefinition>(
                        Identity(
                            recipe.RecipeId,
                            courseObject.EntityId,
                            "规则",
                            condition.ConditionId),
                        new NormalizedRuleDefinition(
                            ruleId,
                            condition.FieldId,
                            condition.OperatorId,
                            condition.ExpectedValue,
                            condition.UnitId),
                        Sources(
                            courseObject.Source,
                            condition.Source,
                            item.Source,
                            recipe.Source)));
                }

                var mutationIds = new List<string>();
                var eventIds = new List<string>();
                ConfigurationSource resultSource = null;
                foreach (var resultId in item.ResultIds)
                {
                    if (!results.TryGetValue(resultId, out var result))
                    {
                        continue;
                    }

                    resultSource ??= result.Source;
                    foreach (var operationId in result.OperationIds)
                    {
                        if (!operations.TryGetValue(
                                operationId,
                                out var operation))
                        {
                            continue;
                        }

                        if (!TryResolveParameters(
                                courseObject,
                                operation,
                                out var parameters,
                                out var reason))
                        {
                            diagnostics.Add(Diagnostic(
                                "recipe.binding.unresolved",
                                operation.Source,
                                operation.OperationId,
                                reason,
                                "补齐实体参数，或改用当前匹配上下文支持的绑定种类。"));
                            continue;
                        }

                        var mutationId =
                            $"状态变化.{Readable(operation.OperationId)}.{courseObject.EntityId}";
                        mutationIds.Add(mutationId);
                        stateChanges.Add(
                            new NormalizedItem<NormalizedStateChangeDefinition>(
                                Identity(
                                    recipe.RecipeId,
                                    courseObject.EntityId,
                                    "状态变化",
                                    operation.OperationId),
                                new NormalizedStateChangeDefinition(
                                    mutationId,
                                    operation.ProtocolOperationId,
                                    parameters),
                                Sources(
                                    courseObject.Source,
                                    operation.Source,
                                    result.Source,
                                    item.Source,
                                    recipe.Source)));
                    }
                }

                var resultGroupId =
                    $"结果组.{item.OperationName}.{courseObject.EntityId}";
                resultGroups.Add(
                    new NormalizedItem<NormalizedActionResultGroupDefinition>(
                        Identity(
                            recipe.RecipeId,
                            courseObject.EntityId,
                            "动作结果组",
                            item.OperationName),
                        new NormalizedActionResultGroupDefinition(
                            resultGroupId,
                            mutationIds,
                            eventIds),
                        Sources(
                            courseObject.Source,
                            resultSource,
                            item.Source,
                            recipe.Source)));

                var effectIds = new List<string>();
                foreach (var presentationId in item.PresentationIds)
                {
                    if (!presentations.TryGetValue(
                            presentationId,
                            out var presentation))
                    {
                        continue;
                    }

                    var effectId =
                        $"效果.{Readable(presentation.PresentationId)}.{courseObject.EntityId}";
                    var stateId = presentation.CreatesState
                        ? $"状态.{courseObject.EntityId}{presentation.StateSuffix}"
                        : string.Empty;
                    effectIds.Add(effectId);
                    presentationEffects.Add(
                        new NormalizedItem<NormalizedPresentationEffectDefinition>(
                            Identity(
                                recipe.RecipeId,
                                courseObject.EntityId,
                                "表现效果",
                                presentation.PresentationId),
                            new NormalizedPresentationEffectDefinition(
                                effectId,
                                presentation.ProtocolId,
                                PresentationSubject(presentation),
                                presentation.LocationKind,
                                PresentationLocationId(
                                    presentation,
                                    string.Empty,
                                    string.Empty),
                                Lifecycle(presentation),
                                presentation.ParameterValues,
                                presentation.CreatesState
                                    ? CoursePresentationTriggerKind.StateActive
                                    : null,
                                stateId,
                                dynamicParameterBindings:
                                    PresentationParameterBindings(
                                        presentation)),
                            Sources(
                                courseObject.Source,
                                presentation.Source,
                                item.Source,
                                recipe.Source)));

                    if (presentation.CreatesState)
                    {
                        if (presentationStates.All(value =>
                                !string.Equals(
                                    value.Definition.StateId,
                                    stateId,
                                    StringComparison.Ordinal)))
                        {
                            var stateRuleIds = new List<string>();
                            foreach (var conditionId in
                                     presentation.StateConditionIds)
                            {
                                if (!conditions.TryGetValue(
                                        conditionId,
                                        out var stateCondition))
                                {
                                    continue;
                                }

                                var stateRuleId =
                                    $"规则.{Readable(stateCondition.ConditionId)}.{courseObject.EntityId}";
                                stateRuleIds.Add(stateRuleId);
                                if (rules.All(value => !string.Equals(
                                        value.Definition.RuleId,
                                        stateRuleId,
                                        StringComparison.Ordinal)))
                                {
                                    rules.Add(
                                        new NormalizedItem<NormalizedRuleDefinition>(
                                            Identity(
                                                recipe.RecipeId,
                                                courseObject.EntityId,
                                                "规则",
                                                stateCondition.ConditionId),
                                            new NormalizedRuleDefinition(
                                                stateRuleId,
                                                stateCondition.FieldId,
                                                stateCondition.OperatorId,
                                                stateCondition.ExpectedValue,
                                                stateCondition.UnitId),
                                            Sources(
                                                courseObject.Source,
                                                stateCondition.Source,
                                                presentation.Source,
                                                recipe.Source)));
                                }
                            }

                            presentationStates.Add(
                                new NormalizedItem<NormalizedPresentationStateDefinition>(
                                    Identity(
                                        recipe.RecipeId,
                                        courseObject.EntityId,
                                        "表现状态",
                                        presentation.PresentationId),
                                    new NormalizedPresentationStateDefinition(
                                        stateId,
                                        courseObject.EntityId,
                                        stateRuleIds),
                                    Sources(
                                        courseObject.Source,
                                        presentation.Source,
                                        recipe.Source)));
                        }
                    }
                }

                var presentationGroupId = string.Empty;
                if (effectIds.Count > 0)
                {
                    presentationGroupId =
                        $"表现组.{item.OperationName}.{courseObject.EntityId}";
                    presentationGroups.Add(
                        new NormalizedItem<
                            NormalizedPresentationGroupDefinition>(
                            Identity(
                                recipe.RecipeId,
                                courseObject.EntityId,
                                "表现组",
                                item.OperationName),
                            new NormalizedPresentationGroupDefinition(
                                presentationGroupId,
                                effectIds),
                            Sources(
                                courseObject.Source,
                                item.Source,
                                recipe.Source)));
                }

                var policyId =
                    $"策略.{item.OperationName}.{courseObject.EntityId}";
                actions.Add(new NormalizedItem<NormalizedActionDefinition>(
                    Identity(
                        recipe.RecipeId,
                        courseObject.EntityId,
                        "动作策略",
                        item.OperationName),
                    new NormalizedActionDefinition(
                        policyId,
                        item.SemanticCommandId,
                        item.OperationId,
                        item.Lifecycle,
                        item.ExecutionModeId,
                        item.Phase,
                        courseObject.EntityId,
                        string.Empty,
                        ruleIds,
                        resultGroupId,
                        presentationGroupId,
                        item.Priority,
                        item.ReviewResult),
                    Sources(
                        courseObject.Source,
                        item.Source,
                        recipe.Source)));
            }
        }

        private static bool TryResolveParameters(
            CourseObjectBlueprint courseObject,
            RecipeOperationDefinition operation,
            out IReadOnlyDictionary<string, string> parameters,
            out string reason,
            CourseObjectBlueprint targetObject = null,
            string sourcePortId = "",
            string targetPortId = "")
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in operation.ConstantParameters)
            {
                result[pair.Key] = pair.Value;
            }

            foreach (var binding in operation.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.OutputName))
                {
                    parameters = null;
                    reason = $"操作“{operation.OperationId}”的绑定缺少输出参数名。";
                    return false;
                }

                string value;
                switch (binding.Kind)
                {
                    case RecipeBindingKind.CurrentEntity:
                        value = courseObject.EntityId;
                        break;
                    case RecipeBindingKind.ActionActor:
                        value = "操作者";
                        break;
                    case RecipeBindingKind.ActionSource:
                        value = "来源";
                        break;
                    case RecipeBindingKind.ActionTarget:
                        value = "目标";
                        break;
                    case RecipeBindingKind.MatchedSourcePort:
                        if (string.IsNullOrWhiteSpace(sourcePortId))
                        {
                            parameters = null;
                            reason = "当前匹配没有来源端口。";
                            return false;
                        }

                        value = sourcePortId;
                        break;
                    case RecipeBindingKind.MatchedTargetPort:
                        if (string.IsNullOrWhiteSpace(targetPortId))
                        {
                            parameters = null;
                            reason = "当前匹配没有目标端口。";
                            return false;
                        }

                        value = targetPortId;
                        break;
                    case RecipeBindingKind.EntityParameter:
                        if (!courseObject.ExtensionValues.TryGetValue(
                                "参数." + binding.ParameterName,
                                out var parameter))
                        {
                            parameters = null;
                            reason =
                                $"实体“{courseObject.EntityId}”缺少参数“{binding.ParameterName}”。";
                            return false;
                        }

                        value = parameter.RawValue;
                        break;
                    default:
                        parameters = null;
                        reason =
                            $"单实体展开不支持绑定“{binding.Kind}”。";
                        return false;
                }

                result[binding.OutputName] = value;
            }

            parameters = result;
            reason = string.Empty;
            return true;
        }

        private static bool TryMatchPair(
            CourseObjectBlueprint source,
            CourseObjectBlueprint target,
            CompatiblePairContract contract,
            out PairMatch match)
        {
            var sourceKey = "参数." + contract.SourcePortParameter;
            var targetKey = "参数." + contract.TargetPortParameter;
            if (!source.ExtensionValues.TryGetValue(
                    sourceKey,
                    out var sourceValue)
                || !target.ExtensionValues.TryGetValue(
                    targetKey,
                    out var targetValue)
                || string.IsNullOrWhiteSpace(sourceValue.RawValue)
                || string.IsNullOrWhiteSpace(targetValue.RawValue))
            {
                match = null;
                return false;
            }

            var sourceGroups = SplitGroups(sourceValue.RawValue);
            var targetGroups = SplitGroups(targetValue.RawValue);
            var sharedGroup = sourceGroups
                .Intersect(targetGroups, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
            var compatible = contract.CompatibilityKind switch
            {
                PairCompatibilityKind.EqualGroup =>
                    sharedGroup != null,
                PairCompatibilityKind.EqualValue =>
                    string.Equals(
                        sourceValue.RawValue.Trim(),
                        targetValue.RawValue.Trim(),
                        StringComparison.Ordinal),
                PairCompatibilityKind.CourseAllowedPair => false,
                _ => false
            };
            if (!compatible)
            {
                match = null;
                return false;
            }

            match = new PairMatch(
                PortId(
                    source.EntityId,
                    contract.SourcePortParameter,
                    sourceGroups.Count > 1 ? sharedGroup : string.Empty),
                PortId(
                    target.EntityId,
                    contract.TargetPortParameter,
                    targetGroups.Count > 1 ? sharedGroup : string.Empty),
                sharedGroup ?? sourceValue.RawValue.Trim(),
                sourceValue.Source,
                targetValue.Source);
            return true;
        }

        private static IReadOnlyList<string> SplitGroups(string rawValue) =>
            (rawValue ?? string.Empty)
            .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private static void ExpandCompatiblePairRecipe(
            CourseObjectBlueprint sourceObject,
            CourseObjectBlueprint targetObject,
            PairMatch match,
            RecipeDefinition recipe,
            IEnumerable<RecipeActionDefinition> recipeActions,
            IReadOnlyDictionary<string, RecipeResultDefinition> results,
            IReadOnlyDictionary<string, RecipeOperationDefinition> operations,
            IReadOnlyDictionary<string, RecipePresentationDefinition> presentations,
            IReadOnlyDictionary<string, RecipeConditionDefinition> conditions,
            ICollection<NormalizedItem<NormalizedPortDefinition>> ports,
            ICollection<NormalizedItem<NormalizedActionDefinition>> actions,
            ICollection<NormalizedItem<NormalizedRuleDefinition>> rules,
            ICollection<NormalizedItem<NormalizedStateChangeDefinition>> stateChanges,
            ICollection<NormalizedItem<NormalizedActionResultGroupDefinition>> resultGroups,
            ICollection<NormalizedItem<NormalizedPresentationStateDefinition>> presentationStates,
            ICollection<NormalizedItem<NormalizedPresentationEffectDefinition>> presentationEffects,
            ICollection<NormalizedItem<NormalizedPresentationGroupDefinition>> presentationGroups,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (recipe.CompatiblePair.CreatesPorts)
            {
                AddPort(
                    sourceObject,
                    targetObject.EntityId,
                    match.SourcePortId,
                    match.CompatibilityGroup,
                    match.SourceParameterSource,
                    recipe,
                    ports);
                AddPort(
                    targetObject,
                    sourceObject.EntityId,
                    match.TargetPortId,
                    match.CompatibilityGroup,
                    match.TargetParameterSource,
                    recipe,
                    ports);
            }

            foreach (var item in recipeActions.OrderBy(
                         value => value.OperationName,
                         StringComparer.Ordinal))
            {
                var ruleIds = new List<string>();
                foreach (var conditionId in item.ConditionIds)
                {
                    if (!conditions.TryGetValue(conditionId, out var condition))
                    {
                        continue;
                    }

                    var ruleId =
                        $"规则.{Readable(condition.ConditionId)}."
                        + $"{sourceObject.EntityId}.{targetObject.EntityId}";
                    ruleIds.Add(ruleId);
                    rules.Add(new NormalizedItem<NormalizedRuleDefinition>(
                        PairIdentity(
                            recipe.RecipeId,
                            sourceObject.EntityId,
                            targetObject.EntityId,
                            "规则",
                            condition.ConditionId),
                        new NormalizedRuleDefinition(
                            ruleId,
                            condition.FieldId,
                            condition.OperatorId,
                            condition.ExpectedValue,
                            condition.UnitId),
                        Sources(
                            sourceObject.Source,
                            targetObject.Source,
                            condition.Source,
                            item.Source,
                            recipe.Source)));
                }

                var mutationIds = new List<string>();
                ConfigurationSource resultSource = null;
                foreach (var resultId in item.ResultIds)
                {
                    if (!results.TryGetValue(resultId, out var result))
                    {
                        continue;
                    }

                    resultSource ??= result.Source;
                    foreach (var operationId in result.OperationIds)
                    {
                        if (!operations.TryGetValue(
                                operationId,
                                out var operation))
                        {
                            continue;
                        }

                        if (!TryResolveParameters(
                                sourceObject,
                                operation,
                                out var parameters,
                                out var reason,
                                targetObject,
                                match.SourcePortId,
                                match.TargetPortId))
                        {
                            diagnostics.Add(Diagnostic(
                                "recipe.binding.unresolved",
                                operation.Source,
                                operation.OperationId,
                                reason,
                                "补齐端口参数，或使用当前双实体匹配支持的绑定种类。"));
                            continue;
                        }

                        var mutationId =
                            $"状态变化.{Readable(operation.OperationId)}."
                            + $"{sourceObject.EntityId}.{targetObject.EntityId}";
                        mutationIds.Add(mutationId);
                        stateChanges.Add(
                            new NormalizedItem<NormalizedStateChangeDefinition>(
                                PairIdentity(
                                    recipe.RecipeId,
                                    sourceObject.EntityId,
                                    targetObject.EntityId,
                                    "状态变化",
                                    operation.OperationId),
                                new NormalizedStateChangeDefinition(
                                    mutationId,
                                    operation.ProtocolOperationId,
                                    parameters),
                                Sources(
                                    sourceObject.Source,
                                    targetObject.Source,
                                    operation.Source,
                                    result.Source,
                                    item.Source,
                                    recipe.Source)));
                    }
                }

                var resultGroupId =
                    $"结果组.{item.OperationName}.{sourceObject.EntityId}."
                    + targetObject.EntityId;
                resultGroups.Add(
                    new NormalizedItem<NormalizedActionResultGroupDefinition>(
                        PairIdentity(
                            recipe.RecipeId,
                            sourceObject.EntityId,
                            targetObject.EntityId,
                            "动作结果组",
                            item.OperationName),
                        new NormalizedActionResultGroupDefinition(
                            resultGroupId,
                            mutationIds,
                            Array.Empty<string>()),
                        Sources(
                            sourceObject.Source,
                            targetObject.Source,
                            resultSource,
                            item.Source,
                            recipe.Source)));

                var effectIds = new List<string>();
                foreach (var presentationId in item.PresentationIds)
                {
                    if (!presentations.TryGetValue(
                            presentationId,
                            out var presentation))
                    {
                        continue;
                    }

                    var effectId =
                        $"效果.{Readable(presentation.PresentationId)}."
                        + $"{sourceObject.EntityId}.{targetObject.EntityId}";
                    var stateId = presentation.CreatesState
                        ? $"状态.{sourceObject.EntityId}"
                            + $"{presentation.StateSuffix}{targetObject.EntityId}"
                        : string.Empty;
                    effectIds.Add(effectId);
                    presentationEffects.Add(
                        new NormalizedItem<NormalizedPresentationEffectDefinition>(
                            PairIdentity(
                                recipe.RecipeId,
                                sourceObject.EntityId,
                                targetObject.EntityId,
                                "表现效果",
                                presentation.PresentationId),
                            new NormalizedPresentationEffectDefinition(
                                effectId,
                                presentation.ProtocolId,
                                PresentationSubject(presentation),
                                presentation.LocationKind,
                                PresentationLocationId(
                                    presentation,
                                    match.SourcePortId,
                                    match.TargetPortId),
                                Lifecycle(presentation),
                                presentation.ParameterValues,
                                presentation.CreatesState
                                    ? CoursePresentationTriggerKind.StateActive
                                    : null,
                                stateId,
                                dynamicParameterBindings:
                                    PresentationParameterBindings(
                                        presentation)),
                            Sources(
                                sourceObject.Source,
                                targetObject.Source,
                                presentation.Source,
                                item.Source,
                                recipe.Source)));

                    if (presentation.CreatesState)
                    {
                        var stateRuleIds = new List<string>();
                        foreach (var stateConditionId in
                                 presentation.StateConditionIds)
                        {
                            if (!conditions.TryGetValue(
                                    stateConditionId,
                                    out var stateCondition))
                            {
                                continue;
                            }

                            var stateRuleId =
                                $"规则.{Readable(stateCondition.ConditionId)}."
                                + $"{sourceObject.EntityId}.{targetObject.EntityId}";
                            stateRuleIds.Add(stateRuleId);
                            if (rules.All(value => !string.Equals(
                                    value.Definition.RuleId,
                                    stateRuleId,
                                    StringComparison.Ordinal)))
                            {
                                rules.Add(
                                    new NormalizedItem<NormalizedRuleDefinition>(
                                        PairIdentity(
                                            recipe.RecipeId,
                                            sourceObject.EntityId,
                                            targetObject.EntityId,
                                            "规则",
                                            stateCondition.ConditionId),
                                        new NormalizedRuleDefinition(
                                            stateRuleId,
                                            stateCondition.FieldId,
                                            stateCondition.OperatorId,
                                            stateCondition.ExpectedValue,
                                            stateCondition.UnitId),
                                        Sources(
                                            sourceObject.Source,
                                            targetObject.Source,
                                            stateCondition.Source,
                                            presentation.Source,
                                            recipe.Source)));
                            }
                        }

                        presentationStates.Add(
                            new NormalizedItem<NormalizedPresentationStateDefinition>(
                                PairIdentity(
                                    recipe.RecipeId,
                                    sourceObject.EntityId,
                                    targetObject.EntityId,
                                    "表现状态",
                                    presentation.PresentationId),
                                new NormalizedPresentationStateDefinition(
                                    stateId,
                                    sourceObject.EntityId,
                                    stateRuleIds,
                                    targetObject.EntityId),
                                Sources(
                                    sourceObject.Source,
                                    targetObject.Source,
                                    presentation.Source,
                                    recipe.Source)));
                    }
                }

                var presentationGroupId = string.Empty;
                if (effectIds.Count > 0)
                {
                    presentationGroupId =
                        $"表现组.{item.OperationName}.{sourceObject.EntityId}."
                        + targetObject.EntityId;
                    presentationGroups.Add(
                        new NormalizedItem<
                            NormalizedPresentationGroupDefinition>(
                            PairIdentity(
                                recipe.RecipeId,
                                sourceObject.EntityId,
                                targetObject.EntityId,
                                "表现组",
                                item.OperationName),
                            new NormalizedPresentationGroupDefinition(
                                presentationGroupId,
                                effectIds),
                            Sources(
                                sourceObject.Source,
                                targetObject.Source,
                                item.Source,
                                recipe.Source)));
                }

                var policyId =
                    $"策略.{item.OperationName}.{sourceObject.EntityId}."
                    + targetObject.EntityId;
                actions.Add(new NormalizedItem<NormalizedActionDefinition>(
                    PairIdentity(
                        recipe.RecipeId,
                        sourceObject.EntityId,
                        targetObject.EntityId,
                        "动作策略",
                        item.OperationName),
                    new NormalizedActionDefinition(
                        policyId,
                        item.SemanticCommandId,
                        item.OperationId,
                        item.Lifecycle,
                        item.ExecutionModeId,
                        item.Phase,
                        sourceObject.EntityId,
                        targetObject.EntityId,
                        ruleIds,
                        resultGroupId,
                        presentationGroupId,
                        item.Priority,
                        item.ReviewResult),
                    Sources(
                        sourceObject.Source,
                        targetObject.Source,
                        item.Source,
                        recipe.Source)));
            }
        }

        private static void AddPort(
            CourseObjectBlueprint courseObject,
            string targetEntityId,
            string portId,
            string compatibilityGroup,
            ConfigurationSource parameterSource,
            RecipeDefinition recipe,
            ICollection<NormalizedItem<NormalizedPortDefinition>> ports)
        {
            if (ports.Any(value => string.Equals(
                    value.Definition.PortId,
                    portId,
                    StringComparison.Ordinal)))
            {
                return;
            }

            ports.Add(new NormalizedItem<NormalizedPortDefinition>(
                PairIdentity(
                    recipe.RecipeId,
                    courseObject.EntityId,
                    targetEntityId,
                    "端口",
                    portId),
                new NormalizedPortDefinition(
                    portId,
                    courseObject.EntityId,
                    compatibilityGroup),
                Sources(
                    courseObject.Source,
                    parameterSource,
                    recipe.Source)));
        }

        private static string PortId(
            string entityId,
            string parameterName,
            string compatibilityGroup = "")
        {
            var parts = parameterName.Split('.');
            var localName = parts.Length >= 2
                ? parts[parts.Length - 2]
                : parameterName;
            return string.IsNullOrWhiteSpace(compatibilityGroup)
                ? $"{entityId}.{localName}"
                : $"{entityId}.{localName}.{compatibilityGroup}";
        }

        private static GeneratedItemIdentity PairIdentity(
            string recipeId,
            string sourceEntityId,
            string targetEntityId,
            string type,
            string localKey) =>
            new GeneratedItemIdentity(
                recipeId,
                sourceEntityId,
                targetEntityId,
                type,
                localKey);

        private static GeneratedItemIdentity Identity(
            string recipeId,
            string sourceEntityId,
            string type,
            string localKey) =>
            new GeneratedItemIdentity(
                recipeId,
                sourceEntityId,
                string.Empty,
                type,
                localKey);

        private static IReadOnlyList<ConfigurationSource> Sources(
            params ConfigurationSource[] sources) =>
            sources
                .Where(value => value != null)
                .GroupBy(value =>
                    $"{value.Layer}\u001F{value.PackageId}\u001F{value.FileName}\u001F"
                    + $"{value.Line}\u001F{value.Column}\u001F{value.ConfigurationId}",
                    StringComparer.Ordinal)
                .Select(value => value.First())
                .ToArray();

        private static string Readable(string value) =>
            (value ?? string.Empty).Replace(".", string.Empty);

        private static CoursePresentationLifecycle Lifecycle(
            RecipePresentationDefinition presentation) =>
            presentation.Lifecycle;

        private static IEnumerable<
            CoursePresentationParameterBindingDefinition>
            PresentationParameterBindings(
                RecipePresentationDefinition presentation)
        {
            foreach (var binding in presentation.Bindings)
            {
                if (binding.Kind != RecipeBindingKind.SignalPayload)
                {
                    throw new InvalidOperationException(
                        $"表现“{presentation.PresentationId}”的动态参数只支持信号载荷绑定。");
                }

                yield return new
                    CoursePresentationParameterBindingDefinition(
                        binding.OutputName,
                        CoursePresentationParameterSource.SignalPayload,
                        null,
                        binding.ParameterName);
            }
        }

        private static string PresentationSubject(
            RecipePresentationDefinition presentation) =>
            presentation.TargetEntityBinding switch
            {
                RecipeBindingKind.ActionSource => string.Empty,
                RecipeBindingKind.ActionTarget =>
                    CoursePresentationSemanticSubjects.ActionTarget,
                _ => throw new InvalidOperationException(
                    $"表现“{presentation.PresentationId}”的目标绑定“"
                    + $"{presentation.TargetEntityBinding}”不受支持。")
            };

        private static string PresentationLocationId(
            RecipePresentationDefinition presentation,
            string sourcePortId,
            string targetPortId) =>
            presentation.LocationIdBinding switch
            {
                RecipeBindingKind.CurrentEntity => presentation.LocationId,
                RecipeBindingKind.MatchedSourcePort => sourcePortId,
                RecipeBindingKind.MatchedTargetPort => targetPortId,
                _ => throw new InvalidOperationException(
                    $"表现“{presentation.PresentationId}”的位置绑定“"
                    + $"{presentation.LocationIdBinding}”不受支持。")
            };

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            ConfigurationSource source,
            string configurationId,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                source?.FileName ?? "共享配方目录",
                source?.Line ?? 1,
                source?.Column ?? 1,
                string.Empty,
                configurationId,
                reason,
                suggestion);

        private sealed class PairMatch
        {
            public PairMatch(
                string sourcePortId,
                string targetPortId,
                string compatibilityGroup,
                ConfigurationSource sourceParameterSource,
                ConfigurationSource targetParameterSource)
            {
                SourcePortId = sourcePortId;
                TargetPortId = targetPortId;
                CompatibilityGroup = compatibilityGroup;
                SourceParameterSource = sourceParameterSource;
                TargetParameterSource = targetParameterSource;
            }

            public string SourcePortId { get; }
            public string TargetPortId { get; }
            public string CompatibilityGroup { get; }
            public ConfigurationSource SourceParameterSource { get; }
            public ConfigurationSource TargetParameterSource { get; }
        }
    }
}
