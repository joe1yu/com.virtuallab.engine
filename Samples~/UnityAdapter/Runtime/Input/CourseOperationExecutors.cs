using System;
using System.Collections.Generic;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Courses;

namespace VirtualLab.UnityAdapters.Input
{
    /// <summary>
    /// 通用执行器只承接已被内核接受的执行计划。科学状态已由模块事务提交，
    /// 这里不回写库存、温度或反应结果。
    /// </summary>
    public abstract class CourseOperationExecutor : ICourseOperationExecutor
    {
        protected CourseOperationExecutor(string executionModeId)
        {
            ExecutionModeId = string.IsNullOrWhiteSpace(executionModeId)
                ? throw new ArgumentException(
                    "执行方式标识不能为空。",
                    nameof(executionModeId))
                : executionModeId.Trim();
        }

        public string ExecutionModeId { get; }

        public void Execute(
            SemanticActionRequest request,
            SemanticActionExecution execution)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (execution == null)
            {
                throw new ArgumentNullException(nameof(execution));
            }

            OnExecute(request, execution);
        }

        protected virtual void OnExecute(
            SemanticActionRequest request,
            SemanticActionExecution execution)
        {
        }
    }

    public sealed class ImmediateCourseOperationExecutor :
        CourseOperationExecutor
    {
        public ImmediateCourseOperationExecutor()
            : base(CourseOperationExecutionModeIds.Immediate) { }
    }

    public sealed class ContinuousInputCourseOperationExecutor :
        CourseOperationExecutor
    {
        public ContinuousInputCourseOperationExecutor()
            : base(CourseOperationExecutionModeIds.ContinuousInput) { }
    }

    public sealed class DirectManipulationCourseOperationExecutor :
        CourseOperationExecutor
    {
        public DirectManipulationCourseOperationExecutor()
            : base(CourseOperationExecutionModeIds.DirectManipulation) { }
    }

    public static class CourseOperationExecutors
    {
        public static IReadOnlyList<ICourseOperationExecutor> CreateDefault() =>
            new ICourseOperationExecutor[]
            {
                new ImmediateCourseOperationExecutor(),
                new ContinuousInputCourseOperationExecutor(),
                new DirectManipulationCourseOperationExecutor()
            };
    }
}
