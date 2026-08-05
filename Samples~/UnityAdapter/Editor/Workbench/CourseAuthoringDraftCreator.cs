using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Drafts;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public sealed class CourseAuthoringDraftCreationResult
    {
        internal CourseAuthoringDraftCreationResult(
            string courseDirectory,
            string authoringDirectory)
        {
            CourseDirectory = courseDirectory;
            AuthoringDirectory = authoringDirectory;
        }

        public string CourseDirectory { get; }
        public string AuthoringDirectory { get; }
    }

    /// <summary>
    /// 一次性创建完整十表草稿；写入完成前只使用同级临时目录。
    /// </summary>
    public sealed class CourseAuthoringDraftCreator
    {
        public CourseAuthoringDraftCreationResult Create(
            string coursesRoot,
            string courseId,
            string displayName,
            string disciplinePackageIds,
            string experimentPrefab,
            string actorEntityId = "学生")
        {
            if (string.IsNullOrWhiteSpace(coursesRoot))
            {
                throw new ArgumentException("课程根目录不能为空。", nameof(coursesRoot));
            }

            courseId = CourseDirectoryName(courseId);
            displayName = Text(displayName, courseId);
            actorEntityId = Text(actorEntityId, "学生");
            var root = Path.GetFullPath(coursesRoot);
            Directory.CreateDirectory(root);
            var target = Path.Combine(root, courseId);
            if (Directory.Exists(target) || File.Exists(target))
            {
                throw new InvalidOperationException(
                    $"课程目录“{target}”已经存在，不会覆盖原有课程。");
            }

            var staging = Path.Combine(
                root,
                ".creating-" + Guid.NewGuid().ToString("N"));
            try
            {
                var authoring = Path.Combine(staging, "Authoring");
                Directory.CreateDirectory(authoring);
                Directory.CreateDirectory(Path.Combine(staging, "Generated"));
                foreach (var schema in CourseAuthoringSchema.Tables)
                {
                    var document = EmptyDocument(schema);
                    if (schema.FileName == CourseAuthoringTableNames.Course)
                    {
                        document.AddConfiguredRow(new[]
                        {
                            Pair(CourseAuthoringColumns.Course.Id, courseId),
                            Pair(CourseAuthoringColumns.Course.DisplayName, displayName),
                            Pair(CourseAuthoringColumns.Course.DisciplinePackage,
                                disciplinePackageIds),
                            Pair(CourseAuthoringColumns.Course.ActorEntityId,
                                actorEntityId),
                            Pair(CourseAuthoringColumns.Course.ExperimentPrefab,
                                experimentPrefab)
                        });
                    }
                    else if (schema.FileName == CourseAuthoringTableNames.Objects)
                    {
                        document.AddConfiguredRow(new[]
                        {
                            Pair(CourseAuthoringColumns.Object.EntityId, actorEntityId),
                            Pair(CourseAuthoringColumns.Object.DisplayName, actorEntityId),
                            Pair(CourseAuthoringColumns.Object.EntityType, string.Empty),
                            Pair(CourseAuthoringColumns.Object.Roles, "操作者"),
                            Pair(CourseAuthoringColumns.Object.Tags, string.Empty),
                            Pair(CourseAuthoringColumns.Object.InitialPosition, "0|0|0"),
                            Pair(CourseAuthoringColumns.Object.InitialRotation, "0|0|0")
                        });
                    }

                    WriteUtf8(
                        Path.Combine(authoring, schema.FileName),
                        document.ToCsv());
                }

                Directory.Move(staging, target);
                return new CourseAuthoringDraftCreationResult(
                    target,
                    Path.Combine(target, "Authoring"));
            }
            catch
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }

                throw;
            }
        }

        private static EditableCsvDocument EmptyDocument(
            CourseAuthoringTableSchema schema) =>
            EditableCsvDocument.Parse(
                schema.FileName,
                string.Join(",", schema.Columns) + "\r\n");

        private static string CourseDirectoryName(string value)
        {
            value = Text(value, string.Empty);
            if (value.Length == 0
                || value == "."
                || value == ".."
                || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || value.IndexOf(Path.DirectorySeparatorChar) >= 0
                || value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                throw new ArgumentException(
                    "课程标识必须是有效的单级目录名称。",
                    nameof(value));
            }

            return value;
        }

        private static string Text(string value, string fallback) =>
            (string.IsNullOrWhiteSpace(value) ? fallback : value.Trim())
            .Normalize(NormalizationForm.FormC);

        private static KeyValuePair<string, string> Pair(
            string key,
            string value) =>
            new KeyValuePair<string, string>(key, value ?? string.Empty);

        private static void WriteUtf8(string path, string content) =>
            File.WriteAllText(path, content, new UTF8Encoding(false));
    }
}
