using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Unity.Authoring.Blueprints
{
    public sealed class CourseBlueprintReadResult
    {
        internal CourseBlueprintReadResult(
            CourseBlueprint blueprint,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Blueprint = blueprint;
            Diagnostics = diagnostics.ToArray();
        }

        public bool IsSuccess => Diagnostics.Count == 0;
        public CourseBlueprint Blueprint { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// 严格读取课程作者维护的高层表，仅解析通用结构，不解释学科参数。
    /// </summary>
    public sealed class CourseBlueprintReader
    {
        private const string CourseFile = "课程.csv";
        private const string ObjectsFile = "实验对象.csv";
        private const string InteractionRulesFile = "交互规则.csv";
        private const string DisciplineProcessesFile = "学科过程.csv";
        private const string TeachingEvaluationsFile = "教学评价.csv";
        private const string PresentationOverridesFile = "表现覆盖.csv";
        private const string AcceptanceRecordsFile = "验收场景.csv";
        private const string ExperimentFlowFile = "实验流程.csv";
        private const string AdvancedOverridesFile = "高级覆盖.csv";

        private static readonly IReadOnlyDictionary<string, FileSchema> Schemas =
            new ReadOnlyDictionary<string, FileSchema>(
                new Dictionary<string, FileSchema>(StringComparer.Ordinal)
                {
                    [CourseFile] = new FileSchema(
                        "课程ID",
                        new[] { "课程ID", "显示名称", "学科配方包", "环境Prefab" },
                        optionalColumns: new[] { "操作者实体ID" }),
                    [ObjectsFile] = new FileSchema(
                        "实体ID",
                        new[]
                        {
                            "实体ID", "显示名称", "Prefab", "特征列表",
                            "初始位置", "初始旋转"
                        },
                        new[] { "参数." }),
                    [InteractionRulesFile] = new FileSchema(
                        "交互ID",
                        new[]
                        {
                            "交互ID", "处理方式", "动作", "来源", "目标", "顺序",
                            "要求类型", "要求主体", "字段", "比较", "值", "单位",
                            "结果配方", "反馈配方", "拒绝文案"
                        }),
                    [DisciplineProcessesFile] = new FileSchema(
                        "配置ID",
                        new[]
                        {
                            "配置ID", "类型", "配方", "主体", "来源", "目标",
                            "操作名称", "协议", "参数"
                        }),
                    [TeachingEvaluationsFile] = new FileSchema(
                        "评价ID",
                        new[]
                        {
                            "评价ID", "类型", "显示名称", "触发类型", "触发值",
                            "顺序", "条件类型", "主体", "字段", "比较", "值",
                            "单位", "分值变化", "提示文案"
                        }),
                    [PresentationOverridesFile] = new FileSchema(
                        "覆盖ID",
                        new[]
                        {
                            "覆盖ID", "对象或状态", "表现原语", "作用位置",
                            "位置ID", "参数名", "参数类型", "参数值"
                        },
                        optionalColumns: new[]
                        {
                            "触发类型", "触发值", "触发来源", "触发目标"
                        }),
                    [AcceptanceRecordsFile] = new FileSchema(
                        "场景ID",
                        new[]
                        {
                            "场景ID", "顺序", "记录类型", "动作", "来源", "目标",
                            "参数名", "参数值", "断言类型", "对象", "字段", "比较",
                            "期望值", "单位"
                        }),
                    [ExperimentFlowFile] = new FileSchema(
                        "步骤ID",
                        new[]
                        {
                            "流程ID", "步骤ID", "顺序", "记录类型", "显示名称",
                            "动作", "来源", "目标", "触发类型", "触发值",
                            "条件类型", "主体", "字段", "比较", "值", "单位",
                            "分值变化", "提示文案", "参数名", "参数值"
                        },
                        optionalColumns: new[]
                        {
                            "后果严重度", "可恢复性", "受阻目标"
                        }),
                    [AdvancedOverridesFile] = new FileSchema(
                        "覆盖ID",
                        new[]
                        {
                            "覆盖ID", "配方ID", "来源实体", "目标实体", "生成项",
                            "操作", "字段", "值"
                        })
                });

        public CourseBlueprintReadResult Read(CourseBlueprintSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var diagnostics = new List<CourseCompilationDiagnostic>();
            var files = source
                .OrderBy(file => file.FileName, StringComparer.Ordinal)
                .ToArray();
            var supported = new Dictionary<string, CourseBlueprintFile>(
                StringComparer.Ordinal);

            foreach (var file in files)
            {
                if (!Schemas.ContainsKey(file.FileName))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.file.unsupported",
                        file.FileName,
                        1,
                        1,
                        string.Empty,
                        string.Empty,
                        $"文件“{file.FileName}”不是课程蓝图允许的高层表。",
                        "删除该文件，或将内容迁移到受支持的高层表之一。"));
                    continue;
                }

                if (supported.ContainsKey(file.FileName))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.file.duplicate",
                        file.FileName,
                        1,
                        1,
                        string.Empty,
                        string.Empty,
                        $"课程蓝图中存在多个“{file.FileName}”。",
                        "每种高层表只保留一个文件。"));
                    continue;
                }

                supported.Add(file.FileName, file);
            }

            RequireFile(supported, CourseFile, diagnostics);
            RequireFile(supported, ObjectsFile, diagnostics);

            var tables = new Dictionary<string, StrictCsvReadResult>(
                StringComparer.Ordinal);
            var csvReader = new StrictCsvReader();
            foreach (var pair in supported.OrderBy(
                         value => value.Key,
                         StringComparer.Ordinal))
            {
                var table = csvReader.ReadBytes(
                    pair.Key,
                    pair.Value.Content);
                tables.Add(pair.Key, table);
                diagnostics.AddRange(table.Diagnostics);
                ValidateSchema(pair.Key, table, diagnostics);
            }

            var course = ReadCourse(tables, diagnostics);
            var courseId = course?.CourseId ?? string.Empty;
            var objects = ReadObjects(tables, courseId, diagnostics);
            if (tables.TryGetValue(CourseFile, out var courseTable)
                && courseTable.Headers.Contains(
                    "操作者实体ID",
                    StringComparer.Ordinal))
            {
                ValidateActor(course, objects, diagnostics);
            }
            var interactions = ReadRecords(
                tables,
                InteractionRulesFile,
                courseId,
                (values, origin) =>
                    new CourseInteractionRuleBlueprint(values, origin))
                .OrderBy(value => value.Order)
                .ThenBy(value => value.InteractionId, StringComparer.Ordinal)
                .ThenBy(value => value.Source.Line)
                .ToArray();
            var processes = ReadDisciplineProcesses(
                    tables,
                    courseId,
                    diagnostics)
                .OrderBy(value => value.DefinitionId, StringComparer.Ordinal)
                .ThenBy(value => value.Source.Line)
                .ToArray();
            var flow = ReadExperimentFlow(
                tables,
                courseId,
                diagnostics);
            var evaluations = (flow == null
                    ? ReadRecords(
                        tables,
                        TeachingEvaluationsFile,
                        courseId,
                        (values, origin) =>
                            new CourseTeachingEvaluationBlueprint(values, origin))
                    : flow.TeachingEvaluations)
                .OrderBy(value => value.Order)
                .ThenBy(value => value.EvaluationId, StringComparer.Ordinal)
                .ThenBy(value => value.Source.Line)
                .ToArray();
            var presentationOverrides = ReadRecords(
                    tables,
                    PresentationOverridesFile,
                    courseId,
                    (values, origin) =>
                        new CoursePresentationOverrideBlueprint(values, origin))
                .OrderBy(value => value.OverrideId, StringComparer.Ordinal)
                .ThenBy(value => value.Source.Line)
                .ToArray();
            var acceptanceRecords = (flow == null
                    ? ReadRecords(
                        tables,
                        AcceptanceRecordsFile,
                        courseId,
                        (values, origin) =>
                            new CourseAcceptanceRecordBlueprint(values, origin))
                    : flow.AcceptanceRecords)
                .OrderBy(value => value.ScenarioId, StringComparer.Ordinal)
                .ThenBy(value => value.Order)
                .ThenBy(value => value.Source.Line)
                .ToArray();
            var advancedOverrides = ReadRecords(
                    tables,
                    AdvancedOverridesFile,
                    courseId,
                    (values, origin) =>
                        new CourseAdvancedOverrideBlueprint(values, origin))
                .OrderBy(value => value.OverrideId, StringComparer.Ordinal)
                .ThenBy(value => value.Source.Line)
                .ToArray();

            var blueprint = new CourseBlueprint(
                course ?? new CourseBlueprintCourse(
                    string.Empty,
                    string.Empty,
                    Array.Empty<string>(),
                    "学生",
                    string.Empty,
                    new ConfigurationSource(
                        ConfigurationLayer.Course,
                        string.Empty,
                        CourseFile,
                        1,
                        1,
                        string.Empty)),
                objects,
                interactions,
                processes,
                evaluations,
                presentationOverrides,
                acceptanceRecords,
                advancedOverrides);
            return new CourseBlueprintReadResult(blueprint, diagnostics);
        }

        private static void RequireFile(
            IReadOnlyDictionary<string, CourseBlueprintFile> files,
            string fileName,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (files.ContainsKey(fileName))
            {
                return;
            }

            diagnostics.Add(Diagnostic(
                "blueprint.file.missing",
                fileName,
                1,
                1,
                string.Empty,
                string.Empty,
                $"课程蓝图缺少必需文件“{fileName}”。",
                $"新建“{fileName}”并填写规定表头。"));
        }

        private static void ValidateSchema(
            string fileName,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var schema = Schemas[fileName];
            foreach (var required in schema.FixedColumns)
            {
                if (table.Headers.Contains(required, StringComparer.Ordinal))
                {
                    continue;
                }

                diagnostics.Add(Diagnostic(
                    "blueprint.column.missing",
                    fileName,
                    1,
                    1,
                    required,
                    string.Empty,
                    $"文件“{fileName}”缺少必需列“{required}”。",
                    $"在表头中增加列“{required}”。"));
            }

            for (var index = 0; index < table.Headers.Count; index++)
            {
                var header = table.Headers[index];
                if (schema.IsKnown(header)
                    || schema.DynamicPrefixes.Any(prefix =>
                        header.StartsWith(prefix, StringComparison.Ordinal)
                        && header.Length > prefix.Length))
                {
                    continue;
                }

                diagnostics.Add(Diagnostic(
                    "blueprint.column.unsupported",
                    fileName,
                    1,
                    index + 1,
                    header,
                    string.Empty,
                    $"列“{header}”不属于“{fileName}”的固定列或允许的扩展命名空间。",
                    "删除该列，或使用已允许的结构化扩展列前缀。"));
            }
        }

        private static CourseBlueprintCourse ReadCourse(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (!tables.TryGetValue(CourseFile, out var table)
                || table.Rows.Count == 0)
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.course.missing",
                    CourseFile,
                    2,
                    1,
                    "课程ID",
                    string.Empty,
                    "课程表必须包含一条课程记录。",
                    "在课程表第二行填写课程 ID、显示名称、学科配方包和环境 Prefab。"));
                return null;
            }

            if (table.Rows.Count != 1)
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.course.count.invalid",
                    CourseFile,
                    table.Rows[1].LineNumber,
                    1,
                    "课程ID",
                    table.Rows[1]["课程ID"],
                    "课程表只能包含一条课程记录。",
                    "每个课程目录只保留一条课程记录。"));
            }

            var row = table.Rows[0];
            var courseId = row["课程ID"].Trim();
            ValidateIdentifier(
                CourseFile,
                row,
                "课程ID",
                courseId,
                diagnostics);
            return new CourseBlueprintCourse(
                courseId,
                row["显示名称"].Trim(),
                SplitList(row["学科配方包"]),
                table.Headers.Contains("操作者实体ID", StringComparer.Ordinal)
                    && !string.IsNullOrWhiteSpace(row["操作者实体ID"])
                        ? row["操作者实体ID"].Trim()
                        : "学生",
                row["环境Prefab"].Trim(),
                Source(CourseFile, table, row, "课程ID", courseId, courseId));
        }

        private static void ValidateActor(
            CourseBlueprintCourse course,
            IReadOnlyList<CourseObjectBlueprint> objects,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (course == null
                || string.IsNullOrWhiteSpace(course.ActorEntityId)
                || objects.Any(value => string.Equals(
                    value.EntityId,
                    course.ActorEntityId,
                    StringComparison.Ordinal)))
            {
                return;
            }

            diagnostics.Add(Diagnostic(
                "blueprint.course.actor-missing",
                CourseFile,
                course.Source.Line,
                course.Source.Column,
                "操作者实体ID",
                course.CourseId,
                $"课程指定的操作者“{course.ActorEntityId}”不在实验对象表中。",
                $"在实验对象.csv 中添加实体“{course.ActorEntityId}”，或修改课程.csv 的操作者实体ID。"));
        }

        private static IReadOnlyList<CourseObjectBlueprint> ReadObjects(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            string courseId,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (!tables.TryGetValue(ObjectsFile, out var table))
            {
                return Array.Empty<CourseObjectBlueprint>();
            }

            var result = new List<CourseObjectBlueprint>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                var entityId = row["实体ID"].Trim();
                ValidateIdentifier(
                    ObjectsFile,
                    row,
                    "实体ID",
                    entityId,
                    diagnostics);
                if (!string.IsNullOrWhiteSpace(entityId)
                    && !identifiers.Add(entityId))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.entity.duplicate",
                        ObjectsFile,
                        row.LineNumber,
                        Column(table, "实体ID"),
                        "实体ID",
                        entityId,
                        $"实体 ID“{entityId}”重复。",
                        "为每个实验对象使用唯一且稳定的中文实体 ID。"));
                }

                var position = ParseVector(
                    ObjectsFile,
                    table,
                    row,
                    "初始位置",
                    entityId,
                    diagnostics);
                var rotation = ParseVector(
                    ObjectsFile,
                    table,
                    row,
                    "初始旋转",
                    entityId,
                    diagnostics);
                var extensions = table.Headers
                    .Where(header => Schemas[ObjectsFile].IsDynamic(header))
                    .Select(header => new KeyValuePair<string, BlueprintValue>(
                        header,
                        new BlueprintValue(
                            header,
                            row[header],
                            Source(
                                ObjectsFile,
                                table,
                                row,
                                header,
                                courseId,
                                entityId))));

                result.Add(new CourseObjectBlueprint(
                    entityId,
                    row["显示名称"].Trim(),
                    row["Prefab"].Trim(),
                    SplitList(row["特征列表"]),
                    position,
                    rotation,
                    CourseBlueprint.ReadOnlyValues(extensions),
                    Source(
                        ObjectsFile,
                        table,
                        row,
                        "实体ID",
                        courseId,
                        entityId)));
            }

            return result
                .OrderBy(value => value.EntityId, StringComparer.Ordinal)
                .ThenBy(value => value.Source.Line)
                .ToArray();
        }

        private static ExperimentFlowProjection ReadExperimentFlow(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            string courseId,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (!tables.TryGetValue(ExperimentFlowFile, out var table))
            {
                return null;
            }

            if (tables.ContainsKey(TeachingEvaluationsFile)
                || tables.ContainsKey(AcceptanceRecordsFile))
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.flow.legacy-conflict",
                    ExperimentFlowFile,
                    1,
                    1,
                    "记录类型",
                    string.Empty,
                    "实验流程.csv 不能与教学评价.csv 或验收场景.csv 同时使用。",
                    "将教学和验收记录迁移到实验流程.csv 后删除两张旧表。"));
            }

            var evaluations = new List<CourseTeachingEvaluationBlueprint>();
            var acceptance = new List<CourseAcceptanceRecordBlueprint>();
            foreach (var row in table.Rows)
            {
                var stepId = row["步骤ID"].Trim();
                var flowId = row["流程ID"].Trim();
                var recordType = row["记录类型"].Trim();
                ValidateIdentifier(
                    ExperimentFlowFile,
                    row,
                    "流程ID",
                    flowId,
                    diagnostics);
                ValidateIdentifier(
                    ExperimentFlowFile,
                    row,
                    "步骤ID",
                    stepId,
                    diagnostics);
                var source = Source(
                    ExperimentFlowFile,
                    table,
                    row,
                    "步骤ID",
                    courseId,
                    stepId);

                if (recordType == "操作")
                {
                    acceptance.Add(new CourseAcceptanceRecordBlueprint(
                        FlowValues(source,
                            Pair("场景ID", flowId),
                            Pair("顺序", row["顺序"]),
                            Pair("记录类型", "动作"),
                            Pair("动作", row["动作"]),
                            Pair("来源", row["来源"]),
                            Pair("目标", row["目标"]),
                            Pair("参数名", row["参数名"]),
                            Pair("参数值", row["参数值"])),
                        source));
                    continue;
                }

                if (recordType == "断言" || recordType == "目标并断言")
                {
                    acceptance.Add(new CourseAcceptanceRecordBlueprint(
                        FlowValues(source,
                            Pair("场景ID", flowId),
                            Pair("顺序", row["顺序"]),
                            Pair("记录类型", "断言"),
                            Pair("断言类型", row["条件类型"]),
                            Pair("对象", row["主体"]),
                            Pair("字段", row["字段"]),
                            Pair("比较", row["比较"]),
                            Pair("期望值", row["值"]),
                            Pair("单位", row["单位"])),
                        source));
                    if (recordType == "断言")
                    {
                        continue;
                    }
                }

                var evaluationType = recordType == "目标并断言"
                    ? "目标"
                    : recordType;
                if (evaluationType != "目标"
                    && evaluationType != "风险"
                    && evaluationType != "评分"
                    && evaluationType != "提示")
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.flow.record-type-unknown",
                        ExperimentFlowFile,
                        row.LineNumber,
                        Column(table, "记录类型"),
                        "记录类型",
                        stepId,
                        $"实验流程记录类型“{recordType}”未注册。",
                        "使用操作、断言、目标、目标并断言、风险、评分或提示。"));
                    continue;
                }

                evaluations.Add(new CourseTeachingEvaluationBlueprint(
                    FlowValues(source,
                        Pair("评价ID", stepId),
                        Pair("类型", evaluationType),
                        Pair("显示名称", row["显示名称"]),
                        Pair("触发类型", row["触发类型"]),
                        Pair("触发值", row["触发值"]),
                        Pair("顺序", row["顺序"]),
                        Pair("条件类型", row["条件类型"]),
                        Pair("主体", row["主体"]),
                        Pair("字段", row["字段"]),
                        Pair("比较", row["比较"]),
                        Pair("值", row["值"]),
                        Pair("单位", row["单位"]),
                        Pair("分值变化", row["分值变化"]),
                        Pair("提示文案", row["提示文案"]),
                        Pair("后果严重度", Optional(row, "后果严重度")),
                        Pair("可恢复性", Optional(row, "可恢复性")),
                        Pair("受阻目标", Optional(row, "受阻目标"))),
                    source));
            }

            return new ExperimentFlowProjection(evaluations, acceptance);
        }

        private static IReadOnlyDictionary<string, BlueprintValue> FlowValues(
            ConfigurationSource source,
            params KeyValuePair<string, string>[] values) =>
            CourseBlueprint.ReadOnlyValues(values.Select(value =>
                InternalValue(value.Key, value.Value, source)));

        private static KeyValuePair<string, string> Pair(
            string name,
            string value) =>
            new KeyValuePair<string, string>(name, value ?? string.Empty);

        private static string Optional(StrictCsvRow row, string columnName) =>
            row.Values.TryGetValue(columnName, out var value)
                ? value
                : string.Empty;

        private static IEnumerable<CourseDisciplineProcessBlueprint>
            ReadDisciplineProcesses(
                IReadOnlyDictionary<string, StrictCsvReadResult> tables,
                string courseId,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (!tables.TryGetValue(DisciplineProcessesFile, out var table))
            {
                return Array.Empty<CourseDisciplineProcessBlueprint>();
            }

            return table.Rows.Select(row =>
            {
                var id = row["配置ID"].Trim();
                ValidateIdentifier(
                    DisciplineProcessesFile,
                    row,
                    "配置ID",
                    id,
                    diagnostics);
                var source = Source(
                    DisciplineProcessesFile,
                    table,
                    row,
                    "配置ID",
                    courseId,
                    id);
                var configuredType = row["类型"].Trim();
                var recordType = InternalDisciplineRecordType(
                    configuredType,
                    out var inferredRole);
                if (recordType == null)
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.discipline.type.unknown",
                        DisciplineProcessesFile,
                        row.LineNumber,
                        Column(table, "类型"),
                        "类型",
                        id,
                        $"学科配置类型“{configuredType}”未注册。",
                        "使用物质、初始物质、反应、反应物、产物、过程或附加操作。"));
                    recordType = configuredType;
                }

                var protocol = ResolveOperationProtocol(
                    row["协议"].Trim(),
                    row,
                    table,
                    id,
                    diagnostics);
                var parameters = ParseCompactParameters(
                    row["参数"],
                    row,
                    table,
                    courseId,
                    id,
                    diagnostics);
                if (recordType == "过程参数"
                    && !string.IsNullOrWhiteSpace(protocol))
                {
                    parameters["参数.操作协议"] = new BlueprintValue(
                        "参数.操作协议",
                        protocol,
                        Source(
                            DisciplineProcessesFile,
                            table,
                            row,
                            "协议",
                            courseId,
                            id));
                }

                var target = string.IsNullOrWhiteSpace(inferredRole)
                    ? row["目标"].Trim()
                    : inferredRole;
                var values = CourseBlueprint.ReadOnlyValues(new[]
                {
                    InternalValue("定义ID", id, source),
                    InternalValue("记录类型", recordType, source),
                    InternalValue("学科配方", row["配方"].Trim(), source),
                    InternalValue("主体", row["主体"].Trim(), source),
                    InternalValue("来源", row["来源"].Trim(), source),
                    InternalValue("目标", target, source),
                    InternalValue(
                        "启动交互",
                        recordType == "过程操作"
                            ? row["操作名称"].Trim()
                            : string.Empty,
                        source),
                    InternalValue(
                        "停止交互",
                        recordType == "过程操作" ? protocol : string.Empty,
                        source)
                }.Concat(parameters));
                return new CourseDisciplineProcessBlueprint(
                    values,
                    source,
                    CourseBlueprint.ReadOnlyValues(parameters));
            });
        }

        private static KeyValuePair<string, BlueprintValue> InternalValue(
            string name,
            string value,
            ConfigurationSource source) =>
            new KeyValuePair<string, BlueprintValue>(
                name,
                new BlueprintValue(name, value ?? string.Empty, source));

        private static string InternalDisciplineRecordType(
            string configuredType,
            out string inferredRole)
        {
            inferredRole = string.Empty;
            switch (configuredType)
            {
                case "物质":
                case "初始物质":
                case "反应":
                    return configuredType;
                case "反应物":
                case "产物":
                    inferredRole = configuredType;
                    return "反应项";
                case "过程":
                    return "过程参数";
                case "附加操作":
                    return "过程操作";
                default:
                    return null;
            }
        }

        private static string ResolveOperationProtocol(
            string configured,
            StrictCsvRow row,
            StrictCsvReadResult table,
            string configurationId,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return string.Empty;
            }

            return configured.Trim();
        }

        private static Dictionary<string, BlueprintValue>
            ParseCompactParameters(
                string raw,
                StrictCsvRow row,
                StrictCsvReadResult table,
                string courseId,
                string configurationId,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var result = new Dictionary<string, BlueprintValue>(
                StringComparer.Ordinal);
            foreach (var segment in (raw ?? string.Empty).Split(
                         new[] { ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = segment.IndexOf('=');
                if (separator <= 0)
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.discipline.parameter.invalid",
                        DisciplineProcessesFile,
                        row.LineNumber,
                        Column(table, "参数"),
                        "参数",
                        configurationId,
                        $"参数片段“{segment.Trim()}”不是“名称=值”格式。",
                        "多个参数使用半角分号分隔，例如“物质标识=水;数量=10”。"));
                    continue;
                }

                var configuredName = segment.Substring(0, separator).Trim();
                var name = configuredName;
                var value = segment.Substring(separator + 1).Trim();
                var key = "参数." + name;
                if (configuredName.Length == 0 || !result.TryAdd(
                        key,
                        new BlueprintValue(
                            key,
                            value,
                            Source(
                                DisciplineProcessesFile,
                                table,
                                row,
                                "参数",
                                courseId,
                                configurationId))))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.discipline.parameter.duplicate",
                        DisciplineProcessesFile,
                        row.LineNumber,
                        Column(table, "参数"),
                        "参数",
                        configurationId,
                        $"参数“{name}”为空或重复。",
                        "同一行的每个参数只填写一次。"));
                }
            }

            return result;
        }

        private static IEnumerable<T> ReadRecords<T>(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            string fileName,
            string courseId,
            Func<IReadOnlyDictionary<string, BlueprintValue>,
                ConfigurationSource,
                T> factory)
        {
            if (!tables.TryGetValue(fileName, out var table))
            {
                return Array.Empty<T>();
            }

            var schema = Schemas[fileName];
            return table.Rows.Select(row =>
            {
                var id = row[schema.IdentifierColumn].Trim();
                return factory(
                    ValuesForRow(fileName, table, row, courseId, id),
                    Source(
                        fileName,
                        table,
                        row,
                        schema.IdentifierColumn,
                        courseId,
                        id));
            });
        }

        private static IReadOnlyDictionary<string, BlueprintValue> ValuesForRow(
            string fileName,
            StrictCsvReadResult table,
            StrictCsvRow row,
            string courseId,
            string configurationId) =>
            CourseBlueprint.ReadOnlyValues(table.Headers.Select(header =>
                new KeyValuePair<string, BlueprintValue>(
                    header,
                    new BlueprintValue(
                        header,
                        row[header],
                        Source(
                            fileName,
                            table,
                            row,
                            header,
                            courseId,
                            configurationId)))));

        private static BlueprintVector3 ParseVector(
            string fileName,
            StrictCsvReadResult table,
            StrictCsvRow row,
            string columnName,
            string configurationId,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var raw = row[columnName];
            var parts = raw.Split('|');
            if (parts.Length == 3
                && TryFiniteDouble(parts[0], out var x)
                && TryFiniteDouble(parts[1], out var y)
                && TryFiniteDouble(parts[2], out var z))
            {
                return new BlueprintVector3(x, y, z);
            }

            diagnostics.Add(Diagnostic(
                "blueprint.vector.invalid",
                fileName,
                row.LineNumber,
                Column(table, columnName),
                columnName,
                configurationId,
                $"“{columnName}”的值“{raw}”不是有效的三维向量。",
                "使用“X|Y|Z”格式填写三个有限数字，例如“0|1|0”。"));
            return new BlueprintVector3(0, 0, 0);
        }

        private static bool TryFiniteDouble(string raw, out double value)
        {
            if (!double.TryParse(
                    raw.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
            {
                return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static IReadOnlyList<string> SplitList(string raw) =>
            (raw ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private static void ValidateIdentifier(
            string fileName,
            StrictCsvRow row,
            string columnName,
            string identifier,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                return;
            }

            diagnostics.Add(Diagnostic(
                "blueprint.id.missing",
                fileName,
                row.LineNumber,
                1,
                columnName,
                string.Empty,
                $"列“{columnName}”不能为空。",
                "填写稳定且可读的中文配置 ID。"));
        }

        private static ConfigurationSource Source(
            string fileName,
            StrictCsvReadResult table,
            StrictCsvRow row,
            string columnName,
            string courseId,
            string configurationId) =>
            new ConfigurationSource(
                ConfigurationLayer.Course,
                courseId,
                fileName,
                row.LineNumber,
                Column(table, columnName),
                configurationId);

        private static int Column(
            StrictCsvReadResult table,
            string columnName)
        {
            for (var index = 0; index < table.Headers.Count; index++)
            {
                if (string.Equals(
                        table.Headers[index],
                        columnName,
                        StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return 1;
        }

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            string fileName,
            int line,
            int column,
            string columnName,
            string configurationId,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                fileName,
                line,
                column,
                columnName,
                configurationId,
                reason,
                suggestion);

        private sealed class ExperimentFlowProjection
        {
            public ExperimentFlowProjection(
                IEnumerable<CourseTeachingEvaluationBlueprint>
                    teachingEvaluations,
                IEnumerable<CourseAcceptanceRecordBlueprint>
                    acceptanceRecords)
            {
                TeachingEvaluations = teachingEvaluations.ToArray();
                AcceptanceRecords = acceptanceRecords.ToArray();
            }

            public IReadOnlyList<CourseTeachingEvaluationBlueprint>
                TeachingEvaluations { get; }
            public IReadOnlyList<CourseAcceptanceRecordBlueprint>
                AcceptanceRecords { get; }
        }

        private sealed class FileSchema
        {
            public FileSchema(
                string identifierColumn,
                IEnumerable<string> fixedColumns,
                IEnumerable<string> dynamicPrefixes = null,
                IEnumerable<string> optionalColumns = null)
            {
                IdentifierColumn = identifierColumn;
                FixedColumns = fixedColumns.ToArray();
                DynamicPrefixes = (dynamicPrefixes ?? Array.Empty<string>())
                    .ToArray();
                OptionalColumns = (optionalColumns ?? Array.Empty<string>())
                    .ToArray();
            }

            public string IdentifierColumn { get; }
            public IReadOnlyList<string> FixedColumns { get; }
            public IReadOnlyList<string> DynamicPrefixes { get; }
            public IReadOnlyList<string> OptionalColumns { get; }

            public bool IsKnown(string header) =>
                FixedColumns.Contains(header, StringComparer.Ordinal)
                || OptionalColumns.Contains(header, StringComparer.Ordinal);

            public bool IsDynamic(string header) =>
                DynamicPrefixes.Any(prefix =>
                    header.StartsWith(prefix, StringComparison.Ordinal)
                    && header.Length > prefix.Length);
        }
    }
}
