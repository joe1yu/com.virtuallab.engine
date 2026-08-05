using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Drafts;

namespace VirtualLab.Unity.Authoring.Workbench
{
    /// <summary>
    /// 十张职责表的事务式编辑会话。每次修改都可撤销，保存前统一检查外部改动。
    /// </summary>
    public sealed class CourseAuthoringSession
    {
        private readonly CourseDocumentSet _documents;
        private readonly Stack<CourseDocumentSnapshot> _undo =
            new Stack<CourseDocumentSnapshot>();
        private readonly Stack<CourseDocumentSnapshot> _redo =
            new Stack<CourseDocumentSnapshot>();

        private CourseAuthoringSession(
            CourseDocumentSet documents,
            CourseAuthoringCatalog catalog,
            CourseAuthoringDraft draft)
        {
            _documents = documents;
            Catalog = catalog;
            Draft = draft;
        }

        public string AuthoringDirectory => _documents.AuthoringDirectory;
        public CourseAuthoringCatalog Catalog { get; }
        public CourseAuthoringDraft Draft { get; private set; }
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool IsModified => _documents.IsModified;

        public static CourseAuthoringSession Load(
            string authoringDirectory,
            CourseAuthoringCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!catalog.IsValid)
            {
                throw new InvalidDataException(
                    "课程创作目录无效：" + string.Join(
                        "；",
                        catalog.Diagnostics.Select(value => value.Reason)));
            }

            var documents = CourseDocumentSet.Load(authoringDirectory);
            documents.EnsureExactDocuments(
                CourseAuthoringSchema.Tables.Select(value => value.FileName));
            return new CourseAuthoringSession(
                documents,
                catalog,
                ReadDraft(documents));
        }

        public string PathOf(string fileName)
        {
            if (!CourseAuthoringSchema.ByFileName.ContainsKey(fileName))
            {
                throw new ArgumentException(
                    $"“{fileName}”不是课程职责表。",
                    nameof(fileName));
            }

            return Path.Combine(AuthoringDirectory, fileName);
        }

        public CourseDraftObject AddSupply(
            string templateId,
            string entityId,
            string displayName)
        {
            if (!Catalog.TryGetTemplate(templateId, out var template))
            {
                throw new ArgumentException(
                    $"用品模板“{templateId}”未由模块注册。",
                    nameof(templateId));
            }

            entityId = Required(entityId, "实体标识");
            if (Draft.Objects.Any(value => value.EntityId == entityId))
            {
                throw new InvalidOperationException(
                    $"实验对象“{entityId}”已经存在。");
            }

            Mutate(() => Document(CourseAuthoringTableNames.Objects)
                .AddConfiguredRow(new[]
                {
                    Pair(CourseAuthoringColumns.Object.EntityId, entityId),
                    Pair(CourseAuthoringColumns.Object.DisplayName,
                        Text(displayName, entityId)),
                    Pair(CourseAuthoringColumns.Object.EntityType,
                        template.TemplateId),
                    Pair(CourseAuthoringColumns.Object.Roles, string.Empty),
                    Pair(CourseAuthoringColumns.Object.Tags, string.Empty),
                    Pair(CourseAuthoringColumns.Object.InitialPosition, "0|0|0"),
                    Pair(CourseAuthoringColumns.Object.InitialRotation, "0|0|0")
                }));
            return Draft.Objects.Single(value => value.EntityId == entityId);
        }

        public void RemoveSupply(string entityId)
        {
            entityId = Required(entityId, "实体标识");
            if (entityId == Draft.Course.ActorEntityId)
            {
                throw new InvalidOperationException("操作者实体不能作为实验用品删除。");
            }

            Mutate(() => RemoveSingle(
                CourseAuthoringTableNames.Objects,
                (document, row) => document.ConfiguredValue(
                    row,
                    CourseAuthoringColumns.Object.EntityId) == entityId,
                $"找不到实验对象“{entityId}”。"));
        }

        public void SetComponentOverride(CourseDraftComponent value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Upsert(
                CourseAuthoringTableNames.Components,
                (document, row) => document.ConfiguredValue(
                    row,
                    CourseAuthoringColumns.Component.Id) == value.ComponentId,
                new[]
                {
                    Pair(CourseAuthoringColumns.Component.Id, value.ComponentId),
                    Pair(CourseAuthoringColumns.Component.SubjectSelectorKind,
                        value.SubjectSelectorKind),
                    Pair(CourseAuthoringColumns.Component.SubjectSelectorValue,
                        value.SubjectSelectorValue),
                    Pair(CourseAuthoringColumns.Component.ComponentType,
                        value.ComponentType),
                    Pair(CourseAuthoringColumns.Component.Parameters,
                        value.Parameters)
                });
        }

