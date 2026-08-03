using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.Unity.Authoring.Normalized
{
    public sealed class PrefabContractDiagnostic
    {
        public PrefabContractDiagnostic(string code, string message)
        {
            Code = code;
            Message = message;
        }

        public string Code { get; }

        public string Message { get; }
    }

    public static class PrefabContractValidator
    {
        public static IReadOnlyList<PrefabContractDiagnostic> Validate(
            GameObject prefab,
            CoursePrefabContractDefinition contract,
            PrefabContractRequirementCatalog requirementCatalog = null)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (contract == null)
            {
                throw new ArgumentNullException(nameof(contract));
            }

            var diagnostics = new List<PrefabContractDiagnostic>();
            var anchors = prefab
                .GetComponentsInChildren<SemanticAnchorMarker>(true);
            var slots = prefab
                .GetComponentsInChildren<PresentationSlotMarker>(true);
            requirementCatalog = requirementCatalog ??
                PrefabContractRequirementCatalog.Default;

            if (prefab.GetComponent<CourseEntityView>() == null)
            {
                diagnostics.Add(new PrefabContractDiagnostic(
                    "prefab.course-entity-view.required",
                    "课程实体 Prefab 根节点必须包含 CourseEntityView。"));
            }

            foreach (var requirement in
                     requirementCatalog.FindFor(contract.CapabilityIds))
            {
                ValidateRequirement(
                    requirement,
                    prefab,
                    anchors,
                    slots,
                    diagnostics);
            }

            foreach (var portId in contract.PortIds)
            {
                if (!anchors.Any(value =>
                        value.Kind == SemanticAnchorKind.ConnectionPort &&
                        string.Equals(
                            value.AnchorId,
                            portId,
                            StringComparison.Ordinal)))
                {
                    diagnostics.Add(new PrefabContractDiagnostic(
                        "prefab.port.missing",
                        $"缺少连接端口锚点“{portId}”。"));
                }
            }

            ValidateUniqueIds(anchors, slots, diagnostics);
            return new ReadOnlyCollection<PrefabContractDiagnostic>(
                diagnostics);
        }

        private static void ValidateRequirement(
            PrefabContractRequirement requirement,
            GameObject prefab,
            IEnumerable<SemanticAnchorMarker> anchors,
            IEnumerable<PresentationSlotMarker> slots,
            ICollection<PrefabContractDiagnostic> diagnostics)
        {
            switch (requirement.Kind)
            {
                case PrefabContractRequirementKind.Collider:
                    if (prefab.GetComponentInChildren<Collider>(true) == null)
                    {
                        AddRequirementDiagnostic(requirement, diagnostics);
                    }

                    return;
                case PrefabContractRequirementKind.SemanticAnchor:
                    var anchorKind =
                        PrefabContractRequirementCatalog.ParseAnchorKind(
                            requirement.Value);
                    if (!anchors.Any(value => value.Kind == anchorKind))
                    {
                        AddRequirementDiagnostic(requirement, diagnostics);
                    }

                    return;
                case PrefabContractRequirementKind.AnyPresentationSlot:
                    var accepted = new HashSet<PresentationSlotKind>(
                        PrefabContractRequirementCatalog.ParseSlotKinds(
                            requirement.Value));
                    if (!slots.Any(value => accepted.Contains(value.Kind)))
                    {
                        AddRequirementDiagnostic(requirement, diagnostics);
                    }

                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void AddRequirementDiagnostic(
            PrefabContractRequirement requirement,
            ICollection<PrefabContractDiagnostic> diagnostics)
        {
            diagnostics.Add(new PrefabContractDiagnostic(
                requirement.DiagnosticCode,
                requirement.Message));
        }

        private static void ValidateUniqueIds(
            IEnumerable<SemanticAnchorMarker> anchors,
            IEnumerable<PresentationSlotMarker> slots,
            ICollection<PrefabContractDiagnostic> diagnostics)
        {
            if (anchors.Any(value => string.IsNullOrWhiteSpace(value.AnchorId)) ||
                anchors.GroupBy(value => value.AnchorId, StringComparer.Ordinal)
                    .Any(group => group.Count() > 1))
            {
                diagnostics.Add(new PrefabContractDiagnostic(
                    "prefab.anchor.id-invalid",
                    "语义锚点 ID 不能为空或重复。"));
            }

            if (slots.Any(value => string.IsNullOrWhiteSpace(value.SlotId)) ||
                slots.GroupBy(value => value.SlotId, StringComparer.Ordinal)
                    .Any(group => group.Count() > 1))
            {
                diagnostics.Add(new PrefabContractDiagnostic(
                    "prefab.slot.id-invalid",
                    "表现插槽 ID 不能为空或重复。"));
            }
        }
    }
}
