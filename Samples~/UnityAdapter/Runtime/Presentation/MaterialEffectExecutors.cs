using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class MaterialEffectExecutor : IPresentationEffectExecutor
    {
        public MaterialEffectExecutor(string effectId)
        {
            EffectId = effectId;
        }

        public string EffectId { get; }

        public static IEnumerable<IPresentationEffectExecutor>
            CreateDefaults()
        {
            yield return new MaterialEffectExecutor("material.set");
            yield return new MaterialEffectExecutor("material.animate");
            yield return new MaterialEffectExecutor("renderer.show");
            yield return new MaterialEffectExecutor("renderer.hide");
        }

        public void Execute(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            var target = effect.Target.Transform;
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                throw new InvalidOperationException(
                    $"表现目标“{effect.Target.EntityId}”缺少 Renderer。");
            }

            switch (EffectId)
            {
                case "renderer.show":
                case "renderer.hide":
                    var visible = EffectId == "renderer.show";
                    renderer.enabled = visible;
                    break;
                case "material.set":
                    renderer.sharedMaterial =
                        effect.Resources.Require<Material>("资源ID");
                    break;
                case "material.animate":
                    AnimateMaterial(
                        target,
                        renderer,
                        effect);
                    break;
            }
        }

        public void Stop(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context)
        {
            if (EffectId != "material.animate")
            {
                return;
            }

            var transition = effect.Target.Transform
                .GetComponent<MaterialTransitionEffect>();
            if (transition != null)
            {
                UnityEngine.Object.Destroy(transition);
            }
        }

        private static void AnimateMaterial(
            Transform target,
            Renderer renderer,
            PreparedPresentationEffect effect)
        {
            var material = effect.Resources.Require<Material>("资源ID");
            var transition =
                target.GetComponent<MaterialTransitionEffect>() ??
                target.gameObject.AddComponent<MaterialTransitionEffect>();
            transition.Configure(
                new[] { renderer },
                material,
                (float)effect.Parameters.RequireNumber("持续秒数"));
        }
    }

    public sealed class MaterialTransitionEffect : MonoBehaviour
    {
        private Renderer[] _renderers = Array.Empty<Renderer>();
        private Material _target;
        private Color[] _startColors = Array.Empty<Color>();
        private float _duration;
        private float _elapsed;

        public void Configure(
            Renderer[] renderers,
            Material target,
            float duration)
        {
            _renderers = renderers ?? Array.Empty<Renderer>();
            _target = target;
            _duration = Mathf.Max(0f, duration);
            _elapsed = 0f;
            _startColors = new Color[_renderers.Length];
            for (var index = 0; index < _renderers.Length; index++)
            {
                _startColors[index] = _renderers[index].material.color;
            }

            if (_duration <= Mathf.Epsilon)
            {
                Complete();
            }
        }

        private void Update()
        {
            if (_target == null || _duration <= Mathf.Epsilon)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            var progress = Mathf.Clamp01(_elapsed / _duration);
            for (var index = 0; index < _renderers.Length; index++)
            {
                if (_renderers[index] != null)
                {
                    _renderers[index].material.color = Color.Lerp(
                        _startColors[index],
                        _target.color,
                        progress);
                }
            }

            if (progress >= 1f)
            {
                Complete();
            }
        }

        private void Complete()
        {
            if (_target != null)
            {
                foreach (var renderer in _renderers)
                {
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = _target;
                    }
                }
            }

            Destroy(this);
        }
    }
}
