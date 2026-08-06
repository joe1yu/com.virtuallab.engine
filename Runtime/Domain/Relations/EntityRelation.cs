using System;
using VirtualLab.Kernel;

namespace VirtualLab.Domain.Relations
{
    public sealed class EntityRelation
    {
        public EntityRelation(RelationTypeId typeId, EntityId source, EntityId target)
            : this(typeId, source, target, null, null)
        {
        }

        public EntityRelation(
            RelationTypeId typeId,
            EntityId source,
            EntityId target,
            string sourcePortId,
            string targetPortId)
        {
            if (string.IsNullOrWhiteSpace(typeId.Value))
            {
                throw new ArgumentException("关系类型标识不能为空。", nameof(typeId));
            }

            TypeId = typeId;
            Source = source;
            Target = target;
            SourcePortId = Optional(sourcePortId);
            TargetPortId = Optional(targetPortId);
            if ((SourcePortId == null) != (TargetPortId == null))
            {
                throw new ArgumentException(
                    "关系必须同时声明来源端口和目标端口。",
                    nameof(sourcePortId));
            }
        }

        public RelationTypeId TypeId { get; }

        public EntityId Source { get; }

        public EntityId Target { get; }

        public string SourcePortId { get; }

        public string TargetPortId { get; }

        public bool HasPortEndpoints => SourcePortId != null;

        private static string Optional(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
