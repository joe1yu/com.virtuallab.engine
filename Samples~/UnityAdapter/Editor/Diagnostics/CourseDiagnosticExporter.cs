using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Blueprints;

namespace VirtualLab.Unity.Authoring.Diagnostics
{
    /// <summary>
    /// 把规范化内存模型导出为便于核对的只读文件。
    /// 导出内容不参与课程编译，避免形成第二套事实来源。
    /// </summary>
    public sealed class CourseDiagnosticExporter
    {
        private const string ReadOnlyMarker =
            "只读诊断，不是课程编译输入";

        public void Export(
            string diagnosticsDirectory,
            CourseBlueprintCompilationResult compilation)
        {
            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (!compilation.IsSuccess || compilation.Normalized == null)
            {
                throw new ArgumentException(
                    "只有成功的蓝图编译结果可以导出诊断。",
                    nameof(compilation));
            }

            var target = ValidateDirectory(diagnosticsDirectory);
            var parent = Path.GetDirectoryName(target);
            Directory.CreateDirectory(parent);
            var temporary = target + ".tmp-"
                + Guid.NewGuid().ToString("N");
            var backup = target + ".backup-"
                + Guid.NewGuid().ToString("N");

            try
            {
                Directory.CreateDirectory(temporary);
                Write(temporary, "动作策略.csv", ActionPolicies(compilation));
                Write(temporary, "条件组.csv", Conditions(compilation));
                Write(temporary, "状态变化.csv", StateChanges(compilation));
                Write(
                    temporary,
                    "表现状态.csv",
                    PresentationStates(compilation));
                Write(temporary, "来源链.csv", Provenance(compilation));
                Write(
                    temporary,
                    "编译报告.txt",
                    CompilationReport(compilation));

                if (Directory.Exists(target))
                {
                    Directory.Move(target, backup);
                }

                try
                {
                    Directory.Move(temporary, target);
                }
                catch
                {
                    if (Directory.Exists(backup)
                        && !Directory.Exists(target))
                    {
                        Directory.Move(backup, target);
                    }

                    throw;
                }

                if (Directory.Exists(backup))
                {
                    Directory.Delete(backup, true);
                }
            }
            finally
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, true);
                }

