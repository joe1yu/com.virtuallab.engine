using System;

namespace VirtualLab.Unity.Authoring
{
    public enum ValidationSeverity
    {
        Warning,
        Error
    }

    public sealed class ValidationIssue
    {
        public ValidationIssue(
            string code,
            string assetPath,
            string fieldPath,
            string message,
            ValidationSeverity severity)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            AssetPath = assetPath ?? throw new ArgumentNullException(nameof(assetPath));
            FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Severity = severity;
        }

        public string Code { get; }
        public string AssetPath { get; }
        public string FieldPath { get; }
        public string Message { get; }
        public ValidationSeverity Severity { get; }
    }
}
