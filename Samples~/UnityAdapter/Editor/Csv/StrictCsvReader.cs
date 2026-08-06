using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Csv
{
    /// <summary>
    /// 将配置作者看到的自然中文表头转换为存量编译协议使用的列名。
    /// 转换只发生在 CSV 输入边界，因此旧表和已生成资产都能继续使用。
    /// </summary>
    public static class ConfigurationVocabulary
    {
        public static string RuntimeHeaderName(string configured)
        {
            var value = configured?.Trim() ?? string.Empty;
            if (!string.Equals(value, "标识", StringComparison.Ordinal))
            {
                value = value.Replace("标识", "ID");
            }

            return value.Replace("预制体", "Prefab");
        }

        public static string NaturalHeaderName(string runtimeName) =>
            (runtimeName?.Trim() ?? string.Empty)
                .Replace("ID", "标识")
                .Replace("Prefab", "预制体");
    }

    public sealed class StrictCsvRow
    {
        private readonly IReadOnlyDictionary<string, string> _values;
        private readonly IReadOnlyDictionary<string, string> _configuredValues;

        internal StrictCsvRow(
            int lineNumber,
            IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<string, string> configuredValues)
        {
            LineNumber = lineNumber;
            _values = values;
            _configuredValues = configuredValues;
        }

        public int LineNumber { get; }
        public IReadOnlyDictionary<string, string> Values => _values;
        public string this[string header] => _values.TryGetValue(
            header,
            out var value)
            ? value
            : string.Empty;
        public bool Has(string header) => _values.ContainsKey(header);
        public string ConfiguredValue(string header) =>
            _configuredValues.TryGetValue(header, out var value)
                ? value
                : string.Empty;
        public bool HasConfigured(string header) =>
            _configuredValues.ContainsKey(header);
    }

    public sealed class StrictCsvReadResult
    {
        internal StrictCsvReadResult(
            IEnumerable<string> headers,
            IEnumerable<StrictCsvRow> rows,
            IEnumerable<CourseCompilationDiagnostic> diagnostics,
            IEnumerable<string> configuredHeaders = null,
            string fileName = null)
        {
            FileName = fileName?.Trim() ?? string.Empty;
            Headers = headers.ToArray();
            ConfiguredHeaders = (configuredHeaders ?? headers).ToArray();
            Rows = rows.ToArray();
            Diagnostics = diagnostics.ToArray();
        }

        public string FileName { get; }
        public IReadOnlyList<string> Headers { get; }
        public IReadOnlyList<string> ConfiguredHeaders { get; }
        public IReadOnlyList<StrictCsvRow> Rows { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// 小型 RFC 4180 读取器；保留物理行号并拒绝模糊表头和非 NFC 文本。
    /// </summary>
    public sealed class StrictCsvReader
    {
        public StrictCsvReadResult ReadBytes(
            string fileName,
            byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            try
            {
                var text = new UTF8Encoding(false, true).GetString(content);
                if (text.Length > 0 && text[0] == '\uFEFF')
                {
                    text = text.Substring(1);
                }

                if (text.IndexOf('\uFEFF') >= 0)
                {
                    return new StrictCsvReadResult(
                        Array.Empty<string>(),
                        Array.Empty<StrictCsvRow>(),
                        new[]
                        {
                            Diagnostic(
                                "csv.bom.invalid",
                                fileName,
                                1,
                                1,
                                string.Empty,
                                "UTF-8 BOM 只能出现在文件开头。",
                                "删除文件内容中的 BOM 字符后重新导出。")
                        },
                        fileName: fileName);
                }

                return Read(fileName, text);
            }
            catch (DecoderFallbackException)
            {
                return new StrictCsvReadResult(
                    Array.Empty<string>(),
                    Array.Empty<StrictCsvRow>(),
                    new[]
                    {
                        Diagnostic(
                            "csv.encoding.invalid",
                            fileName,
                            1,
                            1,
                            string.Empty,
                            "文件不是严格 UTF-8 编码。",
                            "从 Excel 重新导出为 UTF-8 CSV。")
                    },
                    fileName: fileName);
            }
        }

        public StrictCsvReadResult Read(string fileName, string content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var diagnostics = new List<CourseCompilationDiagnostic>();
            var records = Parse(fileName, content, diagnostics);
            if (records.Count == 0)
            {
                diagnostics.Add(Diagnostic(
                    "csv.header.missing",
                    fileName,
                    1,
                    1,
                    string.Empty,
                    "CSV 缺少表头。",
                    "在第一行填写唯一且非空的列名。"));
                return new StrictCsvReadResult(
                    Array.Empty<string>(),
                    Array.Empty<StrictCsvRow>(),
                    diagnostics,
                    fileName: fileName);
            }

            var headers = records[0].Cells
                .Select(ConfigurationVocabulary.RuntimeHeaderName)
                .ToList();
            for (var index = 0; index < headers.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(headers[index]))
                {
                    diagnostics.Add(Diagnostic(
                        "csv.header.empty",
                        fileName,
                        records[0].Line,
                        index + 1,
                        string.Empty,
                        "表头不能包含空列名。",
                        "为该列填写明确且唯一的列名。"));
                }

                if (!headers[index].IsNormalized(NormalizationForm.FormC))
                {
                    diagnostics.Add(Diagnostic(
                        "csv.header.not-normalized",
                        fileName,
                        records[0].Line,
                        index + 1,
                        headers[index],
                        "表头不是 Unicode NFC 规范形式。",
                        "将表头统一规范化为 NFC。"));
                }
            }

            var duplicate = headers
                .GroupBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault(value => value.Count() > 1);
            if (duplicate != null)
            {
                diagnostics.Add(Diagnostic(
                    "csv.header.duplicate",
                    fileName,
                    records[0].Line,
                    Math.Max(1, headers.IndexOf(duplicate.Key) + 1),
                    duplicate.Key,
                    $"表头“{duplicate.Key}”重复。",
                    "为每一列使用唯一列名。"));
            }

            var rows = new List<StrictCsvRow>();
            foreach (var record in records.Skip(1))
            {
                if (record.Cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                if (record.Cells.Count != headers.Count)
                {
                    diagnostics.Add(Diagnostic(
                        "csv.column-count.invalid",
                        fileName,
                        record.Line,
                        1,
                        record.Cells.FirstOrDefault() ?? string.Empty,
                        "数据列数与表头不一致。",
                        "补齐缺列或移除多余单元格。"));
                    continue;
                }

                var values = new Dictionary<string, string>(
                    StringComparer.Ordinal);
                var configuredValues = new Dictionary<string, string>(
                    StringComparer.Ordinal);
                for (var index = 0; index < headers.Count; index++)
                {
                    var cell = record.Cells[index];
                    if (!cell.IsNormalized(NormalizationForm.FormC))
                    {
                        diagnostics.Add(Diagnostic(
                            "csv.unicode.not-normalized",
                            fileName,
                            record.Line,
                            index + 1,
                            record.Cells[0],
                            "文本不是 Unicode NFC 规范形式。",
                            "在 Excel 导出后将文本统一规范化为 NFC。"));
                    }

                    if (!values.ContainsKey(headers[index]))
                    {
                        values.Add(headers[index], cell);
                    }

                    var configuredHeader = records[0].Cells[index];
                    if (!configuredValues.ContainsKey(configuredHeader))
                    {
                        configuredValues.Add(configuredHeader, cell);
                    }
                }

                rows.Add(
                    new StrictCsvRow(
                        record.Line,
                        new ReadOnlyDictionary<string, string>(values),
                        new ReadOnlyDictionary<string, string>(
                            configuredValues)));
            }

            return new StrictCsvReadResult(
                headers,
                rows,
                diagnostics,
                records[0].Cells,
                fileName);
        }

        private static List<Record> Parse(
            string fileName,
            string content,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var records = new List<Record>();
            var cells = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            var afterClosingQuote = false;
            var line = 1;
            var recordLine = 1;
            for (var index = 0; index <= content.Length; index++)
            {
                var current = index < content.Length ? content[index] : '\n';
                if (quoted)
                {
                    if (current == '"')
                    {
                        if (index + 1 < content.Length
                            && content[index + 1] == '"')
                        {
                            cell.Append('"');
                            index++;
                        }
                        else
                        {
                            quoted = false;
                            afterClosingQuote = true;
                        }
                    }
                    else
                    {
                        cell.Append(current);
                        if (current == '\n')
                        {
                            line++;
                        }
                    }

                    continue;
                }

                if (afterClosingQuote)
                {
                    if (current != ','
                        && current != '\r'
                        && current != '\n')
                    {
                        diagnostics.Add(Diagnostic(
                            "csv.quote.invalid",
                            fileName,
                            line,
                            Math.Max(1, cell.Length + 1),
                            cells.FirstOrDefault() ?? string.Empty,
                            "闭引号后只能出现逗号或换行。",
                            "删除闭引号后的额外字符，或将其放入引号内。"));
                        cell.Append(current);
                        afterClosingQuote = false;
                        continue;
                    }

                    afterClosingQuote = false;
                }

                if (current == '"' && cell.Length == 0)
                {
                    quoted = true;
                }
                else if (current == '"')
                {
                    diagnostics.Add(Diagnostic(
                        "csv.quote.invalid",
                        fileName,
                        line,
                        Math.Max(1, cell.Length + 1),
                        cells.FirstOrDefault() ?? string.Empty,
                        "未加引号字段中不能出现双引号。",
                        "使用成对双引号包裹整个字段。"));
                    cell.Append(current);
                }
                else if (current == ',')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else if (current == '\r' || current == '\n')
                {
                    if (current == '\r'
                        && index + 1 < content.Length
                        && content[index + 1] == '\n')
                    {
                        index++;
                    }

                    cells.Add(cell.ToString());
                    cell.Clear();
                    records.Add(new Record(recordLine, new List<string>(cells)));
                    cells.Clear();
                    line++;
                    recordLine = line;
                }
                else
                {
                    cell.Append(current);
                }
            }

            if (quoted)
            {
                diagnostics.Add(Diagnostic(
                    "csv.quote.unclosed",
                    fileName,
                    recordLine,
                    1,
                    string.Empty,
                    "引号字段没有闭合。",
                    "使用成对双引号包裹字段。"));
            }

            return records;
        }

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            string file,
            int line,
            int column,
            string id,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                file,
                line,
                column,
                string.Empty,
                id,
                reason,
                suggestion);

        private sealed class Record
        {
            public Record(int line, List<string> cells)
            {
                Line = line;
                Cells = cells;
            }

            public int Line { get; }
            public List<string> Cells { get; }
        }
    }
}
