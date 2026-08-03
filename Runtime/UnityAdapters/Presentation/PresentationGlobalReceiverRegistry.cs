using System;
using System.Collections.Generic;

namespace VirtualLab.UnityAdapters.Presentation
{
    /// <summary>
    /// 按表现协议注册全局接收器，目标解析器不再了解具体 UI 协议。
    /// </summary>
    public sealed class PresentationGlobalReceiverRegistry
    {
        private readonly IReadOnlyDictionary<string, object> _receivers;

        public PresentationGlobalReceiverRegistry(
            IEnumerable<KeyValuePair<string, object>> receivers)
        {
            if (receivers == null)
            {
                throw new ArgumentNullException(nameof(receivers));
            }

            var copy = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var receiver in receivers)
            {
                if (string.IsNullOrWhiteSpace(receiver.Key)
                    || receiver.Value == null
                    || !copy.TryAdd(receiver.Key.Trim(), receiver.Value))
                {
                    throw new ArgumentException(
                        "全局表现接收器的协议不能为空、实例不能为空且协议不能重复。",
                        nameof(receivers));
                }
            }

            _receivers = copy;
        }

        public object Resolve(PresentationEffectDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            return _receivers.TryGetValue(
                descriptor.ProtocolId,
                out var receiver)
                ? receiver
                : null;
        }

        public static PresentationGlobalReceiverRegistry CreateDefault(
            IPresentationMessageSink messages,
            IPresentationHighlightSink highlights,
            IInteractionAffordanceSink affordances)
        {
            var receivers = new List<KeyValuePair<string, object>>();
            AddIfPresent(
                receivers,
                BuiltInPresentationEffectCatalog.UiMessageProtocolId,
                messages);
            AddIfPresent(
                receivers,
                BuiltInPresentationEffectCatalog.UiHighlightProtocolId,
                highlights);
            AddIfPresent(
                receivers,
                BuiltInPresentationEffectCatalog
                    .InteractionAffordanceProtocolId,
                affordances);
            return new PresentationGlobalReceiverRegistry(receivers);
        }

        private static void AddIfPresent(
            ICollection<KeyValuePair<string, object>> receivers,
            string protocolId,
            object receiver)
        {
            if (receiver != null)
            {
                receivers.Add(
                    new KeyValuePair<string, object>(protocolId, receiver));
            }
        }
    }
}
