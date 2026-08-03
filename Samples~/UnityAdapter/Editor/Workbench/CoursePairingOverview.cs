using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Unity.Authoring.Normalized;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public sealed class CoursePairingOperationSummary
    {
        internal CoursePairingOperationSummary(
            string operationName,
            string policyEffect)
        {
            OperationName = operationName ?? string.Empty;
            PolicyEffect = policyEffect ?? string.Empty;
        }

        public string OperationName { get; }
        public string PolicyEffect { get; }
    }

    public sealed class CoursePairingSummary
    {
        internal CoursePairingSummary(
            string otherEntityId,
            bool selectedIsSource,
            IEnumerable<CoursePairingOperationSummary> operations)
        {
            OtherEntityId = otherEntityId ?? string.Empty;
            SelectedIsSource = selectedIsSource;
            Operations = (operations
                          ?? Array.Empty<CoursePairingOperationSummary>())
                .ToArray();
        }

        public string OtherEntityId { get; }
        public bool SelectedIsSource { get; }
        public IReadOnlyList<CoursePairingOperationSummary> Operations { get; }
    }

    /// <summary>
    /// 从编译后的真实动作反推对象配对关系，避免工作台复制配方匹配规则。
    /// 新增配方、学科操作或课程覆盖后，总览会自动反映最终生成结果。
    /// </summary>
    public static class CoursePairingOverview
    {
        public static IReadOnlyList<CoursePairingSummary> Build(
            IEnumerable<NormalizedItem<NormalizedActionDefinition>> actions,
            string selectedEntityId)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }

            if (string.IsNullOrWhiteSpace(selectedEntityId))
            {
                return Array.Empty<CoursePairingSummary>();
            }

            return actions
                .Where(value => value != null)
                .Where(value =>
                    !string.IsNullOrWhiteSpace(
                        value.Definition.SourceEntityId)
                    && !string.IsNullOrWhiteSpace(
                        value.Definition.TargetEntityId))
                .Select(value => ToCandidate(value, selectedEntityId))
                .Where(value => value != null)
                .GroupBy(
                    value => new
                    {
                        value.OtherEntityId,
                        value.SelectedIsSource
                    })
                .OrderBy(value => value.Key.OtherEntityId, StringComparer.Ordinal)
                .ThenByDescending(value => value.Key.SelectedIsSource)
                .Select(group => new CoursePairingSummary(
                    group.Key.OtherEntityId,
                    group.Key.SelectedIsSource,
                    group
                        .Select(value => new CoursePairingOperationSummary(
                            value.OperationName,
                            value.PolicyEffect))
                        .GroupBy(
                            value => value.OperationName + "\u001F"
                                     + value.PolicyEffect,
                            StringComparer.Ordinal)
                        .Select(value => value.First())
                        .OrderBy(
                            value => value.OperationName,
                            StringComparer.Ordinal)))
                .ToArray();
        }

        private static PairingCandidate ToCandidate(
            NormalizedItem<NormalizedActionDefinition> action,
            string selectedEntityId)
        {
            var definition = action.Definition;
            if (string.Equals(
                    definition.SourceEntityId,
                    selectedEntityId,
                    StringComparison.Ordinal))
            {
                return new PairingCandidate(
                    definition.TargetEntityId,
                    true,
                    CourseWorkbenchDisplayNames.ConfiguredItem(
                        action.Identity.LocalKey),
                    definition.PolicyEffect);
            }

            if (string.Equals(
                    definition.TargetEntityId,
                    selectedEntityId,
                    StringComparison.Ordinal))
            {
                return new PairingCandidate(
                    definition.SourceEntityId,
                    false,
                    CourseWorkbenchDisplayNames.ConfiguredItem(
                        action.Identity.LocalKey),
                    definition.PolicyEffect);
            }

            return null;
        }

        private sealed class PairingCandidate
        {
            public PairingCandidate(
                string otherEntityId,
                bool selectedIsSource,
                string operationName,
                string policyEffect)
            {
                OtherEntityId = otherEntityId;
                SelectedIsSource = selectedIsSource;
                OperationName = operationName;
                PolicyEffect = policyEffect;
            }

            public string OtherEntityId { get; }
            public bool SelectedIsSource { get; }
            public string OperationName { get; }
            public string PolicyEffect { get; }
        }
    }
}
