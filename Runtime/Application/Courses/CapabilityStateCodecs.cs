using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Domain.Capabilities;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 由能力声明模块提供的状态编解码器。通用会话只按稳定能力标识路由，
    /// 不认识容器、连接器或具体学科能力的运行时类型。
    /// </summary>
    public interface ICourseCapabilityStateCodec
    {
        string CapabilityId { get; }

        CourseCapabilityState Capture(ICapability capability);

        ICapability Restore(CourseCapabilityState state);
    }

    /// <summary>
    /// 模块组合后冻结的能力状态编解码表。未注册的配置型能力仍可按通用
    /// 数值和文本载荷保存；带专用结构的能力必须由所属模块显式注册。
    /// </summary>
    public sealed class CourseCapabilityStateCodecRegistry
    {
        private readonly IReadOnlyDictionary<string, ICourseCapabilityStateCodec>
            _codecs;

        public CourseCapabilityStateCodecRegistry(
            IEnumerable<ICourseCapabilityStateCodec> codecs = null)
        {
            var byId = new Dictionary<string, ICourseCapabilityStateCodec>(
                StringComparer.Ordinal);
            foreach (var codec in codecs
                         ?? Array.Empty<ICourseCapabilityStateCodec>())
            {
                if (codec == null)
                {
                    throw new ArgumentException(
                        "能力状态编解码器集合不能包含空项。",
                        nameof(codecs));
                }

                var capabilityId = CourseContractGuard.Required(
                    codec.CapabilityId,
                    "能力状态编解码器的能力标识");
                if (!byId.TryAdd(capabilityId, codec))
                {
                    throw new InvalidOperationException(
                        $"能力“{capabilityId}”重复注册了状态编解码器。");
                }
            }

            _codecs = new ReadOnlyDictionary<
                string,
                ICourseCapabilityStateCodec>(byId);
        }

        public IReadOnlyCollection<string> CapabilityIds =>
            new ReadOnlyCollection<string>(_codecs.Keys.ToArray());

        public CourseCapabilityState Capture(ICapability capability)
        {
            if (capability == null)
            {
                throw new ArgumentNullException(nameof(capability));
            }

            if (_codecs.TryGetValue(capability.CapabilityId, out var codec))
            {
                var state = codec.Capture(capability)
                    ?? throw new InvalidOperationException(
                        $"能力“{capability.CapabilityId}”的状态编解码器返回了空状态。");
                EnsureMatchingId(capability.CapabilityId, state.CapabilityId);
                return state;
            }

            if (capability is IConfiguredCapability configured)
            {
                return new CourseCapabilityState(
                    configured.CapabilityId,
                    configured.NumberValue,
                    configured.TextValue);
            }

            throw new InvalidOperationException(
                $"能力“{capability.CapabilityId}”未由所属模块注册状态编解码器。");
        }

        public ICapability Restore(CourseCapabilityState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (_codecs.TryGetValue(state.CapabilityId, out var codec))
            {
                var capability = codec.Restore(state)
                    ?? throw new InvalidOperationException(
                        $"能力“{state.CapabilityId}”的状态编解码器返回了空能力。");
                EnsureMatchingId(state.CapabilityId, capability.CapabilityId);
                return capability;
            }

            return new ConfiguredCapability(
                state.CapabilityId,
                state.NumberValue,
                state.TextValue);
        }

        private static void EnsureMatchingId(string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"能力状态编解码器声明“{expected}”，却返回了“{actual}”。");
            }
        }
    }
}
