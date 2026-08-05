using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public sealed class CourseAuthoringSectionStatus
    {
        public CourseAuthoringSectionStatus(
            string displayName,
            IEnumerable<string> missingItems)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(
                nameof(displayName));
            MissingItems = (missingItems ?? Array.Empty<string>()).ToArray();
        }

        public string DisplayName { get; }
        public IReadOnlyList<string> MissingItems { get; }
        public bool IsComplete => MissingItems.Count == 0;
    }

    /// <summary>
    /// 面向课程作者的六区工作流。状态按当前草稿即时计算，不保存第二份完成标记。
    /// </summary>
    public sealed class CourseAuthoringWorkflow
    {
        private static readonly string[] SectionNames =
        {
            "课程信息",
            "实验用品",
            "装置与初始状态",
            "操作与科学过程",
            "教学目标与错误后果",
            "检查与生成"
        };

        public CourseAuthoringWorkflow(CourseAuthoringSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public CourseAuthoringSession Session { get; }
        public IReadOnlyList<CourseAuthoringSectionStatus> Sections =>
            BuildSections();

        public CourseAuthoringSectionStatus Section(string displayName) =>
            Sections.Single(value => value.DisplayName == displayName);

        private IReadOnlyList<CourseAuthoringSectionStatus> BuildSections()
        {
            var draft = Session.Draft;
            var courseMissing = new List<string>();
            Missing(courseMissing, draft.Course.CourseId, "课程标识");
            Missing(courseMissing, draft.Course.DisplayName, "显示名称");
            Missing(courseMissing, draft.Course.DisciplinePackageId, "学科配方包");
            Missing(courseMissing, draft.Course.ActorEntityId, "操作者实体");
            Missing(courseMissing, draft.Course.ExperimentPrefabPath, "实验预制体路径");

            var supplies = draft.Objects.Where(value =>
                value.EntityId != draft.Course.ActorEntityId).ToArray();
            var supplyMissing = supplies.Length == 0
                ? new[] { "至少添加一种实验用品" }
                : Array.Empty<string>();
            var setupMissing = supplies.Any(value =>
                    string.IsNullOrWhiteSpace(value.InitialPosition)
                    || string.IsNullOrWhiteSpace(value.InitialRotation))
                ? new[] { "补全用品初始位置和旋转" }
                : Array.Empty<string>();
            var hasOperations = supplies.Any(value =>
                Session.Catalog.TryGetTemplate(value.EntityType, out var template)
                && template.OperationIds.Count > 0)
                || draft.OperationOverrides.Count > 0
                || draft.Processes.Count > 0;
            var operationMissing = hasOperations
                ? Array.Empty<string>()
                : new[] { "至少让一种用品具备操作，或配置一个科学过程" };
            var teachingMissing = draft.TeachingItems.Count == 0
                ? new[] { "至少配置一个教学目标或错误后果" }
                : Array.Empty<string>();
            var reviewMissing = draft.AcceptanceRecords.Count == 0
                ? new[] { "至少配置一个验收场景记录" }
                : Array.Empty<string>();

            var missingBySection = new IReadOnlyList<string>[]
            {
                courseMissing,
                supplyMissing,
                setupMissing,
                operationMissing,
                teachingMissing,
                reviewMissing
            };
            return SectionNames.Select((name, index) =>
                new CourseAuthoringSectionStatus(
                    name,
                    missingBySection[index])).ToArray();
        }

        private static void Missing(
            ICollection<string> target,
            string value,
            string displayName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                target.Add("填写" + displayName);
            }
        }
    }
}
