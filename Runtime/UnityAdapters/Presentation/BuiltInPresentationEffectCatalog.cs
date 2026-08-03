using System;
using System.Collections.Generic;
using VirtualLab.Application.Courses;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.UnityAdapters.Presentation
{
    /// <summary>
    /// 引擎内置表现原语的唯一登记入口。
    /// 新原语必须在此完整声明契约，课程配置只引用中文名或稳定协议 ID。
    /// </summary>
    public static class BuiltInPresentationEffectCatalog
    {
        public const string InteractionAffordanceProtocolId =
            "interaction.affordance";
        public const string UiMessageProtocolId = "ui.message";
        public const string UiHighlightProtocolId = "ui.highlight";

        public static PresentationEffectCatalog Create()
        {
            return new PresentationEffectCatalog(new[]
            {
                Effect(
                    "interaction.follow-anchor",
                    "跟随实体",
                    "transform.position",
                    PersistentLifecycles(),
                    AnchorTarget(),
                    NoParameters(),
                    () => new TransformEffectExecutor(
                        "interaction.follow-anchor")),
                Effect(
                    "interaction.stop-follow",
                    "停止跟随",
                    "transform.position",
                    OneShotLifecycles(),
                    RootTarget(),
                    NoParameters(),
                    () => new TransformEffectExecutor(
                        "interaction.stop-follow")),
                Effect(
                    "interaction.snap-to-anchor",
                    "吸附到端口",
                    "transform.position",
                    PersistentLifecycles(),
                    AnchorTarget(),
                    NoParameters(),
                    () => new TransformEffectExecutor(
                        "interaction.snap-to-anchor")),
                Effect(
                    "transform.attach",
                    "移动到锚点",
                    "transform.parent",
                    PersistentLifecycles(),
                    AnchorTarget(),
                    NoParameters(),
                    () => new TransformEffectExecutor(
                        "transform.attach")),
                Effect(
                    "transform.detach",
                    "从锚点分离",
                    "transform.parent",
                    OneShotLifecycles(),
                    RootTarget(),
                    NoParameters(),
                    () => new TransformEffectExecutor(
                        "transform.detach")),
                Effect(
                    "transform.oscillate",
                    "振荡",
                    "transform.position",
                    OneShotLifecycles(),
                    RootTarget(),
                    new[]
                    {
                        Number("方向X", 1),
                        Number("方向Y", 0),
                        Number("方向Z", 0),
                        Number("幅度", 0.05, 0),
                        Number("频率", 2, 0),
                        Number("持续秒数", 0.5, 0)
                    },
                    () => new TransformEffectExecutor(
                        "transform.oscillate")),
                Effect(
                    "physics.enable",
                    "恢复物理",
                    "physics.enabled",
                    PersistentLifecycles(),
                    RootTarget(),
                    NoParameters(),
                    () => new PhysicsEffectExecutor(
                        "physics.enable",
                        true)),
                Effect(
                    "physics.disable",
                    "禁用物理",
                    "physics.enabled",
                    PersistentLifecycles(),
                    RootTarget(),
                    new[]
                    {
                        Boolean("禁用时保留碰撞", true)
                    },
                    () => new PhysicsEffectExecutor(
                        "physics.disable",
                        false)),
                Effect(
                    "liquid.set-level",
                    "更新液面",
                    "liquid.level",
                    PersistentLifecycles(),
                    SlotTarget(PresentationSlotKind.Liquid),
                    new[]
                    {
                        RequiredNumber("液面比例", 0, 1),
                        Number("底部本地Y", 0)
                    },
                    () => new LiquidEffectExecutor()),
                Effect(
                    "material.set",
                    "设置材质",
                    "renderer.material",
                    PersistentLifecycles(),
                    SlotTarget(PresentationSlotKind.Content),
                    new[]
                    {
                        Resource("资源ID", CourseResourceKind.Material)
                    },
                    () => new MaterialEffectExecutor("material.set")),
                Effect(
                    "material.animate",
                    "材质动画",
                    "renderer.material",
                    OneShotLifecycles(),
                    SlotTarget(PresentationSlotKind.Content),
                    new[]
                    {
                        Resource("资源ID", CourseResourceKind.Material),
                        Number("持续秒数", 0.3, 0)
                    },
                    () => new MaterialEffectExecutor("material.animate")),
                Effect(
                    "renderer.show",
                    "显示渲染器",
                    "renderer.visibility",
                    PersistentLifecycles(),
                    SlotTarget(
                        PresentationSlotKind.Content,
                        PresentationSlotKind.Highlight,
                        PresentationSlotKind.Combustion),
                    NoParameters(),
                    () => new MaterialEffectExecutor("renderer.show")),
                Effect(
                    "renderer.hide",
                    "隐藏渲染器",
                    "renderer.visibility",
                    PersistentLifecycles(),
                    SlotTarget(
                        PresentationSlotKind.Content,
                        PresentationSlotKind.Highlight,
                        PresentationSlotKind.Combustion),
                    NoParameters(),
                    () => new MaterialEffectExecutor("renderer.hide")),
                Effect(
                    "audio.play",
                    "播放音效",
                    "audio.playback",
                    OneShotLifecycles(),
                    SlotTarget(PresentationSlotKind.Content),
                    new[]
                    {
                        Resource("资源ID", CourseResourceKind.Audio),
                        Boolean("循环", false)
                    },
                    () => new AudioVfxEffectExecutor("audio.play")),
                Effect(
                    "vfx.play",
                    "播放特效",
                    "vfx.playback",
                    VfxLifecycles(),
                    SlotTarget(
                        PresentationSlotKind.Content,
                        PresentationSlotKind.Combustion),
                    NoParameters(),
                    () => new AudioVfxEffectExecutor("vfx.play")),
                Effect(
                    InteractionAffordanceProtocolId,
                    "更新动作可操作性",
                    "interaction.affordance",
                    OneShotLifecycles(),
                    GlobalTarget(),
                    new[]
                    {
                        RequiredBoolean("是否允许"),
                        RequiredEnumText(
                            "可操作性分类",
                            Enum.GetNames(
                                typeof(ActionAvailabilityKind))),
                        Text("拒绝代码", string.Empty),
                        Text("文案ID", string.Empty)
                    },
                    () => new UiEffectExecutor(
                        InteractionAffordanceProtocolId)),
                Effect(
                    UiMessageProtocolId,
                    "显示提示",
                    "ui.message",
                    OneShotLifecycles(),
                    GlobalTarget(),
                    new[]
                    {
                        RequiredText("文案"),
                        Number("持续秒数", 2, 0)
                    },
                    () => new UiEffectExecutor(UiMessageProtocolId)),
                Effect(
                    UiHighlightProtocolId,
                    "高亮实体",
                    "ui.highlight",
                    OneShotLifecycles(),
                    SlotTarget(PresentationSlotKind.Highlight),
                    new[]
                    {
                        Number("颜色R", 1, 0, 1),
                        Number("颜色G", 0.92, 0, 1),
                        Number("颜色B", 0.016, 0, 1),
                        Number("颜色A", 1, 0, 1),
                        Number("持续秒数", 2, 0)
                    },
                    () => new UiEffectExecutor(UiHighlightProtocolId))
            });
        }

        private static PresentationEffectDescriptor Effect(
            string protocolId,
            string chineseName,
            string channel,
            IReadOnlyList<PresentationEffectLifecycle> lifecycles,
            PresentationTargetContract target,
            IReadOnlyList<PresentationParameterDescriptor> parameters,
            Func<IPresentationEffectExecutor> createExecutor)
        {
            return new PresentationEffectDescriptor(
                protocolId,
                chineseName,
                channel,
                lifecycles[0],
                lifecycles,
                target,
                parameters,
                createExecutor);
        }

        private static IReadOnlyList<PresentationEffectLifecycle>
            OneShotLifecycles()
        {
            return new[]
            {
                PresentationEffectLifecycle.OneShot
            };
        }

        private static IReadOnlyList<PresentationEffectLifecycle>
            PersistentLifecycles()
        {
            return new[]
            {
                PresentationEffectLifecycle.UntilReplaced,
                PresentationEffectLifecycle.WhileActive
            };
        }

        private static IReadOnlyList<PresentationEffectLifecycle>
            VfxLifecycles()
        {
            return new[]
            {
                PresentationEffectLifecycle.OneShot,
                PresentationEffectLifecycle.UntilReplaced,
                PresentationEffectLifecycle.WhileActive
            };
        }

        private static PresentationTargetContract RootTarget()
        {
            return new PresentationTargetContract(
                new[] { PresentationLocationKind.EntityRoot },
                Array.Empty<PresentationSlotKind>(),
                Array.Empty<SemanticAnchorKind>(),
                Array.Empty<PresentationContextEntityKind>());
        }

        private static PresentationTargetContract AnchorTarget()
        {
            return new PresentationTargetContract(
                new[] { PresentationLocationKind.SemanticAnchor },
                Array.Empty<PresentationSlotKind>(),
                new[]
                {
                    SemanticAnchorKind.ConnectionPort,
                    SemanticAnchorKind.PourOutlet,
                    SemanticAnchorKind.HeatingPoint,
                    SemanticAnchorKind.IgnitionPoint,
                    SemanticAnchorKind.ObservationFocus,
                    SemanticAnchorKind.InteractionGrip
                },
                new[]
                {
                    PresentationContextEntityKind.ActionSource,
                    PresentationContextEntityKind.ActionTarget
                });
        }

        private static PresentationTargetContract SlotTarget(
            params PresentationSlotKind[] slotKinds)
        {
            return new PresentationTargetContract(
                new[] { PresentationLocationKind.PresentationSlot },
                slotKinds,
                Array.Empty<SemanticAnchorKind>(),
                Array.Empty<PresentationContextEntityKind>());
        }

        private static PresentationTargetContract GlobalTarget()
        {
            return new PresentationTargetContract(
                new[] { PresentationLocationKind.GlobalReceiver },
                Array.Empty<PresentationSlotKind>(),
                Array.Empty<SemanticAnchorKind>(),
                Array.Empty<PresentationContextEntityKind>());
        }

        private static IReadOnlyList<PresentationParameterDescriptor>
            NoParameters()
        {
            return Array.Empty<PresentationParameterDescriptor>();
        }

        private static PresentationParameterDescriptor RequiredNumber(
            string name,
            double minimum,
            double maximum)
        {
            return new PresentationParameterDescriptor(
                name,
                PresentationParameterKind.Number,
                true,
                null,
                minimum,
                maximum,
                Array.Empty<string>(),
                null);
        }

        private static PresentationParameterDescriptor Number(
            string name,
            double defaultValue,
            double? minimum = null,
            double? maximum = null)
        {
            return new PresentationParameterDescriptor(
                name,
                PresentationParameterKind.Number,
                false,
                PresentationValue.FromNumber(defaultValue),
                minimum,
                maximum,
                Array.Empty<string>(),
                null);
        }

        private static PresentationParameterDescriptor Boolean(
            string name,
            bool defaultValue)
        {
            return new PresentationParameterDescriptor(
                name,
                PresentationParameterKind.Boolean,
                false,
                PresentationValue.FromBoolean(defaultValue),
                null,
                null,
                Array.Empty<string>(),
                null);
        }

        private static PresentationParameterDescriptor RequiredBoolean(
            string name)
        {
            return new PresentationParameterDescriptor(
                name,
                PresentationParameterKind.Boolean,
                true,
                null,
                null,
                null,
                Array.Empty<string>(),
                null);
        }

        private static PresentationParameterDescriptor RequiredEnumText(
            string name,
            IEnumerable<string> allowedValues)
        {
            return new PresentationParameterDescriptor(
                name,
                PresentationParameterKind.EnumText,
                true,
                null,
                null,
                null,
                allowedValues,
                null);
        }

        private static PresentationParameterDescriptor Text(
            string name,
            string defaultValue)
        {
            return new PresentationParameterDescriptor(
                name,
                PresentationParameterKind.Text,
                false,
                PresentationValue.FromText(defaultValue),
                null,
                null,
                Array.Empty<string>(),
                null);
        }

        private static PresentationParameterDescriptor RequiredText(
            string name)
        {
            return new PresentationParameterDescriptor(
                name,
                PresentationParameterKind.Text,
                true,
                null,
                null,
                null,
                Array.Empty<string>(),
                null);
        }

        private static PresentationParameterDescriptor Resource(
            string name,
            CourseResourceKind kind)
        {
            return new PresentationParameterDescriptor(
                name,
                PresentationParameterKind.Resource,
                true,
                null,
                null,
                null,
                Array.Empty<string>(),
                kind);
        }
    }
}