        public void SetInitialRelation(CourseDraftInitialRelation value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Upsert(
                CourseAuthoringTableNames.InitialRelations,
                (document, row) => document.ConfiguredValue(
                    row,
                    CourseAuthoringColumns.InitialRelation.Id) == value.RelationId,
                new[]
                {
                    Pair(CourseAuthoringColumns.InitialRelation.Id, value.RelationId),
                    Pair(CourseAuthoringColumns.InitialRelation.RelationType,
                        value.RelationType),
                    Pair(CourseAuthoringColumns.InitialRelation.SourceEntity,
                        value.SourceEntityId),
                    Pair(CourseAuthoringColumns.InitialRelation.TargetEntity,
                        value.TargetEntityId),
                    Pair(CourseAuthoringColumns.InitialRelation.SourcePortId,
                        value.SourcePortId),
                    Pair(CourseAuthoringColumns.InitialRelation.TargetPortId,
                        value.TargetPortId)
                });
        }

        public void SetOperationOverride(CourseDraftOperationOverride value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Upsert(
                CourseAuthoringTableNames.OperationOverrides,
                (document, row) => document.ConfiguredValue(
                    row,
                    CourseAuthoringColumns.OperationOverride.Id) == value.OverrideId,
                new[]
                {
                    Pair(CourseAuthoringColumns.OperationOverride.Id, value.OverrideId),
                    Pair(CourseAuthoringColumns.OperationOverride.Handling,
                        value.Handling),
                    Pair(CourseAuthoringColumns.OperationOverride.Operation,
                        value.OperationId),
                    Pair(CourseAuthoringColumns.OperationOverride.SourceSelectorKind,
                        value.SourceSelectorKind),
                    Pair(CourseAuthoringColumns.OperationOverride.SourceSelectorValue,
                        value.SourceSelectorValue),
                    Pair(CourseAuthoringColumns.OperationOverride.TargetSelectorKind,
                        value.TargetSelectorKind),
                    Pair(CourseAuthoringColumns.OperationOverride.TargetSelectorValue,
                        value.TargetSelectorValue),
                    Pair(CourseAuthoringColumns.OperationOverride.Order,
                        Number(value.Order)),
                    Pair(CourseAuthoringColumns.OperationOverride.SubjectSelectorKind,
                        value.SubjectSelectorKind),
                    Pair(CourseAuthoringColumns.OperationOverride.SubjectSelectorValue,
                        value.SubjectSelectorValue),
                    Pair(CourseAuthoringColumns.OperationOverride.Fact, value.Fact),
                    Pair(CourseAuthoringColumns.OperationOverride.Comparison,
                        value.Comparison),
                    Pair(CourseAuthoringColumns.OperationOverride.Value, value.Value),
                    Pair(CourseAuthoringColumns.OperationOverride.Unit, value.Unit),
                    Pair(CourseAuthoringColumns.OperationOverride.RejectionMessage,
                        value.RejectionMessage),
                    Pair(CourseAuthoringColumns.OperationOverride.ConsequenceTemplate,
                        value.ConsequenceTemplateId)
                });
        }

        public void SetProcess(CourseDraftProcess value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Upsert(
                CourseAuthoringTableNames.Processes,
                (document, row) => document.ConfiguredValue(
                    row,
                    CourseAuthoringColumns.Process.Id) == value.ProcessId,
                new[]
                {
                    Pair(CourseAuthoringColumns.Process.Id, value.ProcessId),
                    Pair(CourseAuthoringColumns.Process.ProcessType, value.ProcessType),
                    Pair(CourseAuthoringColumns.Process.SubjectSelectorKind,
                        value.SubjectSelectorKind),
                    Pair(CourseAuthoringColumns.Process.SubjectSelectorValue,
                        value.SubjectSelectorValue),
                    Pair(CourseAuthoringColumns.Process.SourceSelectorKind,
                        value.SourceSelectorKind),
                    Pair(CourseAuthoringColumns.Process.SourceSelectorValue,
                        value.SourceSelectorValue),
                    Pair(CourseAuthoringColumns.Process.TargetSelectorKind,
                        value.TargetSelectorKind),
                    Pair(CourseAuthoringColumns.Process.TargetSelectorValue,
                        value.TargetSelectorValue),
                    Pair(CourseAuthoringColumns.Process.Parameters, value.Parameters)
                });
        }

