using System;
using System.Text.RegularExpressions;
using VirtualLab.Domain.Events;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Events
{
    /// <summary>
    /// 事件类型的统一命名协议。配置和内部事件都使用分段的自然中文名称，
    /// 所有入口共享同一校验规则。
    /// </summary>
    public static class EventTypeProtocol
    {
        // 使用“类别.事实”的中文分段格式，避免把作者可见协议重新变成内部编码。
        private static readonly Regex NaturalChineseEventType = new Regex(
            "^[\\u3400-\\u4dbf\\u4e00-\\u9fff0-9]+(?:\\.[\\u3400-\\u4dbf\\u4e00-\\u9fff0-9]+)*$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// 判断事件类型是否为无首尾空白、仅含中文分段和数字的稳定名称。
        /// </summary>
        public static bool IsStable(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            return NaturalChineseEventType.IsMatch(value);
        }
    }

    /// <summary>
    /// 为领域事件补充顺序、命令和模拟时刻，作为评价、表现与持久化共享的事件记录。
    /// </summary>
    public sealed class DomainEventEnvelope
    {

        public DomainEventEnvelope(long sequence, string commandId, SimulationTick tick, IDomainEvent domainEvent)
        {
            if (sequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence), "An event sequence must be positive.");
            }

            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("An event command ID cannot be blank.", nameof(commandId));
            }

            if (domainEvent == null)
            {
                throw new ArgumentNullException(nameof(domainEvent));
            }

            if (string.IsNullOrWhiteSpace(domainEvent.EventType))
            {
                throw new ArgumentException("An event type cannot be blank.", nameof(domainEvent));
            }

            if (tick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "An event tick cannot be negative.");
            }

            var eventType = domainEvent.EventType;
            if (!EventTypeProtocol.IsStable(eventType))
            {
                throw new ArgumentException(
                    "事件类型必须使用自然中文名称。",
                    nameof(domainEvent));
            }

            Sequence = sequence;
            CommandId = commandId;
            Tick = tick;
            Event = domainEvent;
            EventType = eventType;
        }

        public long Sequence { get; }

        public string CommandId { get; }

        public SimulationTick Tick { get; }

        public IDomainEvent Event { get; }

        public string EventType { get; }
    }
}
