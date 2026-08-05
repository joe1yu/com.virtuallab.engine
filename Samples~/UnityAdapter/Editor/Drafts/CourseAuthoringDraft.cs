using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Drafts
{
    public abstract class CourseAuthoringDraftRecord
    {
        protected CourseAuthoringDraftRecord(ConfigurationSource source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public ConfigurationSource Source { get; }

        protected static string Text(string value) => value?.Trim() ?? string.Empty;
    }

    public sealed class CourseDraftCourse : CourseAuthoringDraftRecord
    {
        public CourseDraftCourse(
            string courseId,
            string displayName,
            string disciplinePackageId,
            string actorEntityId,
            string experimentPrefabPath,
            ConfigurationSource source)
            : base(source)
        {
            CourseId = Text(courseId);
            DisplayName = Text(displayName);
            DisciplinePackageId = Text(disciplinePackageId);
            ActorEntityId = Text(actorEntityId);
            ExperimentPrefabPath = Text(experimentPrefabPath);
        }

        public string CourseId { get; }
        public string DisplayName { get; }
        public string DisciplinePackageId { get; }
        public string ActorEntityId { get; }
        public string ExperimentPrefabPath { get; }
    }

    public sealed class CourseDraftObject : CourseAuthoringDraftRecord
    {
        public CourseDraftObject(
            string entityId,
            string displayName,
            string entityType,
            string roles,
            string tags,
            string initialPosition,
            string initialRotation,
            ConfigurationSource source)
            : base(source)
        {
            EntityId = Text(entityId);
            DisplayName = Text(displayName);
            EntityType = Text(entityType);
            Roles = Text(roles);
            Tags = Text(tags);
            InitialPosition = Text(initialPosition);
            InitialRotation = Text(initialRotation);
        }

        public string EntityId { get; }
        public string DisplayName { get; }
        public string EntityType { get; }
        public string Roles { get; }
        public string Tags { get; }
        public string InitialPosition { get; }
        public string InitialRotation { get; }
    }

    public sealed class CourseDraftComponent : CourseAuthoringDraftRecord
    {
        public CourseDraftComponent(
            string componentId,
            string subjectSelectorKind,
            string subjectSelectorValue,
            string componentType,
            string parameters,
            ConfigurationSource source)
            : base(source)
        {
            ComponentId = Text(componentId);
            SubjectSelectorKind = Text(subjectSelectorKind);
            SubjectSelectorValue = Text(subjectSelectorValue);
            ComponentType = Text(componentType);
            Parameters = Text(parameters);
        }

        public string ComponentId { get; }
        public string SubjectSelectorKind { get; }
        public string SubjectSelectorValue { get; }
        public string ComponentType { get; }
        public string Parameters { get; }
    }

    public sealed class CourseDraftInitialRelation : CourseAuthoringDraftRecord
    {
        public CourseDraftInitialRelation(
            string relationId,
            string relationType,
            string sourceEntityId,
            string targetEntityId,
            string sourcePortId,
            string targetPortId,
            ConfigurationSource source)
            : base(source)
        {
            RelationId = Text(relationId);
            RelationType = Text(relationType);
            SourceEntityId = Text(sourceEntityId);
            TargetEntityId = Text(targetEntityId);
            SourcePortId = Text(sourcePortId);
            TargetPortId = Text(targetPortId);
        }

        public string RelationId { get; }
        public string RelationType { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public string SourcePortId { get; }
        public string TargetPortId { get; }
    }

    public sealed class CourseDraftOperationOverride : CourseAuthoringDraftRecord
    {
        public CourseDraftOperationOverride(
            string overrideId,
            string handling,
            string operationId,
            string sourceSelectorKind,
            string sourceSelectorValue,
            string targetSelectorKind,
            string targetSelectorValue,
            int order,
            string subjectSelectorKind,
            string subjectSelectorValue,
            string fact,
            string comparison,
            string value,
            string unit,
            string rejectionMessage,
            string consequenceTemplateId,
            ConfigurationSource source)
            : base(source)
        {
            OverrideId = Text(overrideId);
            Handling = Text(handling);
            OperationId = Text(operationId);
            SourceSelectorKind = Text(sourceSelectorKind);
            SourceSelectorValue = Text(sourceSelectorValue);
            TargetSelectorKind = Text(targetSelectorKind);
            TargetSelectorValue = Text(targetSelectorValue);
            Order = order;
            SubjectSelectorKind = Text(subjectSelectorKind);
            SubjectSelectorValue = Text(subjectSelectorValue);
            Fact = Text(fact);
            Comparison = Text(comparison);
            Value = Text(value);
            Unit = Text(unit);
            RejectionMessage = Text(rejectionMessage);
            ConsequenceTemplateId = Text(consequenceTemplateId);
        }

        public string OverrideId { get; }
        public string Handling { get; }
        public string OperationId { get; }
        public string SourceSelectorKind { get; }
        public string SourceSelectorValue { get; }
        public string TargetSelectorKind { get; }
        public string TargetSelectorValue { get; }
        public int Order { get; }
        public string SubjectSelectorKind { get; }
        public string SubjectSelectorValue { get; }
        public string Fact { get; }
        public string Comparison { get; }
        public string Value { get; }
        public string Unit { get; }
        public string RejectionMessage { get; }
        public string ConsequenceTemplateId { get; }
    }

    public sealed class CourseDraftProcess : CourseAuthoringDraftRecord
    {
        public CourseDraftProcess(
            string processId,
            string processType,
            string subjectSelectorKind,
            string subjectSelectorValue,
            string sourceSelectorKind,
            string sourceSelectorValue,
            string targetSelectorKind,
            string targetSelectorValue,
            string parameters,
            ConfigurationSource source)
            : base(source)
        {
            ProcessId = Text(processId);
            ProcessType = Text(processType);
            SubjectSelectorKind = Text(subjectSelectorKind);
            SubjectSelectorValue = Text(subjectSelectorValue);
            SourceSelectorKind = Text(sourceSelectorKind);
            SourceSelectorValue = Text(sourceSelectorValue);
            TargetSelectorKind = Text(targetSelectorKind);
            TargetSelectorValue = Text(targetSelectorValue);
            Parameters = Text(parameters);
        }

        public string ProcessId { get; }
        public string ProcessType { get; }
        public string SubjectSelectorKind { get; }
        public string SubjectSelectorValue { get; }
        public string SourceSelectorKind { get; }
        public string SourceSelectorValue { get; }
        public string TargetSelectorKind { get; }
        public string TargetSelectorValue { get; }
        public string Parameters { get; }
    }

    public sealed class CourseDraftTeachingItem : CourseAuthoringDraftRecord
    {
        public CourseDraftTeachingItem(
            string teachingItemId,
            string type,
            string displayName,
            string triggerType,
            string triggerValue,
            int scoreDelta,
            string prompt,
            string errorSeverity,
            string continuation,
            string affectedGoals,
            ConfigurationSource source)
            : base(source)
        {
            TeachingItemId = Text(teachingItemId);
            Type = Text(type);
            DisplayName = Text(displayName);
            TriggerType = Text(triggerType);
            TriggerValue = Text(triggerValue);
            ScoreDelta = scoreDelta;
            Prompt = Text(prompt);
            ErrorSeverity = Text(errorSeverity);
            Continuation = Text(continuation);
            AffectedGoals = Text(affectedGoals);
        }

        public string TeachingItemId { get; }
        public string Type { get; }
        public string DisplayName { get; }
        public string TriggerType { get; }
        public string TriggerValue { get; }
        public int ScoreDelta { get; }
        public string Prompt { get; }
        public string ErrorSeverity { get; }
        public string Continuation { get; }
        public string AffectedGoals { get; }
    }

    public sealed class CourseDraftTeachingCondition : CourseAuthoringDraftRecord
    {
        public CourseDraftTeachingCondition(
            string teachingItemId,
            int order,
            string subjectSelectorKind,
            string subjectSelectorValue,
            string fact,
            string comparison,
            string value,
            string unit,
            ConfigurationSource source)
            : base(source)
        {
            TeachingItemId = Text(teachingItemId);
            Order = order;
            SubjectSelectorKind = Text(subjectSelectorKind);
            SubjectSelectorValue = Text(subjectSelectorValue);
            Fact = Text(fact);
            Comparison = Text(comparison);
            Value = Text(value);
            Unit = Text(unit);
        }

        public string TeachingItemId { get; }
        public int Order { get; }
        public string SubjectSelectorKind { get; }
        public string SubjectSelectorValue { get; }
        public string Fact { get; }
        public string Comparison { get; }
        public string Value { get; }
        public string Unit { get; }
    }

    public sealed class CourseDraftPresentation : CourseAuthoringDraftRecord
    {
        public CourseDraftPresentation(
            string presentationId,
            string triggerType,
            string triggerValue,
            string subjectSelectorKind,
            string subjectSelectorValue,
            string signal,
            string location,
            string locationId,
            string parameters,
            ConfigurationSource source)
            : base(source)
        {
            PresentationId = Text(presentationId);
            TriggerType = Text(triggerType);
            TriggerValue = Text(triggerValue);
            SubjectSelectorKind = Text(subjectSelectorKind);
            SubjectSelectorValue = Text(subjectSelectorValue);
            Signal = Text(signal);
            Location = Text(location);
            LocationId = Text(locationId);
            Parameters = Text(parameters);
        }

        public string PresentationId { get; }
        public string TriggerType { get; }
        public string TriggerValue { get; }
        public string SubjectSelectorKind { get; }
        public string SubjectSelectorValue { get; }
        public string Signal { get; }
        public string Location { get; }
        public string LocationId { get; }
        public string Parameters { get; }
    }

    public sealed class CourseDraftAcceptanceRecord : CourseAuthoringDraftRecord
    {
        public CourseDraftAcceptanceRecord(
            string scenarioId,
            int order,
            string recordType,
            string operationId,
            string sourceEntityId,
            string targetEntityId,
            string parameterName,
            string parameterValue,
            string assertionType,
            string objectId,
            string fact,
            string comparison,
            string expectedValue,
            string unit,
            ConfigurationSource source)
            : base(source)
        {
            ScenarioId = Text(scenarioId);
            Order = order;
            RecordType = Text(recordType);
            OperationId = Text(operationId);
            SourceEntityId = Text(sourceEntityId);
            TargetEntityId = Text(targetEntityId);
            ParameterName = Text(parameterName);
            ParameterValue = Text(parameterValue);
            AssertionType = Text(assertionType);
            ObjectId = Text(objectId);
            Fact = Text(fact);
            Comparison = Text(comparison);
            ExpectedValue = Text(expectedValue);
            Unit = Text(unit);
        }

        public string ScenarioId { get; }
        public int Order { get; }
        public string RecordType { get; }
        public string OperationId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public string ParameterName { get; }
        public string ParameterValue { get; }
        public string AssertionType { get; }
        public string ObjectId { get; }
        public string Fact { get; }
        public string Comparison { get; }
        public string ExpectedValue { get; }
        public string Unit { get; }
    }

    /// <summary>
    /// 十张表的只读快照；集合顺序已经标准化，后续展开不依赖文件枚举顺序。
    /// </summary>
    public sealed class CourseAuthoringDraft
    {
        public CourseAuthoringDraft(
            CourseDraftCourse course,
            IEnumerable<CourseDraftObject> objects,
            IEnumerable<CourseDraftComponent> components,
            IEnumerable<CourseDraftInitialRelation> initialRelations,
            IEnumerable<CourseDraftOperationOverride> operationOverrides,
            IEnumerable<CourseDraftProcess> processes,
            IEnumerable<CourseDraftTeachingItem> teachingItems,
            IEnumerable<CourseDraftTeachingCondition> teachingConditions,
            IEnumerable<CourseDraftPresentation> presentations,
            IEnumerable<CourseDraftAcceptanceRecord> acceptanceRecords)
        {
            Course = course ?? throw new ArgumentNullException(nameof(course));
            Objects = Copy(objects);
            Components = Copy(components);
            InitialRelations = Copy(initialRelations);
            OperationOverrides = Copy(operationOverrides);
            Processes = Copy(processes);
            TeachingItems = Copy(teachingItems);
            TeachingConditions = Copy(teachingConditions);
            Presentations = Copy(presentations);
            AcceptanceRecords = Copy(acceptanceRecords);
        }

        public CourseDraftCourse Course { get; }
        public IReadOnlyList<CourseDraftObject> Objects { get; }
        public IReadOnlyList<CourseDraftComponent> Components { get; }
        public IReadOnlyList<CourseDraftInitialRelation> InitialRelations { get; }
        public IReadOnlyList<CourseDraftOperationOverride> OperationOverrides { get; }
        public IReadOnlyList<CourseDraftProcess> Processes { get; }
        public IReadOnlyList<CourseDraftTeachingItem> TeachingItems { get; }
        public IReadOnlyList<CourseDraftTeachingCondition> TeachingConditions { get; }
        public IReadOnlyList<CourseDraftPresentation> Presentations { get; }
        public IReadOnlyList<CourseDraftAcceptanceRecord> AcceptanceRecords { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) =>
            (values ?? throw new ArgumentNullException(nameof(values))).ToArray();
    }
}
