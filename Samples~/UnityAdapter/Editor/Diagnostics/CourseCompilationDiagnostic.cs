namespace VirtualLab.Unity.Authoring.Diagnostics
{
    /// <summary>
    /// 诊断对应的编辑入口。文件、记录、列和动作均由诊断产生方明确给出，
    /// 工作台不需要从原因或建议文案中反向猜测定位信息。
    /// </summary>
    public sealed class CourseDiagnosticTarget
    {
        public CourseDiagnosticTarget(
            string fileName,
            string configurationId,
            string columnName,
            string actionId)
        {
            FileName = fileName ?? string.Empty;
            ConfigurationId = configurationId ?? string.Empty;
            ColumnName = columnName ?? string.Empty;
            ActionId = actionId ?? string.Empty;
        }

        public string FileName { get; }
        public string ConfigurationId { get; }
        public string ColumnName { get; }
        public string ActionId { get; }
    }

    /// <summary>
    /// 工作台能够执行的通用诊断动作。具体面板只负责解释这些动作，
    /// 验证器不引用 Unity 控件或窗口类型。
    /// </summary>
    public static class CourseDiagnosticActionIds
    {
        public const string LocateConfiguration = "定位配置";
        public const string SelectEntity = "选择实验对象";
        public const string SelectOperation = "选择操作";
        public const string SelectUnit = "选择单位";
        public const string SelectPresentation = "选择表现";
        public const string EditExperimentPrefab = "编辑实验总预制体";
    }

    public enum CourseDiagnosticSeverity
    {
        Error,
        Warning
    }

    public sealed class CourseCompilationDiagnostic
    {
        public CourseCompilationDiagnostic(
            string code,
            string fileName,
            int line,
            int column,
            string columnName,
            string configurationId,
            string reason,
            string suggestion)
            : this(
                code,
                fileName,
                line,
                column,
                columnName,
                configurationId,
                reason,
                suggestion,
                CourseDiagnosticSeverity.Error,
                null,
                null)
        {
        }

        public CourseCompilationDiagnostic(
            string code,
            string fileName,
            int line,
            int column,
            string columnName,
            string configurationId,
            string reason,
            string suggestion,
            CourseDiagnosticSeverity severity,
            ConfigurationProvenance provenance)
            : this(
                code,
                fileName,
                line,
                column,
                columnName,
                configurationId,
                reason,
                suggestion,
                severity,
                provenance,
                null)
        {
        }

        public CourseCompilationDiagnostic(
            string code,
            string fileName,
            int line,
            int column,
            string columnName,
            string configurationId,
            string reason,
            string suggestion,
            CourseDiagnosticSeverity severity,
            ConfigurationProvenance provenance,
            CourseDiagnosticTarget target)
        {
            Code = code;
            FileName = fileName;
            Line = line;
            Column = column;
            ColumnName = columnName ?? string.Empty;
            ConfigurationId = configurationId ?? string.Empty;
            Reason = reason;
            Suggestion = suggestion;
            Severity = severity;
            Provenance = provenance;
            Target = target ?? new CourseDiagnosticTarget(
                fileName,
                configurationId,
                columnName,
                CourseDiagnosticActionIds.LocateConfiguration);
        }

        public string Code { get; }
        public string FileName { get; }
        public int Line { get; }
        public int Column { get; }
        public string ColumnName { get; }
        public string ConfigurationId { get; }
        public string Reason { get; }
        public string Suggestion { get; }
        public CourseDiagnosticSeverity Severity { get; }
        public ConfigurationProvenance Provenance { get; }
        public CourseDiagnosticTarget Target { get; }
    }
}
