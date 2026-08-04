using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Normalized
{
    public interface INormalizedDefinition
    {
        string DefinitionId { get; }
    }

    public interface INormalizedItem
    {
        string GeneratedItemId { get; }
        string DefinitionId { get; }
        ConfigurationProvenance Provenance { get; }
    }

    /// <summary>
    /// 结构化生成身份，不使用哈希；长度前缀确保分隔符不会造成身份碰撞。
    /// </summary>
    public sealed class GeneratedItemIdentity
    {
        public GeneratedItemIdentity(
            string recipeId,
            string sourceEntityId,
            string targetEntityId,
            string generatedItemType,
            string localKey)
        {
            RecipeId = Required(recipeId, nameof(recipeId));
            SourceEntityId = sourceEntityId ?? string.Empty;
            TargetEntityId = targetEntityId ?? string.Empty;
            GeneratedItemType = Required(
                generatedItemType,
                nameof(generatedItemType));
            LocalKey = Required(localKey, nameof(localKey));
            GeneratedItemId = string.Join(
                "|",
                Part("配方", RecipeId),
                Part("来源", SourceEntityId),
                Part("目标", TargetEntityId),
                Part("类型", GeneratedItemType),
                Part("局部键", LocalKey));
        }

        public string RecipeId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public string GeneratedItemType { get; }
        public string LocalKey { get; }
        public string GeneratedItemId { get; }

        private static string Part(string name, string value) =>
            $"{name}[{Encoding.UTF8.GetByteCount(value)}]={value}";

        private static string Required(string value, string parameterName) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("生成身份分量不能为空。", parameterName)
                : value.Trim();
    }

    public sealed class NormalizedItem<T> : INormalizedItem
        where T : INormalizedDefinition
    {
        public NormalizedItem(
            GeneratedItemIdentity identity,
            T definition,
            IEnumerable<ConfigurationSource> sources,
            string overrideOperation = null)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Provenance = new ConfigurationProvenance(
                identity.GeneratedItemId,
                sources ?? throw new ArgumentNullException(nameof(sources)),
                overrideOperation);
        }

        public GeneratedItemIdentity Identity { get; }
        public string GeneratedItemId => Identity.GeneratedItemId;
        public T Definition { get; }
        public string DefinitionId => Definition.DefinitionId;
        public ConfigurationProvenance Provenance { get; }
    }

    public sealed class NormalizedEntityDefinition : INormalizedDefinition
    {
        public NormalizedEntityDefinition(
            string entityId,
            IEnumerable<string> featureIds = null)
        {
            EntityId = entityId ?? string.Empty;
            FeatureIds = Copy(featureIds);
        }

        public string EntityId { get; }
        public IReadOnlyList<string> FeatureIds { get; }
        public string DefinitionId => EntityId;

        private static IReadOnlyList<string> Copy(IEnumerable<string> values) =>
            (values ?? Array.Empty<string>()).ToArray();
    }

    public sealed class NormalizedActionDefinition : INormalizedDefinition
    {
        public NormalizedActionDefinition(
            string policyId,
            string actionId,
            string sourceEntityId,
            string targetEntityId,
            IEnumerable<string> ruleIds,
            string resultGroupId,
            string presentationGroupId,
            int priority = 100,
            string policyEffect = "允许",
            string messageId = "",
            string rejectionCode = "",
            string rejectionMessage = "")
        {
            PolicyId = policyId ?? string.Empty;
            ActionId = actionId ?? string.Empty;
            SourceEntityId = sourceEntityId ?? string.Empty;
            TargetEntityId = targetEntityId ?? string.Empty;
            RuleIds = Copy(ruleIds);
            ResultGroupId = resultGroupId ?? string.Empty;
            PresentationGroupId = presentationGroupId ?? string.Empty;
            Priority = priority;
            PolicyEffect = policyEffect ?? string.Empty;
            MessageId = messageId ?? string.Empty;
            RejectionCode = rejectionCode ?? string.Empty;
            RejectionMessage = rejectionMessage ?? string.Empty;
        }

        public string PolicyId { get; }
        public string ActionId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public IReadOnlyList<string> RuleIds { get; }
        public string ResultGroupId { get; }
        public string PresentationGroupId { get; }
        public int Priority { get; }
        public string PolicyEffect { get; }
        public string MessageId { get; }
        public string RejectionCode { get; }
        public string RejectionMessage { get; }
        public string DefinitionId => PolicyId;

        private static IReadOnlyList<string> Copy(IEnumerable<string> values) =>
            (values ?? Array.Empty<string>()).ToArray();
    }

    public sealed class NormalizedPortDefinition : INormalizedDefinition
    {
        public NormalizedPortDefinition(
            string portId,
            string entityId,
            string compatibilityGroup)
        {
            PortId = portId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            CompatibilityGroup = compatibilityGroup ?? string.Empty;
        }

        public string PortId { get; }
        public string EntityId { get; }
        public string CompatibilityGroup { get; }
        public string DefinitionId => PortId;
    }

    public sealed class NormalizedRuleDefinition : INormalizedDefinition
    {
        public NormalizedRuleDefinition(
            string ruleId,
            string fieldId,
            string operatorId,
            string expectedValue,
            string unitId)
        {
            RuleId = ruleId ?? string.Empty;
            FieldId = fieldId ?? string.Empty;
            OperatorId = operatorId ?? string.Empty;
            ExpectedValue = expectedValue ?? string.Empty;
            UnitId = unitId ?? string.Empty;
        }

        public string RuleId { get; }
        public string FieldId { get; }
        public string OperatorId { get; }
        public string ExpectedValue { get; }
        public string UnitId { get; }
        public string DefinitionId => RuleId;
    }

    public sealed class NormalizedStateChangeDefinition : INormalizedDefinition
    {
        public NormalizedStateChangeDefinition(
            string mutationId,
            string operationId,
            IEnumerable<KeyValuePair<string, string>> parameters = null)
        {
            MutationId = mutationId ?? string.Empty;
            OperationId = operationId ?? string.Empty;
            Parameters = CopyParameters(parameters);
        }

        public string MutationId { get; }
        public string OperationId { get; }
        public IReadOnlyDictionary<string, string> Parameters { get; }
        public string DefinitionId => MutationId;

        private static IReadOnlyDictionary<string, string> CopyParameters(
            IEnumerable<KeyValuePair<string, string>> parameters)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in parameters
                         ?? Array.Empty<KeyValuePair<string, string>>())
            {
                if (!result.ContainsKey(pair.Key))
                {
                    result.Add(pair.Key, pair.Value ?? string.Empty);
                }
            }

            return new ReadOnlyDictionary<string, string>(result);
        }
    }

    public sealed class NormalizedDomainEventDefinition : INormalizedDefinition
    {
        public NormalizedDomainEventDefinition(string eventId, string eventType)
        {
            EventId = eventId ?? string.Empty;
            EventType = eventType ?? string.Empty;
        }

        public string EventId { get; }
        public string EventType { get; }
        public string DefinitionId => EventId;
    }

    public sealed class NormalizedActionResultGroupDefinition :
        INormalizedDefinition
    {
        public NormalizedActionResultGroupDefinition(
            string groupId,
            IEnumerable<string> mutationIds,
            IEnumerable<string> eventIds)
        {
            GroupId = groupId ?? string.Empty;
            MutationIds = Copy(mutationIds);
            EventIds = Copy(eventIds);
        }

        public string GroupId { get; }
        public IReadOnlyList<string> MutationIds { get; }
        public IReadOnlyList<string> EventIds { get; }
        public string DefinitionId => GroupId;

        private static IReadOnlyList<string> Copy(IEnumerable<string> values) =>
            (values ?? Array.Empty<string>()).ToArray();
    }

    public sealed class NormalizedPresentationStateDefinition :
        INormalizedDefinition
    {
        public NormalizedPresentationStateDefinition(
            string stateId,
            string subjectEntityId,
            IEnumerable<string> ruleIds,
            string contextTargetEntityId = "")
        {
            StateId = stateId ?? string.Empty;
            SubjectEntityId = subjectEntityId ?? string.Empty;
            RuleIds = (ruleIds ?? Array.Empty<string>()).ToArray();
            ContextTargetEntityId = contextTargetEntityId ?? string.Empty;
        }

        public string StateId { get; }
        public string SubjectEntityId { get; }
        public IReadOnlyList<string> RuleIds { get; }
        public string ContextTargetEntityId { get; }
        public string DefinitionId => StateId;
    }

    public sealed class NormalizedPresentationEffectDefinition :
        INormalizedDefinition
    {
        public NormalizedPresentationEffectDefinition(
            string effectId,
            string protocolId)
            : this(
                effectId,
                protocolId,
                string.Empty,
                null,
                string.Empty,
                CoursePresentationLifecycle.OneShot,
                Array.Empty<KeyValuePair<string, StructuredValue>>(),
                null,
                string.Empty)
        {
        }

        public NormalizedPresentationEffectDefinition(
            string effectId,
            string protocolId,
            string subjectId,
            CoursePresentationLocationKind? locationKind,
            string locationId,
            CoursePresentationLifecycle lifecycle,
            IEnumerable<KeyValuePair<string, StructuredValue>>
                parameterValues,
            CoursePresentationTriggerKind? triggerKind = null,
            string triggerValue = "",
            string triggerSourceEntityId = "",
            string triggerTargetEntityId = "",
            IEnumerable<CoursePresentationParameterBindingDefinition>
                dynamicParameterBindings = null)
        {
            EffectId = effectId ?? string.Empty;
            ProtocolId = protocolId ?? string.Empty;
            SubjectId = subjectId ?? string.Empty;
            LocationKind = locationKind;
            LocationId = locationId ?? string.Empty;
            Lifecycle = lifecycle;
            ParameterValues = CopyParameters(parameterValues);
            ParameterBindings = CopyBindings(
                ParameterValues.Select(value =>
                    new CoursePresentationParameterBindingDefinition(
                        value.Key,
                        CoursePresentationParameterSource.Constant,
                        value.Value,
                        null))
                    .Concat(dynamicParameterBindings
                        ?? Array.Empty<
                            CoursePresentationParameterBindingDefinition>()));
            TriggerKind = triggerKind;
            TriggerValue = triggerValue ?? string.Empty;
            TriggerSourceEntityId = triggerSourceEntityId ?? string.Empty;
            TriggerTargetEntityId = triggerTargetEntityId ?? string.Empty;
        }

        public string EffectId { get; }
        public string ProtocolId { get; }
        public string SubjectId { get; }
        public CoursePresentationLocationKind? LocationKind { get; }
        public string LocationId { get; }
        public CoursePresentationLifecycle Lifecycle { get; }
        public CoursePresentationTriggerKind? TriggerKind { get; }
        public string TriggerValue { get; }
        public string TriggerSourceEntityId { get; }
        public string TriggerTargetEntityId { get; }
        public IReadOnlyDictionary<string, StructuredValue> ParameterValues
        {
            get;
        }
        public IReadOnlyList<CoursePresentationParameterBindingDefinition>
            ParameterBindings { get; }
        public string DefinitionId => EffectId;

        private static IReadOnlyDictionary<string, StructuredValue>
            CopyParameters(
                IEnumerable<KeyValuePair<string, StructuredValue>> values)
        {
            var result = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var pair in values
                         ?? Array.Empty<KeyValuePair<string, StructuredValue>>())
            {
                if (!result.ContainsKey(pair.Key))
                {
                    result.Add(pair.Key, pair.Value);
                }
            }

            return new ReadOnlyDictionary<string, StructuredValue>(result);
        }

        private static IReadOnlyList<
            CoursePresentationParameterBindingDefinition> CopyBindings(
                IEnumerable<CoursePresentationParameterBindingDefinition>
                    values)
        {
            var result = new List<
                CoursePresentationParameterBindingDefinition>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values ?? Array.Empty<
                         CoursePresentationParameterBindingDefinition>())
            {
                if (value != null && names.Add(value.Name))
                {
                    result.Add(value);
                }
            }

            return new ReadOnlyCollection<
                CoursePresentationParameterBindingDefinition>(result);
        }
    }

    public sealed class NormalizedPresentationGroupDefinition :
        INormalizedDefinition
    {
        public NormalizedPresentationGroupDefinition(
            string groupId,
            IEnumerable<string> effectIds)
        {
            GroupId = groupId ?? string.Empty;
            EffectIds = (effectIds ?? Array.Empty<string>()).ToArray();
        }

        public string GroupId { get; }
        public IReadOnlyList<string> EffectIds { get; }
        public string DefinitionId => GroupId;
    }

    public sealed class NormalizedEvaluationDefinition : INormalizedDefinition
    {
        public NormalizedEvaluationDefinition(
            string evaluationId,
            IEnumerable<string> conditionRuleIds)
            : this(
                evaluationId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                (conditionRuleIds ?? Array.Empty<string>())
                .Select(value => new NormalizedEvaluationConditionDefinition(
                    value,
                    string.Empty,
                    string.Empty)),
                0,
                string.Empty)
        {
        }

        public NormalizedEvaluationDefinition(
            string evaluationId,
            string evaluationType,
            string displayName,
            string triggerType,
            string triggerValue,
            int order,
            IEnumerable<NormalizedEvaluationConditionDefinition> conditions,
            int scoreDelta,
            string promptMessage)
        {
            EvaluationId = evaluationId ?? string.Empty;
            EvaluationType = evaluationType ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            TriggerType = triggerType ?? string.Empty;
            TriggerValue = triggerValue ?? string.Empty;
            Order = order;
            Conditions = (conditions
                          ?? Array.Empty<NormalizedEvaluationConditionDefinition>())
                .OrderBy(value => value.RuleId, StringComparer.Ordinal)
                .ToArray();
            ConditionRuleIds = Conditions
                .Select(value => value.RuleId)
                .ToArray();
            ScoreDelta = scoreDelta;
            PromptMessage = promptMessage ?? string.Empty;
        }

        public string EvaluationId { get; }
        public string EvaluationType { get; }
        public string DisplayName { get; }
        public string TriggerType { get; }
        public string TriggerValue { get; }
        public int Order { get; }
        public IReadOnlyList<NormalizedEvaluationConditionDefinition>
            Conditions { get; }
        public IReadOnlyList<string> ConditionRuleIds { get; }
        public int ScoreDelta { get; }
        public string PromptMessage { get; }
        public string DefinitionId => EvaluationId;
    }

    public sealed class NormalizedEvaluationConditionDefinition
    {
        public NormalizedEvaluationConditionDefinition(
            string ruleId,
            string conditionType,
            string subjectEntityId)
        {
            RuleId = ruleId ?? string.Empty;
            ConditionType = conditionType ?? string.Empty;
            SubjectEntityId = subjectEntityId ?? string.Empty;
        }

        public string RuleId { get; }
        public string ConditionType { get; }
        public string SubjectEntityId { get; }
    }

    public sealed class NormalizedTeachingGoalDefinition :
        INormalizedDefinition
    {
        public NormalizedTeachingGoalDefinition(
            string goalId,
            string displayName,
            string triggerType,
            string triggerValue,
            int order,
            IEnumerable<NormalizedEvaluationConditionDefinition> conditions)
        {
            GoalId = goalId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            TriggerType = triggerType ?? string.Empty;
            TriggerValue = triggerValue ?? string.Empty;
            Order = order;
            Conditions = (conditions
                          ?? Array.Empty<NormalizedEvaluationConditionDefinition>())
                .OrderBy(value => value.RuleId, StringComparer.Ordinal)
                .ToArray();
        }

        public string GoalId { get; }
        public string DisplayName { get; }
        public string TriggerType { get; }
        public string TriggerValue { get; }
        public int Order { get; }
        public IReadOnlyList<NormalizedEvaluationConditionDefinition>
            Conditions { get; }
        public string DefinitionId => "教学目标." + GoalId;
    }

    public sealed class NormalizedTeachingRiskDefinition :
        INormalizedDefinition
    {
        public NormalizedTeachingRiskDefinition(
            string riskId,
            string displayName,
            string triggerType,
            string triggerValue,
            int order,
            IEnumerable<NormalizedEvaluationConditionDefinition> conditions,
            string consequenceSeverity = null,
            string recoverability = null,
            IEnumerable<string> blockedGoalIds = null)
        {
            RiskId = riskId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            TriggerType = triggerType ?? string.Empty;
            TriggerValue = triggerValue ?? string.Empty;
            Order = order;
            ConsequenceSeverity = consequenceSeverity ?? string.Empty;
            Recoverability = recoverability ?? string.Empty;
            BlockedGoalIds = (blockedGoalIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            Conditions = (conditions
                          ?? Array.Empty<NormalizedEvaluationConditionDefinition>())
                .OrderBy(value => value.RuleId, StringComparer.Ordinal)
                .ToArray();
        }

        public string RiskId { get; }
        public string DisplayName { get; }
        public string TriggerType { get; }
        public string TriggerValue { get; }
        public int Order { get; }
        public string ConsequenceSeverity { get; }
        public string Recoverability { get; }
        public IReadOnlyList<string> BlockedGoalIds { get; }
        public IReadOnlyList<NormalizedEvaluationConditionDefinition>
            Conditions { get; }
        public string DefinitionId => "教学风险." + RiskId;
    }

    public sealed class NormalizedTeachingScoreDefinition :
        INormalizedDefinition
    {
        public NormalizedTeachingScoreDefinition(
            string evaluationId,
            int scoreDelta)
        {
            EvaluationId = evaluationId ?? string.Empty;
            ScoreDelta = scoreDelta;
        }

        public string EvaluationId { get; }
        public int ScoreDelta { get; }
        public string DefinitionId => "教学评分." + EvaluationId;
    }

    public sealed class NormalizedTeachingHintDefinition :
        INormalizedDefinition
    {
        public NormalizedTeachingHintDefinition(
            string evaluationId,
            string message)
        {
            EvaluationId = evaluationId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string EvaluationId { get; }
        public string Message { get; }
        public string DefinitionId => "教学提示." + EvaluationId;
    }

    public sealed class NormalizedAcceptanceAssertionDefinition
    {
        public NormalizedAcceptanceAssertionDefinition(
            string assertionType,
            string objectId,
            string fieldId,
            string operatorId,
            string expectedValue,
            string unitId)
        {
            AssertionType = assertionType ?? string.Empty;
            ObjectId = objectId ?? string.Empty;
            FieldId = fieldId ?? string.Empty;
            OperatorId = operatorId ?? string.Empty;
            ExpectedValue = expectedValue ?? string.Empty;
            UnitId = unitId ?? string.Empty;
        }

        public string AssertionType { get; }
        public string ObjectId { get; }
        public string FieldId { get; }
        public string OperatorId { get; }
        public string ExpectedValue { get; }
        public string UnitId { get; }
    }

    public sealed class NormalizedAcceptanceStepDefinition
    {
        public NormalizedAcceptanceStepDefinition(
            int order,
            SemanticActionRequest actionRequest,
            IEnumerable<NormalizedAcceptanceAssertionDefinition> assertions)
        {
            Order = order;
            ActionRequest = actionRequest;
            Assertions = (assertions
                          ?? Array.Empty<NormalizedAcceptanceAssertionDefinition>())
                .ToArray();
        }

        public int Order { get; }
        public SemanticActionRequest ActionRequest { get; }
        public IReadOnlyList<NormalizedAcceptanceAssertionDefinition>
            Assertions { get; }
    }

    public sealed class NormalizedAcceptanceScenarioDefinition :
        INormalizedDefinition
    {
        public NormalizedAcceptanceScenarioDefinition(
            string scenarioId,
            IEnumerable<NormalizedAcceptanceStepDefinition> steps)
        {
            ScenarioId = scenarioId ?? string.Empty;
            Steps = (steps ?? Array.Empty<NormalizedAcceptanceStepDefinition>())
                .OrderBy(value => value.Order)
                .ToArray();
        }

        public string ScenarioId { get; }
        public IReadOnlyList<NormalizedAcceptanceStepDefinition> Steps
        {
            get;
        }
        public string DefinitionId => "验收场景." + ScenarioId;
    }

    public sealed class NormalizedPrefabContractDefinition :
        INormalizedDefinition
    {
        public NormalizedPrefabContractDefinition(
            string contractId,
            string entityId,
            string contractKind,
            string identifier)
        {
            ContractId = contractId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            ContractKind = contractKind ?? string.Empty;
            Identifier = identifier ?? string.Empty;
        }

        public string ContractId { get; }
        public string EntityId { get; }
        public string ContractKind { get; }
        public string Identifier { get; }
        public string DefinitionId => ContractId;
    }

    /// <summary>
    /// 学科生成物向通用工作台提供的只读摘要。类别和字段均由学科包定义，
    /// 因此物理、生物等学科无需让工作台认识其具体数据类型。
    /// </summary>
    public sealed class GeneratedCourseOverviewEntry
    {
        public GeneratedCourseOverviewEntry(
            string category,
            string itemId,
            string displayName,
            IEnumerable<KeyValuePair<string, string>> fields = null,
            string subjectId = "")
        {
            Category = Required(category, nameof(category));
            ItemId = Required(itemId, nameof(itemId));
            DisplayName = Required(displayName, nameof(displayName));
            SubjectId = subjectId?.Trim() ?? string.Empty;
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields
                         ?? Array.Empty<KeyValuePair<string, string>>())
            {
                var name = Required(field.Key, nameof(fields));
                if (!copy.TryAdd(name, field.Value ?? string.Empty))
                {
                    throw new ArgumentException(
                        $"学科总览“{ItemId}”包含重复字段“{name}”。",
                        nameof(fields));
                }
            }

            Fields = new ReadOnlyDictionary<string, string>(copy);
        }

        public string Category { get; }
        public string ItemId { get; }
        public string DisplayName { get; }
        public string SubjectId { get; }
        public IReadOnlyDictionary<string, string> Fields { get; }

        private static string Required(string value, string parameterName) =>
            string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException(
                    "学科总览字段不能为空。",
                    parameterName)
                : value.Trim();
    }

    public sealed class GeneratedCourseArtifact : INormalizedDefinition
    {
        private readonly byte[] _content;

        public GeneratedCourseArtifact(
            string artifactId,
            string suggestedFileName,
            byte[] content,
            IEnumerable<GeneratedCourseOverviewEntry> overviewEntries = null)
        {
            ArtifactId = artifactId ?? string.Empty;
            SuggestedFileName = suggestedFileName ?? string.Empty;
            _content = (content ?? throw new ArgumentNullException(nameof(content)))
                .ToArray();
            OverviewEntries = (overviewEntries
                               ?? Array.Empty<GeneratedCourseOverviewEntry>())
                .Select(value => value
                    ?? throw new ArgumentException(
                        "学科总览不能包含空值。",
                        nameof(overviewEntries)))
                .OrderBy(value => value.Category, StringComparer.Ordinal)
                .ThenBy(value => value.ItemId, StringComparer.Ordinal)
                .ToArray();
        }

        public string ArtifactId { get; }
        public string SuggestedFileName { get; }
        public byte[] Content => _content.ToArray();
        public IReadOnlyList<GeneratedCourseOverviewEntry> OverviewEntries
        {
            get;
        }
        public string DefinitionId => ArtifactId;
    }

    /// <summary>
    /// 仅存在于 Editor 内存中的规范化课程；所有集合在构造时稳定排序。
    /// </summary>
    public sealed class NormalizedCourseModel
    {
        public NormalizedCourseModel(
            string courseId,
            IEnumerable<NormalizedItem<NormalizedEntityDefinition>> entities = null,
            IEnumerable<NormalizedItem<NormalizedPortDefinition>> ports = null,
            IEnumerable<NormalizedItem<NormalizedActionDefinition>> actions = null,
            IEnumerable<NormalizedItem<NormalizedRuleDefinition>> rules = null,
            IEnumerable<NormalizedItem<NormalizedStateChangeDefinition>> stateChanges = null,
            IEnumerable<NormalizedItem<NormalizedDomainEventDefinition>> domainEvents = null,
            IEnumerable<NormalizedItem<NormalizedActionResultGroupDefinition>> actionResultGroups = null,
            IEnumerable<NormalizedItem<NormalizedPresentationStateDefinition>> presentationStates = null,
            IEnumerable<NormalizedItem<NormalizedPresentationEffectDefinition>> presentationEffects = null,
            IEnumerable<NormalizedItem<NormalizedPresentationGroupDefinition>> presentationGroups = null,
            IEnumerable<NormalizedItem<NormalizedEvaluationDefinition>> evaluations = null,
            IEnumerable<NormalizedItem<NormalizedTeachingGoalDefinition>> teachingGoals = null,
            IEnumerable<NormalizedItem<NormalizedTeachingRiskDefinition>> teachingRisks = null,
            IEnumerable<NormalizedItem<NormalizedTeachingScoreDefinition>> teachingScores = null,
            IEnumerable<NormalizedItem<NormalizedTeachingHintDefinition>> teachingHints = null,
            IEnumerable<NormalizedItem<NormalizedAcceptanceScenarioDefinition>> acceptanceScenarios = null,
            IEnumerable<NormalizedItem<NormalizedPrefabContractDefinition>> prefabContracts = null,
            IEnumerable<NormalizedItem<GeneratedCourseArtifact>> generatedArtifacts = null,
            IEnumerable<string> knownUnitIds = null,
            IEnumerable<string> knownOperationIds = null,
            IEnumerable<string> knownPresentationProtocolIds = null)
        {
            CourseId = courseId ?? string.Empty;
            Entities = Sort(entities);
            Ports = Sort(ports);
            Actions = Sort(actions);
            Rules = Sort(rules);
            StateChanges = Sort(stateChanges);
            DomainEvents = Sort(domainEvents);
            ActionResultGroups = Sort(actionResultGroups);
            PresentationStates = Sort(presentationStates);
            PresentationEffects = Sort(presentationEffects);
            PresentationGroups = Sort(presentationGroups);
            Evaluations = Sort(evaluations);
            TeachingGoals = Sort(teachingGoals);
            TeachingRisks = Sort(teachingRisks);
            TeachingScores = Sort(teachingScores);
            TeachingHints = Sort(teachingHints);
            AcceptanceScenarios = Sort(acceptanceScenarios);
            PrefabContracts = Sort(prefabContracts);
            GeneratedArtifacts = Sort(generatedArtifacts);
            ProvenanceByGeneratedItemId =
                new ReadOnlyDictionary<string, ConfigurationProvenance>(
                    AllItems()
                        .GroupBy(
                            value => value.GeneratedItemId,
                            StringComparer.Ordinal)
                        .ToDictionary(
                            value => value.Key,
                            value => value.First().Provenance,
                            StringComparer.Ordinal));
            KnownUnitIds = Strings(knownUnitIds);
            KnownOperationIds = Strings(knownOperationIds);
            KnownPresentationProtocolIds = Strings(
                knownPresentationProtocolIds);
        }

        public string CourseId { get; }
        public IReadOnlyList<NormalizedItem<NormalizedEntityDefinition>> Entities { get; }
        public IReadOnlyList<NormalizedItem<NormalizedPortDefinition>> Ports { get; }
        public IReadOnlyList<NormalizedItem<NormalizedActionDefinition>> Actions { get; }
        public IReadOnlyList<NormalizedItem<NormalizedRuleDefinition>> Rules { get; }
        public IReadOnlyList<NormalizedItem<NormalizedStateChangeDefinition>> StateChanges { get; }
        public IReadOnlyList<NormalizedItem<NormalizedDomainEventDefinition>> DomainEvents { get; }
        public IReadOnlyList<NormalizedItem<NormalizedActionResultGroupDefinition>> ActionResultGroups { get; }
        public IReadOnlyList<NormalizedItem<NormalizedPresentationStateDefinition>> PresentationStates { get; }
        public IReadOnlyList<NormalizedItem<NormalizedPresentationEffectDefinition>> PresentationEffects { get; }
        public IReadOnlyList<NormalizedItem<NormalizedPresentationGroupDefinition>> PresentationGroups { get; }
        public IReadOnlyList<NormalizedItem<NormalizedEvaluationDefinition>> Evaluations { get; }
        public IReadOnlyList<NormalizedItem<NormalizedTeachingGoalDefinition>>
            TeachingGoals { get; }
        public IReadOnlyList<NormalizedItem<NormalizedTeachingRiskDefinition>>
            TeachingRisks { get; }
        public IReadOnlyList<NormalizedItem<NormalizedTeachingScoreDefinition>>
            TeachingScores { get; }
        public IReadOnlyList<NormalizedItem<NormalizedTeachingHintDefinition>>
            TeachingHints { get; }
        public IReadOnlyList<NormalizedItem<NormalizedAcceptanceScenarioDefinition>>
            AcceptanceScenarios { get; }
        public IReadOnlyList<NormalizedItem<NormalizedPrefabContractDefinition>> PrefabContracts { get; }
        public IReadOnlyList<NormalizedItem<GeneratedCourseArtifact>> GeneratedArtifacts { get; }
        public IReadOnlyDictionary<string, ConfigurationProvenance>
            ProvenanceByGeneratedItemId { get; }
        public IReadOnlyList<string> KnownUnitIds { get; }
        public IReadOnlyList<string> KnownOperationIds { get; }
        public IReadOnlyList<string> KnownPresentationProtocolIds { get; }

        public IEnumerable<INormalizedItem> AllItems()
        {
            return Entities.Cast<INormalizedItem>()
                .Concat(Ports)
                .Concat(Actions)
                .Concat(Rules)
                .Concat(StateChanges)
                .Concat(DomainEvents)
                .Concat(ActionResultGroups)
                .Concat(PresentationStates)
                .Concat(PresentationEffects)
                .Concat(PresentationGroups)
                .Concat(Evaluations)
                .Concat(TeachingGoals)
                .Concat(TeachingRisks)
                .Concat(TeachingScores)
                .Concat(TeachingHints)
                .Concat(AcceptanceScenarios)
                .Concat(PrefabContracts)
                .Concat(GeneratedArtifacts);
        }

        private static IReadOnlyList<NormalizedItem<T>> Sort<T>(
            IEnumerable<NormalizedItem<T>> values)
            where T : INormalizedDefinition =>
            (values ?? Array.Empty<NormalizedItem<T>>())
            .OrderBy(value => value.GeneratedItemId, StringComparer.Ordinal)
            .ToArray();

        private static IReadOnlyList<string> Strings(
            IEnumerable<string> values) =>
            (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }
}
