using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Recipes
{
    public sealed class RecipePackageSourceReadResult
    {
        internal RecipePackageSourceReadResult(
            IReadOnlyDictionary<string, StrictCsvReadResult> tables,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Tables = tables;
            Diagnostics = diagnostics.ToArray();
        }

        public bool IsSuccess => Diagnostics.Count == 0;
        public IReadOnlyDictionary<string, StrictCsvReadResult> Tables { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
    }

    public sealed class RecipePackageFile
    {
        private readonly byte[] _content;

        public RecipePackageFile(string fileName, byte[] content)
        {
            FileName = string.IsNullOrWhiteSpace(fileName)
                ? throw new ArgumentException("配方文件名不能为空。", nameof(fileName))
                : fileName;
            _content = content == null
                ? throw new ArgumentNullException(nameof(content))
                : content.ToArray();
        }

        public string FileName { get; }
        public byte[] Content => _content.ToArray();
    }

    /// <summary>
    /// 共享配方包的文件快照。具体表结构由显式提供者转换为强类型契约。
    /// </summary>
    public sealed class RecipePackageSource : IEnumerable<RecipePackageFile>
    {
        public const string RecipePrefabRequirementFileName =
            "配方附加的预制体要求.csv";

        private static readonly HashSet<string> AllowedFiles =
            new HashSet<string>(
                new[]
                {
                    "配方.csv",
                    "参数契约.csv",
                    RecipePrefabRequirementFileName,
                    "操作.csv",
                    "条件.csv",
                    "状态变化.csv",
                    "表现.csv"
                },
                StringComparer.Ordinal);

        private readonly IReadOnlyList<RecipePackageFile> _files;

        public RecipePackageSource(IEnumerable<RecipePackageFile> files)
        {
            _files = (files ?? throw new ArgumentNullException(nameof(files)))
                .Select(file => file ?? throw new ArgumentException(
                    "配方文件集合不能包含空项。",
                    nameof(files)))
                .ToArray();
        }

        public static RecipePackageSource FromDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("配方目录不能为空。", nameof(directory));
            }

            return new RecipePackageSource(
                Directory.EnumerateFiles(directory, "*.csv", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                    .Select(path => new RecipePackageFile(
                        Path.GetFileName(path),
                        File.ReadAllBytes(path))));
        }

        public static bool IsAllowed(string fileName) =>
            AllowedFiles.Contains(fileName);

        public RecipePackageSourceReadResult ReadTables()
        {
            var diagnostics = new List<CourseCompilationDiagnostic>();
            var tables = new Dictionary<string, StrictCsvReadResult>(
                StringComparer.Ordinal);
            var reader = new StrictCsvReader();
            foreach (var file in _files.OrderBy(
                         value => value.FileName,
                         StringComparer.Ordinal))
            {
                if (!IsAllowed(file.FileName))
                {
                    diagnostics.Add(new CourseCompilationDiagnostic(
                        "recipe.file.unsupported",
                        file.FileName,
                        1,
                        1,
                        string.Empty,
                        string.Empty,
                        $"文件“{file.FileName}”不是共享配方包允许的表。",
                        "删除该文件，或把内容迁移到七张结构化共享配方表之一。"));
                    continue;
                }

                if (tables.ContainsKey(file.FileName))
                {
                    diagnostics.Add(new CourseCompilationDiagnostic(
                        "recipe.file.duplicate",
                        file.FileName,
                        1,
                        1,
                        string.Empty,
                        string.Empty,
                        $"共享配方包中存在多个“{file.FileName}”。",
                        "每种共享配方表只保留一个文件。"));
                    continue;
                }

                var table = reader.ReadBytes(file.FileName, file.Content);
                tables.Add(file.FileName, table);
                diagnostics.AddRange(table.Diagnostics);
            }

            return new RecipePackageSourceReadResult(
                new ReadOnlyDictionary<string, StrictCsvReadResult>(tables),
                diagnostics);
        }

        public IEnumerator<RecipePackageFile> GetEnumerator() =>
            _files.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
