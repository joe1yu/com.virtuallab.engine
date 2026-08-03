using System;
using UnityEngine;

namespace VirtualLab.UnityAdapters.Authoring
{
    public enum PresentationSlotKind
    {
        Content,
        Liquid,
        Combustion,
        Highlight
    }

    /// <summary>
    /// Prefab 内可被通用表现执行器绑定的位置，不包含具体课程逻辑。
    /// </summary>
    public sealed class PresentationSlotMarker : MonoBehaviour
    {
        [SerializeField] private string slotId;
        [SerializeField] private PresentationSlotKind kind;

        public string SlotId => string.IsNullOrWhiteSpace(slotId)
            ? null
            : slotId.Trim();

        public PresentationSlotKind Kind => kind;

        public void Configure(string newSlotId, PresentationSlotKind newKind)
        {
            if (string.IsNullOrWhiteSpace(newSlotId))
            {
                throw new ArgumentException(
                    "表现插槽 ID 不能为空。",
                    nameof(newSlotId));
            }

            slotId = newSlotId.Trim();
            kind = newKind;
            NotifyOwner();
        }

        private void OnEnable()
        {
            NotifyOwner();
        }

        private void OnDisable()
        {
            NotifyOwner();
        }

        private void OnValidate()
        {
            NotifyOwner();
        }

        private void NotifyOwner()
        {
            GetComponentInParent<CourseEntityView>(true)
                ?.InvalidateSemanticIndex();
        }
    }
}
