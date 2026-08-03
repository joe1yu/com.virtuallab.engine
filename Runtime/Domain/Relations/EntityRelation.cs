using System;
using VirtualLab.Kernel;

namespace VirtualLab.Domain.Relations
{
    public sealed class EntityRelation
    {
        public EntityRelation(RelationKind kind, EntityId source, EntityId target)
            : this(kind, source, target, null, null)
        {
        }

        public EntityRelation(
            RelationKind kind,
            EntityId source,
            EntityId target,
            string sourcePortId,
            string targetPortId)
        {
            Kind = kind;
            Source = source;
            Target = target;
            SourcePortId = Optional(sourcePortId);
            TargetPortId = Optional(targetPortId);
            if ((SourcePortId == null) != (TargetPortId == null))
            {
                throw new ArgumentException(
                    "连接关系必须同时声明来源端口和目标端口。",
                    nameof(sourcePortId));
            }

            if (Kind != RelationKind.连接对象 && HasPortEndpoints)
            {
                throw new ArgumentException(
                    "只有连接关系可以声明端口端点。",
                    nameof(sourcePortId));
            }
        }

        public RelationKind Kind { get; }

        public EntityId Source { get; }

        public EntityId Target { get; }

        public string SourcePortId { get; }

        public string TargetPortId { get; }

        public bool HasPortEndpoints => SourcePortId != null;

        private static string Optional(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
