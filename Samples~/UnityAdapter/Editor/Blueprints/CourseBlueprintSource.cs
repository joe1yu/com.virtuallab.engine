using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VirtualLab.Unity.Authoring.Blueprints
{
    public sealed class CourseBlueprintFile
    {
        private readonly byte[] _content;

        public CourseBlueprintFile(string fileName, string content)
            : this(
                fileName,
                new UTF8Encoding(false, true).GetBytes(
                    content ?? throw new ArgumentNullException(nameof(content))))
        {
        }

        public CourseBlueprintFile(string fileName, byte[] content)
        {
            FileName = string.IsNullOrWhiteSpace(fileName)
                ? throw new ArgumentException("蓝图文件名不能为空。", nameof(fileName))
                : fileName;
            _content = content == null
                ? throw new ArgumentNullException(nameof(content))
                : content.ToArray();
        }

        public string FileName { get; }
        public byte[] Content => _content.ToArray();
    }

    /// <summary>
    /// 蓝图输入快照。它保留未知文件，交由读取器生成可定位诊断。
    /// </summary>
    public sealed class CourseBlueprintSource : IEnumerable<CourseBlueprintFile>
    {
        private readonly IReadOnlyList<CourseBlueprintFile> _files;

        public CourseBlueprintSource(IEnumerable<CourseBlueprintFile> files)
        {
            _files = (files ?? throw new ArgumentNullException(nameof(files)))
                .Select(file => file ?? throw new ArgumentException(
                    "蓝图文件集合不能包含空项。",
                    nameof(files)))
                .ToArray();
        }

        public static CourseBlueprintSource FromDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("蓝图目录不能为空。", nameof(directory));
            }

            return new CourseBlueprintSource(
                Directory.EnumerateFiles(directory, "*.csv", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                    .Select(path => new CourseBlueprintFile(
                        Path.GetFileName(path),
                        File.ReadAllBytes(path))));
        }

        public CourseBlueprintSource Replace(string fileName, string content) =>
            Replace(fileName, new UTF8Encoding(false, true).GetBytes(content));

        public CourseBlueprintSource Replace(string fileName, byte[] content)
        {
            var replaced = false;
            var files = _files.Select(file =>
            {
                if (!string.Equals(file.FileName, fileName, StringComparison.Ordinal))
                {
                    return file;
                }

                replaced = true;
                return new CourseBlueprintFile(fileName, content);
            }).ToList();

            if (!replaced)
            {
                files.Add(new CourseBlueprintFile(fileName, content));
            }

            return new CourseBlueprintSource(files);
        }

        public CourseBlueprintSource Append(string fileName, string content) =>
            Append(new CourseBlueprintFile(fileName, content));

        public CourseBlueprintSource Append(CourseBlueprintFile file) =>
            new CourseBlueprintSource(_files.Concat(new[] { file }));

        public CourseBlueprintSource Reverse() =>
            new CourseBlueprintSource(_files.Reverse());

        public IEnumerator<CourseBlueprintFile> GetEnumerator() =>
            _files.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
