using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VirtualLab.Presentation
{
    public enum PresentationTriggerKind
    {
        ActionAccepted,
        ActionRejected,
        DomainEvent,
        StateEntered,
        StateActive,
        StateExited,
        ActionAvailabilityChanged,
        CourseInitialized
    }

    public static class PresentationSignalIds
    {
        public const string CourseInitialized = "course.initialized";
    }

    /// <summary>
    /// 表现规则的输入信号，只携带语义触发和结构化载荷。
    /// </summary>
    public sealed class PresentationSignal
    {
        public PresentationSignal(
            string signalId,
            PresentationTriggerKind triggerKind,
            string subjectEntityId,
            string actionSourceEntityId,
            string actionTargetEntityId,
            IEnumerable<KeyValuePair<string, PresentationValue>> payload)
        {
            SignalId = PresentationContractGuard.Required(
                signalId,
                "表现信号 ID");
            TriggerKind = triggerKind;
            SubjectEntityId = PresentationContractGuard.Optional(
                subjectEntityId);
            ActionSourceEntityId = PresentationContractGuard.Optional(
                actionSourceEntityId);
            ActionTargetEntityId = PresentationContractGuard.Optional(
                actionTargetEntityId);
            Payload = PresentationContractGuard.CopyValues(
                payload,
                $"表现信号“{SignalId}”");
        }

        public string SignalId { get; }

        public PresentationTriggerKind TriggerKind { get; }

        public string SubjectEntityId { get; }

        public string ActionSourceEntityId { get; }

        public string ActionTargetEntityId { get; }

        public IReadOnlyDictionary<string, PresentationValue> Payload { get; }
    }

    internal static class PresentationContractGuard
    {
        public static string Required(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    $"{context}不能为空。",
                    nameof(value));
            }

            return value.Trim();
        }

        public static string Optional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static IReadOnlyDictionary<string, PresentationValue> CopyValues(
            IEnumerable<KeyValuePair<string, PresentationValue>> values,
            string context)
        {
            if (values == null)
            {
                throw new ArgumentNullException(
                    nameof(values),
                    $"{context}的参数集合不能为空。");
            }

            var copy = new Dictionary<string, PresentationValue>(
                StringComparer.Ordinal);
            foreach (var pair in values)
            {
                var key = Required(pair.Key, $"{context}的参数名");
                if (pair.Value == null || !copy.TryAdd(key, pair.Value))
                {
                    throw new ArgumentException(
                        $"{context}的参数“{key}”为空或重复。",
                        nameof(values));
                }
            }

            return new ReadOnlyDictionary<string, PresentationValue>(copy);
        }
    }
}
