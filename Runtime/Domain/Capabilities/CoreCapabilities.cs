using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Domain.Capabilities
{
    /// <summary>
    /// 平台内置能力使用的中文唯一协议。能力类型和课程配置均引用这些值。
    /// </summary>
    public static class CoreCapabilityIds
    {
        public const string Grabbable = "可抓取";
        public const string Container = "容器";
        public const string Connector = "可连接";
        public const string Observable = "可观察";
        public const string Clampable = "可夹持";
        public const string Coverable = "可覆盖";
        public const string Breakable = "可破损";
        public const string PositionableSource = "可定位源";
        public const string PositionableTarget = "可定位目标";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Grabbable,
            Container,
            Connector,
            Observable,
            Clampable,
            Coverable,
            Breakable,
            PositionableSource,
            PositionableTarget
        };
    }

    public sealed class GrabbableCapability : ICapability
    {
    }

    public sealed class ContainerCapability : ICapability
    {
        public ContainerCapability(decimal capacityMillilitres)
        {
            if (capacityMillilitres < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityMillilitres), "Container capacity cannot be negative.");
            }

            CapacityMillilitres = capacityMillilitres;
        }

        public decimal CapacityMillilitres { get; }
    }

    /// <summary>
    /// 连接端口是实体内部可独立占用的稳定端点。端口 ID 对应实验预制体实体节点中的语义锚点，
    /// 兼容组只负责判断两个端口能否建立连接。
    /// </summary>
    public sealed class ConnectionPortDefinition
    {
        public ConnectionPortDefinition(
            string portId,
            string compatibilityGroup)
        {
            if (string.IsNullOrWhiteSpace(portId))
            {
                throw new ArgumentException("连接端口 ID 不能为空。", nameof(portId));
            }

            PortId = portId.Trim();
            CompatibilityGroup = string.IsNullOrWhiteSpace(compatibilityGroup)
                ? null
                : compatibilityGroup.Trim();
        }

        public string PortId { get; }

        public string CompatibilityGroup { get; }
    }

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
        /// 兼容旧的单端口调用。多端口实体必须通过 Ports 读取每个端口的兼容组。
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
