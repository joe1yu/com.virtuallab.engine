using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Diagnostics;
using Columns = VirtualLab.Unity.Authoring.Catalogs.CourseAuthoringCatalogCsvSchema.Columns;
using Values = VirtualLab.Unity.Authoring.Catalogs.CourseAuthoringCatalogCsvSchema.Values;

namespace VirtualLab.Unity.Authoring.Catalogs
{
    /// <summary>
    /// 集中定义模块创作目录的文件名和自然中文表头。
    /// 模块只需维护 CSV 数据，不需要在提供者中重复协议字符串。
    /// </summary>
    public static class CourseAuthoringCatalogCsvSchema
    {
        public const string CategoriesFile = "用品类别.csv";
        public const string TemplatesFile = "用品模板.csv";
        public const string TemplateComponentsFile = "模板组件.csv";
        public const string ComponentsFile = "能力组件.csv";
        public const string OperationsFile = "抽象操作.csv";
        public const string ProcessesFile = "科学过程.csv";
        public const string OptionsFile = "创作选项.csv";

        public static class Columns
        {
            public const string CategoryId = "类别标识";
            public const string TemplateId = "模板标识";
            public const string ComponentId = "组件标识";
            public const string OperationId = "操作标识";
            public const string ProcessId = "过程标识";
            public const string OptionKind = "选项类型";
            public const string OptionId = "选项标识";
            public const string DisplayName = "显示名称";
            public const string Description = "用途说明";
            public const string DisplayOrder = "显示顺序";
            public const string AvailableOperations = "可用操作";
            public const string SuggestedRoles = "建议角色";
            public const string SuggestedTags = "建议标签";
            public const string Parameters = "参数";
            public const string ParameterId = "参数标识";
            public const string ParameterName = "参数名称";
            public const string ParameterType = "参数类型";
            public const string IsRequired = "是否必填";
            public const string Unit = "单位";
            public const string Minimum = "最小值";
            public const string Maximum = "最大值";
            public const string DefaultValue = "默认值";
            public const string Choices = "可选值";
            public const string Lifecycle = "生命周期";
            public const string ExecutionMode = "执行方式";
            public const string StartAction = "开始动作";
            public const string ObservationAction = "观测动作";
            public const string CompletionAction = "完成动作";
            public const string CancellationAction = "取消动作";
        }

        public static class Values
        {
            public const string Yes = "是";
            public const string No = "否";
            public const string Instant = "即时";
            public const string Continuous = "持续";
            public const string Manipulation = "操纵";
            public const string Text = "文本";
            public const string Boolean = "布尔";
            public const string Integer = "整数";
            public const string Number = "数值";
            public const string Entity = "实体";
            public const string Port = "端口";
            public const string Choice = "选项";
            public const string RelationType = "关系类型";
            public const string FactField = "事实字段";
            public const string PresentationSignal = "表现信号";
            public const string ConsequenceTemplate = "后果模板";
        }

        public static readonly IReadOnlyList<string> CategoryHeaders =
            new[]
            {
                Columns.CategoryId, Columns.DisplayName, Columns.Description,
                Columns.DisplayOrder
            };

        public static readonly IReadOnlyList<string> TemplateHeaders =
            new[]
            {
                Columns.TemplateId, Columns.CategoryId, Columns.DisplayName,
                Columns.Description, Columns.DisplayOrder,
                Columns.AvailableOperations, Columns.SuggestedRoles,
                Columns.SuggestedTags
            };

        public static readonly IReadOnlyList<string> TemplateComponentHeaders =
            new[]
            {
                Columns.TemplateId, Columns.ComponentId, Columns.Parameters
            };

        public static readonly IReadOnlyList<string> ComponentHeaders =
            new[]
            {
                Columns.ComponentId, Columns.DisplayName, Columns.Description,
                Columns.ParameterId, Columns.ParameterName,
                Columns.ParameterType, Columns.IsRequired, Columns.Unit,
                Columns.Minimum, Columns.Maximum, Columns.DefaultValue,
                Columns.Choices
            };