        public void SetTeachingItem(CourseDraftTeachingItem value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Upsert(
                CourseAuthoringTableNames.Teaching,
                (document, row) => document.ConfiguredValue(
                    row,
                    CourseAuthoringColumns.Teaching.Id) == value.TeachingItemId,
                new[]
                {
                    Pair(CourseAuthoringColumns.Teaching.Id, value.TeachingItemId),
                    Pair(CourseAuthoringColumns.Teaching.Type, value.Type),
                    Pair(CourseAuthoringColumns.Teaching.DisplayName,
                        value.DisplayName),
                    Pair(CourseAuthoringColumns.Teaching.TriggerType,
                        value.TriggerType),
                    Pair(CourseAuthoringColumns.Teaching.TriggerValue,
                        value.TriggerValue),
                    Pair(CourseAuthoringColumns.Teaching.ScoreDelta,
                        Number(value.ScoreDelta)),
                    Pair(CourseAuthoringColumns.Teaching.Prompt, value.Prompt),
                    Pair(CourseAuthoringColumns.Teaching.ErrorSeverity,
                        value.ErrorSeverity),
                    Pair(CourseAuthoringColumns.Teaching.Continuation,
                        value.Continuation),
                    Pair(CourseAuthoringColumns.Teaching.AffectedGoals,
                        value.AffectedGoals)
                });
        }

        public void SetTeachingCondition(CourseDraftTeachingCondition value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Upsert(
                CourseAuthoringTableNames.TeachingConditions,
                (document, row) => document.ConfiguredValue(
                           row,
                           CourseAuthoringColumns.TeachingCondition.TeachingItemId)
                       == value.TeachingItemId
                       && document.ConfiguredValue(
                           row,
                           CourseAuthoringColumns.TeachingCondition.Order)
                       == Number(value.Order),
                new[]
                {
                    Pair(CourseAuthoringColumns.TeachingCondition.TeachingItemId,
                        value.TeachingItemId),
                    Pair(CourseAuthoringColumns.TeachingCondition.Order,
                        Number(value.Order)),
                    Pair(CourseAuthoringColumns.TeachingCondition.SubjectSelectorKind,
                        value.SubjectSelectorKind),
                    Pair(CourseAuthoringColumns.TeachingCondition.SubjectSelectorValue,
                        value.SubjectSelectorValue),
                    Pair(CourseAuthoringColumns.TeachingCondition.Fact, value.Fact),
                    Pair(CourseAuthoringColumns.TeachingCondition.Comparison,
                        value.Comparison),
                    Pair(CourseAuthoringColumns.TeachingCondition.Value, value.Value),
                    Pair(CourseAuthoringColumns.TeachingCondition.Unit, value.Unit)
                });
        }

        public void SetPresentation(CourseDraftPresentation value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Upsert(
                CourseAuthoringTableNames.Presentation,
                (document, row) => document.ConfiguredValue(
                    row,
                    CourseAuthoringColumns.Presentation.Id) == value.PresentationId,
                new[]
                {
                    Pair(CourseAuthoringColumns.Presentation.Id,
                        value.PresentationId),
                    Pair(CourseAuthoringColumns.Presentation.TriggerType,
                        value.TriggerType),
                    Pair(CourseAuthoringColumns.Presentation.TriggerValue,
                        value.TriggerValue),
                    Pair(CourseAuthoringColumns.Presentation.SubjectSelectorKind,
                        value.SubjectSelectorKind),
                    Pair(CourseAuthoringColumns.Presentation.SubjectSelectorValue,
                        value.SubjectSelectorValue),
                    Pair(CourseAuthoringColumns.Presentation.Signal, value.Signal),
                    Pair(CourseAuthoringColumns.Presentation.Location,
                        value.Location),
                    Pair(CourseAuthoringColumns.Presentation.LocationId,
                        value.LocationId),
                    Pair(CourseAuthoringColumns.Presentation.Parameters,
                        value.Parameters)
                });
        }

