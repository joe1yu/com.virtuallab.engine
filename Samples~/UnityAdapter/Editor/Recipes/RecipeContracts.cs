using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain.Relations;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Normalized;

namespace VirtualLab.Unity.Authoring.Recipes
{
    public enum RecipeLayer
    {
        Platform,
        Discipline
    }

    public enum RecipeMatchKind
    {
        SingleEntity,
        CompatiblePair,
        Process
    }

    public enum RecipeSafetyLevel
    {
        NonWeakenable,
        CourseMayTighten,
        CourseMayReplacePresentation
    }

    public enum RecipeBindingKind
    {
        CurrentEntity,
        ActionActor,
        ActionSource,
        ActionTarget,
        MatchedSourcePort,
        MatchedTargetPort,
        EntityParameter,
        SignalPayload
    }

    public enum RecipeParameterType
    {
        Text,
        Boolean,
        Integer,
        Number,
        EntityId,
        PortId
    }

    public enum PairCompatibilityKind
    {
        EqualGroup,
        EqualValue,
        CourseAllowedPair
    }

    public sealed class CompatiblePairContract
    {
        public CompatiblePairContract(
            string sourceFeatureId,
            string targetFeatureId,
            string sourcePortParameter,
            string targetPortParameter,
            PairCompatibilityKind compatibilityKind,
            bool createsPorts = true)
        {
            SourceFeatureId = sourceFeatureId ?? string.Empty;
            TargetFeatureId = targetFeatureId ?? string.Empty;
            SourcePortParameter = sourcePortParameter ?? string.Empty;
            TargetPortParameter = targetPortParameter ?? string.Empty;
            CompatibilityKind = compatibilityKind;
            CreatesPorts = createsPorts;
        }

        public string SourceFeatureId { get; }
        public string TargetFeatureId { get; }
        public string SourcePortParameter { get; }
        public string TargetPortParameter { get; }
        public PairCompatibilityKind CompatibilityKind { get; }
        public bool CreatesPorts { get; }
    }

    public sealed class DisciplineRecordCompilationResult
    {
        public DisciplineRecordCompilationResult(
            IEnumerable<NormalizedItem<GeneratedCourseArtifact>> artifacts,
            IEnumerable<CourseCompilationDiagnostic> diagnostics,
            IEnumerable<DisciplineMutationOverride> mutationOverrides = null,
            IEnumerable<DisciplineMutationAddition> mutationAdditions = null)
        {
            Artifacts = (artifacts
                         ?? Array.Empty<NormalizedItem<GeneratedCourseArtifact>>())
                .ToArray();
            Diagnostics = (diagnostics
                           ?? Array.Empty<CourseCompilationDiagnostic>())
                .ToArray();
            MutationOverrides = (mutationOverrides
                                 ?? Array.Empty<DisciplineMutationOverride>())
                .ToArray();
            MutationAdditions = (mutationAdditions
                                 ?? Array.Empty<DisciplineMutationAddition>())
                .ToArray();
        }

        public IReadOnlyList<NormalizedItem<GeneratedCourseArtifact>> Artifacts
        {
            get;
        }

        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics
        {
            get;
        }

        public IReadOnlyList<DisciplineMutationOverride> MutationOverrides
        {
            get;
        }

        public IReadOnlyList<DisciplineMutationAddition> MutationAdditions
        {
            get;
        }
    }

    /// <summary>
    /// 学科记录对共享配方所生成协议操作的结构化参数覆盖。
    /// 以配方、实体对和配方内操作键定位，不依赖最终生成名称。
    /// </summary>
    public sealed class DisciplineMutationOverride
    {
        public DisciplineMutationOverride(
            string recipeId,
            string sourceEntityId,
            string targetEntityId,
            string operationLocalKey,
            string protocolOperationId,
            IEnumerable<KeyValuePair<string, string>> parameters,
            IEnumerable<ConfigurationSource> sources)
        {
            RecipeId = recipeId ?? string.Empty;
            SourceEntityId = sourceEntityId ?? string.Empty;
            TargetEntityId = targetEntityId ?? string.Empty;
            OperationLocalKey = operationLocalKey ?? string.Empty;
            ProtocolOperationId = protocolOperationId ?? string.Empty;
            Parameters = (parameters
                          ?? Array.Empty<KeyValuePair<string, string>>())
                .ToArray();
            Sources = (sources ?? Array.Empty<ConfigurationSource>())
                .Where(value => value != null)
                .ToArray();
        }

