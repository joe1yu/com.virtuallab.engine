using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Capabilities;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Domain;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 固体取用的拒绝原因。课程可在状态变化参数中覆盖提示文案，
    /// 但不得改变判定语义。
    /// </summary>
    public static class SolidMatterPickupRejectionCodes
    {
        public const string ContainerCovered = "固体取用.容器已覆盖";
        public const string ContainerUnsupported = "固体取用.容器不支持";
        public const string ToolUnsupported = "固体取用.工具不支持";
        public const string SubstanceMissing = "固体取用.物质不存在";
        public const string QuantityInvalid = "固体取用.数量无效";
    }

    /// <summary>
    /// 将容器中的定量固体原子转移到工具。Unity 只提交工具和容器观测；
    /// 物质、数量、覆盖关系、工具能力和库存均由化学模块裁决。
    /// </summary>
    public sealed class SolidMatterPickupOperations
    {
        public const string OperationId = "取用固体";
        public const string ToolContentChangedEventType = "工具内容变化";

        public void RegisterWith(ConfiguredStateOperationRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(new PickupOperation());
        }

        /// <summary>
        /// 为定量固体取用创建原子转移与事件发布两个状态变化。
        /// 二者由课程会话在同一世界事务中执行。
        /// </summary>
        public static IReadOnlyList<ConfiguredMutationDefinition>
            CreateMutations(string substanceId, double quantityGrams)
        {
            if (string.IsNullOrWhiteSpace(substanceId))
            {
                throw new ArgumentException("物质标识不能为空。", nameof(substanceId));
            }

            if (double.IsNaN(quantityGrams)
                || double.IsInfinity(quantityGrams)
                || quantityGrams <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantityGrams),
                    "固体取用数量必须是有限正数。");
            }

            var substance = substanceId.Trim();
            var parameters = new[]
            {
                Pair(
                    ChemistryConfigurationKeys.Common.SubstanceId,
                    StructuredValue.FromText(substance)),
                Pair(
                    ChemistryConfigurationKeys.Common.Quantity,
                    StructuredValue.FromNumber(quantityGrams))
            };
            return new[]
            {
                new ConfiguredMutationDefinition(
                    "变化.取用固体",
                    OperationId,
                    parameters),
                new ConfiguredMutationDefinition(
                    "变化.发布工具内容变化",
                    ConfiguredStateOperationIds.EventEmit,
                    parameters.Concat(new[]
                    {
                        Pair(
                            CourseConfigurationKeys.Mutation.EventType,
                            StructuredValue.FromText(
                                ToolContentChangedEventType))
                    }))
            };
        }

        private static KeyValuePair<string, StructuredValue> Pair(
            string key,
            StructuredValue value) =>
            new KeyValuePair<string, StructuredValue>(key, value);

        private sealed class PickupOperation : IConfiguredStateOperation
        {
            public string OperationId => SolidMatterPickupOperations.OperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                if (world == null)
                {
                    throw new ArgumentNullException(nameof(world));
                }

                var toolId = new EntityId(request.SourceEntityId);
                var containerId = new EntityId(request.TargetEntityId
                    ?? throw new ConfiguredOperationRejectedException(
                        new[] { SolidMatterPickupRejectionCodes.ContainerUnsupported }));
                if (!world.TryGetEntity(containerId, out var container)
                    || !container.HasCapability<ContainerCapability>())
                {
                    Reject(
                        mutation,
                        ChemistryConfigurationKeys.SolidPickup
                            .ContainerUnsupportedRejectionReason,
                        SolidMatterPickupRejectionCodes.ContainerUnsupported);
                }

                if (world.Relations.Any(value =>
                        value.TypeId == InteractionRelationTypeIds.Cover
                        && value.Target == containerId))
                {
                    Reject(
                        mutation,
                        ChemistryConfigurationKeys.SolidPickup
                            .ContainerCoveredRejectionReason,
                        SolidMatterPickupRejectionCodes.ContainerCovered);
                }

                if (!world.TryGetEntity(toolId, out var tool)
                    || !tool.HasCapability<SolidMatterCarrierCapability>())
                {
                    Reject(
                        mutation,
                        ChemistryConfigurationKeys.SolidPickup
                            .ToolUnsupportedRejectionReason,
                        SolidMatterPickupRejectionCodes.ToolUnsupported);
                }

                var substanceId = Text(
                    mutation,
                    ChemistryConfigurationKeys.Common.SubstanceId);
                var quantity = Number(
                    mutation,
                    ChemistryConfigurationKeys.Common.Quantity);
                if (quantity <= 0m)
                {
                    Reject(
                        mutation,
                        ChemistryConfigurationKeys.SolidPickup
                            .QuantityInvalidRejectionReason,
                        SolidMatterPickupRejectionCodes.QuantityInvalid);
                }

                var inventory = world.RequireMatterInventory();
                var solidSelection = MatterBatchSelection.InPhase(
                    substanceId,
                    MatterPhase.Solid);
                if (inventory.Total(
                        containerId,
                        solidSelection,
                        ChemistryUnits.Gram).Value < quantity)
                {
                    Reject(
                        mutation,
                        ChemistryConfigurationKeys.SolidPickup
                            .SubstanceMissingRejectionReason,
                        SolidMatterPickupRejectionCodes.SubstanceMissing);
                }

                inventory.Transfer(
                    containerId,
                    toolId,
                    solidSelection,
                    new Quantity(quantity, ChemistryUnits.Gram),
                    new SimulationTick(0),
                    SilentProcessEventCollector.Instance);
            }
        }

        private static string Text(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ConfiguredOperationRejectedException(
                    new[] { SolidMatterPickupRejectionCodes.SubstanceMissing });
            }

            return value.Text.Trim();
        }

        private static decimal Number(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Number
                || value.Number <= 0d
                || value.Number > (double)decimal.MaxValue)
            {
                return 0m;
            }

            return (decimal)value.Number;
        }

        private static void Reject(
            ConfiguredMutationDefinition mutation,
            string configuredReasonKey,
            string fallback)
        {
            var reason = mutation.Parameters.TryGetValue(
                    configuredReasonKey,
                    out var configured)
                && configured.Kind == StructuredValueKind.Text
                && !string.IsNullOrWhiteSpace(configured.Text)
                    ? configured.Text.Trim()
                    : fallback;
            throw new ConfiguredOperationRejectedException(new[] { reason });
        }
    }
}
