using System;
using VirtualLab.Domain.Events;
using VirtualLab.Kernel;

namespace VirtualLab.Domain.Processes
{
    /// <summary>
    /// 接收过程产生的事件，并保证状态变化与事件历史同步提交。
    /// </summary>
    public interface IProcessEventCollector
    {
        /// <summary>
        /// 原子提交一个领域事件及其已准备好的状态变更。实现方必须先完成全部
        /// 校验和资源准备，再且仅调用一次 <paramref name="commitState"/>；之后只能
        /// 通过不会抛出异常的引用赋值发布事件。准备失败时不得执行回调，也不得
        /// 改变领域状态或事件历史。
        /// </summary>
        void CommitAtomically(
            string commandId,
            SimulationTick tick,
            IDomainEvent domainEvent,
            Action commitState);
    }

    /// <summary>
    /// 按模拟时刻推进一个持续过程，并通过事件收集器提交产生的状态变化。
    /// </summary>
    public interface IProcessHandler
    {
        void Advance(ExperimentWorld world, SimulationTick tick, IProcessEventCollector events);
    }
}
