using System;
using System.Linq;
using UnityEngine;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class ResolvedPresentationTarget
    {
        public ResolvedPresentationTarget(
            string entityId,
            PresentationLocationKind locationKind,
            Transform transform,
            PresentationSlotMarker slot,
            SemanticAnchorMarker anchor,
            object globalReceiver)
        {
            EntityId = entityId;
            LocationKind = locationKind;
            Transform = transform;
            Slot = slot;
            Anchor = anchor;
            GlobalReceiver = globalReceiver;
        }

        public string EntityId { get; }

        public PresentationLocationKind LocationKind { get; }

        public Transform Transform { get; }

        public PresentationSlotMarker Slot { get; }

        public SemanticAnchorMarker Anchor { get; }

        public object GlobalReceiver { get; }
    }

    public sealed class ResolvedPresentationSignalContext
    {
        public ResolvedPresentationSignalContext(
            CourseEntityView subject,
            CourseEntityView actionSource,
            CourseEntityView actionTarget)
        {
            SubjectView = subject;
            ActionSourceView = actionSource;
            ActionTargetView = actionTarget;
            SubjectTransform = subject == null ? null : subject.transform;
            ActionSourceTransform = actionSource == null
                ? null
                : actionSource.transform;
            ActionTargetTransform = actionTarget == null
                ? null
                : actionTarget.transform;
        }

        public CourseEntityView SubjectView { get; }

        public CourseEntityView ActionSourceView { get; }

        public CourseEntityView ActionTargetView { get; }

        public Transform SubjectTransform { get; }

        public Transform ActionSourceTransform { get; }

        public Transform ActionTargetTransform { get; }

    }

    /// <summary>
    /// 把内核产生的稳定语义引用精确解析为场景对象。解析失败发生在仲裁和执行之前，
    /// 从而不会污染当前持续表现。
    /// </summary>
    public sealed class PresentationTargetResolver
    {
        private readonly CourseEntityViewRegistry _courseViews;
        private readonly Func<PresentationEffectDescriptor, object>
            _globalReceiverResolver;

        public PresentationTargetResolver(
            CourseEntityViewRegistry views,
            Func<PresentationEffectDescriptor, object>
                globalReceiverResolver = null)
        {
            _courseViews = views ??
                throw new ArgumentNullException(nameof(views));
            _globalReceiverResolver = globalReceiverResolver;
        }

        public ResolvedPresentationTarget ResolveTarget(
            PresentationEffectCommand command,
            PresentationEffectDescriptor descriptor)
        {
            RequireArguments(command, descriptor);
            if (!descriptor.TargetContract.AllowedLocations.Contains(
                    command.Target.LocationKind))
            {
                throw new InvalidOperationException(
                    $"表现命令“{command.CommandId}”的作用位置“" +
                    $"{command.Target.LocationKind}”不符合原语“" +
                    $"{descriptor.ChineseName}”的目标契约。");
            }

            if (command.Target.LocationKind ==
                PresentationLocationKind.GlobalReceiver)
            {
                var receiver = _globalReceiverResolver?.Invoke(descriptor);
                if (receiver == null)
                {
                    throw new InvalidOperationException(
                        $"表现命令“{command.CommandId}”找不到原语“" +
                        $"{descriptor.ChineseName}”的全局接收器。");
                }

                return new ResolvedPresentationTarget(
                    null,
                    command.Target.LocationKind,
                    null,
                    null,
                    null,
                    receiver);
            }

            var view = RequireEntity(
                command.Target.EntityId,
                command.CommandId,
                "目标实体");
            switch (command.Target.LocationKind)
            {
                case PresentationLocationKind.EntityRoot:
                    return new ResolvedPresentationTarget(
                        view.EntityId,
                        command.Target.LocationKind,
                        view.transform,
                        null,
                        null,
                        _globalReceiverResolver?.Invoke(descriptor));
                case PresentationLocationKind.PresentationSlot:
                    return ResolveSlot(command, descriptor, view);
                case PresentationLocationKind.SemanticAnchor:
                    return ResolveAnchor(command, descriptor, view);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public ResolvedPresentationSignalContext ResolveSignalContext(
            PresentationEffectCommand command,
            PresentationEffectDescriptor descriptor)
        {
            RequireArguments(command, descriptor);
            var required = descriptor.TargetContract.RequiredContextEntities;
            return new ResolvedPresentationSignalContext(
                required.Contains(PresentationContextEntityKind.SignalSubject)
                    ? RequireEntity(
                        command.SignalContext.SubjectEntityId,
                        command.CommandId,
                        "信号主体")
                    : null,
                required.Contains(PresentationContextEntityKind.ActionSource)
                    ? RequireEntity(
                        command.SignalContext.ActionSourceEntityId,
                        command.CommandId,
                        "动作来源")
                    : null,
                required.Contains(PresentationContextEntityKind.ActionTarget)
                    ? RequireEntity(
                        command.SignalContext.ActionTargetEntityId,
                        command.CommandId,
                        "动作目标")
                    : null);
        }

        private ResolvedPresentationTarget ResolveSlot(
            PresentationEffectCommand command,
            PresentationEffectDescriptor descriptor,
            CourseEntityView view)
        {
            if (!view.TryGetPresentationSlot(
                    command.Target.LocationId,
                    out var slot))
            {
                throw new InvalidOperationException(
                    $"目标实体“{view.EntityId}”找不到表现插槽“" +
                    $"{command.Target.LocationId}”。");
            }
            if (!descriptor.TargetContract.AllowedSlotKinds.Contains(
                    slot.Kind))
            {
                throw new InvalidOperationException(
                    $"表现插槽“{command.Target.LocationId}”的种类“" +
                    $"{slot.Kind}”不符合原语“{descriptor.ChineseName}”的目标契约。");
            }

            return new ResolvedPresentationTarget(
                view.EntityId,
                command.Target.LocationKind,
                slot.transform,
                slot,
                null,
                _globalReceiverResolver?.Invoke(descriptor));
        }

        private ResolvedPresentationTarget ResolveAnchor(
            PresentationEffectCommand command,
            PresentationEffectDescriptor descriptor,
            CourseEntityView view)
        {
            if (!view.TryGetAnchor(
                    command.Target.LocationId,
                    out var anchor))
            {
                throw new InvalidOperationException(
                    $"目标实体“{view.EntityId}”找不到语义锚点“" +
                    $"{command.Target.LocationId}”。");
            }
            if (!descriptor.TargetContract.AllowedAnchorKinds.Contains(
                    anchor.Kind))
            {
                throw new InvalidOperationException(
                    $"语义锚点“{command.Target.LocationId}”的种类“" +
                    $"{anchor.Kind}”不符合原语“{descriptor.ChineseName}”的目标契约。");
            }

            return new ResolvedPresentationTarget(
                view.EntityId,
                command.Target.LocationKind,
                anchor.transform,
                null,
                anchor,
                _globalReceiverResolver?.Invoke(descriptor));
        }

        private CourseEntityView RequireEntity(
            string entityId,
            string commandId,
            string role)
        {
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                if (_courseViews != null &&
                    _courseViews.TryGet(entityId, out var courseView))
                {
                    return courseView;
                }
            }

            throw new InvalidOperationException(
                $"表现命令“{commandId}”找不到{role}“{entityId}”。");
        }

        private static void RequireArguments(
            PresentationEffectCommand command,
            PresentationEffectDescriptor descriptor)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }
        }
    }
}
