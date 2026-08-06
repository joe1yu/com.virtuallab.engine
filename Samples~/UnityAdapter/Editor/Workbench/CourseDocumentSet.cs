using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Csv;

namespace VirtualLab.Unity.Authoring.Workbench
{
    /// <summary>
    /// 一门课程全部 CSV 的编辑会话。可编辑表与暂时无法解析的原始表共同构成
    /// 同一份蓝图快照，避免工作台只看见实验对象表而遗漏跨表引用。
    /// </summary>
    public sealed class CourseDocumentSet
    {
        private readonly Dictionary<string, EditableCsvDocument> _documents;
        private readonly Dictionary<string, RawDocument> _rawDocuments;

        private CourseDocumentSet(
            string authoringDirectory,
            IDictionary<string, EditableCsvDocument> documents,
            IDictionary<string, RawDocument> rawDocuments,
            IEnumerable<string> loadMessages)
        {
            AuthoringDirectory = authoringDirectory;
            _documents = new Dictionary<string, EditableCsvDocument>(
                documents,
                StringComparer.Ordinal);
            _rawDocuments = new Dictionary<string, RawDocument>(
                rawDocuments,
                StringComparer.Ordinal);
            LoadMessages = (loadMessages ?? Array.Empty<string>()).ToArray();
        }

        public string AuthoringDirectory { get; }
        public IReadOnlyDictionary<string, EditableCsvDocument> Documents =>
            new ReadOnlyDictionary<string, EditableCsvDocument>(_documents);
        public IReadOnlyList<string> LoadMessages { get; }
        public bool HasUnreadableDocuments => _rawDocuments.Count > 0;
        public bool IsModified => _documents.Values.Any(value => value.IsModified);

        public static CourseDocumentSet Load(string authoringDirectory)
        {
            if (string.IsNullOrWhiteSpace(authoringDirectory))
            {
                throw new ArgumentException("课程配置目录不能为空。", nameof(authoringDirectory));
            }

            var fullPath = Path.GetFullPath(authoringDirectory);
            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException(
                    $"找不到课程配置目录：{fullPath}");
            }

            var documents = new Dictionary<string, EditableCsvDocument>(
                StringComparer.Ordinal);
            var rawDocuments = new Dictionary<string, RawDocument>(
                StringComparer.Ordinal);
            var messages = new List<string>();
            foreach (var path in Directory.EnumerateFiles(
                         fullPath,
                         "*.csv",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var fileName = Path.GetFileName(path);
                try
                {
                    documents.Add(fileName, EditableCsvDocument.Load(path));
                }
                catch (InvalidDataException exception)
                {
                    var content = File.ReadAllBytes(path);
                    rawDocuments.Add(
                        fileName,
                        new RawDocument(path, content));
                    messages.Add(
                        $"{fileName} 暂时不能进行表单编辑：{exception.Message}");
                }
            }

            return new CourseDocumentSet(
                fullPath,
                documents,
                rawDocuments,
                messages);
        }

        public bool TryGetDocument(
            string fileName,
            out EditableCsvDocument document) =>
            _documents.TryGetValue(fileName, out document);

        public EditableCsvDocument GetRequiredDocument(string fileName)
        {
            if (_documents.TryGetValue(fileName, out var document))
            {
                return document;
            }

            if (_rawDocuments.ContainsKey(fileName))
            {
                throw new InvalidDataException(
                    $"课程表“{fileName}”存在 CSV 结构错误，暂时不能编辑。");
            }

            throw new FileNotFoundException(
                $"课程缺少必需表“{fileName}”。",
                Path.Combine(AuthoringDirectory, fileName));
        }

