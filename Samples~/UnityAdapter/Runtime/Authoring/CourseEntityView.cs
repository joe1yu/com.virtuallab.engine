using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;

namespace VirtualLab.UnityAdapters.Authoring
{
    [DisallowMultipleComponent]
    public sealed class CourseEntityView : MonoBehaviour
    {
        [SerializeField]
        private string _entityId;
        private SemanticAnchorMarker[] _anchors =
            Array.Empty<SemanticAnchorMarker>();
        private PresentationSlotMarker[] _presentationSlots =
            Array.Empty<PresentationSlotMarker>();
        private IReadOnlyDictionary<string, SemanticAnchorMarker>
            _anchorsById;
        private IReadOnlyDictionary<string, PresentationSlotMarker>
            _slotsById;
        private bool _indexReady;

        /// <summary>
        /// 课程实体 ID 保存在实验总预制体中，用于将预制体节点绑定到领域实体。
        /// </summary>
        public string EntityId => string.IsNullOrWhiteSpace(_entityId)
            ? null
            : _entityId;


        public IReadOnlyList<SemanticAnchorMarker> Anchors
        {
            get
            {
                EnsureIndex();
                return _anchors;
            }
        }

        public IReadOnlyList<PresentationSlotMarker> PresentationSlots
        {
            get
            {
                EnsureIndex();
                return _presentationSlots;
            }
        }

        public void Configure(string newEntityId)
        {
            if (string.IsNullOrWhiteSpace(newEntityId))
            {
                throw new ArgumentException(
                    "课程实体 ID 不能为空。",
                    nameof(newEntityId));
            }

            _entityId = newEntityId.Trim();
            RebuildIndex(throwOnInvalidId: true);

        }

        public bool TryGetAnchor(
            string anchorId,
            out SemanticAnchorMarker anchor)
        {
            EnsureIndex();
            if (string.IsNullOrWhiteSpace(anchorId))
            {
                anchor = null;
                return false;
            }

            return _anchorsById.TryGetValue(anchorId.Trim(), out anchor) &&
                   anchor != null;
        }

        public bool TryGetPresentationSlot(
            string slotId,
            out PresentationSlotMarker slot)
        {
            EnsureIndex();
            if (string.IsNullOrWhiteSpace(slotId))
            {
                slot = null;
                return false;
            }

            return _slotsById.TryGetValue(slotId.Trim(), out slot) &&
                   slot != null;
        }

        /// <summary>
        /// 重建预制体视图索引。组合预制体中的嵌套实体由其自身管理，
        /// 不会被父实体误认为自己的锚点或表现插槽。
        /// </summary>
        public void RefreshSemanticIndex()
        {
            RebuildIndex(throwOnInvalidId: true);
        }

        internal void InvalidateSemanticIndex()
        {
            _indexReady = false;
        }

        private void OnEnable()
        {
            RebuildIndex(throwOnInvalidId: false);
        }

        private void OnTransformChildrenChanged()
        {
            _indexReady = false;
        }

        private void EnsureIndex()
        {
            if (!_indexReady)
            {
                RebuildIndex(throwOnInvalidId: true);
            }
        }

        private void RebuildIndex(bool throwOnInvalidId)
        {
            _anchors = GetComponentsInChildren<SemanticAnchorMarker>(true)
                .Where(IsOwnedByThisView)
                .ToArray();
            _presentationSlots =
                GetComponentsInChildren<PresentationSlotMarker>(true)
                    .Where(IsOwnedByThisView)
                    .ToArray();
            _anchorsById = BuildIndex(
                _anchors,
                value => value.AnchorId,
                "语义锚点",
                throwOnInvalidId);
            _slotsById = BuildIndex(
                _presentationSlots,
                value => value.SlotId,
                "表现插槽",
                throwOnInvalidId);
            _indexReady = true;
        }

        private bool IsOwnedByThisView(Component marker)
        {
            return marker != null &&
                   marker.GetComponentInParent<CourseEntityView>(true) == this;
        }

        private static IReadOnlyDictionary<string, T> BuildIndex<T>(
            IEnumerable<T> values,
            Func<T, string> selectId,
            string kind,
            bool throwOnInvalidId)
            where T : UnityEngine.Object
        {
            var index = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                var id = value == null ? null : selectId(value);
                if (string.IsNullOrWhiteSpace(id))
                {
                    if (throwOnInvalidId)
                    {
                        throw new InvalidOperationException(
                            $"{kind} ID 不能为空，请检查预制体节点。" );
                    }

                    continue;
                }

                id = id.Trim();
                if (!index.TryAdd(id, value) && throwOnInvalidId)
                {
                    throw new InvalidOperationException(
                        $"存在重复{kind}“{id}”，请为每个节点设置唯一 ID。" );
                }
            }

            return new ReadOnlyDictionary<string, T>(index);
        }
    }
}