        public string RecipeId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public string OperationLocalKey { get; }
        public string ProtocolOperationId { get; }
        public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }
        public IReadOnlyList<ConfigurationSource> Sources { get; }
    }

    /// <summary>
    /// 学科记录向某个配方动作结果追加协议操作。
    /// 用于一次语义动作需要组合多个原子科学变化的场景。
    /// </summary>
    public sealed class DisciplineMutationAddition
    {
        public DisciplineMutationAddition(
            string recipeId,
            string sourceEntityId,
            string targetEntityId,
            string resultLocalKey,
            string operationLocalKey,
            string protocolOperationId,
            IEnumerable<KeyValuePair<string, string>> parameters,
            IEnumerable<ConfigurationSource> sources)
        {
            RecipeId = recipeId ?? string.Empty;
            SourceEntityId = sourceEntityId ?? string.Empty;
            TargetEntityId = targetEntityId ?? string.Empty;
            ResultLocalKey = resultLocalKey ?? string.Empty;
            OperationLocalKey = operationLocalKey ?? string.Empty;
            ProtocolOperationId = protocolOperationId ?? string.Empty;
            Parameters = (parameters
                          ?? Array.Empty<KeyValuePair<string, string>>())
                .ToArray();
            Sources = (sources ?? Array.Empty<ConfigurationSource>())
                .Where(value => value != null)
                .ToArray();
        }

        public string RecipeId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public string ResultLocalKey { get; }
        public string OperationLocalKey { get; }
        public string ProtocolOperationId { get; }
        public IReadOnlyList<KeyValuePair<string, string>> Parameters { get; }
        public IReadOnlyList<ConfigurationSource> Sources { get; }
    }

    public interface IDisciplineRecordCompiler
    {
        DisciplineRecordCompilationResult Compile(CourseBlueprint blueprint);
    }

    public interface IRecipePackageProvider
    {
        string PackageId { get; }
        IReadOnlyList<string> RequiredRuntimeModuleIds { get; }
        IReadOnlyList<RelationTypeId> RelationTypeIds { get; }
        IReadOnlyList<string> RegisteredStateOperationIds { get; }
        RecipePackage Load();
    }

    public sealed class RecipeValueBinding
    {
        public RecipeValueBinding(
            RecipeBindingKind kind,
            string parameterName)
        {
            Kind = kind;
            ParameterName = parameterName ?? string.Empty;
            OutputName = string.Empty;
        }

        public RecipeValueBinding(
            string outputName,
            RecipeBindingKind kind,
            string parameterName)
        {
            OutputName = outputName ?? string.Empty;
            Kind = kind;
            ParameterName = parameterName ?? string.Empty;
        }

        public string OutputName { get; }
        public RecipeBindingKind Kind { get; }
        public string ParameterName { get; }
    }

    public sealed class RecipeDefinition
    {
        public RecipeDefinition(
            string recipeId,
            RecipeMatchKind matchKind,
            string matchFeatureId,
            string extendsRecipeId,
            string replacesRecipeId,
            RecipeSafetyLevel safetyLevel,
            IEnumerable<string> referencedEntityIds,
            ConfigurationSource source = null,
            CompatiblePairContract compatiblePair = null)
        {
            RecipeId = recipeId ?? string.Empty;
            MatchKind = matchKind;
            MatchFeatureId = matchFeatureId ?? string.Empty;
            ExtendsRecipeId = extendsRecipeId ?? string.Empty;
            ReplacesRecipeId = replacesRecipeId ?? string.Empty;
            SafetyLevel = safetyLevel;
            ReferencedEntityIds = Copy(referencedEntityIds);
            Source = source;
            CompatiblePair = compatiblePair;
        }

        public string RecipeId { get; }
        public RecipeMatchKind MatchKind { get; }
        public string MatchFeatureId { get; }
        public string ExtendsRecipeId { get; }
        public string ReplacesRecipeId { get; }
        public RecipeSafetyLevel SafetyLevel { get; }
        public IReadOnlyList<string> ReferencedEntityIds { get; }
        public ConfigurationSource Source { get; }
        public CompatiblePairContract CompatiblePair { get; }

        private static IReadOnlyList<string> Copy(IEnumerable<string> values) =>
            (values ?? Array.Empty<string>()).ToArray();
    }

    public sealed class RecipeParameterContract
    {
        public RecipeParameterContract(
            string recipeId,
            string parameterName,
            RecipeParameterType parameterType,
            bool isRequired,
            string unit,
            double? minimum,
            double? maximum,
            string defaultValue,
            bool courseMayDelete,
            RecipeSafetyLevel safetyLevel)
        {
            RecipeId = recipeId ?? string.Empty;
            ParameterName = parameterName ?? string.Empty;
            ParameterType = parameterType;
            IsRequired = isRequired;
            Unit = unit ?? string.Empty;
            Minimum = minimum;
            Maximum = maximum;
            DefaultValue = defaultValue ?? string.Empty;
            CourseMayDelete = courseMayDelete;
            SafetyLevel = safetyLevel;
        }

        public string RecipeId { get; }
        public string ParameterName { get; }
        public RecipeParameterType ParameterType { get; }
        public bool IsRequired { get; }
        public string Unit { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }
        public string DefaultValue { get; }
        public bool CourseMayDelete { get; }
        public RecipeSafetyLevel SafetyLevel { get; }
    }

    /// <summary>
    /// 描述一个配方对外提供的语义操作，以及审核通过后执行的状态结果和表现反馈。
    /// </summary>
    public sealed class RecipeActionDefinition
    {
        public RecipeActionDefinition(
            string recipeId,
            string operationName,
            IEnumerable<string> conditionIds,
            IEnumerable<string> resultIds,
            IEnumerable<string> presentationIds,
            string semanticCommandId = "",
            int priority = 100,
            string reviewResult = "允许",
            ConfigurationSource source = null)
        {
            RecipeId = recipeId ?? string.Empty;
            OperationName = operationName ?? string.Empty;
            ConditionIds = Copy(conditionIds);
            ResultIds = Copy(resultIds);
            PresentationIds = Copy(presentationIds);
            SemanticCommandId = semanticCommandId ?? string.Empty;
            Priority = priority;
            ReviewResult = reviewResult ?? string.Empty;
            Source = source;
        }

        public string RecipeId { get; }
        public string OperationName { get; }
        public IReadOnlyList<string> ConditionIds { get; }
        public IReadOnlyList<string> ResultIds { get; }
        public IReadOnlyList<string> PresentationIds { get; }
        public string SemanticCommandId { get; }
        public int Priority { get; }
        public string ReviewResult { get; }
        public ConfigurationSource Source { get; }

        private static IReadOnlyList<string> Copy(IEnumerable<string> values) =>
            (values ?? Array.Empty<string>()).ToArray();
    }

    public sealed class RecipeConditionDefinition
    {
        public RecipeConditionDefinition(
            string conditionId,
            IEnumerable<RecipeValueBinding> bindings = null,
            string recipeId = "",
            string fieldId = "",
            string operatorId = "",
            string expectedValue = "",
            string unitId = "",
            string rejectionCode = "",
            string appliesToOperationName = "",
            ConfigurationSource source = null)
        {
            ConditionId = conditionId ?? string.Empty;
            Bindings = Copy(bindings);
            RecipeId = recipeId ?? string.Empty;
            FieldId = fieldId ?? string.Empty;
            OperatorId = operatorId ?? string.Empty;
            ExpectedValue = expectedValue ?? string.Empty;
            UnitId = unitId ?? string.Empty;
            RejectionCode = rejectionCode ?? string.Empty;
            AppliesToOperationName = appliesToOperationName ?? string.Empty;
            Source = source;
        }

        public string ConditionId { get; }
        public IReadOnlyList<RecipeValueBinding> Bindings { get; }
        public string RecipeId { get; }
        public string FieldId { get; }
        public string OperatorId { get; }
        public string ExpectedValue { get; }
        public string UnitId { get; }
        public string RejectionCode { get; }
        public string AppliesToOperationName { get; }
        public ConfigurationSource Source { get; }

        private static IReadOnlyList<RecipeValueBinding> Copy(
            IEnumerable<RecipeValueBinding> values) =>
            (values ?? Array.Empty<RecipeValueBinding>()).ToArray();
    }

    public sealed class RecipeResultDefinition
    {
        public RecipeResultDefinition(
            string resultId,
            IEnumerable<RecipeValueBinding> bindings = null,
            string recipeId = "",
            IEnumerable<string> operationIds = null,
            IEnumerable<string> eventIds = null,
            ConfigurationSource source = null)
        {
            ResultId = resultId ?? string.Empty;
            Bindings = Copy(bindings);
            RecipeId = recipeId ?? string.Empty;
            OperationIds = CopyStrings(operationIds);
            EventIds = CopyStrings(eventIds);
            Source = source;
        }

        public string ResultId { get; }
        public IReadOnlyList<RecipeValueBinding> Bindings { get; }
        public string RecipeId { get; }
        public IReadOnlyList<string> OperationIds { get; }
        public IReadOnlyList<string> EventIds { get; }
        public ConfigurationSource Source { get; }

        private static IReadOnlyList<RecipeValueBinding> Copy(
            IEnumerable<RecipeValueBinding> values) =>
            (values ?? Array.Empty<RecipeValueBinding>()).ToArray();

        private static IReadOnlyList<string> CopyStrings(
            IEnumerable<string> values) =>
            (values ?? Array.Empty<string>()).ToArray();
    }

    public sealed class RecipePresentationDefinition
    {
        public RecipePresentationDefinition(
            string presentationId,
            IEnumerable<RecipeValueBinding> bindings = null,
            IEnumerable<string> scientificOperationIds = null,
            string recipeId = "",
            string protocolId = "",
            bool createsState = false,
            string stateSuffix = "",
            IEnumerable<string> stateConditionIds = null,
            RecipeBindingKind targetEntityBinding =
                RecipeBindingKind.ActionSource,
            CoursePresentationLocationKind locationKind =
                CoursePresentationLocationKind.EntityRoot,
            RecipeBindingKind locationIdBinding =
                RecipeBindingKind.CurrentEntity,
            string locationId = "",
            IEnumerable<KeyValuePair<string, StructuredValue>>
                parameterValues = null,
            CoursePresentationLifecycle lifecycle =
                CoursePresentationLifecycle.OneShot,
            ConfigurationSource source = null)
        {
            PresentationId = presentationId ?? string.Empty;
            Bindings = Copy(bindings);
            ScientificOperationIds = (
                scientificOperationIds ?? Array.Empty<string>()).ToArray();
            RecipeId = recipeId ?? string.Empty;
            ProtocolId = protocolId ?? string.Empty;
            CreatesState = createsState;
            StateSuffix = stateSuffix ?? string.Empty;
            StateConditionIds = (
                stateConditionIds ?? Array.Empty<string>()).ToArray();
            TargetEntityBinding = targetEntityBinding;
            LocationKind = locationKind;
            LocationIdBinding = locationIdBinding;
            LocationId = locationId ?? string.Empty;
            ParameterValues = new Dictionary<string, StructuredValue>(
                parameterValues
                ?? Array.Empty<KeyValuePair<string, StructuredValue>>(),
                StringComparer.Ordinal);
            Lifecycle = lifecycle;
            Source = source;
        }

        public string PresentationId { get; }
        public IReadOnlyList<RecipeValueBinding> Bindings { get; }
        public IReadOnlyList<string> ScientificOperationIds { get; }
        public string RecipeId { get; }
        public string ProtocolId { get; }
        public bool CreatesState { get; }
        public string StateSuffix { get; }
        public IReadOnlyList<string> StateConditionIds { get; }
        public RecipeBindingKind TargetEntityBinding { get; }
        public CoursePresentationLocationKind LocationKind { get; }
        public RecipeBindingKind LocationIdBinding { get; }
        public string LocationId { get; }
        public IReadOnlyDictionary<string, StructuredValue> ParameterValues
        {
            get;
        }
        public CoursePresentationLifecycle Lifecycle { get; }
        public ConfigurationSource Source { get; }

        private static IReadOnlyList<RecipeValueBinding> Copy(
            IEnumerable<RecipeValueBinding> values) =>
            (values ?? Array.Empty<RecipeValueBinding>()).ToArray();
    }

    public sealed class RecipeOperationDefinition
    {
        public RecipeOperationDefinition(
            string operationId,
            IEnumerable<RecipeValueBinding> bindings = null,
            string recipeId = "",
            string protocolOperationId = "",
            IEnumerable<KeyValuePair<string, string>> constantParameters = null,
            ConfigurationSource source = null)
        {
            OperationId = operationId ?? string.Empty;
            Bindings = Copy(bindings);
            RecipeId = recipeId ?? string.Empty;
            ProtocolOperationId = protocolOperationId ?? string.Empty;
            ConstantParameters = CopyConstants(constantParameters);
            Source = source;
        }

        public string OperationId { get; }
        public IReadOnlyList<RecipeValueBinding> Bindings { get; }
        public string RecipeId { get; }
        public string ProtocolOperationId { get; }
        public IReadOnlyDictionary<string, string> ConstantParameters { get; }
        public ConfigurationSource Source { get; }

        private static IReadOnlyList<RecipeValueBinding> Copy(
            IEnumerable<RecipeValueBinding> values) =>
            (values ?? Array.Empty<RecipeValueBinding>()).ToArray();

        private static IReadOnlyDictionary<string, string> CopyConstants(
            IEnumerable<KeyValuePair<string, string>> values)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values
                         ?? Array.Empty<KeyValuePair<string, string>>())
            {
                if (!result.ContainsKey(pair.Key))
                {
                    result.Add(pair.Key, pair.Value ?? string.Empty);
                }
            }

            return result;
        }
    }

    public sealed class RecipePrefabContract
    {
        public RecipePrefabContract(
            string recipeId,
            string contractKind,
            string identifier,
            ConfigurationSource source = null)
        {
            RecipeId = recipeId ?? string.Empty;
            ContractKind = contractKind ?? string.Empty;
            Identifier = identifier ?? string.Empty;
            Source = source;
        }

        public string RecipeId { get; }
        public string ContractKind { get; }
        public string Identifier { get; }
        public ConfigurationSource Source { get; }
    }

    public sealed class RecipePackage
    {
        public RecipePackage(
            string packageId,
            RecipeLayer layer,
            IEnumerable<RecipeDefinition> recipes = null,
            IEnumerable<RecipeParameterContract> parameters = null,
            IEnumerable<RecipePrefabContract> prefabContracts = null,
            IEnumerable<RecipeActionDefinition> actions = null,
            IEnumerable<RecipeConditionDefinition> conditions = null,
            IEnumerable<RecipeResultDefinition> results = null,
            IEnumerable<RecipePresentationDefinition> presentations = null,
            IEnumerable<RecipeOperationDefinition> operations = null,
            IDisciplineRecordCompiler disciplineRecordCompiler = null)
        {
            PackageId = packageId ?? string.Empty;
            Layer = layer;
            Recipes = Copy(recipes);
            Parameters = Copy(parameters);
            PrefabContracts = Copy(prefabContracts);
            Actions = Copy(actions);
            Conditions = Copy(conditions);
            Results = Copy(results);
            Presentations = Copy(presentations);
            Operations = Copy(operations);
            DisciplineRecordCompiler = disciplineRecordCompiler;
        }

        public string PackageId { get; }
        public RecipeLayer Layer { get; }
        public IReadOnlyList<RecipeDefinition> Recipes { get; }
        public IReadOnlyList<RecipeParameterContract> Parameters { get; }
        public IReadOnlyList<RecipePrefabContract> PrefabContracts { get; }
        public IReadOnlyList<RecipeActionDefinition> Actions { get; }
        public IReadOnlyList<RecipeConditionDefinition> Conditions { get; }
        public IReadOnlyList<RecipeResultDefinition> Results { get; }
        public IReadOnlyList<RecipePresentationDefinition> Presentations { get; }
        public IReadOnlyList<RecipeOperationDefinition> Operations { get; }
        public IDisciplineRecordCompiler DisciplineRecordCompiler { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) =>
            (values ?? Array.Empty<T>()).ToArray();
    }
}