        /// <summary>
        /// 新课程会话必须恰好包含指定职责表，防止旧表或损坏表被悄悄带入保存。
        /// </summary>
        public void EnsureExactDocuments(IEnumerable<string> requiredFileNames)
        {
            if (requiredFileNames == null)
            {
                throw new ArgumentNullException(nameof(requiredFileNames));
            }

            var required = new HashSet<string>(
                requiredFileNames,
                StringComparer.Ordinal);
            var actual = new HashSet<string>(
                _documents.Keys.Concat(_rawDocuments.Keys),
                StringComparer.Ordinal);
            var missing = required.Except(actual).OrderBy(
                value => value,
                StringComparer.Ordinal).ToArray();
            var unexpected = actual.Except(required).OrderBy(
                value => value,
                StringComparer.Ordinal).ToArray();
            if (missing.Length > 0 || unexpected.Length > 0
                || _rawDocuments.Count > 0)
            {
                throw new InvalidDataException(string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        missing.Length == 0
                            ? string.Empty
                            : "缺少职责表：" + string.Join("、", missing),
                        unexpected.Length == 0
                            ? string.Empty
                            : "包含协议外表：" + string.Join("、", unexpected),
                        _rawDocuments.Count == 0
                            ? string.Empty
                            : "存在无法解析的职责表："
                              + string.Join("、", _rawDocuments.Keys.OrderBy(
                                  value => value,
                                  StringComparer.Ordinal))
                    }.Where(value => value.Length > 0)));
            }
        }

        public CourseBlueprintSource CreateBlueprintSource()
        {
            var editable = _documents.Select(pair =>
                new CourseBlueprintFile(pair.Key, pair.Value.ToCsv()));
            var raw = _rawDocuments.Select(pair =>
                new CourseBlueprintFile(pair.Key, pair.Value.Content));
            return new CourseBlueprintSource(editable.Concat(raw));
        }

        public CourseDocumentSnapshot CaptureSnapshot() =>
            new CourseDocumentSnapshot(
                _documents.ToDictionary(
                    value => value.Key,
                    value => value.Value.ToCsv(),
                    StringComparer.Ordinal));

        public void Restore(CourseDocumentSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            foreach (var pair in snapshot.ContentByFileName)
            {
                if (!_documents.TryGetValue(pair.Key, out var document))
                {
                    throw new InvalidOperationException(
                        $"撤销快照引用了当前会话中不存在的课程表“{pair.Key}”。");
                }

                document.Restore(pair.Value);
            }
        }

        public bool HasExternalChanges()
        {
            return _documents.Values.Any(value => value.HasExternalChanges())
                   || _rawDocuments.Values.Any(value => value.HasExternalChanges());
        }

        public void SaveModified()
        {
            if (HasExternalChanges())
            {
                throw new InvalidOperationException(
                    "至少一张课程 CSV 已被外部程序修改。请先重新载入课程，再重新应用当前修改。");
            }

            var modified = _documents.Values
                .Where(value => value.IsModified)
                .OrderBy(value => value.SourcePath, StringComparer.Ordinal)
                .ToArray();
            var previous = modified.ToDictionary(
                value => value,
                value => new SavedState(
                    value.SourceBytesSnapshot,
                    value.BaselineCsv));
            var saved = new List<EditableCsvDocument>();
            try
            {
                foreach (var document in modified)
                {
                    document.SaveAtomic();
                    saved.Add(document);
                }
            }
            catch (Exception exception)
            {
                var rollbackErrors = new List<string>();
                foreach (var document in saved.AsEnumerable().Reverse())
                {
                    var state = previous[document];
                    try
                    {
                        ReplaceBytes(document.SourcePath, state.SourceBytes);
                        document.RestoreSavedState(
                            state.SourceBytes,
                            state.BaselineCsv);
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackErrors.Add(
                            $"{Path.GetFileName(document.SourcePath)}：{rollbackException.Message}");
                    }
                }

                throw new IOException(
                    "课程多表保存失败，已尝试回滚此前写入的表："
                    + exception.Message
                    + (rollbackErrors.Count == 0
                        ? string.Empty
                        : Environment.NewLine + "以下文件回滚失败："
                          + string.Join("；", rollbackErrors)),
                    exception);
            }
        }

        private static void ReplaceBytes(string path, byte[] content)
        {
            var temporaryPath = path + ".rollback-"
                + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, content);
                File.Replace(temporaryPath, path, null);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private sealed class SavedState
        {
            public SavedState(byte[] sourceBytes, string baselineCsv)
            {
                SourceBytes = sourceBytes;
                BaselineCsv = baselineCsv;
            }

            public byte[] SourceBytes { get; }
            public string BaselineCsv { get; }
        }

        private sealed class RawDocument
        {
            private readonly string _path;
            private readonly byte[] _content;

            public RawDocument(string path, byte[] content)
            {
                _path = path;
                _content = content.ToArray();
            }

            public byte[] Content => _content.ToArray();

            public bool HasExternalChanges()
            {
                return !File.Exists(_path)
                       || !_content.SequenceEqual(File.ReadAllBytes(_path));
            }
        }
    }

    public sealed class CourseDocumentSnapshot
    {
        internal CourseDocumentSnapshot(
            IReadOnlyDictionary<string, string> contentByFileName)
        {
            ContentByFileName = contentByFileName;
        }

        internal IReadOnlyDictionary<string, string> ContentByFileName { get; }
    }
}
