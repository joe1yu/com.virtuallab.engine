using System.Collections.Generic;
using VirtualLab.Domain.Relations;

namespace VirtualLab.Interaction.Relations
{
    /// <summary>
    /// 交互模块拥有的关系类型标识。课程与适配器只能引用这里的稳定协议，
    /// 不应复制关系标识文本。
    /// </summary>
    public static class InteractionRelationTypeIds
    {
        public static readonly RelationTypeId ContainedBy =
            new RelationTypeId("交互.关系.位于容器内");
        public static readonly RelationTypeId Connection =
            new RelationTypeId("交互.关系.连接");
        public static readonly RelationTypeId Cover =
            new RelationTypeId("交互.关系.覆盖");
        public static readonly RelationTypeId FixedBy =
            new RelationTypeId("交互.关系.固定");
        public static readonly RelationTypeId ImmersedIn =
            new RelationTypeId("交互.关系.浸入");
        public static readonly RelationTypeId Contact =
            new RelationTypeId("交互.关系.接触");
        public static readonly RelationTypeId HeldBy =
            new RelationTypeId("交互.关系.持有");
    }

    /// <summary>
    /// 交互模块的关系模式目录。关系基数和端口规则不再散落在内核分支中。
    /// </summary>
    public static class InteractionRelationSchemas
    {
        public static readonly RelationSchema ContainedBy = new RelationSchema(
            InteractionRelationTypeIds.ContainedBy,
            sourceUnique: true);

        public static readonly RelationSchema Connection = new RelationSchema(
            InteractionRelationTypeIds.Connection,
            isDirected: false,
            endpointUnique: true,
            portPolicy: RelationPortPolicy.可选);

        public static readonly RelationSchema Cover = new RelationSchema(
            InteractionRelationTypeIds.Cover,
            sourceUnique: true,
            targetUnique: true);

        public static readonly RelationSchema FixedBy = new RelationSchema(
            InteractionRelationTypeIds.FixedBy,
            sourceUnique: true);

        public static readonly RelationSchema ImmersedIn = new RelationSchema(
            InteractionRelationTypeIds.ImmersedIn,
            sourceUnique: true);

        public static readonly RelationSchema Contact = new RelationSchema(
            InteractionRelationTypeIds.Contact,
            isDirected: false,
            allowSelfRelation: false);

        public static readonly RelationSchema HeldBy = new RelationSchema(
            InteractionRelationTypeIds.HeldBy,
            sourceUnique: true);

        public static IReadOnlyList<RelationSchema> All { get; } =
            new[]
            {
                ContainedBy,
                Connection,
                Cover,
                FixedBy,
                ImmersedIn,
                Contact,
                HeldBy
            };
    }
}