        public static readonly IReadOnlyList<string> OperationHeaders =
            new[]
            {
                Columns.OperationId, Columns.DisplayName, Columns.Description,
                Columns.Lifecycle, Columns.ExecutionMode, Columns.StartAction,
                Columns.ObservationAction, Columns.CompletionAction,
                Columns.CancellationAction
            };

        public static readonly IReadOnlyList<string> ProcessHeaders =
            new[]
            {
                Columns.ProcessId, Columns.DisplayName, Columns.Description,
                Columns.ParameterId, Columns.ParameterName,
                Columns.ParameterType, Columns.IsRequired, Columns.Unit,
                Columns.Minimum, Columns.Maximum, Columns.DefaultValue,
                Columns.Choices
            };

        public static readonly IReadOnlyList<string> OptionHeaders =
            new[]
            {
                Columns.OptionKind, Columns.OptionId, Columns.DisplayName,
                Columns.Description
            };
    }

    public sealed class CourseAuthoringCatalogCsvLoadResult
    {
        internal CourseAuthoringCatalogCsvLoadResult(
            CourseAuthoringModuleDescriptor module,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Module = module;
            Diagnostics = (diagnostics
                           ?? Array.Empty<CourseCompilationDiagnostic>())
                .ToArray();
        }

        public CourseAuthoringModuleDescriptor Module { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
        public bool IsSuccess =>
            Module != null
            && Diagnostics.All(value =>
                value.Severity != CourseDiagnosticSeverity.Error);
    }

    /// <summary>
    /// 从模块自有目录加载创作描述。只接受固定的自然中文表头和值，
    /// 不提供英文别名或字段猜测，避免课程配置形成第二套隐式协议。
    /// </summary>
    public sealed class CourseAuthoringCatalogCsvLoader
    {
        private readonly StrictCsvReader _reader = new StrictCsvReader();

        public CourseAuthoringCatalogCsvLoadResult Read(
            string packageId,
            string directory)
        {
            var diagnostics = new List<CourseCompilationDiagnostic>();
            if (string.IsNullOrWhiteSpace(packageId))
            {
                diagnostics.Add(Diagnostic(
                    "catalog.package-id.missing",
                    string.Empty,
                    1,
                    string.Empty,
                    "模块包标识不能为空。",
                    "为目录提供者填写稳定且唯一的中文包标识。"));
            }

            if (string.IsNullOrWhiteSpace(directory)
                || !Directory.Exists(directory))
            {
                diagnostics.Add(Diagnostic(
                    "catalog.directory.missing",
                    directory ?? string.Empty,
                    1,
                    packageId,
                    $"创作目录“{directory}”不存在。",
                    "确认模块 Sample 已导入，并将七张目录表放入 Catalogs 目录。"));
                return new CourseAuthoringCatalogCsvLoadResult(
                    null,
                    diagnostics);
            }

            var categoriesTable = ReadTable(
                directory,
                CourseAuthoringCatalogCsvSchema.CategoriesFile,
                CourseAuthoringCatalogCsvSchema.CategoryHeaders,
                diagnostics);
            var templatesTable = ReadTable(
                directory,
                CourseAuthoringCatalogCsvSchema.TemplatesFile,
                CourseAuthoringCatalogCsvSchema.TemplateHeaders,
                diagnostics);
            var templateComponentsTable = ReadTable(
                directory,
                CourseAuthoringCatalogCsvSchema.TemplateComponentsFile,
                CourseAuthoringCatalogCsvSchema.TemplateComponentHeaders,
                diagnostics);
            var componentsTable = ReadTable(
                directory,
                CourseAuthoringCatalogCsvSchema.ComponentsFile,
                CourseAuthoringCatalogCsvSchema.ComponentHeaders,
                diagnostics);
            var operationsTable = ReadTable(
                directory,
                CourseAuthoringCatalogCsvSchema.OperationsFile,
                CourseAuthoringCatalogCsvSchema.OperationHeaders,
                diagnostics);
            var processesTable = ReadTable(
                directory,
                CourseAuthoringCatalogCsvSchema.ProcessesFile,
                CourseAuthoringCatalogCsvSchema.ProcessHeaders,
                diagnostics);
            var optionsTable = ReadTable(
                directory,
                CourseAuthoringCatalogCsvSchema.OptionsFile,
                CourseAuthoringCatalogCsvSchema.OptionHeaders,
                diagnostics);

            if (diagnostics.Any(value =>
                    value.Severity == CourseDiagnosticSeverity.Error))
            {
                return new CourseAuthoringCatalogCsvLoadResult(
                    null,
                    diagnostics);
            }

            var categories = ParseCategories(categoriesTable, diagnostics);
            var components = ParseComponents(componentsTable, diagnostics);
            var operations = ParseOperations(operationsTable, diagnostics);
            var processes = ParseProcesses(processesTable, diagnostics);
            var options = ParseOptions(optionsTable, diagnostics);
            var templates = ParseTemplates(
                templatesTable,
                templateComponentsTable,
                diagnostics);

            if (diagnostics.Any(value =>
                    value.Severity == CourseDiagnosticSeverity.Error))
            {
                return new CourseAuthoringCatalogCsvLoadResult(
                    null,
                    diagnostics);
            }

            return new CourseAuthoringCatalogCsvLoadResult(
                new CourseAuthoringModuleDescriptor(
                    packageId,
                    categories,
                    components,
                    templates,
                    operations,
                    processes,
                    options),
                diagnostics);
        }

