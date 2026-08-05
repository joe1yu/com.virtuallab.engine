using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using VirtualLab.Application.Courses;
using VirtualLab.Interaction.Actions;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.UnityAdapters.Input
{
    /// <summary>
    /// 鼠标直接操纵适配器。它只把设备手势翻译为语义命令；视图移动必须在
    /// 抓取获准后发生，所有科学状态仍由配置驱动课程内核修改。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CourseSemanticInputGateway))]
    [DefaultExecutionOrder(200)]
    public sealed class CourseDirectManipulationController : MonoBehaviour
    {
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private string actorEntityId;
        [SerializeField] private LayerMask interactionLayers = ~0;
        [SerializeField] private float maximumRayDistance = 100f;
        [SerializeField] private float rotationDegreesPerPixel = 0.35f;

        private CourseSemanticInputGateway _gateway;
        private CourseEntityView _selected;
        private float _dragDepth;
        private Vector3 _lastPointerPosition;
        private int _commandSequence;
        private string _lastPreviewKey;
        private readonly CourseManipulationPreview _preview =
            new CourseManipulationPreview();

        public CourseEntityView Selected => _selected;

        /// <summary>
        /// 当前落点的只读内核裁决；没有语义目标时为空。表现或辅助 UI 可以直接
        /// 读取允许状态和拒绝原因，不需要自行解释课程规则。
        /// </summary>
        public ActionAvailability CurrentDropAvailability { get; private set; }

        public void Configure(
            string actorId,
            Camera camera = null)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                throw new ArgumentException(
                    "直接操纵控制器的操作者实体 ID 不能为空。",
                    nameof(actorId));
            }

            actorEntityId = actorId.Trim();
            interactionCamera = camera;
        }

        private void Start()
        {
            _gateway = GetComponent<CourseSemanticInputGateway>();
            _gateway.Initialize();
            if (string.IsNullOrWhiteSpace(actorEntityId))
            {
                actorEntityId = _gateway.ActorEntityId;
            }
            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }
        }

        private void Update()
        {
            if (_gateway == null || interactionCamera == null)
            {
                return;
            }

            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current == null
                    || !EventSystem.current.IsPointerOverGameObject())
                {
                    TryBeginManipulation(UnityEngine.Input.mousePosition);
                }
            }

            if (_selected != null && UnityEngine.Input.GetMouseButton(0))
            {
                UpdateManipulation(UnityEngine.Input.mousePosition);
            }

            if (_selected != null && UnityEngine.Input.GetMouseButtonDown(1))
            {
                _lastPointerPosition = UnityEngine.Input.mousePosition;
            }

            if (_selected != null && UnityEngine.Input.GetMouseButton(1))
            {
                Rotate(UnityEngine.Input.mousePosition);
            }

            if (_selected != null && UnityEngine.Input.GetMouseButtonUp(0))
            {
                CompleteManipulation(UnityEngine.Input.mousePosition);
            }
        }

        /// <summary>
        /// 尝试开始一次指针操纵。公开入口也供触控适配器和自动化测试复用。
        /// </summary>
        public bool TryBeginManipulation(Vector2 pointer)
        {
            if (_selected != null)
            {
                return false;
            }

            var source = RaycastEntity(pointer, null);
            if (source == null)
            {
                return false;
            }

            _preview.Begin(source);
            var result = _gateway.Dispatch(
                NextCommandId(InteractionSemanticActionIds.Grab),
                InteractionSemanticActionIds.Grab,
                actorEntityId,
                source.EntityId);
            if (!result.Outcome.IsAccepted)
            {
                _preview.Cancel();
                return false;
            }

            _selected = source;
            _dragDepth = interactionCamera.WorldToScreenPoint(
                source.transform.position).z;
            _lastPointerPosition = pointer;
            _lastPreviewKey = null;
            CurrentDropAvailability = null;
            return true;
        }

        /// <summary>
        /// 更新临时表现姿态，不提交任何科学状态。
        /// </summary>
        public void UpdateManipulation(Vector2 pointer)
        {
            if (_selected == null)
            {
                return;
            }

            var point = new Vector3(pointer.x, pointer.y, _dragDepth);
            var position = interactionCamera.ScreenToWorldPoint(point);
            _preview.MoveTo(position);
            PreviewDrop(pointer);
        }

        private void Rotate(Vector2 pointer)
        {
            var delta = pointer - (Vector2)_lastPointerPosition;
            _lastPointerPosition = pointer;
            _preview.RotateAround(
                interactionCamera.transform.up,
                -delta.x * rotationDegreesPerPixel,
                interactionCamera.transform.right,
                delta.y * rotationDegreesPerPixel);
        }

        private void PreviewDrop(Vector2 pointer)
        {
            var target = RaycastEntity(pointer, _selected);
            var resolution = ResolveDropCandidate(target);
            if (resolution == null)
            {
                CurrentDropAvailability = null;
                _lastPreviewKey = null;
                return;
            }

            CurrentDropAvailability = resolution.Availability;
            var previewKey = resolution.Candidate.ActionId
                             + "|" + target.EntityId
                             + "|" + resolution.Availability.IsAllowed
                             + "|" + resolution.Availability.RejectionCode;
            if (string.Equals(
                    previewKey,
                    _lastPreviewKey,
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastPreviewKey = previewKey;
            CurrentDropAvailability = _gateway.QueryAvailability(
                NextCommandId("预判"),
                resolution.Candidate.ActionId,
                actorEntityId,
                resolution.Candidate.SourceEntityId,
                resolution.Candidate.TargetEntityId).Availability;
        }

        /// <summary>
        /// 提交落点语义。只有落点动作获准时保留预览姿态，否则恢复抓取前姿态。
        /// </summary>
        public bool CompleteManipulation(Vector2 pointer)
        {
            if (_selected == null)
            {
                return false;
            }

            var source = _selected;
            var target = RaycastEntity(pointer, source);
            var resolution = ResolveDropCandidate(target);
            var dropAccepted = false;
            if (resolution != null)
            {
                var result = _gateway.Dispatch(
                    NextCommandId("落点"),
                    resolution.Candidate.ActionId,
                    actorEntityId,
                    resolution.Candidate.SourceEntityId,
                    resolution.Candidate.TargetEntityId);
                dropAccepted = result.Outcome.IsAccepted;
            }

            var release = _gateway.Dispatch(
                NextCommandId(InteractionSemanticActionIds.Release),
                InteractionSemanticActionIds.Release,
                actorEntityId,
                source.EntityId);
            var freePlacement = resolution == null
                                && target == null
                                && IsPointerInsideViewport(pointer);
            var committed = release.Outcome.IsAccepted
                            && (dropAccepted || freePlacement);
            if (release.Outcome.IsAccepted)
            {
                if (committed)
                {
                    _preview.Commit();
                }
                else
                {
                    _preview.Cancel();
                }

                _selected = null;
                _lastPreviewKey = null;
                CurrentDropAvailability = null;
                return committed;
            }

            // 内核仍保留“被持有”关系时，输入适配器也必须保留选择状态。
            // 已接受的落点姿态成为下一次尝试的起点；否则恢复抓取前姿态。
            if (dropAccepted)
            {
                _preview.Commit();
            }
            else
            {
                _preview.Cancel();
            }

            _preview.Begin(source);
            _lastPreviewKey = null;
            return false;
        }

        private DropCandidateResolution ResolveDropCandidate(
            CourseEntityView target)
        {
            if (_selected == null || target == null)
            {
                return null;
            }

            var candidates = _gateway.FindCandidates(
                _selected.EntityId,
                target.EntityId);
            DropCandidateResolution firstDenied = null;
            foreach (var candidate in candidates)
            {
                var availability = _gateway.ProbeAvailability(
                    NextCommandId("探测"),
                    candidate.ActionId,
                    actorEntityId,
                    candidate.SourceEntityId,
                    candidate.TargetEntityId);
                if (availability.IsAllowed)
                {
                    return new DropCandidateResolution(
                        candidate,
                        availability);
                }

                firstDenied ??= new DropCandidateResolution(
                    candidate,
                    availability);
            }

            return firstDenied;
        }

        private CourseEntityView RaycastEntity(
            Vector2 pointer,
            CourseEntityView excluded)
        {
            var ray = interactionCamera.ScreenPointToRay(pointer);
            return UnityEngine.Physics.RaycastAll(
                    ray,
                    maximumRayDistance,
                    interactionLayers,
                    QueryTriggerInteraction.Collide)
                .OrderBy(value => value.distance)
                .Select(value => value.collider.GetComponentInParent<
                    CourseEntityView>())
                .FirstOrDefault(value =>
                    value != null && value != excluded);
        }

        private string NextCommandId(string operation)
        {
            _commandSequence = checked(_commandSequence + 1);
            return $"直接操纵.{GetInstanceID()}.{operation}.{_commandSequence}";
        }

        /// <summary>
        /// 供鼠标、触控、VR 等设备适配器提交单对象语义手势。动作是否合法仍由
        /// 课程内核裁决，本方法不直接改变科学状态。
        /// </summary>
        public CourseDispatchResult DispatchUnaryAction(
            string actionId,
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters = null)
        {
            if (_selected == null)
            {
                throw new InvalidOperationException("当前没有正在操作的实验对象。");
            }

            return _gateway.Dispatch(
                NextCommandId("单对象手势"),
                actionId,
                actorEntityId,
                _selected.EntityId,
                null,
                parameters);
        }

        private bool IsPointerInsideViewport(Vector2 pointer)
        {
            return interactionCamera != null
                   && interactionCamera.pixelRect.Contains(pointer);
        }

        private sealed class DropCandidateResolution
        {
            public DropCandidateResolution(
                SemanticActionCandidate candidate,
                ActionAvailability availability)
            {
                Candidate = candidate ??
                    throw new ArgumentNullException(nameof(candidate));
                Availability = availability ??
                    throw new ArgumentNullException(nameof(availability));
            }

            public SemanticActionCandidate Candidate { get; }

            public ActionAvailability Availability { get; }
        }
    }
}
