using System;

namespace VirtualLab.Presentation
{
    public enum PresentationEntitySelectorKind
    {
        SignalSubject,
        ActionSource,
        ActionTarget,
        PayloadField,
        FixedEntity,
        Global
    }

    public enum PresentationLocationKind
    {
        EntityRoot,
        PresentationSlot,
        SemanticAnchor,
        GlobalReceiver
    }

    /// <summary>
    /// 表现配置中的目标选择规则。该类型只描述语义，不访问场景对象。
    /// </summary>
    public sealed class PresentationTargetSelector
    {
        public PresentationTargetSelector(
            PresentationEntitySelectorKind entityKind,
            string entityValue,
            PresentationLocationKind locationKind,
            string locationId)
        {
            EntityKind = entityKind;
            EntityValue = PresentationContractGuard.Optional(entityValue);
            LocationKind = locationKind;
            LocationId = PresentationContractGuard.Optional(locationId);

            var requiresEntityValue =
                entityKind == PresentationEntitySelectorKind.PayloadField ||
                entityKind == PresentationEntitySelectorKind.FixedEntity;
            if (requiresEntityValue && EntityValue == null)
            {
                throw new ArgumentException(
                    $"实体选择方式“{entityKind}”必须提供字段或实体 ID。",
                    nameof(entityValue));
            }

            var requiresLocationId =
                locationKind == PresentationLocationKind.PresentationSlot ||
                locationKind == PresentationLocationKind.SemanticAnchor;
            if (requiresLocationId && LocationId == null)
            {
                throw new ArgumentException(
                    $"作用位置“{locationKind}”必须提供位置 ID。",
                    nameof(locationId));
            }
        }

        public PresentationEntitySelectorKind EntityKind { get; }

        public string EntityValue { get; }

        public PresentationLocationKind LocationKind { get; }

        public string LocationId { get; }
    }

    /// <summary>
    /// 反应引擎解析后的表现目标引用，Unity 层再把它定位到具体对象。
    /// </summary>
    public sealed class PresentationTargetReference :
        IEquatable<PresentationTargetReference>
    {
        public PresentationTargetReference(
            string entityId,
            PresentationLocationKind locationKind,
            string locationId)
        {
            EntityId = PresentationContractGuard.Optional(entityId);
            LocationKind = locationKind;
            LocationId = PresentationContractGuard.Optional(locationId);

            if (locationKind != PresentationLocationKind.GlobalReceiver &&
                EntityId == null)
            {
                throw new ArgumentException(
                    "非全局表现目标必须解析出实体 ID。",
                    nameof(entityId));
            }

            var requiresLocationId =
                locationKind == PresentationLocationKind.PresentationSlot ||
                locationKind == PresentationLocationKind.SemanticAnchor;
            if (requiresLocationId && LocationId == null)
            {
                throw new ArgumentException(
                    $"作用位置“{locationKind}”必须提供位置 ID。",
                    nameof(locationId));
            }
        }

        public string EntityId { get; }

        public PresentationLocationKind LocationKind { get; }

        public string LocationId { get; }

        public bool Equals(PresentationTargetReference other)
        {
            return other != null &&
                   string.Equals(
                       EntityId,
                       other.EntityId,
                       StringComparison.Ordinal) &&
                   LocationKind == other.LocationKind &&
                   string.Equals(
                       LocationId,
                       other.LocationId,
                       StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PresentationTargetReference);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = EntityId == null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(EntityId);
                hash = (hash * 397) ^ (int)LocationKind;
                hash = (hash * 397) ^ (LocationId == null
                    ? 0
                    : StringComparer.Ordinal.GetHashCode(LocationId));
                return hash;
            }
        }
    }

    /// <summary>
    /// 保留产生表现命令时的语义实体上下文，供需要双端实体的执行器统一准备。
    /// </summary>
    public sealed class PresentationSignalContextReference
    {
        public PresentationSignalContextReference(
            string subjectEntityId,
            string actionSourceEntityId,
            string actionTargetEntityId)
        {
            SubjectEntityId =
                PresentationContractGuard.Optional(subjectEntityId);
            ActionSourceEntityId =
                PresentationContractGuard.Optional(actionSourceEntityId);
            ActionTargetEntityId =
                PresentationContractGuard.Optional(actionTargetEntityId);
        }

        public string SubjectEntityId { get; }

        public string ActionSourceEntityId { get; }

        public string ActionTargetEntityId { get; }
    }
}