        public CourseAuthoringModuleDescriptor LoadRequired(
            string packageId,
            string directory)
        {
            var result = Read(packageId, directory);
            if (result.IsSuccess)
            {
                return result.Module;
            }

            throw new InvalidDataException(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(value =>
                    $"{value.FileName}({value.Line}): {value.Reason} "
                    + value.Suggestion)));
        }

        private StrictCsvReadResult ReadTable(
            string directory,
            string fileName,
            IReadOnlyList<string> expectedHeaders,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                diagnostics.Add(Diagnostic(
                    "catalog.file.missing",
                    fileName,
                    1,
                    string.Empty,
                    $"缺少创作目录表“{fileName}”。",
                    $"在 Catalogs 目录创建“{fileName}”，并使用规定的中文表头。"));
                return EmptyTable();
            }

            var result = _reader.ReadBytes(fileName, File.ReadAllBytes(path));
            foreach (var diagnostic in result.Diagnostics)
            {
                diagnostics.Add(diagnostic);
            }

            if (!result.ConfiguredHeaders.SequenceEqual(
                    expectedHeaders,
                    StringComparer.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    "catalog.header.invalid",
                    fileName,
                    1,
                    string.Empty,
                    "表头与该目录表的固定结构不一致。当前为：“"
                    + string.Join(",", result.ConfiguredHeaders)
                    + "”。",
                    "将第一行改为：“"
                    + string.Join(",", expectedHeaders)
                    + "”。"));
            }

