using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Drafts;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.Unity.Authoring.Normalized
{
    public sealed class PrefabContractDiagnostic
    {
        public PrefabContractDiagnostic(
            string code,
            string message,
            CourseDiagnosticTarget target)
        {
            Code = code;
            Message = message;
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public string Code { get; }

        public string Message { get; }

        public CourseDiagnosticTarget Target { get; }
    }

    public static class PrefabContractValidator
    {
        public static IReadOnlyList<PrefabContractDiagnostic> Validate(
            CourseEntityView view,
            CoursePrefabContractDefinition contract,
            PrefabContractRequirementCatalog requirementCatalog = null)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (contract == null)
            {
                throw new ArgumentNullException(nameof(contract));
            }

            var diagnostics = new List<PrefabContractDiagnostic>();
            var anchors = view.Anchors;
            var slots = view.PresentationSlots;
            requirementCatalog = requirementCatalog ??
                PrefabContractRequirementCatalog.Default;

            if (!string.Equals(
                    view.EntityId,
                    contract.EntityId,
                    StringComparison.Ordinal))
            {
                diagnostics.Add(new PrefabContractDiagnostic(
                    "prefab.entity-id.mismatch",
                    $"实体视图 ID“{view.EntityId}”与契约实体 ID“{contract.EntityId}”不一致。",
                    PrefabTarget(contract.EntityId)));
            }

            foreach (var requirement in
                     requirementCatalog.FindFor(contract.CapabilityIds))
            {
                ValidateRequirement(
                    requirement,
                    contract.EntityId,
                    view,
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
                        $"缺少连接端口锚点“{portId}”。",
                        PrefabTarget(contract.EntityId)));
                }
            }

            ValidateUniqueIds(
                contract.EntityId,
                anchors,
                slots,
                diagnostics);
            return new ReadOnlyCollection<PrefabContractDiagnostic>(
                diagnostics);
        }

        private static void ValidateRequirement(
            PrefabContractRequirement requirement,
            string entityId,
            CourseEntityView view,
            IEnumerable<SemanticAnchorMarker> anchors,
            IEnumerable<PresentationSlotMarker> slots,
            ICollection<PrefabContractDiagnostic> diagnostics)
        {
            switch (requirement.Kind)
            {
                case PrefabContractRequirementKind.Collider:
                    if (!view.GetComponentsInChildren<Collider>(true)
                            .Any(value => value.GetComponentInParent<
                                CourseEntityView>(true) == view))
                    {
                        AddRequirementDiagnostic(
                            requirement,
                            entityId,
                            diagnostics);
                    }

                    return;
                case PrefabContractRequirementKind.SemanticAnchor:
                    var anchorKind =
                        PrefabContractRequirementCatalog.ParseAnchorKind(
                            requirement.Value);
                    if (!anchors.Any(value => value.Kind == anchorKind))
                    {
                        AddRequirementDiagnostic(
                            requirement,
                            entityId,
                            diagnostics);
                    }

                    return;
                case PrefabContractRequirementKind.AnyPresentationSlot:
                    var accepted = new HashSet<PresentationSlotKind>(
                        PrefabContractRequirementCatalog.ParseSlotKinds(
                            requirement.Value));
                    if (!slots.Any(value => accepted.Contains(value.Kind)))
                    {
                        AddRequirementDiagnostic(
                            requirement,
                            entityId,
                            diagnostics);
                    }

                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void AddRequirementDiagnostic(
            PrefabContractRequirement requirement,
            string entityId,
            ICollection<PrefabContractDiagnostic> diagnostics)
        {
            diagnostics.Add(new PrefabContractDiagnostic(
                requirement.DiagnosticCode,
                requirement.Message,
                PrefabTarget(entityId)));
        }

        private static void ValidateUniqueIds(
            string entityId,
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
                    "语义锚点 ID 不能为空或重复。",
                    PrefabTarget(entityId)));
            }

            if (slots.Any(value => string.IsNullOrWhiteSpace(value.SlotId)) ||
                slots.GroupBy(value => value.SlotId, StringComparer.Ordinal)
                    .Any(group => group.Count() > 1))
            {
                diagnostics.Add(new PrefabContractDiagnostic(
                    "prefab.slot.id-invalid",
                    "表现插槽 ID 不能为空或重复。",
                    PrefabTarget(entityId)));
            }
        }

        private static CourseDiagnosticTarget PrefabTarget(string entityId) =>
            new CourseDiagnosticTarget(
                CourseAuthoringTableNames.Course,
                entityId,
                CourseAuthoringColumns.Course.ExperimentPrefab,
                CourseDiagnosticActionIds.EditExperimentPrefab);
    }
}