                if (Directory.Exists(backup)
                    && !Directory.Exists(target))
                {
                    Directory.Move(backup, target);
                }
            }
        }

        private static IEnumerable<string> ActionPolicies(
            CourseBlueprintCompilationResult compilation)
        {
            yield return Csv(
                "策略ID",
                "语义动作",
                "抽象操作",
                "生命周期",
                "执行方式",
                "阶段",
                "源实体",
                "目标实体",
                "裁决",
                "优先级",
                "条件ID",
                "结果组ID",
                "表现组ID");
            foreach (var item in compilation.Normalized.Actions)
            {
                var value = item.Definition;
                yield return Csv(
                    value.PolicyId,
                    value.ActionId,
                    value.OperationId,
                    Lifecycle(value.Lifecycle),
                    value.ExecutionModeId,
                    Phase(value.Phase),
                    value.SourceEntityId,
                    value.TargetEntityId,
                    value.PolicyEffect,
                    value.Priority.ToString(CultureInfo.InvariantCulture),
                    string.Join("|", value.RuleIds),
                    value.ResultGroupId,
                    value.PresentationGroupId);
            }
        }

        private static string Lifecycle(SemanticActionLifecycle value) =>
            value switch
            {
                SemanticActionLifecycle.Instant => "即时",
                SemanticActionLifecycle.Continuous => "持续",
                SemanticActionLifecycle.Manipulation => "操纵",
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        private static string Phase(SemanticActionPhase value) =>
            value switch
            {
                SemanticActionPhase.Start => "开始",
                SemanticActionPhase.Observe => "观测",
                SemanticActionPhase.Complete => "完成",
                SemanticActionPhase.Cancel => "取消",
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        private static IEnumerable<string> Conditions(
            CourseBlueprintCompilationResult compilation)
        {
            yield return Csv(
                "条件ID",
                "事实字段",
                "操作符",
                "期望值",
                "单位");
            foreach (var item in compilation.Normalized.Rules)
            {
                var value = item.Definition;
                yield return Csv(
                    value.RuleId,
                    value.FieldId,
                    value.OperatorId,
                    value.ExpectedValue,
                    value.UnitId);
            }
        }

        private static IEnumerable<string> StateChanges(
            CourseBlueprintCompilationResult compilation)
        {
            yield return Csv("状态变化ID", "操作协议", "参数");
            foreach (var item in compilation.Normalized.StateChanges)
            {
                var value = item.Definition;
                yield return Csv(
                    value.MutationId,
                    value.OperationId,
                    string.Join(
                        "|",
                        value.Parameters.Select(parameter =>
                            parameter.Key + "=" + parameter.Value)));
            }
        }

        private static IEnumerable<string> PresentationStates(
            CourseBlueprintCompilationResult compilation)
        {
            yield return Csv(
                "类型",
                "标识",
                "主体",
                "协议",
                "条件或参数");
            foreach (var item in compilation.Normalized.PresentationStates)
            {
                var value = item.Definition;
                yield return Csv(
                    "表现状态",
                    value.StateId,
                    value.SubjectEntityId,
                    string.Empty,
                    string.Join("|", value.RuleIds));
            }

            foreach (var item in compilation.Normalized.PresentationEffects)
            {
                var value = item.Definition;
                yield return Csv(
                    "表现效果",
                    value.EffectId,
                    value.SubjectId,
                    value.ProtocolId,
                    string.Join(
                        "|",
                        value.ParameterValues.Select(parameter =>
                            parameter.Key + "="
                            + StructuredValueText(parameter.Value))));
            }
        }

        private static IEnumerable<string> Provenance(
            CourseBlueprintCompilationResult compilation)
        {
            yield return Csv(
                "生成项ID",
                "来源层",
                "配方包",
                "文件",
                "行",
                "列",
                "原配置ID",
                "覆盖操作");
            foreach (var pair in compilation.ProvenanceByGeneratedItemId
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                foreach (var source in pair.Value.Sources)
                {
                    yield return Csv(
                        pair.Key,
                        LayerText(source.Layer),
                        source.PackageId,
                        source.FileName,
                        source.Line.ToString(CultureInfo.InvariantCulture),
                        source.Column.ToString(CultureInfo.InvariantCulture),
                        source.ConfigurationId,
                        pair.Value.OverrideOperation);
                }
            }
        }

        private static IEnumerable<string> CompilationReport(
            CourseBlueprintCompilationResult compilation)
        {
            var summary = compilation.Summary;
            yield return $"课程：{compilation.Domain.CourseId}";
            yield return $"实验对象：{summary.ObjectCount}";
            yield return $"平台配方：{summary.PlatformRecipeCount}";
            yield return $"学科配方：{summary.DisciplineRecipeCount}";
            yield return $"自动交互：{summary.AutomaticInteractionCount}";
            yield return $"特殊交互：{summary.SpecialInteractionCount}";
            yield return $"课程覆盖：{summary.OverrideCount}";
            yield return $"警告：{summary.WarningCount}";
            yield return $"错误：{summary.ErrorCount}";
            yield return "编译阶段："
                + string.Join(" → ", compilation.Stages);
            foreach (var diagnostic in compilation.Diagnostics)
            {
                yield return string.Join(
                    " | ",
                    diagnostic.Severity,
                    diagnostic.Code,
                    diagnostic.FileName + ":" + diagnostic.Line,
                    diagnostic.Reason,
                    diagnostic.Suggestion);
            }
        }

        private static void Write(
            string directory,
            string fileName,
            IEnumerable<string> lines)
        {
            var content = new[] { ReadOnlyMarker }
                .Concat(lines ?? Array.Empty<string>());
            File.WriteAllText(
                Path.Combine(directory, fileName),
                string.Join(Environment.NewLine, content)
                + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static string Csv(params string[] values) =>
            string.Join(",", values.Select(EscapeCsv));

        private static string EscapeCsv(string value)
        {
            var text = value ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string StructuredValueText(StructuredValue value)
        {
            switch (value.Kind)
            {
                case StructuredValueKind.Null:
                    return string.Empty;
                case StructuredValueKind.Boolean:
                    return value.Boolean ? "是" : "否";
                case StructuredValueKind.Number:
                    return value.Number.ToString(
                        CultureInfo.InvariantCulture);
                case StructuredValueKind.Text:
                    return value.Text ?? string.Empty;
                case StructuredValueKind.TextList:
                    return string.Join("|", value.TextList);
                default:
                    return string.Empty;
            }
        }

        private static string LayerText(ConfigurationLayer layer)
        {
            switch (layer)
            {
                case ConfigurationLayer.Platform:
                    return "平台";
                case ConfigurationLayer.Discipline:
                    return "学科";
                case ConfigurationLayer.Course:
                    return "课程";
                case ConfigurationLayer.Override:
                    return "覆盖";
                default:
                    return layer.ToString();
            }
        }

        private static string ValidateDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException(
                    "诊断目录不能为空。",
                    nameof(directory));
            }

            var fullPath = Path.GetFullPath(directory);
            if (string.Equals(
                    fullPath,
                    Path.GetPathRoot(fullPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "诊断目录不能是磁盘根目录。",
                    nameof(directory));
            }

            return fullPath;
        }
    }
}
