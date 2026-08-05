using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace VirtualLab.Unity.Authoring.Csv
{
    /// <summary>
    /// 可编辑 CSV 行。列名由所属文档管理，设置值时会统一为 Unicode NFC，
    /// 避免中文 ID 因组合字符差异产生肉眼不可见的不一致。
    /// </summary>
    public sealed class EditableCsvRow
    {
        private readonly Dictionary<string, string> _values;

        internal EditableCsvRow(
            IEnumerable<string> headers,
            IReadOnlyDictionary<string, string> values = null)
        {
            _values = headers.ToDictionary(
                header => header,
                header => values != null && values.TryGetValue(header, out var value)
                    ? value
                    : string.Empty,
                StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, string> Values =>
            new ReadOnlyDictionary<string, string>(_values);

        public string this[string header]
        {
            get => _values.TryGetValue(header, out var value)
                ? value
                : string.Empty;
            set
            {
                if (!_values.ContainsKey(header))
                {
                    throw new ArgumentException(
                        $"列“{header}”不属于当前 CSV 文档。",
                        nameof(header));
                }

                _values[header] = Normalize(value);
            }
        }

        internal void AddColumn(string header, string defaultValue) =>
            _values.Add(header, Normalize(defaultValue));

        private static string Normalize(string value) =>
            (value ?? string.Empty).Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// 面向编辑器的 CSV 文档模型。它保留原始列顺序、未知扩展列和数据行顺序，
    /// 并在保存前检测外部修改，防止工作台覆盖 Excel 刚导出的内容。
    /// </summary>
    public sealed class EditableCsvDocument
    {
        private readonly List<string> _headers;
        private readonly List<string> _configuredHeaders;
        private readonly List<EditableCsvRow> _rows;
        private readonly string _sourcePath;
        private byte[] _sourceBytes;
        private string _baselineCsv;

        private EditableCsvDocument(
            IEnumerable<string> headers,
            IEnumerable<string> configuredHeaders,
            IEnumerable<EditableCsvRow> rows,
            string sourcePath,
            byte[] sourceBytes)
        {
            _headers = headers.ToList();
            _configuredHeaders = configuredHeaders.ToList();
            if (_headers.Count != _configuredHeaders.Count)
            {
                throw new ArgumentException("CSV 内部列名与配置表头数量不一致。");
            }
            _rows = rows.ToList();
            _sourcePath = sourcePath ?? string.Empty;
            _sourceBytes = sourceBytes?.ToArray() ?? Array.Empty<byte>();
            _baselineCsv = ToCsv();
        }

        public IReadOnlyList<string> Headers => _headers;
        public IReadOnlyList<string> ConfiguredHeaders => _configuredHeaders;
        public IReadOnlyList<EditableCsvRow> Rows => _rows;
        public string SourcePath => _sourcePath;
        public bool IsModified => !string.Equals(
            ToCsv(),
            _baselineCsv,
            StringComparison.Ordinal);
        internal byte[] SourceBytesSnapshot => _sourceBytes.ToArray();
        internal string BaselineCsv => _baselineCsv;

        public static EditableCsvDocument Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("CSV 路径不能为空。", nameof(path));
            }

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("找不到要编辑的 CSV 文件。", fullPath);
            }

            var content = File.ReadAllBytes(fullPath);
            return Parse(
                Path.GetFileName(fullPath),
                content,
                fullPath,
                content);
        }

        public static EditableCsvDocument Parse(string fileName, string content)
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(
                content ?? throw new ArgumentNullException(nameof(content)));
            return Parse(
                fileName,
                bytes,
                string.Empty,
                bytes);
        }

        public EditableCsvRow AddRow(
            IEnumerable<KeyValuePair<string, string>> initialValues = null)
        {
            var values = (initialValues
                          ?? Array.Empty<KeyValuePair<string, string>>())
                .Where(value => _headers.Contains(
                    value.Key,
                    StringComparer.Ordinal))
                .GroupBy(value => value.Key, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.Last().Value ?? string.Empty,
                    StringComparer.Ordinal);
            var row = new EditableCsvRow(_headers, values);
            _rows.Add(row);
            return row;
        }

        /// <summary>
        /// 按作者实际看到的中文表头增加一行，不经过旧运行时表头词汇转换。
        /// </summary>
        public EditableCsvRow AddConfiguredRow(
            IEnumerable<KeyValuePair<string, string>> initialValues = null)
        {
            var translated = (initialValues
                              ?? Array.Empty<KeyValuePair<string, string>>())
                .Select(value => new KeyValuePair<string, string>(
                    RuntimeHeader(value.Key),
                    value.Value));
            return AddRow(translated);
        }

        public string ConfiguredValue(
            EditableCsvRow row,
            string configuredHeader)
        {
            EnsureOwnedRow(row);
            return row[RuntimeHeader(configuredHeader)];
        }

        public void SetConfiguredValue(
            EditableCsvRow row,
            string configuredHeader,
            string value)
        {
            EnsureOwnedRow(row);
            row[RuntimeHeader(configuredHeader)] = value;
        }

        public void RemoveRow(EditableCsvRow row)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (!_rows.Remove(row))
            {
                throw new ArgumentException("要删除的行不属于当前 CSV 文档。", nameof(row));
            }
        }

        public void EnsureColumn(string header, string defaultValue = "")
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                throw new ArgumentException("CSV 列名不能为空。", nameof(header));
            }

            header = header.Normalize(NormalizationForm.FormC);
            if (_headers.Contains(header, StringComparer.Ordinal))
            {
                return;
            }

            _headers.Add(header);
            _configuredHeaders.Add(
                ConfigurationVocabulary.NaturalHeaderName(header));
            foreach (var row in _rows)
            {
                row.AddColumn(header, defaultValue);
            }
        }

        public bool HasExternalChanges()
        {
            if (string.IsNullOrEmpty(_sourcePath))
            {
                return false;
            }

            if (!File.Exists(_sourcePath))
            {
                return true;
            }

            var current = File.ReadAllBytes(_sourcePath);
            return !_sourceBytes.SequenceEqual(current);
        }

        public string ToCsv()
        {
            var result = new StringBuilder();
            AppendRecord(result, _configuredHeaders);
            foreach (var row in _rows)
            {
                AppendRecord(result, _headers.Select(header => row[header]));
            }

            return result.ToString();
        }

        /// <summary>
        /// 从编辑会话快照恢复当前内容，但不改变磁盘来源快照和保存基线。
        /// 用于工作台自己的撤销/重做，不创建第二份正式配置源。
        /// </summary>
        public void Restore(string content)
        {
            var parsed = new StrictCsvReader().Read(
                string.IsNullOrEmpty(_sourcePath)
                    ? "内存课程表.csv"
                    : Path.GetFileName(_sourcePath),
                content ?? throw new ArgumentNullException(nameof(content)));
            ThrowIfInvalid(parsed);

            _headers.Clear();
            _headers.AddRange(parsed.Headers);
            _configuredHeaders.Clear();
            _configuredHeaders.AddRange(parsed.ConfiguredHeaders);
            _rows.Clear();
            _rows.AddRange(parsed.Rows.Select(row =>
                new EditableCsvRow(parsed.Headers, row.Values)));
        }

        /// <summary>
        /// 在同一目录写入临时文件后原子替换正式文件。若文件已被 Excel 或其他程序
        /// 修改则拒绝保存，由用户先重新载入后再合并，绝不静默覆盖外部工作。
        /// </summary>
        public void SaveAtomic()
        {
            if (string.IsNullOrEmpty(_sourcePath))
            {
                throw new InvalidOperationException("内存 CSV 没有关联保存路径。");
            }

            if (HasExternalChanges())
            {
                throw new InvalidOperationException(
                    "CSV 已被外部程序修改。请先重新载入，再重新应用当前修改。");
            }

            var temporaryPath = _sourcePath + ".workbench-"
                + Guid.NewGuid().ToString("N")
                + ".tmp";
            try
            {
                var csv = ToCsv();
                var bytes = new UTF8Encoding(false).GetBytes(csv);
                File.WriteAllBytes(temporaryPath, bytes);
                File.Replace(temporaryPath, _sourcePath, null);

                _sourceBytes = bytes;
                _baselineCsv = csv;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static EditableCsvDocument Parse(
            string fileName,
            byte[] content,
            string sourcePath,
            byte[] sourceBytes)
        {
            var parsed = new StrictCsvReader().ReadBytes(fileName, content);
            ThrowIfInvalid(parsed);

            var rows = parsed.Rows.Select(row =>
                new EditableCsvRow(parsed.Headers, row.Values));
            return new EditableCsvDocument(
                parsed.Headers,
                parsed.ConfiguredHeaders,
                rows,
                sourcePath,
                sourceBytes);
        }

        private static void ThrowIfInvalid(StrictCsvReadResult parsed)
        {
            if (parsed.Diagnostics.Count == 0)
            {
                return;
            }

            throw new InvalidDataException(string.Join(
                Environment.NewLine,
                parsed.Diagnostics.Select(value =>
                    $"{value.FileName}:{value.Line} {value.Reason}")));
        }

        private static void AppendRecord(
            StringBuilder target,
            IEnumerable<string> cells)
        {
            var first = true;
            foreach (var cell in cells)
            {
                if (!first)
                {
                    target.Append(',');
                }

                target.Append(Escape(cell));
                first = false;
            }

            target.Append("\r\n");
        }

        private string RuntimeHeader(string configuredHeader)
        {
            var index = _configuredHeaders.FindIndex(value =>
                string.Equals(value, configuredHeader, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new ArgumentException(
                    $"配置表头“{configuredHeader}”不属于当前 CSV 文档。",
                    nameof(configuredHeader));
            }

            return _headers[index];
        }

        private void EnsureOwnedRow(EditableCsvRow row)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (!_rows.Contains(row))
            {
                throw new ArgumentException(
                    "要访问的行不属于当前 CSV 文档。",
                    nameof(row));
            }
        }

        internal void RestoreSavedState(
            byte[] sourceBytes,
            string baselineCsv)
        {
            _sourceBytes = sourceBytes?.ToArray() ?? Array.Empty<byte>();
            _baselineCsv = baselineCsv ?? string.Empty;
        }

        private static string Escape(string value)
        {
            value ??= string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
