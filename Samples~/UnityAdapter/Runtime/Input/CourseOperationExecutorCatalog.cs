using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Courses;

namespace VirtualLab.UnityAdapters.Input
{
    public interface ICourseOperationExecutor
    {
        string ExecutionModeId { get; }

        void Execute(
            SemanticActionRequest request,
            SemanticActionExecution execution);
    }

    /// <summary>
    /// 一个执行方式只能有一个 Unity 执行器。目录在课程启动时一次性构建，
    /// 避免全局可变注册导致不同会话互相影响。
    /// </summary>
    public sealed class CourseOperationExecutorCatalog
    {
        public const string MissingExecutorRejectionCode =
            "课程配置.执行方式未注册";

        private readonly IReadOnlyDictionary<string, ICourseOperationExecutor>
            _executors;

        public CourseOperationExecutorCatalog(
            IEnumerable<ICourseOperationExecutor> executors)
        {
            var values = new Dictionary<string, ICourseOperationExecutor>(
                StringComparer.Ordinal);
            foreach (var executor in executors
                         ?? throw new ArgumentNullException(nameof(executors)))
            {
                if (executor == null
                    || string.IsNullOrWhiteSpace(executor.ExecutionModeId))
                {
                    throw new ArgumentException(
                        "执行器及其执行方式标识不能为空。",
                        nameof(executors));
                }

                var executionModeId = executor.ExecutionModeId.Trim();
                if (!values.TryAdd(executionModeId, executor))
                {
                    throw new InvalidOperationException(
                        $"执行方式“{executionModeId}”存在多个执行器。");
                }
            }

            _executors =
                new ReadOnlyDictionary<string, ICourseOperationExecutor>(
                    values);
        }

        public IReadOnlyList<string> ExecutionModeIds =>
            _executors.Keys.OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

        public bool TryGet(
            string executionModeId,
            out ICourseOperationExecutor executor) =>
            _executors.TryGetValue(
                executionModeId?.Trim() ?? string.Empty,
                out executor);
    }
}
