using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Csv;

namespace VirtualLab.Chemistry.Authoring
{
    /// <summary>
    /// 把课程作者选择的中文过程类型投影为化学配方编译记录。
    /// 映射保存在模块目录中，课程表不暴露配方局部项和状态操作协议。
    /// </summary>
    internal sealed class ChemistryAuthoringProcessProjector
    {
        private const string FileName = "过程编译规则.csv";
        private static readonly string[] Headers =
        {
            "过程类型", "转换类型", "学科配方", "配方局部项", "状态操作", "物质参数"
        };
        private readonly IReadOnlyDictionary<string, ProjectionRule> _rules;

        public ChemistryAuthoringProcessProjector()
        {
            var path = Path.Combine(
                ChemistrySamplePaths.Root,
                "Editor",
                "Authoring",
                "Catalogs",
                FileName);
            var table = new StrictCsvReader().ReadBytes(
                FileName,
                File.ReadAllBytes(path));
            if (table.Diagnostics.Count > 0)
            {
                throw new InvalidDataException(
                    "化学过程编译规则无效：" + string.Join(
                        "；",
                        table.Diagnostics.Select(value => value.Reason)));
            }

            if (!table.ConfiguredHeaders.SequenceEqual(
                    Headers,
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"{FileName} 表头必须是：{string.Join("，", Headers)}。");
            }

            var rules = table.Rows.Select(row => new ProjectionRule(
                    Value(row, "过程类型"),
                    Value(row, "转换类型"),
                    Value(row, "学科配方"),
                    Value(row, "配方局部项"),
                    Value(row, "状态操作"),
                    Value(row, "物质参数")))
                .ToArray();
            var duplicate = rules.GroupBy(value => value.ProcessType,
                    StringComparer.Ordinal)
                .FirstOrDefault(value => value.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidDataException(
                    $"化学过程类型“{duplicate.Key}”存在多条编译规则。");
            }

            _rules = rules.ToDictionary(
                value => value.ProcessType,
                StringComparer.Ordinal);
        }

        public IReadOnlyList<CourseDisciplineProcessBlueprint> Project(
            IEnumerable<CourseDisciplineProcessBlueprint> records)
        {
            return (records
                    ?? throw new ArgumentNullException(nameof(records)))
                .Select(Project)
                .ToArray();
        }

        private CourseDisciplineProcessBlueprint Project(
            CourseDisciplineProcessBlueprint record)
        {
            if (record.RecordType != "开始"
                || !_rules.TryGetValue(record.DisciplineRecipeId, out var rule))
            {
                return record;
            }

            var parameters = record.Parameters;
            var subject = record.SubjectEntityId;
            var source = string.IsNullOrWhiteSpace(record.SourceEntityId)
                ? subject
                : record.SourceEntityId;
            var target = record.TargetEntityId;
            var resultLocalKey = string.Empty;
            var operationLocalKey = string.Empty;
            var protocolOperation = string.Empty;

            switch (rule.ProjectionType)
            {
                case "初始物质":
                    source = RequiredParameter(record, rule.SubstanceParameter);
                    break;
                case "过程参数":
                    subject = rule.RecipeLocalKey;
                    break;
                case "过程操作":
                    subject = rule.RecipeLocalKey;
                    resultLocalKey = record.DefinitionId;
                    operationLocalKey = rule.StateOperation;
                    break;
                default:
                    throw new InvalidDataException(
                        $"化学过程“{rule.ProcessType}”使用了未知转换类型“{rule.ProjectionType}”。");
            }

            return new CourseDisciplineProcessBlueprint(
                Values(record,
                    Pair("定义ID", record.DefinitionId),
                    Pair("记录类型", rule.ProjectionType),
                    Pair("学科配方", rule.RecipeId),
                    Pair("主体", subject),
                    Pair("来源", source),
                    Pair("目标", target),
                    Pair("启动交互", resultLocalKey),
                    Pair("停止交互", operationLocalKey),
                    Pair("操作协议", protocolOperation)),
                record.Source,
                parameters);
        }

        private static string RequiredParameter(
            CourseDisciplineProcessBlueprint record,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)
                || (!record.Parameters.TryGetValue(parameterName, out var value)
                    && !record.Parameters.TryGetValue(
                        "参数." + parameterName,
                        out value))
                || string.IsNullOrWhiteSpace(value.RawValue))
            {
                throw new InvalidDataException(
                    $"化学过程“{record.DefinitionId}”缺少参数“{parameterName}”。");
            }

            return value.RawValue.Trim();
        }

        private static IReadOnlyDictionary<string, BlueprintValue> Values(
            CourseDisciplineProcessBlueprint record,
            params KeyValuePair<string, string>[] values) =>
            new ReadOnlyDictionary<string, BlueprintValue>(
                values.ToDictionary(
                    value => value.Key,
                    value => new BlueprintValue(
                        value.Key,
                        value.Value,
                        record.Source),
                    StringComparer.Ordinal));

        private static KeyValuePair<string, string> Pair(
            string key,
            string value) => new KeyValuePair<string, string>(
            key,
            value ?? string.Empty);

        private static string Value(StrictCsvRow row, string name)
        {
            var value = row.ConfiguredValue(name).Trim();
            if (value.Length == 0
                && name != "配方局部项"
                && name != "状态操作"
                && name != "物质参数")
            {
                throw new InvalidDataException(
                    $"{FileName} 第 {row.LineNumber} 行缺少“{name}”。");
            }

            return value;
        }

        private sealed class ProjectionRule
        {
            public ProjectionRule(
                string processType,
                string projectionType,
                string recipeId,
                string recipeLocalKey,
                string stateOperation,
                string substanceParameter)
            {
                ProcessType = processType;
                ProjectionType = projectionType;
                RecipeId = recipeId;
                RecipeLocalKey = recipeLocalKey;
                StateOperation = stateOperation;
                SubstanceParameter = substanceParameter;
            }

            public string ProcessType { get; }
            public string ProjectionType { get; }
            public string RecipeId { get; }
            public string RecipeLocalKey { get; }
            public string StateOperation { get; }
            public string SubstanceParameter { get; }
        }
    }
}
