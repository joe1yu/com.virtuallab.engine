using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Interaction.Capabilities;

namespace VirtualLab.Interaction.Courses
{
    /// <summary>
    /// 交互模块拥有的能力状态编解码器。连接端口和容器容量等专用结构
    /// 在这里转换，通用会话存档不再依赖任何交互运行时类型。
    /// </summary>
    public static class InteractionCapabilityStateCodecs
    {
        public static IReadOnlyList<ICourseCapabilityStateCodec> All { get; } =
            Array.AsReadOnly(new ICourseCapabilityStateCodec[]
            {
                Codec(
                    InteractionCapabilityIds.Grabbable,
                    capability => GenericState(capability),
                    state => new GrabbableCapability()),
                Codec(
                    InteractionCapabilityIds.Container,
                    CaptureContainer,
                    state => new ContainerCapability(state.NumberValue)),
                Codec(
                    InteractionCapabilityIds.Connector,
                    CaptureConnector,
                    RestoreConnector),
                Codec(
                    InteractionCapabilityIds.Observable,
                    capability => GenericState(capability),
                    state => new ObservableCapability()),
                Codec(
                    InteractionCapabilityIds.Clampable,
                    capability => GenericState(capability),
                    state => new ClampableCapability()),
                Codec(
                    InteractionCapabilityIds.Coverable,
                    capability => GenericState(capability),
                    state => new CoverableCapability()),
                Codec(
                    InteractionCapabilityIds.Breakable,
                    capability => GenericState(capability),
                    state => new BreakableCapability())
            });

        private static ICourseCapabilityStateCodec Codec(
            string capabilityId,
            Func<ICapability, CourseCapabilityState> capture,
            Func<CourseCapabilityState, ICapability> restore) =>
            new DelegateCapabilityStateCodec(
                capabilityId,
                capture,
                restore);

        private static CourseCapabilityState CaptureContainer(
            ICapability capability)
        {
            if (capability is ContainerCapability container)
            {
                return new CourseCapabilityState(
                    container.CapabilityId,
                    container.CapacityMillilitres);
            }

            return GenericState(capability);
        }

        private static CourseCapabilityState CaptureConnector(
            ICapability capability)
        {
            if (capability is ConnectorCapability connector)
            {
                return new CourseCapabilityState(
                    connector.CapabilityId,
                    0m,
                    connector.CompatibilityGroup,
                    connector.Ports.Select(port =>
                        new KeyValuePair<string, string>(
                            port.PortId,
                            port.CompatibilityGroup)));
            }

            return GenericState(capability);
        }

        private static ICapability RestoreConnector(
            CourseCapabilityState state)
        {
            return state.TextProperties.Count > 0
                ? new ConnectorCapability(state.TextProperties.Select(port =>
                    new ConnectionPortDefinition(
                        port.Key,
                        port.Value)))
                : new ConnectorCapability(state.TextValue);
        }

        private static CourseCapabilityState GenericState(
            ICapability capability)
        {
            return capability is IConfiguredCapability configured
                ? new CourseCapabilityState(
                    configured.CapabilityId,
                    configured.NumberValue,
                    configured.TextValue)
                : new CourseCapabilityState(capability.CapabilityId);
        }

        private sealed class DelegateCapabilityStateCodec :
            ICourseCapabilityStateCodec
        {
            private readonly Func<ICapability, CourseCapabilityState> _capture;
            private readonly Func<CourseCapabilityState, ICapability> _restore;

            public DelegateCapabilityStateCodec(
                string capabilityId,
                Func<ICapability, CourseCapabilityState> capture,
                Func<CourseCapabilityState, ICapability> restore)
            {
                CapabilityId = capabilityId;
                _capture = capture
                    ?? throw new ArgumentNullException(nameof(capture));
                _restore = restore
                    ?? throw new ArgumentNullException(nameof(restore));
            }

            public string CapabilityId { get; }

            public CourseCapabilityState Capture(ICapability capability) =>
                _capture(capability);

            public ICapability Restore(CourseCapabilityState state) =>
                _restore(state);
        }
    }
}
