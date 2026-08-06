using System;
using System.Linq;
using VirtualLab.Domain;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Kernel;

namespace VirtualLab.Interaction.Relations
{
    /// <summary>
    /// 一次确定性端口选择的结果。这里仅保存领域实体和端口，不依赖输入设备或课程请求。
    /// </summary>
    public sealed class ConnectionPortPair
    {
        public ConnectionPortPair(
            EntityId sourceEntityId,
            ConnectionPortDefinition sourcePort,
            EntityId targetEntityId,
            ConnectionPortDefinition targetPort)
        {
            SourceEntityId = sourceEntityId;
            SourcePort = sourcePort
                ?? throw new ArgumentNullException(nameof(sourcePort));
            TargetEntityId = targetEntityId;
            TargetPort = targetPort
                ?? throw new ArgumentNullException(nameof(targetPort));
        }

        public EntityId SourceEntityId { get; }
        public ConnectionPortDefinition SourcePort { get; }
        public EntityId TargetEntityId { get; }
        public ConnectionPortDefinition TargetPort { get; }
    }

    /// <summary>
    /// 按实体、可选首选端口和兼容组选择连接端点。鼠标、触控和课程配置
    /// 先把各自输入转换为这些纯领域参数，再共享同一套确定性选择规则。
    /// </summary>
    public static class ConnectionPortResolver
    {
        public static bool TryResolve(
            ExperimentWorld world,
            EntityId sourceId,
            EntityId targetId,
            string preferredSourcePortId,
            string preferredTargetPortId,
            out ConnectionPortPair pair)
        {
            if (!TryResolveEndpoints(
                    world,
                    sourceId,
                    targetId,
                    preferredSourcePortId,
                    preferredTargetPortId,
                    out pair))
            {
                return false;
            }

            if (pair.SourcePort.CompatibilityGroup != null
                && string.Equals(
                    pair.SourcePort.CompatibilityGroup,
                    pair.TargetPort.CompatibilityGroup,
                    StringComparison.Ordinal))
            {
                return true;
            }

            // 首个端口不兼容时继续寻找稳定排序后的兼容组合。
            return TryResolveCompatiblePair(
                world,
                sourceId,
                targetId,
                preferredSourcePortId,
                preferredTargetPortId,
                out pair);
        }

        /// <summary>
        /// 即使两端不兼容也返回候选端口，供事实读取器同时报告占用和不兼容原因。
        /// </summary>
        public static bool TryResolveForFacts(
            ExperimentWorld world,
            EntityId sourceId,
            EntityId targetId,
            string preferredSourcePortId,
            string preferredTargetPortId,
            out ConnectionPortPair pair)
        {
            return TryResolve(
                    world,
                    sourceId,
                    targetId,
                    preferredSourcePortId,
                    preferredTargetPortId,
                    out pair)
                || TryResolveEndpoints(
                    world,
                    sourceId,
                    targetId,
                    preferredSourcePortId,
                    preferredTargetPortId,
                    out pair);
        }

        public static EntityRelation FindConnection(
            ExperimentWorld world,
            EntityId entityId,
            string portId)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            return world.Relations.FirstOrDefault(value =>
                value.TypeId == InteractionRelationTypeIds.Connection
                && ((!value.HasPortEndpoints
                        && (value.Source == entityId
                            || value.Target == entityId))
                    || (value.Source == entityId
                        && string.Equals(
                            value.SourcePortId,
                            portId,
                            StringComparison.Ordinal))
                    || (value.Target == entityId
                        && string.Equals(
                            value.TargetPortId,
                            portId,
                            StringComparison.Ordinal))));
        }

        private static bool TryResolveEndpoints(
            ExperimentWorld world,
            EntityId sourceId,
            EntityId targetId,
            string preferredSourcePortId,
            string preferredTargetPortId,
            out ConnectionPortPair pair)
        {
            pair = null;
            if (world == null
                || !world.TryGetEntity(sourceId, out var source)
                || !world.TryGetEntity(targetId, out var target)
                || !source.HasCapability<ConnectorCapability>()
                || !target.HasCapability<ConnectorCapability>())
            {
                return false;
            }

            var sourcePorts = FilterPorts(
                source.GetCapability<ConnectorCapability>(),
                preferredSourcePortId);
            var targetPorts = FilterPorts(
                target.GetCapability<ConnectorCapability>(),
                preferredTargetPortId).ToArray();
            var sourcePort = sourcePorts.FirstOrDefault();
            var targetPort = targetPorts.FirstOrDefault();
            if (sourcePort == null || targetPort == null)
            {
                return false;
            }

            pair = new ConnectionPortPair(
                sourceId,
                sourcePort,
                targetId,
                targetPort);
            return true;
        }

        private static bool TryResolveCompatiblePair(
            ExperimentWorld world,
            EntityId sourceId,
            EntityId targetId,
            string preferredSourcePortId,
            string preferredTargetPortId,
            out ConnectionPortPair pair)
        {
            pair = null;
            if (world == null
                || !world.TryGetEntity(sourceId, out var source)
                || !world.TryGetEntity(targetId, out var target))
            {
                return false;
            }

            var sourcePorts = FilterPorts(
                source.GetCapability<ConnectorCapability>(),
                preferredSourcePortId);
            var targetPorts = FilterPorts(
                target.GetCapability<ConnectorCapability>(),
                preferredTargetPortId).ToArray();
            foreach (var sourcePort in sourcePorts)
            {
                var targetPort = targetPorts.FirstOrDefault(value =>
                    sourcePort.CompatibilityGroup != null
                    && string.Equals(
                        sourcePort.CompatibilityGroup,
                        value.CompatibilityGroup,
                        StringComparison.Ordinal));
                if (targetPort == null)
                {
                    continue;
                }

                pair = new ConnectionPortPair(
                    sourceId,
                    sourcePort,
                    targetId,
                    targetPort);
                return true;
            }

            return false;
        }

        private static IOrderedEnumerable<ConnectionPortDefinition> FilterPorts(
            ConnectorCapability capability,
            string preferredPortId)
        {
            return capability.Ports
                .Where(value => string.IsNullOrWhiteSpace(preferredPortId)
                    || string.Equals(
                        value.PortId,
                        preferredPortId,
                        StringComparison.Ordinal))
                .OrderBy(value => value.PortId, StringComparer.Ordinal);
        }
    }
}
