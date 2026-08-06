using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Chemistry.Authoring
{
    /// <summary>
    /// 化学学科包维护的可复用知识。课程仍可用学科配置中的同 ID 记录覆盖或补充，
    /// 因此把常见物质和反应上移不会削弱课程扩展能力。
    /// </summary>
    internal sealed class ChemistryKnowledgeCatalog
    {
        public ChemistryKnowledgeCatalog(
            IEnumerable<CourseDisciplineProcessBlueprint> records)
        {
            Records = (records
                       ?? Array.Empty<CourseDisciplineProcessBlueprint>())
                .ToArray();
        }

        public IReadOnlyList<CourseDisciplineProcessBlueprint> Records { get; }
    }

    internal static class ChemistryKnowledgeCatalogLoader
    {
        private static readonly string[] RequiredColumns =
        {
            "知识ID", "类型", "主体", "来源", "目标", "参数"
        };

        public static ChemistryKnowledgeCatalog Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("化学知识表路径不能为空。", nameof(path));
            }

            var table = new StrictCsvReader().ReadBytes(
                "化学知识.csv",
                File.ReadAllBytes(path));
            if (table.Diagnostics.Count > 0)
            {
                throw new InvalidDataException(string.Join(
                    Environment.NewLine,
                    table.Diagnostics.Select(value => value.Reason)));
            }

            var missing = RequiredColumns
                .Where(column => !table.Headers.Contains(
                    column,
                    StringComparer.Ordinal))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidDataException(
                    "化学知识.csv 缺少列：" + string.Join("、", missing));
            }

            var records = new List<CourseDisciplineProcessBlueprint>();
            foreach (var row in table.Rows)
            {
                var id = row["知识ID"].Trim();
                var configuredType = row["类型"].Trim();
                var role = configuredType == "反应物"
                           || configuredType == "产物"
                    ? configuredType
                    : string.Empty;
                var recordType = string.IsNullOrEmpty(role)
                    ? configuredType
                    : "反应项";
                if (recordType != "物质"
                    && recordType != "反应"
                    && recordType != "反应项")
                {
                    throw new InvalidDataException(
                        $"化学知识“{id}”使用了未知类型“{configuredType}”。");
                }

                var source = new ConfigurationSource(
                    ConfigurationLayer.Discipline,
                    "化学基础",
                    "化学知识.csv",
                    row.LineNumber,
                    1,
                    id);
                var parameters = ParseParameters(row["参数"], source, id);
                var values = new[]
                {
                    Value("定义ID", id, source),
                    Value("记录类型", recordType, source),
                    Value("学科配方", "化学.运行配置", source),
                    Value("主体", row["主体"].Trim(), source),
                    Value("来源", row["来源"].Trim(), source),
                    Value(
                        "目标",
                        string.IsNullOrEmpty(role)
                            ? row["目标"].Trim()
                            : role,
                        source),
                    Value("启动交互", string.Empty, source),
                    Value("停止交互", string.Empty, source)
                }.Concat(parameters)
                    .ToDictionary(
                        value => value.Key,
                        value => value.Value,
                        StringComparer.Ordinal);
                records.Add(new CourseDisciplineProcessBlueprint(
                    values,
                    source,
                    parameters));
            }

            return new ChemistryKnowledgeCatalog(records);
        }

        private static IReadOnlyDictionary<string, BlueprintValue>
            ParseParameters(
                string raw,
                ConfigurationSource source,
                string knowledgeId)
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
                    throw new InvalidDataException(
                        $"化学知识“{knowledgeId}”的参数“{segment}”不是名称=值格式。");
                }

                var name = segment.Substring(0, separator).Trim();
                var key = "参数." + name;
                if (name.Length == 0 || result.ContainsKey(key))
                {
                    throw new InvalidDataException(
                        $"化学知识“{knowledgeId}”包含空参数名或重复参数“{name}”。");
                }

                result.Add(key, new BlueprintValue(
                    key,
                    segment.Substring(separator + 1).Trim(),
                    source));
            }

            return result;
        }

        private static KeyValuePair<string, BlueprintValue> Value(
            string name,
            string value,
            ConfigurationSource source) =>
            new KeyValuePair<string, BlueprintValue>(
                name,
                new BlueprintValue(name, value ?? string.Empty, source));
    }
}
