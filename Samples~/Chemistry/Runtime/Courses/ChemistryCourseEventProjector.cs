using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain.Events;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 化学事件的持久化投影。字段名属于化学协议，由化学包负责演进，
    /// 通用课程运行时只保存投影结果，不依赖具体化学事件类型。
    /// </summary>
    public sealed class ChemistryCourseEventProjector : ICourseEventProjector
    {
        public bool TryProject(
            IDomainEvent domainEvent,
            out IReadOnlyDictionary<string, StructuredValue> payload)
        {
            switch (domainEvent)
            {
                case GasGeneratedEvent generated:
                    payload = Reaction(
                        generated.ReactionId,
                        generated.LocationId.Value);
                    return true;
                case ReactionAdvancedEvent advanced:
                    payload = Reaction(
                        advanced.ReactionId,
                        advanced.LocationId.Value);
                    return true;
                case PourAdvancedEvent poured:
                    payload = Transfer(
                        poured.SourceId.Value,
                        poured.TargetId.Value,
                        poured.SubstanceId,
                        (double)poured.TransferredQuantity.Value,
                        poured.TransferredQuantity.Unit.ToString(),
                        poured.ProcessStopped);
                    return true;
                case PourCapacityReachedEvent capacity:
                    payload = Transfer(
                        capacity.SourceId.Value,
                        capacity.TargetId.Value,
                        capacity.SubstanceId,
                        (double)capacity.PreventedQuantity.Value,
                        capacity.PreventedQuantity.Unit.ToString(),
                        true);
                    return true;
                case MixtureTransferredEvent mixture:
                    payload = new Dictionary<string, StructuredValue>
                    {
                        [CourseConfigurationKeys.EventPayload.SourceEntityId] =
                            StructuredValue.FromText(
                            mixture.SourceId.Value),
                        [CourseConfigurationKeys.EventPayload.TargetEntityId] =
                            StructuredValue.FromText(
                            mixture.TargetId.Value),
                        [ChemistryConfigurationKeys.EventPayload.Components] =
                            StructuredValue.FromTextList(
                            mixture.Components.Select(component =>
                                component.SubstanceId
                                + ":"
                                + component.Quantity.Value
                                + ":"
                                + component.Quantity.Unit))
                    };
                    return true;
                default:
                    payload = null;
                    return false;
            }
        }

        private static IReadOnlyDictionary<string, StructuredValue> Reaction(
            string reactionId,
            string locationId) =>
            new Dictionary<string, StructuredValue>
            {
                [ChemistryConfigurationKeys.Common.ReactionId] =
                    StructuredValue.FromText(reactionId),
                [ChemistryConfigurationKeys.EventPayload.LocationEntityId] =
                    StructuredValue.FromText(locationId)
            };

        private static IReadOnlyDictionary<string, StructuredValue> Transfer(
            string sourceId,
            string targetId,
            string substanceId,
            double quantity,
            string unit,
            bool processStopped) =>
            new Dictionary<string, StructuredValue>
            {
                [CourseConfigurationKeys.EventPayload.SourceEntityId] =
                    StructuredValue.FromText(sourceId),
                [CourseConfigurationKeys.EventPayload.TargetEntityId] =
                    StructuredValue.FromText(targetId),
                [CourseConfigurationKeys.EventPayload.SubstanceId] =
                    StructuredValue.FromText(substanceId),
                [CourseConfigurationKeys.EventPayload.Quantity] =
                    StructuredValue.FromNumber(quantity),
                [CourseConfigurationKeys.EventPayload.Unit] =
                    StructuredValue.FromText(unit),
                [ChemistryConfigurationKeys.EventPayload.ProcessStopped] =
                    StructuredValue.FromBoolean(processStopped)
            };
    }
}
