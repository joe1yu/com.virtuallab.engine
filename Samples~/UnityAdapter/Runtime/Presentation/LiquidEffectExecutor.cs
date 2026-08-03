using System;
using UnityEngine;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class LiquidEffectExecutor : IPresentationEffectExecutor
    {
        public string EffectId => "liquid.set-level";

        public void Execute(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            var target = effect.Target.Transform;
            var level = Mathf.Clamp01(
                (float)effect.Parameters.RequireNumber("液面比例"));
            var scale = target.localScale;
            scale.y = level;
            target.localScale = scale;

            // 默认以容器底部为基准缩放；课程可通过“底部本地Y”校准模型。
            var bottomY =
                (float)effect.Parameters.RequireNumber("底部本地Y");
            var position = target.localPosition;
            position.y = bottomY + level * 0.5f;
            target.localPosition = position;
        }

        public void Stop(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
        }
    }
}
