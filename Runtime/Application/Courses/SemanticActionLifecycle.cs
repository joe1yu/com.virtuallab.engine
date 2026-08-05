using System;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 一次抽象操作由输入适配器报告的设备无关阶段。
    /// </summary>
    public enum SemanticActionPhase
    {
        Start,
        Observe,
        Complete,
        Cancel
    }

    /// <summary>
    /// 模块为抽象操作声明的运行方式。
    /// </summary>
    public enum SemanticActionLifecycle
    {
        Instant,
        Continuous,
        Manipulation
    }

    /// <summary>
    /// 内核接受操作后返回给表现层的执行计划。执行方式只是稳定逻辑标识，
    /// 不包含 Unity 类型、动画或资源引用。
    /// </summary>
    public sealed class SemanticActionExecution
    {
        public SemanticActionExecution(
            string operationInstanceId,
            string operationId,
            SemanticActionLifecycle lifecycle,
            string executionModeId,
            SemanticActionPhase phase)
        {
            if (!Enum.IsDefined(typeof(SemanticActionLifecycle), lifecycle))
            {
                throw new ArgumentOutOfRangeException(nameof(lifecycle));
            }

            if (!Enum.IsDefined(typeof(SemanticActionPhase), phase))
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            OperationInstanceId = CourseContractGuard.Required(
                operationInstanceId,
                "操作实例 ID");
            OperationId = CourseContractGuard.Required(
                operationId,
                "抽象操作 ID");
            Lifecycle = lifecycle;
            ExecutionModeId = CourseContractGuard.Required(
                executionModeId,
                "执行方式 ID");
            Phase = phase;
        }

        public string OperationInstanceId { get; }
        public string OperationId { get; }
        public SemanticActionLifecycle Lifecycle { get; }
        public string ExecutionModeId { get; }
        public SemanticActionPhase Phase { get; }
    }
}