            return result;
        }

        private static StrictCsvReadResult EmptyTable() =>
            new StrictCsvReader().Read("空目录.csv", string.Empty);

        private static IReadOnlyList<AuthoringCategoryDescriptor>
            ParseCategories(
                StrictCsvReadResult table,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            return table.Rows.Select(row =>
                new AuthoringCategoryDescriptor(
                    Required(row, Columns.CategoryId, table, diagnostics),
                    Required(row, Columns.DisplayName, table, diagnostics),
                    Value(row, Columns.Description),
                    Integer(row, Columns.DisplayOrder, table, diagnostics)))
                .OrderBy(value => value.DisplayOrder)
                .ThenBy(value => value.CategoryId, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<AuthoringComponentDescriptor>
            ParseComponents(
                StrictCsvReadResult table,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            return table.Rows
                .GroupBy(
                    row => Value(row, Columns.ComponentId),
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    var parameters = group
                        .Where(row => !string.IsNullOrWhiteSpace(
                            Value(row, Columns.ParameterId)))
                        .Select(row => ParseParameter(row, table, diagnostics))
                        .ToArray();
                    return new AuthoringComponentDescriptor(
                        Required(first, Columns.ComponentId, table, diagnostics),
                        Required(first, Columns.DisplayName, table, diagnostics),
                        Value(first, Columns.Description),
                        parameters,
                        Array.Empty<AuthoringPortDescriptor>());
                })
                .OrderBy(value => value.ComponentId, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<AuthoringOperationDescriptor>
            ParseOperations(
                StrictCsvReadResult table,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            return table.Rows.Select(row =>
                new AuthoringOperationDescriptor(
                    Required(row, Columns.OperationId, table, diagnostics),
                    Required(row, Columns.DisplayName, table, diagnostics),
                    Value(row, Columns.Description),
                    Lifecycle(row, table, diagnostics),
                    Required(row, Columns.ExecutionMode, table, diagnostics),
                    Value(row, Columns.StartAction),
                    Value(row, Columns.ObservationAction),
                    Value(row, Columns.CompletionAction),
                    Value(row, Columns.CancellationAction)))
                .OrderBy(value => value.OperationId, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<AuthoringProcessDescriptor>
            ParseProcesses(
                StrictCsvReadResult table,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            return table.Rows
                .GroupBy(
                    row => Value(row, Columns.ProcessId),
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    var parameters = group
                        .Where(row => !string.IsNullOrWhiteSpace(
                            Value(row, Columns.ParameterId)))
                        .Select(row => ParseParameter(row, table, diagnostics))
                        .ToArray();
                    return new AuthoringProcessDescriptor(
                        Required(first, Columns.ProcessId, table, diagnostics),
                        Required(first, Columns.DisplayName, table, diagnostics),
                        Value(first, Columns.Description),
                        parameters);
                })
                .OrderBy(value => value.ProcessId, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<AuthoringOptionDescriptor> ParseOptions(
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            return table.Rows.Select(row =>
                new AuthoringOptionDescriptor(
                    OptionKind(row, table, diagnostics),
                    Required(row, Columns.OptionId, table, diagnostics),
                    Required(row, Columns.DisplayName, table, diagnostics),
                    Value(row, Columns.Description)))
                .OrderBy(value => value.Kind)
                .ThenBy(value => value.OptionId, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<AuthoringItemTemplateDescriptor>
            ParseTemplates(
                StrictCsvReadResult templatesTable,
                StrictCsvReadResult componentsTable,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var bindings = componentsTable.Rows
                .GroupBy(
                    row => Value(row, Columns.TemplateId),
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.Ordinal);

            return templatesTable.Rows.Select(row =>
                {
                    var templateId = Required(
                        row,
                        Columns.TemplateId,
                        templatesTable,
                        diagnostics);
                    var componentRows = bindings.TryGetValue(
                            templateId,
                            out var found)
                        ? found
                        : Array.Empty<StrictCsvRow>();
                    var parameters = ParseTemplateParameters(
                        componentRows,
                        componentsTable,
                        diagnostics);
                    return new AuthoringItemTemplateDescriptor(
                        templateId,
                        Required(
                            row,
                            Columns.CategoryId,
                            templatesTable,
                            diagnostics),
                        Required(
                            row,
                            Columns.DisplayName,
                            templatesTable,
                            diagnostics),
                        Value(row, Columns.Description),
                        Integer(
                            row,
                            Columns.DisplayOrder,
                            templatesTable,
                            diagnostics),
                        componentRows.Select(value =>
                            Required(
                                value,
                                Columns.ComponentId,
                                componentsTable,
                                diagnostics)),
                        parameters,
                        List(Value(row, Columns.AvailableOperations)),
                        List(Value(row, Columns.SuggestedRoles)),
                        List(Value(row, Columns.SuggestedTags)));
                })
                .OrderBy(value => value.DisplayOrder)
                .ThenBy(value => value.TemplateId, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<KeyValuePair<string, string>>
            ParseTemplateParameters(
                IEnumerable<StrictCsvRow> rows,
                StrictCsvReadResult table,
                ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                foreach (var pair in ParameterPairs(
                             Value(row, Columns.Parameters),
                             row,
                             table,
                             diagnostics))
                {
                    if (result.ContainsKey(pair.Key))
                    {
                        diagnostics.Add(Diagnostic(
                            "catalog.template-parameter.duplicate",
                            FileName(table),
                            row.LineNumber,
                            Value(row, Columns.TemplateId),
                            $"模板默认参数“{pair.Key}”被重复设置。",
                            "每个模板参数只保留一个值。",
                            Columns.Parameters));
                        continue;
                    }

                    result.Add(pair.Key, pair.Value);
                }
            }

            return result.ToArray();
        }

        private static AuthoringParameterDescriptor ParseParameter(
            StrictCsvRow row,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var minimum = NullableNumber(
                row,
                Columns.Minimum,
                table,
                diagnostics);
            var maximum = NullableNumber(
                row,
                Columns.Maximum,
                table,
                diagnostics);
            if (minimum.HasValue
                && maximum.HasValue
                && minimum.Value > maximum.Value)
            {
                diagnostics.Add(Diagnostic(
                    "catalog.parameter.range.invalid",
                    FileName(table),
                    row.LineNumber,
                    Value(row, table.ConfiguredHeaders[0]),
                    "参数最小值不能大于最大值。",
                    "调换最小值和最大值，或修正其中一个边界。",
                    Columns.Minimum));
            }

            return new AuthoringParameterDescriptor(
                Required(row, Columns.ParameterId, table, diagnostics),
                Required(row, Columns.ParameterName, table, diagnostics),
                string.Empty,
                ParameterType(row, table, diagnostics),
                Boolean(row, Columns.IsRequired, table, diagnostics),
                Value(row, Columns.Unit),
                minimum,
                maximum,
                Value(row, Columns.DefaultValue),
                List(Value(row, Columns.Choices)));
        }

        private static IEnumerable<KeyValuePair<string, string>> ParameterPairs(
            string text,
            StrictCsvRow row,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var item in List(text))
            {
                var separator = item.IndexOf('=');
                if (separator <= 0)
                {
                    diagnostics.Add(Diagnostic(
                        "catalog.parameters.invalid",
                        FileName(table),
                        row.LineNumber,
                        Value(row, table.ConfiguredHeaders[0]),
                        $"参数项“{item}”不是“名称=值”格式。",
                        "使用分号分隔参数，并为每项填写“名称=值”。",
                        Columns.Parameters));
                    continue;
                }

                yield return new KeyValuePair<string, string>(
                    item.Substring(0, separator).Trim(),
                    item.Substring(separator + 1).Trim());
            }
        }

        private static string Required(
            StrictCsvRow row,
            string header,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var value = Value(row, header);
            if (value.Length == 0)
            {
                diagnostics.Add(Diagnostic(
                    "catalog.value.required",
                    FileName(table),
                    row.LineNumber,
                    Value(row, table.ConfiguredHeaders[0]),
                    $"“{header}”不能为空。",
                    $"填写明确的“{header}”。",
                    header));
            }

            return value;
        }

        private static int Integer(
            StrictCsvRow row,
            string header,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var value = Value(row, header);
            if (int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }

            diagnostics.Add(Diagnostic(
                "catalog.integer.invalid",
                FileName(table),
                row.LineNumber,
                Value(row, table.ConfiguredHeaders[0]),
                $"“{value}”不是有效整数。",
                $"在“{header}”中填写不带单位的整数。",
                header));
            return 0;
        }

        private static double? NullableNumber(
            StrictCsvRow row,
            string header,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var value = Value(row, header);
            if (value.Length == 0)
            {
                return null;
            }

            if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }

            diagnostics.Add(Diagnostic(
                "catalog.number.invalid",
                FileName(table),
                row.LineNumber,
                Value(row, table.ConfiguredHeaders[0]),
                $"“{value}”不是有效数值。",
                $"在“{header}”中使用小数点表示数值，不要附加单位。",
                header));
            return null;
        }

        private static bool Boolean(
            StrictCsvRow row,
            string header,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var value = Value(row, header);
            if (value == Values.Yes)
            {
                return true;
            }

            if (value == Values.No)
            {
                return false;
            }

            diagnostics.Add(Diagnostic(
                "catalog.boolean.invalid",
                FileName(table),
                row.LineNumber,
                Value(row, table.ConfiguredHeaders[0]),
                $"“{value}”不是有效的是非值。",
                $"在“{header}”中只填写“是”或“否”。",
                header));
            return false;
        }

        private static AuthoringParameterType ParameterType(
            StrictCsvRow row,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var value = Value(row, Columns.ParameterType);
            switch (value)
            {
                case Values.Text: return AuthoringParameterType.Text;
                case Values.Boolean: return AuthoringParameterType.Boolean;
                case Values.Integer: return AuthoringParameterType.Integer;
                case Values.Number: return AuthoringParameterType.Number;
                case Values.Entity: return AuthoringParameterType.Entity;
                case Values.Port: return AuthoringParameterType.Port;
                case Values.Choice: return AuthoringParameterType.Choice;
                default:
                    diagnostics.Add(Diagnostic(
                        "catalog.parameter-type.invalid",
                        FileName(table),
                        row.LineNumber,
                        Value(row, table.ConfiguredHeaders[0]),
                        $"参数类型“{value}”未注册。",
                        "只填写：文本、布尔、整数、数值、实体、端口或选项。",
                        Columns.ParameterType));
                    return AuthoringParameterType.Text;
            }
        }

        private static AuthoringOperationLifecycle Lifecycle(
            StrictCsvRow row,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var value = Value(row, Columns.Lifecycle);
            switch (value)
            {
                case Values.Instant: return AuthoringOperationLifecycle.Instant;
                case Values.Continuous:
                    return AuthoringOperationLifecycle.Continuous;
                case Values.Manipulation:
                    return AuthoringOperationLifecycle.Manipulation;
                default:
                    diagnostics.Add(Diagnostic(
                        "catalog.lifecycle.invalid",
                        FileName(table),
                        row.LineNumber,
                        Value(row, Columns.OperationId),
                        $"生命周期“{value}”未注册。",
                        "只填写“即时”“持续”或“操纵”。",
                        Columns.Lifecycle));
                    return AuthoringOperationLifecycle.Instant;
            }
        }

        private static AuthoringOptionKind OptionKind(
            StrictCsvRow row,
            StrictCsvReadResult table,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var value = Value(row, Columns.OptionKind);
            switch (value)
            {
                case Values.RelationType:
                    return AuthoringOptionKind.RelationType;
                case Values.FactField:
                    return AuthoringOptionKind.FactField;
                case Values.PresentationSignal:
                    return AuthoringOptionKind.PresentationSignal;
                case Values.ConsequenceTemplate:
                    return AuthoringOptionKind.ConsequenceTemplate;
                default:
                    diagnostics.Add(Diagnostic(
                        "catalog.option-kind.invalid",
                        FileName(table),
                        row.LineNumber,
                        Value(row, Columns.OptionId),
                        $"选项类型“{value}”未注册。",
                        "只填写：关系类型、事实字段、表现信号或后果模板。",
                        Columns.OptionKind));
                    return AuthoringOptionKind.RelationType;
            }
        }

        private static string Value(StrictCsvRow row, string naturalHeader) =>
            row.ConfiguredValue(naturalHeader).Trim();

        private static IReadOnlyList<string> List(string value) =>
            (value ?? string.Empty)
            .Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .ToArray();

        private static string FileName(StrictCsvReadResult table) =>
            table.FileName;

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            string fileName,
            int line,
            string configurationId,
            string reason,
            string suggestion,
            string columnName = "") =>
            new CourseCompilationDiagnostic(
                code,
                fileName,
                line,
                1,
                columnName,
                configurationId,
                reason,
                suggestion);
    }
}
