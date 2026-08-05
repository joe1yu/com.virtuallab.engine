using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Domain.Capabilities
{
    /// <summary>
    /// 实体可以被操作者抓取。
    /// </summary>
    public sealed class GrabbableCapability : ICapability
    {
    }

    /// <summary>
    /// 实体可以容纳其他对象或定量内容。
    /// </summary>
    public sealed class ContainerCapability : ICapability
    {
        public ContainerCapability(decimal capacityMillilitres)
        {
            if (capacityMillilitres < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacityMillilitres),
                    "容器容量不能为负数。");
            }

            CapacityMillilitres = capacityMillilitres;
        }

        public decimal CapacityMillilitres { get; }
    }

    /// <summary>
    /// 连接端口是实体内部可独立占用的稳定端点。端口 ID 对应实验预制体
    /// 实体节点中的语义锚点，兼容组只负责判断两个端口能否建立连接。
    /// </summary>
    public sealed class ConnectionPortDefinition
    {
        public ConnectionPortDefinition(
            string portId,
            string compatibilityGroup)
        {
            if (string.IsNullOrWhiteSpace(portId))
            {
                throw new ArgumentException(
                    "连接端口 ID 不能为空。",
                    nameof(portId));
            }

            PortId = portId.Trim();
            CompatibilityGroup = string.IsNullOrWhiteSpace(compatibilityGroup)
                ? null
                : compatibilityGroup.Trim();
        }

        public string PortId { get; }

        public string CompatibilityGroup { get; }
    }

    /// <summary>
    /// 实体通过一个或多个独立端口参与连接。
    /// </summary>
    public sealed class ConnectorCapability : ICapability
    {
        public ConnectorCapability()
            : this(Array.Empty<ConnectionPortDefinition>())
        {
        }

        public ConnectorCapability(string compatibilityGroup)
            : this(string.IsNullOrWhiteSpace(compatibilityGroup)
                ? Array.Empty<ConnectionPortDefinition>()
                : new[]
                {
                    new ConnectionPortDefinition(
                        "默认端口",
                        compatibilityGroup)
                })
        {
        }

        public ConnectorCapability(
            IEnumerable<ConnectionPortDefinition> ports)
        {
            if (ports == null)
            {
                throw new ArgumentNullException(nameof(ports));
            }

            var copy = ports.ToArray();
            if (copy.Any(value => value == null))
            {
                throw new ArgumentException(
                    "连接端口集合不能包含空项。",
                    nameof(ports));
            }

            var duplicate = copy
                .GroupBy(value => value.PortId, StringComparer.Ordinal)
                .FirstOrDefault(value => value.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException(
                    $"连接端口 ID“{duplicate.Key}”重复。",
                    nameof(ports));
            }

            Ports = new ReadOnlyCollection<ConnectionPortDefinition>(copy);
        }

        /// <summary>
        /// 单端口能力的兼容组；多端口能力必须从 <see cref="Ports"/>
        /// 读取各端口的兼容组。
        /// </summary>
        public string CompatibilityGroup =>
            Ports.Count == 1 ? Ports[0].CompatibilityGroup : null;

        public IReadOnlyList<ConnectionPortDefinition> Ports { get; }

        public bool TryGetPort(
            string portId,
            out ConnectionPortDefinition port)
        {
            port = Ports.FirstOrDefault(value => string.Equals(
                value.PortId,
                portId,
                StringComparison.Ordinal));
            return port != null;
        }
    }

    public sealed class ObservableCapability : ICapability
    {
    }

    public sealed class ClampableCapability : ICapability
    {
    }

    public sealed class CoverableCapability : ICapability
    {
    }

    public sealed class BreakableCapability : ICapability
    {
    }
}
