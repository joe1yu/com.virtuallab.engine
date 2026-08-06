using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using VirtualLab.Application.Events;

namespace VirtualLab.Application.Goals
{
    /// <summary>
    /// 不可变的事件驱动学习目标。匹配事件只记录为达成证据；
    /// 所有前置目标完成后，该目标才会正式完成。
    /// </summary>
    public sealed class GoalDefinition
    {
        private static readonly Regex StableId = new Regex(
            "^[a-z0-9]+(?:[._-][a-z0-9]+)*$",
            RegexOptions.CultureInvariant);

        public GoalDefinition(
            string id,
            IReadOnlyList<string> prerequisiteGoalIds,
            string requiredEventType,
            decimal weight)
            : this(id, prerequisiteGoalIds, requiredEventType, weight, null, null)
        {
        }

        public GoalDefinition(
            string id,
            IReadOnlyList<string> prerequisiteGoalIds,
            string requiredEventType,
            decimal weight,
            string missingConditionCategory,
            string suggestedAction)
        {
            Id = RequireStableId(id, nameof(id), "goal");
            RequiredEventType = RequireStableEventType(requiredEventType, nameof(requiredEventType));
            if (weight < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), "A goal weight cannot be negative.");
            }

            Weight = weight;
            PrerequisiteGoalIds = CopyPrerequisites(prerequisiteGoalIds);
            MissingConditionCategory = NormalizeOptional(
                missingConditionCategory,
                "event " + RequiredEventType);
            SuggestedAction = NormalizeOptional(
                suggestedAction,
                "Produce " + RequiredEventType + ".");
        }

        public string Id { get; }

        public IReadOnlyList<string> PrerequisiteGoalIds { get; }

        /// <summary>为该目标提供达成证据的稳定事件类型。</summary>
        public string RequiredEventType { get; }

        public decimal Weight { get; }

        public string MissingConditionCategory { get; }

        public string SuggestedAction { get; }

        public bool IsSatisfiedBy(DomainEventEnvelope domainEvent)
        {
            if (domainEvent == null)
            {
                throw new ArgumentNullException(nameof(domainEvent));
            }

            return string.Equals(domainEvent.EventType, RequiredEventType, StringComparison.Ordinal);
        }

        internal static string RequireStableId(string value, string parameterName, string kind)
        {
            if (string.IsNullOrWhiteSpace(value) || !StableId.IsMatch(value.Trim()))
            {
                throw new ArgumentException("A " + kind + " ID must be a stable lowercase identifier.", parameterName);
            }

            return value.Trim();
        }

        private static string RequireStableEventType(string value, string parameterName)
        {
            if (!EventTypeProtocol.IsStable(value))
            {
                throw new ArgumentException(
                    "目标所需事件类型必须使用自然中文名称。",
                    parameterName);
            }

            return value.Trim();
        }

        private static IReadOnlyList<string> CopyPrerequisites(IReadOnlyList<string> prerequisites)
        {
            if (prerequisites == null)
            {
                throw new ArgumentNullException(nameof(prerequisites));
            }

            var copy = new List<string>(prerequisites.Count);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prerequisite in prerequisites)
            {
                var normalized = RequireStableId(prerequisite, nameof(prerequisites), "prerequisite goal");
                if (!known.Add(normalized))
                {
                    throw new ArgumentException("A goal cannot repeat a prerequisite ID.", nameof(prerequisites));
                }

                copy.Add(normalized);
            }

            return new ReadOnlyCollection<string>(copy);
        }

        private static string NormalizeOptional(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
