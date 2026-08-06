using System;

namespace VirtualLab.Domain.Relations
{
    /// <summary>
    /// 关系使用端口的方式。可选表示实体级端点和端口级端点都是该协议的正式形式。
    /// </summary>
    public enum RelationPortPolicy
    {
        禁止,
        可选,
        必须
    }

    /// <summary>
    /// 模块注册的关系约束。内核只解释这些通用约束，不识别具体关系名称。
    /// </summary>
    public sealed class RelationSchema : IEquatable<RelationSchema>
    {
        public RelationSchema(
            RelationTypeId typeId,
            bool isDirected = true,
            bool allowSelfRelation = false,
            bool sourceUnique = false,
            bool targetUnique = false,
            bool endpointUnique = false,
            RelationPortPolicy portPolicy = RelationPortPolicy.禁止,
            bool allowDuplicateSet = true,
            bool requireExistingOnRemove = true)
        {
            if (string.IsNullOrWhiteSpace(typeId.Value))
            {
                throw new ArgumentException("关系类型标识不能为空。", nameof(typeId));
            }

            TypeId = typeId;
            IsDirected = isDirected;
            AllowSelfRelation = allowSelfRelation;
            SourceUnique = sourceUnique;
            TargetUnique = targetUnique;
            EndpointUnique = endpointUnique;
            PortPolicy = portPolicy;
            AllowDuplicateSet = allowDuplicateSet;
            RequireExistingOnRemove = requireExistingOnRemove;

            if (endpointUnique && portPolicy == RelationPortPolicy.禁止)
            {
                throw new ArgumentException("端点唯一关系必须允许或要求端口。", nameof(endpointUnique));
            }
        }

        public RelationTypeId TypeId { get; }
        public bool IsDirected { get; }
        public bool AllowSelfRelation { get; }
        public bool SourceUnique { get; }
        public bool TargetUnique { get; }
        public bool EndpointUnique { get; }
        public RelationPortPolicy PortPolicy { get; }
        public bool AllowDuplicateSet { get; }
        public bool RequireExistingOnRemove { get; }

        public bool Equals(RelationSchema other) =>
            other != null
            && TypeId == other.TypeId
            && IsDirected == other.IsDirected
            && AllowSelfRelation == other.AllowSelfRelation
            && SourceUnique == other.SourceUnique
            && TargetUnique == other.TargetUnique
            && EndpointUnique == other.EndpointUnique
            && PortPolicy == other.PortPolicy
            && AllowDuplicateSet == other.AllowDuplicateSet
            && RequireExistingOnRemove == other.RequireExistingOnRemove;

        public override bool Equals(object obj) => Equals(obj as RelationSchema);

        public override int GetHashCode() => TypeId.GetHashCode();
    }
}
