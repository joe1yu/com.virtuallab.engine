using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class PhysicsEffectExecutor : IPresentationEffectExecutor
    {
        public PhysicsEffectExecutor(string effectId, bool enabled)
        {
            EffectId = effectId;
            Enabled = enabled;
        }

        public string EffectId { get; }

        private bool Enabled { get; }

        public static IEnumerable<IPresentationEffectExecutor>
            CreateDefaults()
        {
            yield return new PhysicsEffectExecutor(
                "physics.enable",
                true);
            yield return new PhysicsEffectExecutor(
                "physics.disable",
                false);
        }

        public void Execute(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            var target = effect.Target.Transform;
            var body = target.GetComponent<Rigidbody>();
            if (body == null)
            {
                throw new InvalidOperationException(
                    $"表现目标“{effect.Target.EntityId}”缺少 Rigidbody。");
            }

            body.isKinematic = !Enabled;
            body.detectCollisions = Enabled ||
                                    effect.Parameters.RequireBoolean(
                                        "禁用时保留碰撞");
        }

        public void Stop(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
        }
    }
}