        public void SetAcceptanceRecord(CourseDraftAcceptanceRecord value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Upsert(
                CourseAuthoringTableNames.AcceptanceScenarios,
                (document, row) => document.ConfiguredValue(
                           row,
                           CourseAuthoringColumns.Acceptance.ScenarioId)
                       == value.ScenarioId
                       && document.ConfiguredValue(
                           row,
                           CourseAuthoringColumns.Acceptance.Order)
                       == Number(value.Order),
                new[]
                {
                    Pair(CourseAuthoringColumns.Acceptance.ScenarioId,
                        value.ScenarioId),
                    Pair(CourseAuthoringColumns.Acceptance.Order,
                        Number(value.Order)),
                    Pair(CourseAuthoringColumns.Acceptance.RecordType,
                        value.RecordType),
                    Pair(CourseAuthoringColumns.Acceptance.Operation,
                        value.OperationId),
                    Pair(CourseAuthoringColumns.Acceptance.Source,
                        value.SourceEntityId),
                    Pair(CourseAuthoringColumns.Acceptance.Target,
                        value.TargetEntityId),
                    Pair(CourseAuthoringColumns.Acceptance.ParameterName,
                        value.ParameterName),
                    Pair(CourseAuthoringColumns.Acceptance.ParameterValue,
                        value.ParameterValue),
                    Pair(CourseAuthoringColumns.Acceptance.AssertionType,
                        value.AssertionType),
                    Pair(CourseAuthoringColumns.Acceptance.Object, value.ObjectId),
                    Pair(CourseAuthoringColumns.Acceptance.Fact, value.Fact),
                    Pair(CourseAuthoringColumns.Acceptance.Comparison,
                        value.Comparison),
                    Pair(CourseAuthoringColumns.Acceptance.ExpectedValue,
                        value.ExpectedValue),
                    Pair(CourseAuthoringColumns.Acceptance.Unit, value.Unit)
                });
        }

        public void Save()
        {
            _documents.SaveModified();
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            var current = _documents.CaptureSnapshot();
            _documents.Restore(_undo.Pop());
            _redo.Push(current);
            Draft = ReadDraft(_documents);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var current = _documents.CaptureSnapshot();
            _documents.Restore(_redo.Pop());
            _undo.Push(current);
            Draft = ReadDraft(_documents);
            return true;
        }

        private void Upsert(
            string fileName,
            Func<EditableCsvDocument, EditableCsvRow, bool> matches,
            IEnumerable<KeyValuePair<string, string>> values)
        {
            Mutate(() =>
            {
                var document = Document(fileName);
                var matching = document.Rows.Where(row =>
                    matches(document, row)).ToArray();
                if (matching.Length > 1)
                {
                    throw new InvalidDataException(
                        $"课程表“{fileName}”包含重复记录，无法确定要修改哪一行。");
                }

                var row = matching.SingleOrDefault()
                          ?? document.AddConfiguredRow();
                foreach (var pair in values)
                {
                    document.SetConfiguredValue(row, pair.Key, pair.Value);
                }
            });
        }

        private void RemoveSingle(
            string fileName,
            Func<EditableCsvDocument, EditableCsvRow, bool> matches,
            string missingMessage)
        {
            var document = Document(fileName);
            var matching = document.Rows.Where(row =>
                matches(document, row)).ToArray();
            if (matching.Length != 1)
            {
                throw new InvalidOperationException(
                    matching.Length == 0
                        ? missingMessage
                        : $"课程表“{fileName}”包含重复记录，不能安全删除。");
            }

            document.RemoveRow(matching[0]);
        }

        private void Mutate(Action mutation)
        {
            var before = _documents.CaptureSnapshot();
            try
            {
                mutation();
                var draft = ReadDraft(_documents);
                _undo.Push(before);
                _redo.Clear();
                Draft = draft;
            }
            catch
            {
                _documents.Restore(before);
                throw;
            }
        }

        private EditableCsvDocument Document(string fileName) =>
            _documents.GetRequiredDocument(fileName);

        private static CourseAuthoringDraft ReadDraft(
            CourseDocumentSet documents)
        {
            var result = new CourseAuthoringDraftReader().Read(
                documents.CreateBlueprintSource());
            if (result.IsSuccess)
            {
                return result.Draft;
            }

            throw new InvalidDataException(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(value =>
                    $"{value.FileName}:{value.Line} {value.Reason}")));
        }

        private static KeyValuePair<string, string> Pair(
            string key,
            string value) =>
            new KeyValuePair<string, string>(key, value ?? string.Empty);

        private static string Number(int value) =>
            value.ToString(CultureInfo.InvariantCulture);

        private static string Required(string value, string displayName)
        {
            var result = Text(value, string.Empty);
            if (result.Length == 0)
            {
                throw new ArgumentException($"{displayName}不能为空。", nameof(value));
            }

            return result;
        }

        private static string Text(string value, string fallback) =>
            (string.IsNullOrWhiteSpace(value) ? fallback : value.Trim())
            .Normalize(NormalizationForm.FormC);
    }
}
