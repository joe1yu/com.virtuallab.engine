using System;
using System.Collections.Generic;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public enum CourseWorkbenchStepStatus
    {
        Pending,
        Current,
        NeedsAttention,
        Complete
    }

    public enum CourseWorkbenchRecommendedAction
    {
        CreateCourse,
        AddObject,
        CompleteObject,
        ReviewDiagnostics,
        ReloadCourse,
        SaveAndBuild,
        None
    }

    public sealed class CourseWorkbenchGuideStep
    {
        internal CourseWorkbenchGuideStep(
            string title,
            string description,
            CourseWorkbenchStepStatus status)
        {
            Title = title;
            Description = description;
            Status = status;
        }

        public string Title { get; }
        public string Description { get; }
        public CourseWorkbenchStepStatus Status { get; }
    }

    public sealed class CourseWorkbenchGuideState
    {
        internal CourseWorkbenchGuideState(
            IEnumerable<CourseWorkbenchGuideStep> steps,
            CourseWorkbenchRecommendedAction recommendedAction,
            string recommendation)
        {
            Steps = new List<CourseWorkbenchGuideStep>(steps).AsReadOnly();
            RecommendedAction = recommendedAction;
            Recommendation = recommendation ?? string.Empty;
        }

        public IReadOnlyList<CourseWorkbenchGuideStep> Steps { get; }
        public CourseWorkbenchRecommendedAction RecommendedAction { get; }
        public string Recommendation { get; }
        public int CompletedStepCount
        {
            get
            {
                var count = 0;
                foreach (var step in Steps)
                {
                    if (step.Status == CourseWorkbenchStepStatus.Complete)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>
    /// 将工作台的技术状态收敛为用户能理解的制作步骤。该类不依赖窗口控件，
    /// 便于确保不同空状态和错误路径始终只推荐一个明确的下一步。
    /// </summary>
    public static class CourseWorkbenchGuide
    {
        public static CourseWorkbenchGuideState Evaluate(
            bool hasCourse,
            int objectCount,
            int incompleteObjectCount,
            bool hasCompilation,
            bool compilationSucceeded,
            int diagnosticCount,
            bool hasUnsavedChanges,
            bool hasCurrentGeneratedAsset,
            bool hasExternalChanges)
        {
            if (!hasCourse)
            {
                return State(
                    Step("1. 选择课程", "选择已有课程，或新建一门课程。", CourseWorkbenchStepStatus.Current),
                    Step("2. 准备对象", "在实验总预制体中放置对象，并声明对象能力。", CourseWorkbenchStepStatus.Pending),
                    Step("3. 检查配置", "让工作台自动生成并检查规则。", CourseWorkbenchStepStatus.Pending),
                    Step("4. 生成课程", "保存 CSV，并生成运行时课程资产。", CourseWorkbenchStepStatus.Pending),
                    CourseWorkbenchRecommendedAction.CreateCourse,
                    "先选择上方已有课程，或新建一门课程。");
            }

            var objectStatus = objectCount == 0
                ? CourseWorkbenchStepStatus.Current
                : incompleteObjectCount > 0
                    ? CourseWorkbenchStepStatus.NeedsAttention
                    : CourseWorkbenchStepStatus.Complete;
            var checkStatus = objectStatus != CourseWorkbenchStepStatus.Complete
                ? CourseWorkbenchStepStatus.Pending
                : !hasCompilation
                    ? CourseWorkbenchStepStatus.Current
                    : compilationSucceeded
                        ? CourseWorkbenchStepStatus.Complete
                        : CourseWorkbenchStepStatus.NeedsAttention;
            var buildStatus = checkStatus != CourseWorkbenchStepStatus.Complete
                ? CourseWorkbenchStepStatus.Pending
                : hasCurrentGeneratedAsset && !hasUnsavedChanges
                    ? CourseWorkbenchStepStatus.Complete
                    : CourseWorkbenchStepStatus.Current;

            var steps = new[]
            {
                Step("1. 选择课程", "当前课程已经载入。", CourseWorkbenchStepStatus.Complete),
                Step("2. 准备对象", ObjectDescription(objectCount, incompleteObjectCount), objectStatus),
                Step("3. 检查配置", CheckDescription(hasCompilation, compilationSucceeded, diagnosticCount), checkStatus),
                Step("4. 生成课程", BuildDescription(hasUnsavedChanges, hasCurrentGeneratedAsset), buildStatus)
            };

            if (hasExternalChanges)
            {
                return State(
                    steps[0], steps[1], steps[2],
                    Step("4. 生成课程", "外部 CSV 已变化，保存前必须重新载入。", CourseWorkbenchStepStatus.NeedsAttention),
                    CourseWorkbenchRecommendedAction.ReloadCourse,
                    "检测到 Excel 修改了课程表。请先重新载入，避免覆盖外部内容。");
            }

            if (objectCount == 0)
            {
                return State(steps, CourseWorkbenchRecommendedAction.AddObject,
                    "添加第一个实验对象，在实验总预制体中放置同 ID 对象，然后声明对象能力。");
            }

            if (incompleteObjectCount > 0)
            {
                return State(steps, CourseWorkbenchRecommendedAction.CompleteObject,
                    $"有 {incompleteObjectCount} 个对象尚未填写对象 ID 或显示名称。先补全第一个对象。");
            }

            if (!hasCompilation || !compilationSucceeded)
            {
                return State(steps, CourseWorkbenchRecommendedAction.ReviewDiagnostics,
                    diagnosticCount > 0
                        ? $"配置有 {diagnosticCount} 个问题。先处理下方第一条诊断，再继续。"
                        : "等待自动检查配置，或点击“刷新检查”。");
            }

            if (!hasCurrentGeneratedAsset || hasUnsavedChanges)
            {
                return State(steps, CourseWorkbenchRecommendedAction.SaveAndBuild,
                    hasUnsavedChanges
                        ? "配置已经通过检查。点击“保存并生成课程”完成本次修改。"
                        : "配置已经通过检查。点击“生成课程”创建可运行的课程资产。");
            }

            return State(steps, CourseWorkbenchRecommendedAction.None,
                "本次配置已保存并生成课程资产，可以进入场景验证实验流程。");
        }

        private static string ObjectDescription(int count, int incompleteCount)
        {
            if (count == 0)
            {
                return "还没有实验对象。";
            }

            return incompleteCount == 0
                ? $"{count} 个对象的基础信息完整。"
                : $"{count} 个对象中有 {incompleteCount} 个待补全。";
        }

        private static string CheckDescription(
            bool attempted,
            bool succeeded,
            int diagnosticCount)
        {
            if (!attempted)
            {
                return "尚未检查配置。";
            }

            return succeeded
                ? "对象、规则和表现配置已通过检查。"
                : $"发现 {diagnosticCount} 个配置问题。";
        }

        private static string BuildDescription(bool dirty, bool built)
        {
            if (built && !dirty)
            {
                return "本次配置已保存并生成课程资产。";
            }

            return dirty ? "有修改尚未保存和生成。" : "等待生成课程资产。";
        }

        private static CourseWorkbenchGuideStep Step(
            string title,
            string description,
            CourseWorkbenchStepStatus status) =>
            new CourseWorkbenchGuideStep(title, description, status);

        private static CourseWorkbenchGuideState State(
            CourseWorkbenchGuideStep first,
            CourseWorkbenchGuideStep second,
            CourseWorkbenchGuideStep third,
            CourseWorkbenchGuideStep fourth,
            CourseWorkbenchRecommendedAction action,
            string recommendation) =>
            State(new[] { first, second, third, fourth }, action, recommendation);

        private static CourseWorkbenchGuideState State(
            IEnumerable<CourseWorkbenchGuideStep> steps,
            CourseWorkbenchRecommendedAction action,
            string recommendation) =>
            new CourseWorkbenchGuideState(steps, action, recommendation);
    }
}
