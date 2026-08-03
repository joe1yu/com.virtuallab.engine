using System.Collections.Generic;
using UnityEngine;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class UiEffectExecutor : IPresentationEffectExecutor
    {
        public UiEffectExecutor(string effectId)
        {
            EffectId = effectId;
        }

        public string EffectId { get; }

        public static IEnumerable<IPresentationEffectExecutor>
            CreateDefaults()
        {
            yield return new UiEffectExecutor(
                BuiltInPresentationEffectCatalog
                    .InteractionAffordanceProtocolId);
            yield return new UiEffectExecutor(
                BuiltInPresentationEffectCatalog.UiMessageProtocolId);
            yield return new UiEffectExecutor(
                BuiltInPresentationEffectCatalog.UiHighlightProtocolId);
        }

        public void Execute(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            if (EffectId == BuiltInPresentationEffectCatalog
                    .InteractionAffordanceProtocolId)
            {
                var receiver =
                    effect.Target.GlobalReceiver
                    as IInteractionAffordanceSink;
                receiver.ApplyActionAvailability(
                    effect.Command.SignalContext.ActionSourceEntityId,
                    effect.Command.SignalContext.ActionTargetEntityId,
                    effect.Parameters.RequireBoolean("是否允许"),
                    effect.Parameters.RequireText("可操作性分类"),
                    EmptyAsNull(
                        effect.Parameters.RequireText("拒绝代码")),
                    EmptyAsNull(
                        effect.Parameters.RequireText("文案ID")));
                return;
            }

            var duration =
                effect.Parameters.RequireNumber("持续秒数");
            if (EffectId == BuiltInPresentationEffectCatalog
                    .UiMessageProtocolId)
            {
                var receiver =
                    effect.Target.GlobalReceiver as IPresentationMessageSink;
                receiver.ShowMessage(
                    effect.Parameters.RequireText("文案"),
                    duration);
                return;
            }

            var highlights =
                effect.Target.GlobalReceiver as IPresentationHighlightSink;
            highlights.Highlight(
                effect.Target.EntityId,
                new Color(
                    (float)effect.Parameters.RequireNumber("颜色R"),
                    (float)effect.Parameters.RequireNumber("颜色G"),
                    (float)effect.Parameters.RequireNumber("颜色B"),
                    (float)effect.Parameters.RequireNumber("颜色A")),
                duration);
        }

        public void Stop(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
        }

        private static string EmptyAsNull(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
