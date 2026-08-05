using System;
using VirtualLab.Kernel;

namespace VirtualLab.Domain.WorldStates
{
    /// <summary>
    /// 模块附加到实验世界的权威状态契约。
    /// 内核只负责状态的复制、原子替换和实体引用清理，不解释具体状态含义。
    /// </summary>
    public interface IWorldStateExtension
    {
        WorldStateTypeId TypeId { get; }

        /// <summary>
        /// 创建独立副本。副本必须使用目标世界的实体存在性检查，不能继续引用来源世界。
        /// </summary>
        IWorldStateExtension CreateCopy(Func<EntityId, bool> entityExists);

        /// <summary>
        /// 在世界开始提交前验证来源状态。验证成功后，<see cref="ReplaceStateFrom"/>
        /// 对同一个来源不得再因状态内容抛出异常。
        /// </summary>
        void ValidateReplacement(IWorldStateExtension source);

        /// <summary>
        /// 用已验证的独立状态替换当前内容。
        /// </summary>
        void ReplaceStateFrom(IWorldStateExtension source);

        /// <summary>
        /// 在实体删除事务的临时副本中清理对该实体的全部引用。
        /// </summary>
        void RemoveEntityReferences(EntityId entityId);
    }
}
