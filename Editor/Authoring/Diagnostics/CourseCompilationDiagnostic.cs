namespace VirtualLab.Unity.Authoring.Diagnostics
{
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
    }
}
