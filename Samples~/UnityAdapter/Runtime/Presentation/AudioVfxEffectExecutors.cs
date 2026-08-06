using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class AudioVfxEffectExecutor : IPresentationEffectExecutor
    {
        public AudioVfxEffectExecutor(string effectId)
        {
            EffectId = effectId;
        }

        public string EffectId { get; }

        public static IEnumerable<IPresentationEffectExecutor>
            CreateDefaults()
        {
            yield return new AudioVfxEffectExecutor("audio.play");
            yield return new AudioVfxEffectExecutor("vfx.play");
        }

        public void Execute(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            var target = effect.Target.Transform;
            if (EffectId == "audio.play")
            {
                var source = target.GetComponent<AudioSource>();
                if (source == null)
                {
                    throw new InvalidOperationException(
                        $"表现目标“{effect.Target.EntityId}”缺少 AudioSource。");
                }

                source.clip = effect.Resources.Require<AudioClip>("资源ID");
                source.loop = effect.Parameters.RequireBoolean("循环");
                source.Play();
                return;
            }

            var particles = target.GetComponent<ParticleSystem>();
            if (particles == null)
            {
                throw new InvalidOperationException(
                    $"表现目标“{effect.Target.EntityId}”缺少 ParticleSystem。");
            }

            particles.Play();
        }

        public void Stop(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            var target = effect.Target.Transform;
            if (EffectId == "audio.play")
            {
                target.GetComponent<AudioSource>()?.Stop();
                return;
            }

            target.GetComponent<ParticleSystem>()?.Stop(
                true,
                ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
