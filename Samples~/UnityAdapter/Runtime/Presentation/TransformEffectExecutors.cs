using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class TransformEffectExecutor : IPresentationEffectExecutor
    {
        private static readonly string[] Supported =
        {
            "interaction.follow-anchor",
            "interaction.stop-follow",
            "interaction.snap-to-anchor",
            "transform.attach",
            "transform.detach",
            "transform.oscillate"
        };

        public TransformEffectExecutor(string effectId)
        {
            if (!Supported.Contains(effectId, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"不支持 Transform 效果“{effectId}”。",
                    nameof(effectId));
            }

            EffectId = effectId;
        }

        public string EffectId { get; }

        public static IEnumerable<IPresentationEffectExecutor>
            CreateDefaults()
        {
            return Supported.Select(value =>
                (IPresentationEffectExecutor)new TransformEffectExecutor(
                    value));
        }

        public void Execute(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            switch (EffectId)
            {
                case "interaction.follow-anchor":
                case "transform.attach":
                    Attach(
                        RequireActionSource(effect),
                        effect.Target.Transform);
                    break;
                case "interaction.stop-follow":
                case "transform.detach":
                    effect.Target.Transform.SetParent(null, true);
                    break;
                case "interaction.snap-to-anchor":
                    Snap(
                        RequireActionSource(effect),
                        effect.Target.Transform);
                    break;
                case "transform.oscillate":
                    ConfigureOscillation(
                        effect.Target.Transform,
                        effect.Parameters);
                    break;
            }
        }

        public void Stop(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            if (EffectId == "interaction.follow-anchor" ||
                EffectId == "transform.attach")
            {
                RequireActionSource(effect).SetParent(null, true);
            }
            else if (EffectId == "transform.oscillate")
            {
                var oscillation =
                    effect.Target.Transform
                        .GetComponent<TransformOscillationEffect>();
                if (oscillation != null)
                {
                    UnityEngine.Object.Destroy(oscillation);
                }
            }
        }

        private static Transform RequireActionSource(
            PreparedPresentationEffect effect)
        {
            if (effect.SignalContext.ActionSourceTransform == null)
            {
                throw new InvalidOperationException(
                    $"表现命令“{effect.Command.CommandId}”没有已解析的动作来源。");
            }

            return effect.SignalContext.ActionSourceTransform;
        }

        private static void Attach(Transform target, Transform anchor)
        {
            target.SetParent(anchor, false);
            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
        }

        private static void Snap(Transform target, Transform anchor)
        {
            target.position = anchor.position;
            target.rotation = anchor.rotation;
        }

        private static void ConfigureOscillation(
            Transform target,
            PreparedPresentationParameters parameters)
        {
            var effect = target.GetComponent<TransformOscillationEffect>() ??
                         target.gameObject.AddComponent<
                             TransformOscillationEffect>();
            effect.Configure(
                new Vector3(
                    (float)parameters.RequireNumber("方向X"),
                    (float)parameters.RequireNumber("方向Y"),
                    (float)parameters.RequireNumber("方向Z")),
                (float)parameters.RequireNumber("幅度"),
                (float)parameters.RequireNumber("频率"),
                (float)parameters.RequireNumber("持续秒数"));
        }
    }

    public sealed class TransformOscillationEffect : MonoBehaviour
    {
        private Vector3 _origin;
        private Vector3 _direction = Vector3.right;
        private float _amplitude;
        private float _frequency;
        private float _duration;
        private float _startedAt;

        public void Configure(
            Vector3 direction,
            float amplitude,
            float frequency,
            float duration)
        {
            _origin = transform.localPosition;
            _direction = direction.sqrMagnitude <= Mathf.Epsilon
                ? Vector3.right
                : direction.normalized;
            _amplitude = Mathf.Max(0f, amplitude);
            _frequency = Mathf.Max(0f, frequency);
            _duration = Mathf.Max(0f, duration);
            _startedAt = Time.time;
        }

        private void Update()
        {
            transform.localPosition =
                _origin +
                _direction *
                (Mathf.Sin(Time.time * _frequency * Mathf.PI * 2f) *
                 _amplitude);
            if (_duration > Mathf.Epsilon &&
                Time.time - _startedAt >= _duration)
            {
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            if (transform != null)
            {
                transform.localPosition = _origin;
            }
        }
    }
}
