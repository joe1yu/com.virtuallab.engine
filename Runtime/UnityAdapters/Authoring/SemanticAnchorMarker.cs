using System;
using UnityEngine;

namespace VirtualLab.UnityAdapters.Authoring
{
    public enum SemanticAnchorKind
    {
        ConnectionPort,
        PourOutlet,
        HeatingPoint,
        IgnitionPoint,
        ObservationFocus,
        InteractionGrip
    }

    /// <summary>
    /// 稳定语义锚点。课程配置引用 AnchorId，不引用易变化的 Transform 路径。
    /// </summary>
    public sealed class SemanticAnchorMarker : MonoBehaviour
    {
        [SerializeField] private string anchorId;
        [SerializeField] private SemanticAnchorKind kind;

        public string AnchorId => string.IsNullOrWhiteSpace(anchorId)
            ? null
            : anchorId.Trim();

        public SemanticAnchorKind Kind => kind;

        public void Configure(string newAnchorId, SemanticAnchorKind newKind)
        {
            if (string.IsNullOrWhiteSpace(newAnchorId))
            {
                throw new ArgumentException(
                    "语义锚点 ID 不能为空。",
                    nameof(newAnchorId));
            }

            anchorId = newAnchorId.Trim();
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
