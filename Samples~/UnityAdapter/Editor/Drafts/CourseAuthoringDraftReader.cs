using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Drafts
{
    public sealed class CourseAuthoringDraftReadResult
    {
        public CourseAuthoringDraftReadResult(
            CourseAuthoringDraft draft,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Draft = draft;
            Diagnostics = (diagnostics
                ?? throw new ArgumentNullException(nameof(diagnostics)))
                .ToArray();
        }

        public CourseAuthoringDraft Draft { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
        public bool IsSuccess => Draft != null && Diagnostics.Count == 0;
    }

    /// <summary>
    /// 严格读取新课程十表。它不解释模块语义，只形成可定位、可确定排序的草稿。
    /// </summary>
    public sealed class CourseAuthoringDraftReader
    {
        public CourseAuthoringDraftReadResult Read(CourseBlueprintSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var diagnostics = new List<CourseCompilationDiagnostic>();
            var files = new Dictionary<string, CourseBlueprintFile>(
                StringComparer.Ordinal);
            foreach (var file in source.OrderBy(
                         value => value.FileName,
                         StringComparer.Ordinal))
            {
                if (!CourseAuthoringSchema.ByFileName.ContainsKey(file.FileName))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.file.unsupported",
                        file.FileName,
                        1,
                        string.Empty,
                        $"文件“{file.FileName}”不属于新课程草稿协议。",
                        "删除旧表，或把内容迁移到十张职责表中的对应位置。"));
                    continue;
                }

                if (files.ContainsKey(file.FileName))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.file.duplicate",
                        file.FileName,
                        1,
                        string.Empty,
                        $"课程草稿包含多个“{file.FileName}”。",
                        "每种职责表只保留一个文件。"));
                    continue;
                }

                files.Add(file.FileName, file);
            }

            foreach (var schema in CourseAuthoringSchema.Tables)
            {
                if (!files.ContainsKey(schema.FileName))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.file.missing",
                        schema.FileName,
                        1,
                        schema.IdentityColumn,
                        $"课程草稿缺少“{schema.FileName}”。",
                        $"创建“{schema.FileName}”并使用规定中文表头。"));
                }
            }

            var tables = new Dictionary<string, StrictCsvReadResult>(
                StringComparer.Ordinal);
            var csvReader = new StrictCsvReader();
            foreach (var pair in files.OrderBy(
                         value => value.Key,
                         StringComparer.Ordinal))
            {
                var table = csvReader.ReadBytes(pair.Key, pair.Value.Content);
                tables.Add(pair.Key, table);
                diagnostics.AddRange(table.Diagnostics);
                ValidateHeader(
                    CourseAuthoringSchema.ByFileName[pair.Key],
                    table,
                    diagnostics);
            }

            ValidateIdentities(tables, diagnostics);
            var course = ReadCourse(tables, diagnostics);
            if (diagnostics.Count > 0 || course == null)
            {
                return new CourseAuthoringDraftReadResult(null, diagnostics);
            }

            var courseId = course.CourseId;
            var draft = new CourseAuthoringDraft(
                course,
                Rows(tables, CourseAuthoringTableNames.Objects)
                    .Select(row => new CourseDraftObject(
                        Value(row, CourseAuthoringColumns.Object.EntityId),
                        Value(row, CourseAuthoringColumns.Object.DisplayName),
                        Value(row, CourseAuthoringColumns.Object.EntityType),
                        Value(row, CourseAuthoringColumns.Object.Roles),
                        Value(row, CourseAuthoringColumns.Object.Tags),
                        Value(row, CourseAuthoringColumns.Object.InitialPosition),
                        Value(row, CourseAuthoringColumns.Object.InitialRotation),
                        Source(courseId, CourseAuthoringTableNames.Objects, row)))
                    .OrderBy(value => value.EntityId, StringComparer.Ordinal)
                    .ThenBy(value => value.Source.Line),
                Rows(tables, CourseAuthoringTableNames.Components)
                    .Select(row => new CourseDraftComponent(
                        Value(row, CourseAuthoringColumns.Component.Id),
                        Value(row, CourseAuthoringColumns.Component.SubjectSelectorKind),
                        Value(row, CourseAuthoringColumns.Component.SubjectSelectorValue),
                        Value(row, CourseAuthoringColumns.Component.ComponentType),
                        Value(row, CourseAuthoringColumns.Component.Parameters),
                        Source(courseId, CourseAuthoringTableNames.Components, row)))
                    .OrderBy(value => value.ComponentId, StringComparer.Ordinal)
                    .ThenBy(value => value.Source.Line),
                Rows(tables, CourseAuthoringTableNames.InitialRelations)
                    .Select(row => new CourseDraftInitialRelation(
                        Value(row, CourseAuthoringColumns.InitialRelation.Id),
                        Value(row, CourseAuthoringColumns.InitialRelation.RelationType),
                        Value(row, CourseAuthoringColumns.InitialRelation.SourceEntity),
                        Value(row, CourseAuthoringColumns.InitialRelation.TargetEntity),
                        Value(row, CourseAuthoringColumns.InitialRelation.SourcePortId),
                        Value(row, CourseAuthoringColumns.InitialRelation.TargetPortId),
                        Source(courseId, CourseAuthoringTableNames.InitialRelations, row)))
                    .OrderBy(value => value.RelationId, StringComparer.Ordinal)
                    .ThenBy(value => value.Source.Line),
                ReadOperationOverrides(tables, courseId, diagnostics),
                Rows(tables, CourseAuthoringTableNames.Processes)
                    .Select(row => new CourseDraftProcess(
                        Value(row, CourseAuthoringColumns.Process.Id),
                        Value(row, CourseAuthoringColumns.Process.ProcessType),
                        Value(row, CourseAuthoringColumns.Process.SubjectSelectorKind),
                        Value(row, CourseAuthoringColumns.Process.SubjectSelectorValue),
                        Value(row, CourseAuthoringColumns.Process.SourceSelectorKind),
                        Value(row, CourseAuthoringColumns.Process.SourceSelectorValue),
                        Value(row, CourseAuthoringColumns.Process.TargetSelectorKind),
                        Value(row, CourseAuthoringColumns.Process.TargetSelectorValue),
                        Value(row, CourseAuthoringColumns.Process.Parameters),
                        Source(courseId, CourseAuthoringTableNames.Processes, row)))
                    .OrderBy(value => value.ProcessId, StringComparer.Ordinal)
                    .ThenBy(value => value.Source.Line),
                ReadTeachingItems(tables, courseId, diagnostics),
                ReadTeachingConditions(tables, courseId, diagnostics),
                Rows(tables, CourseAuthoringTableNames.Presentation)
                    .Select(row => new CourseDraftPresentation(
                        Value(row, CourseAuthoringColumns.Presentation.Id),
                        Value(row, CourseAuthoringColumns.Presentation.TriggerType),
                        Value(row, CourseAuthoringColumns.Presentation.TriggerValue),
                        Value(row, CourseAuthoringColumns.Presentation.SubjectSelectorKind),
                        Value(row, CourseAuthoringColumns.Presentation.SubjectSelectorValue),
                        Value(row, CourseAuthoringColumns.Presentation.Signal),
                        Value(row, CourseAuthoringColumns.Presentation.Location),
                        Value(row, CourseAuthoringColumns.Presentation.LocationId),
                        Value(row, CourseAuthoringColumns.Presentation.Parameters),
                        Source(courseId, CourseAuthoringTableNames.Presentation, row)))
                    .OrderBy(value => value.PresentationId, StringComparer.Ordinal)
                    .ThenBy(value => value.Source.Line),
                ReadAcceptanceRecords(tables, courseId, diagnostics));

            return diagnostics.Count == 0
                ? new CourseAuthoringDraftReadResult(draft, diagnostics)
                : new CourseAuthoringDraftReadResult(null, diagnostics);
        }

        private static CourseDraftCourse ReadCourse(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var rows = Rows(tables, CourseAuthoringTableNames.Course).ToArray();
            if (rows.Length != 1)
            {
                diagnostics.Add(Diagnostic(
                    "draft.course.row-count.invalid",
                    CourseAuthoringTableNames.Course,
                    1,
                    CourseAuthoringColumns.Course.Id,
                    "课程表必须且只能包含一条课程记录。",
                    "保留一条课程信息，其余内容移入对应职责表。"));
                return null;
            }

            var row = rows[0];
            var courseId = Value(row, CourseAuthoringColumns.Course.Id);
            return new CourseDraftCourse(
                courseId,
                Value(row, CourseAuthoringColumns.Course.DisplayName),
                Value(row, CourseAuthoringColumns.Course.DisciplinePackage),
                Value(row, CourseAuthoringColumns.Course.ActorEntityId),
                Value(row, CourseAuthoringColumns.Course.ExperimentPrefab),
                Source(courseId, CourseAuthoringTableNames.Course, row));
        }

        private static IEnumerable<CourseDraftOperationOverride>
            ReadOperationOverrides(
                IReadOnlyDictionary<string, StrictCsvReadResult> tables,
                string courseId,
                ICollection<CourseCompilationDiagnostic> diagnostics) =>
            Rows(tables, CourseAuthoringTableNames.OperationOverrides)
                .Select(row => new CourseDraftOperationOverride(
                    Value(row, CourseAuthoringColumns.OperationOverride.Id),
                    Value(row, CourseAuthoringColumns.OperationOverride.Handling),
                    Value(row, CourseAuthoringColumns.OperationOverride.Operation),
                    Value(row, CourseAuthoringColumns.OperationOverride.SourceSelectorKind),
                    Value(row, CourseAuthoringColumns.OperationOverride.SourceSelectorValue),
                    Value(row, CourseAuthoringColumns.OperationOverride.TargetSelectorKind),
                    Value(row, CourseAuthoringColumns.OperationOverride.TargetSelectorValue),
                    Integer(row, CourseAuthoringColumns.OperationOverride.Order,
                        CourseAuthoringTableNames.OperationOverrides, diagnostics),
                    Value(row, CourseAuthoringColumns.OperationOverride.SubjectSelectorKind),
                    Value(row, CourseAuthoringColumns.OperationOverride.SubjectSelectorValue),
                    Value(row, CourseAuthoringColumns.OperationOverride.Fact),
                    Value(row, CourseAuthoringColumns.OperationOverride.Comparison),
                    Value(row, CourseAuthoringColumns.OperationOverride.Value),
                    Value(row, CourseAuthoringColumns.OperationOverride.Unit),
                    Value(row, CourseAuthoringColumns.OperationOverride.RejectionMessage),
                    Value(row, CourseAuthoringColumns.OperationOverride.ConsequenceTemplate),
                    Source(courseId, CourseAuthoringTableNames.OperationOverrides, row)))
                .OrderBy(value => value.OverrideId, StringComparer.Ordinal)
                .ThenBy(value => value.Order)
                .ThenBy(value => value.Source.Line);

        private static IEnumerable<CourseDraftTeachingItem> ReadTeachingItems(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            string courseId,
            ICollection<CourseCompilationDiagnostic> diagnostics) =>
            Rows(tables, CourseAuthoringTableNames.Teaching)
                .Select(row => new CourseDraftTeachingItem(
                    Value(row, CourseAuthoringColumns.Teaching.Id),
                    Value(row, CourseAuthoringColumns.Teaching.Type),
                    Value(row, CourseAuthoringColumns.Teaching.DisplayName),
                    Value(row, CourseAuthoringColumns.Teaching.TriggerType),
                    Value(row, CourseAuthoringColumns.Teaching.TriggerValue),
                    Integer(row, CourseAuthoringColumns.Teaching.ScoreDelta,
                        CourseAuthoringTableNames.Teaching, diagnostics),
                    Value(row, CourseAuthoringColumns.Teaching.Prompt),
                    Value(row, CourseAuthoringColumns.Teaching.ErrorSeverity),
                    Value(row, CourseAuthoringColumns.Teaching.Continuation),
                    Value(row, CourseAuthoringColumns.Teaching.AffectedGoals),
                    Source(courseId, CourseAuthoringTableNames.Teaching, row)))
                .OrderBy(value => value.TeachingItemId, StringComparer.Ordinal)
                .ThenBy(value => value.Source.Line);

        private static IEnumerable<CourseDraftTeachingCondition>
            ReadTeachingConditions(
                IReadOnlyDictionary<string, StrictCsvReadResult> tables,
                string courseId,
                ICollection<CourseCompilationDiagnostic> diagnostics) =>
            Rows(tables, CourseAuthoringTableNames.TeachingConditions)
                .Select(row => new CourseDraftTeachingCondition(
                    Value(row, CourseAuthoringColumns.TeachingCondition.TeachingItemId),
                    Integer(row, CourseAuthoringColumns.TeachingCondition.Order,
                        CourseAuthoringTableNames.TeachingConditions, diagnostics),
                    Value(row, CourseAuthoringColumns.TeachingCondition.SubjectSelectorKind),
                    Value(row, CourseAuthoringColumns.TeachingCondition.SubjectSelectorValue),
                    Value(row, CourseAuthoringColumns.TeachingCondition.Fact),
                    Value(row, CourseAuthoringColumns.TeachingCondition.Comparison),
                    Value(row, CourseAuthoringColumns.TeachingCondition.Value),
                    Value(row, CourseAuthoringColumns.TeachingCondition.Unit),
                    Source(courseId, CourseAuthoringTableNames.TeachingConditions, row)))
                .OrderBy(value => value.TeachingItemId, StringComparer.Ordinal)
                .ThenBy(value => value.Order)
                .ThenBy(value => value.Source.Line);

        private static IEnumerable<CourseDraftAcceptanceRecord>
            ReadAcceptanceRecords(
                IReadOnlyDictionary<string, StrictCsvReadResult> tables,
                string courseId,
                ICollection<CourseCompilationDiagnostic> diagnostics) =>
            Rows(tables, CourseAuthoringTableNames.AcceptanceScenarios)
                .Select(row => new CourseDraftAcceptanceRecord(
                    Value(row, CourseAuthoringColumns.Acceptance.ScenarioId),
                    Integer(row, CourseAuthoringColumns.Acceptance.Order,
                        CourseAuthoringTableNames.AcceptanceScenarios, diagnostics),
                    Value(row, CourseAuthoringColumns.Acceptance.RecordType),
                    Value(row, CourseAuthoringColumns.Acceptance.Operation),
                    Value(row, CourseAuthoringColumns.Acceptance.Source),
                    Value(row, CourseAuthoringColumns.Acceptance.Target),
                    Value(row, CourseAuthoringColumns.Acceptance.ParameterName),
                    Value(row, CourseAuthoringColumns.Acceptance.ParameterValue),
                    Value(row, CourseAuthoringColumns.Acceptance.AssertionType),
                    Value(row, CourseAuthoringColumns.Acceptance.Object),
                    Value(row, CourseAuthoringColumns.Acceptance.Fact),
                    Value(row, CourseAuthoringColumns.Acceptance.Comparison),
                    Value(row, CourseAuthoringColumns.Acceptance.ExpectedValue),
                    Value(row, CourseAuthoringColumns.Acceptance.Unit),
                    Source(courseId, CourseAuthoringTableNames.AcceptanceScenarios, row)))
                .OrderBy(value => value.ScenarioId, StringComparer.Ordinal)
                .ThenBy(value => value.Order)
                .ThenBy(value => value.Source.Line);

        private static void ValidateHeader(
            CourseAuthoringTableSchema schema,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (table.ConfiguredHeaders.SequenceEqual(
                    schema.Columns,
                    StringComparer.Ordinal))
            {
                return;
            }

            diagnostics.Add(Diagnostic(
                "draft.header.invalid",
                schema.FileName,
                1,
                schema.IdentityColumn,
                "表头与新课程草稿的固定结构不一致。当前为：“"
                + string.Join(",", table.ConfiguredHeaders) + "”。",
                "将第一行改为：“" + string.Join(",", schema.Columns) + "”。"));
        }

        private static void ValidateIdentities(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var repeatable = new HashSet<string>(StringComparer.Ordinal)
            {
                CourseAuthoringTableNames.OperationOverrides,
                CourseAuthoringTableNames.TeachingConditions,
                CourseAuthoringTableNames.AcceptanceScenarios
            };
            foreach (var schema in CourseAuthoringSchema.Tables)
            {
                var rows = Rows(tables, schema.FileName).ToArray();
                foreach (var row in rows.Where(value => string.IsNullOrWhiteSpace(
                             Value(value, schema.IdentityColumn))))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.identity.missing",
                        schema.FileName,
                        row.LineNumber,
                        schema.IdentityColumn,
                        $"“{schema.IdentityColumn}”不能为空。",
                        "填写稳定且可读的自然中文标识。"));
                }

                if (repeatable.Contains(schema.FileName))
                {
                    continue;
                }

                foreach (var duplicate in rows
                             .Select(row => Value(row, schema.IdentityColumn))
                             .Where(value => value.Length > 0)
                             .GroupBy(value => value, StringComparer.Ordinal)
                             .Where(group => group.Count() > 1))
                {
                    diagnostics.Add(Diagnostic(
                        "draft.identity.duplicate",
                        schema.FileName,
                        1,
                        schema.IdentityColumn,
                        $"标识“{duplicate.Key}”在“{schema.FileName}”中重复。",
                        "为每条独立记录使用唯一标识。"));
                }
            }
        }

        private static int Integer(
            StrictCsvRow row,
            string column,
            string fileName,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var configured = Value(row, column);
            if (int.TryParse(
                    configured,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return value;
            }

            diagnostics.Add(Diagnostic(
                "draft.number.invalid",
                fileName,
                row.LineNumber,
                column,
                $"“{configured}”不是有效整数。",
                $"在“{column}”填写不带单位的整数。"));
            return 0;
        }

        private static IEnumerable<StrictCsvRow> Rows(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            string fileName) =>
            tables.TryGetValue(fileName, out var table)
                ? table.Rows
                : Array.Empty<StrictCsvRow>();

        private static string Value(StrictCsvRow row, string column) =>
            row.ConfiguredValue(column).Trim();

        private static ConfigurationSource Source(
            string courseId,
            string fileName,
            StrictCsvRow row)
        {
            var schema = CourseAuthoringSchema.ByFileName[fileName];
            var column = schema.Columns
                .Select((value, index) => new { value, index })
                .First(value => value.value == schema.IdentityColumn)
                .index + 1;
            return new ConfigurationSource(
                ConfigurationLayer.Course,
                courseId,
                fileName,
                row.LineNumber,
                column,
                Value(row, schema.IdentityColumn));
        }

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            string fileName,
            int line,
            string columnName,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                fileName,
                line,
                1,
                columnName,
                string.Empty,
                reason,
                suggestion);
    }
}
