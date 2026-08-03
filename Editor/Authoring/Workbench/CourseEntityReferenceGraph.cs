using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VirtualLab.Unity.Authoring.Csv;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public sealed class CourseEntityReference
    {
        internal CourseEntityReference(
            string fileName,
            int rowIndex,
            string columnName,
            EditableCsvRow row)
        {
            FileName = fileName;
            RowIndex = rowIndex;
            ColumnName = columnName;
            Row = row;
        }

        public string FileName { get; }
        public int RowIndex { get; }
        public int CsvLine => RowIndex + 2;
        public string ColumnName { get; }
        internal EditableCsvRow Row { get; }
    }

    /// <summary>
    /// 课程高层表中的实体引用契约。重命名只处理明确承载实体 ID 的列，
    /// 不扫描任意文本或参数表达式，避免把文案、状态 ID 和物质 ID 意外替换。
    /// </summary>
    public sealed class CourseEntityReferenceGraph
    {
        private const string ObjectsFile = "实验对象.csv";

        private static readonly IReadOnlyDictionary<string, string[]> ReferenceColumns =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [ObjectsFile] = new[] { "实体ID" },
                ["交互规则.csv"] = new[] { "来源", "目标", "要求主体" },
                ["学科过程.csv"] = new[] { "来源", "目标" },
                ["教学评价.csv"] = new[] { "主体" },
                ["表现覆盖.csv"] = new[]
                {
                    "触发来源", "触发目标", "对象或状态"
                },
                ["验收场景.csv"] = new[] { "来源", "目标", "对象" },
                ["实验流程.csv"] = new[] { "来源", "目标", "主体" },
                ["高级覆盖.csv"] = new[] { "来源实体", "目标实体" }
            };

        public IReadOnlyList<CourseEntityReference> Find(
            CourseDocumentSet documents,
            string entityId)
        {
            if (documents == null)
            {
                throw new ArgumentNullException(nameof(documents));
            }

            if (string.IsNullOrWhiteSpace(entityId))
            {
                return Array.Empty<CourseEntityReference>();
            }

            var result = new List<CourseEntityReference>();
            foreach (var contract in ReferenceColumns)
            {
                if (!documents.TryGetDocument(contract.Key, out var document))
                {
                    continue;
                }

                for (var rowIndex = 0; rowIndex < document.Rows.Count; rowIndex++)
                {
                    var row = document.Rows[rowIndex];
                    foreach (var column in contract.Value.Where(value =>
                                 document.Headers.Contains(value, StringComparer.Ordinal)))
                    {
                        if (string.Equals(
                                row[column].Trim(),
                                entityId,
                                StringComparison.Ordinal))
                        {
                            result.Add(new CourseEntityReference(
                                contract.Key,
                                rowIndex,
                                column,
                                row));
                        }
                    }
                }
            }

            return result
                .OrderBy(value => value.FileName, StringComparer.Ordinal)
                .ThenBy(value => value.RowIndex)
                .ThenBy(value => value.ColumnName, StringComparer.Ordinal)
                .ToArray();
        }

        public IReadOnlyList<CourseEntityReference> FindExternalReferences(
            CourseDocumentSet documents,
            string entityId) =>
            Find(documents, entityId)
                .Where(value => value.FileName != ObjectsFile
                                || value.ColumnName != "实体ID")
                .ToArray();

        public IReadOnlyList<CourseEntityReference> Rename(
            CourseDocumentSet documents,
            string oldEntityId,
            string newEntityId)
        {
            if (documents == null)
            {
                throw new ArgumentNullException(nameof(documents));
            }

            if (documents.HasUnreadableDocuments)
            {
                throw new InvalidOperationException(
                    "至少一张课程表存在 CSV 结构错误，无法保证跨表重命名完整。请先修复所有表的结构错误。");
            }

            oldEntityId = NormalizeId(oldEntityId, nameof(oldEntityId));
            newEntityId = NormalizeId(newEntityId, nameof(newEntityId));
            var objectDocument = documents.GetRequiredDocument(ObjectsFile);
            var oldRows = objectDocument.Rows.Where(value =>
                    value["实体ID"] == oldEntityId)
                .ToArray();
            if (oldRows.Length != 1)
            {
                throw new InvalidOperationException(
                    $"实验对象表中应当恰好存在一个实体“{oldEntityId}”，实际为 {oldRows.Length} 个。");
            }

            if (objectDocument.Rows.Any(value =>
                    value["实体ID"] == newEntityId))
            {
                throw new InvalidOperationException(
                    $"实体 ID“{newEntityId}”已经存在，不能重命名。");
            }

            var references = Find(documents, oldEntityId);
            foreach (var reference in references)
            {
                reference.Row[reference.ColumnName] = newEntityId;
            }

            return references;
        }

        private static string NormalizeId(string value, string argumentName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("实体 ID 不能为空。", argumentName);
            }

            return value.Trim().Normalize(NormalizationForm.FormC);
        }
    }
}
