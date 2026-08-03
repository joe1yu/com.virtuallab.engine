using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Presentation;
using Object = UnityEngine.Object;

namespace VirtualLab.Engine.PlayModeTests
{
    public sealed class ConfiguredPresentationExecutorTests
    {
        [UnityTest]
        public IEnumerator 通用执行器按标准效果类型更新目标表现()
        {
            var root = new GameObject("表现执行器测试");
            var registry = new CourseEntityViewRegistry();
            var target = new GameObject("试管");
            target.transform.SetParent(root.transform);
            var targetView = target.AddComponent<CourseEntityView>();
            targetView.Configure("器材.试管");
            registry.Register(targetView);
            var body = target.AddComponent<Rigidbody>();
            var dispatcher = UnityPresentationDispatcher.CreateDefault(
                registry);

            dispatcher.Dispatch(Command(
                "命令.关闭物理",
                "physics.disable",
                "physics.enabled"));

            Assert.That(body.isKinematic, Is.True);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 缺少语义锚点时准备失败且不会移动实体()
        {
            var root = new GameObject("附着执行器测试");
            var registry = new CourseEntityViewRegistry();
            var target = CreateView(root, registry, "器材.试管");
            var anchorOwner = CreateView(root, registry, "器材.铁架台");
            var dispatcher = UnityPresentationDispatcher.CreateDefault(
                registry,
                failurePolicy:
                PresentationFailurePolicy.RecordAndContinue);
            var originalPosition = target.transform.position;

            var result = dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.附着",
                "transform.attach",
                new PresentationTargetReference(
                    "器材.铁架台",
                    PresentationLocationKind.SemanticAnchor,
                    "端口.夹持点"),
                new PresentationSignalContextReference(
                    "器材.试管",
                    "器材.试管",
                    "器材.铁架台"),
                "transform.parent",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                new Dictionary<string, PresentationValue>()));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("找不到语义锚点"));
            Assert.That(target.transform.position, Is.EqualTo(originalPosition));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 液面和渲染效果只修改配置指定的插槽()
        {
            var root = new GameObject("插槽执行测试");
            var registry = new CourseEntityViewRegistry();
            var entity = CreateView(root, registry, "器材.试管");
            var rootRenderer = entity.AddComponent<MeshRenderer>();
            var liquidSlot = CreateSlot(
                entity,
                "插槽.液体",
                PresentationSlotKind.Liquid);
            liquidSlot.transform.localScale = Vector3.one;
            var contentSlot = CreateSlot(
                entity,
                "插槽.内容",
                PresentationSlotKind.Content);
            var slotRenderer = contentSlot.AddComponent<MeshRenderer>();
            var dispatcher = UnityPresentationDispatcher.CreateDefault(
                registry);

            dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.液面",
                "liquid.set-level",
                SlotTarget("器材.试管", "插槽.液体"),
                Context("器材.试管"),
                "liquid.level",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                new[]
                {
                    Pair("液面比例", PresentationValue.FromNumber(0.4))
                }));
            dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.隐藏",
                "renderer.hide",
                SlotTarget("器材.试管", "插槽.内容"),
                Context("器材.试管"),
                "renderer.visibility",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                new Dictionary<string, PresentationValue>()));

            Assert.That(entity.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(liquidSlot.transform.localScale.y, Is.EqualTo(0.4f));
            Assert.That(rootRenderer.enabled, Is.True);
            Assert.That(slotRenderer.enabled, Is.False);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 吸附效果移动动作来源而不是锚点所属实体()
        {
            var root = new GameObject("语义锚点执行测试");
            var registry = new CourseEntityViewRegistry();
            var source = CreateView(root, registry, "器材.导管");
            var target = CreateView(root, registry, "器材.集气瓶");
            source.transform.position = new Vector3(5f, 0f, 0f);
            target.transform.position = new Vector3(1f, 0f, 0f);
            var anchor = new GameObject("进气口");
            anchor.transform.SetParent(target.transform);
            anchor.transform.localPosition = new Vector3(0f, 2f, 0f);
            anchor.AddComponent<SemanticAnchorMarker>().Configure(
                "端口.进气",
                SemanticAnchorKind.ConnectionPort);
            var dispatcher = UnityPresentationDispatcher.CreateDefault(
                registry);
            var command = new PresentationEffectCommand(
                "命令.吸附",
                "interaction.snap-to-anchor",
                new PresentationTargetReference(
                    "器材.集气瓶",
                    PresentationLocationKind.SemanticAnchor,
                    "端口.进气"),
                new PresentationSignalContextReference(
                    "器材.导管",
                    "器材.导管",
                    "器材.集气瓶"),
                "transform.position",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                new Dictionary<string, PresentationValue>());

            dispatcher.Dispatch(command);

            Assert.That(source.transform.position, Is.EqualTo(
                anchor.transform.position));
            Assert.That(target.transform.position, Is.EqualTo(
                new Vector3(1f, 0f, 0f)));

            dispatcher.End(command);
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 粒子效果只播放指定插槽上的组件()
        {
            var root = new GameObject("特效插槽执行测试");
            var registry = new CourseEntityViewRegistry();
            var entity = CreateView(root, registry, "器材.木条");
            var rootParticles = entity.AddComponent<ParticleSystem>();
            var slot = CreateSlot(
                entity,
                "插槽.燃烧",
                PresentationSlotKind.Combustion);
            var slotParticles = slot.AddComponent<ParticleSystem>();
            rootParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            slotParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            var dispatcher = UnityPresentationDispatcher.CreateDefault(
                registry);

            dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.火焰",
                "vfx.play",
                SlotTarget("器材.木条", "插槽.燃烧"),
                Context("器材.木条"),
                "vfx.playback",
                10,
                PresentationEffectLifecycle.OneShot,
                new Dictionary<string, PresentationValue>()));
            yield return null;

            Assert.That(rootParticles.isPlaying, Is.False);
            Assert.That(slotParticles.isPlaying, Is.True);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 材质和音频使用准备阶段解析的资源与指定插槽()
        {
            var root = new GameObject("资源执行测试");
            var registry = new CourseEntityViewRegistry();
            var entity = CreateView(root, registry, "器材.试管");
            var rootRenderer = entity.AddComponent<MeshRenderer>();
            var rootAudio = entity.AddComponent<AudioSource>();
            var materialSlot = CreateSlot(
                entity,
                "插槽.材质",
                PresentationSlotKind.Content);
            var slotRenderer = materialSlot.AddComponent<MeshRenderer>();
            var audioSlot = CreateSlot(
                entity,
                "插槽.音频",
                PresentationSlotKind.Content);
            var slotAudio = audioSlot.AddComponent<AudioSource>();
            var shader = Shader.Find("Sprites/Default");
            var material = new Material(shader);
            var clip = AudioClip.Create(
                "测试音效",
                128,
                1,
                8000,
                false);
            var resources = new RecordingResourceResolver(
                new Dictionary<string, Object>
                {
                    ["材质.测试"] = material,
                    ["音效.测试"] = clip
                });
            var dispatcher = UnityPresentationDispatcher.CreateDefault(
                registry,
                resources);

            dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.材质",
                "material.set",
                SlotTarget("器材.试管", "插槽.材质"),
                Context("器材.试管"),
                "renderer.material",
                10,
                PresentationEffectLifecycle.UntilReplaced,
                new[]
                {
                    Pair(
                        "资源ID",
                        PresentationValue.FromText("材质.测试"))
                }));
            dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.音频",
                "audio.play",
                SlotTarget("器材.试管", "插槽.音频"),
                Context("器材.试管"),
                "audio.playback",
                10,
                PresentationEffectLifecycle.OneShot,
                new[]
                {
                    Pair(
                        "资源ID",
                        PresentationValue.FromText("音效.测试"))
                }));

            Assert.That(rootRenderer.sharedMaterial, Is.Not.SameAs(material));
            Assert.That(slotRenderer.sharedMaterial, Is.SameAs(material));
            Assert.That(rootAudio.clip, Is.Null);
            Assert.That(slotAudio.clip, Is.SameAs(clip));
            Assert.That(
                resources.ResolvedIds,
                Is.EquivalentTo(new[] { "材质.测试", "音效.测试" }));

            Object.Destroy(root);
            Object.Destroy(material);
            Object.Destroy(clip);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 缺少插槽组件时执行失败且不会回退修改根节点组件()
        {
            var root = new GameObject("缺组件执行测试");
            var registry = new CourseEntityViewRegistry();
            var entity = CreateView(root, registry, "器材.木条");
            var rootParticles = entity.AddComponent<ParticleSystem>();
            rootParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            CreateSlot(
                entity,
                "插槽.燃烧",
                PresentationSlotKind.Combustion);
            var dispatcher = UnityPresentationDispatcher.CreateDefault(
                registry,
                failurePolicy:
                PresentationFailurePolicy.RecordAndContinue);

            var result = dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.错误火焰",
                "vfx.play",
                SlotTarget("器材.木条", "插槽.燃烧"),
                Context("器材.木条"),
                "vfx.playback",
                10,
                PresentationEffectLifecycle.OneShot,
                new Dictionary<string, PresentationValue>()));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("ParticleSystem"));
            Assert.That(rootParticles.isPlaying, Is.False);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UI效果只调用准备阶段解析的接收器()
        {
            var root = new GameObject("UI 接收器测试");
            var registry = new CourseEntityViewRegistry();
            var entity = CreateView(root, registry, "器材.试管");
            CreateSlot(
                entity,
                "插槽.高亮",
                PresentationSlotKind.Highlight);
            var messages = new RecordingMessageSink();
            var highlights = new RecordingHighlightSink();
            var dispatcher = UnityPresentationDispatcher.CreateDefault(
                registry,
                messages: messages,
                highlights: highlights);

            dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.提示",
                "ui.message",
                new PresentationTargetReference(
                    null,
                    PresentationLocationKind.GlobalReceiver,
                    null),
                Context("器材.试管"),
                "ui.message",
                10,
                PresentationEffectLifecycle.OneShot,
                new[]
                {
                    Pair("文案", PresentationValue.FromText("请重新连接"))
                }));
            dispatcher.Dispatch(new PresentationEffectCommand(
                "命令.高亮",
                "ui.highlight",
                SlotTarget("器材.试管", "插槽.高亮"),
                Context("器材.试管"),
                "ui.highlight",
                10,
                PresentationEffectLifecycle.OneShot,
                new Dictionary<string, PresentationValue>()));

            Assert.That(messages.Text, Is.EqualTo("请重新连接"));
            Assert.That(messages.Duration, Is.EqualTo(2d));
            Assert.That(highlights.EntityId, Is.EqualTo("器材.试管"));
            Assert.That(highlights.Duration, Is.EqualTo(2d));

            Object.Destroy(root);
            yield return null;
        }

        [Test]
        public void 默认执行器覆盖首批全部标准效果()
        {
            var root = new GameObject("执行器注册测试");
            try
            {
                var dispatcher = UnityPresentationDispatcher.CreateDefault(
                    new CourseEntityViewRegistry());
                Assert.That(
                    dispatcher.RegisteredEffectIds,
                Is.EquivalentTo(new[]
                {
                    "interaction.affordance",
                    "interaction.follow-anchor",
                        "interaction.stop-follow",
                        "interaction.snap-to-anchor",
                        "transform.attach",
                        "transform.detach",
                        "transform.oscillate",
                        "physics.enable",
                        "physics.disable",
                        "liquid.set-level",
                        "material.set",
                        "material.animate",
                        "renderer.show",
                        "renderer.hide",
                        "audio.play",
                        "vfx.play",
                        "ui.message",
                        "ui.highlight"
                    }));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static PresentationEffectCommand Command(
            string commandId,
            string effectId,
            string channel)
        {
            return new PresentationEffectCommand(
                commandId,
                effectId,
                Target("器材.试管"),
                Context("器材.试管"),
                channel,
                10,
                PresentationEffectLifecycle.UntilReplaced,
                new Dictionary<string, PresentationValue>());
        }

        private static PresentationTargetReference Target(string entityId)
        {
            return new PresentationTargetReference(
                entityId,
                PresentationLocationKind.EntityRoot,
                null);
        }

        private static PresentationTargetReference SlotTarget(
            string entityId,
            string slotId)
        {
            return new PresentationTargetReference(
                entityId,
                PresentationLocationKind.PresentationSlot,
                slotId);
        }

        private static KeyValuePair<string, PresentationValue> Pair(
            string name,
            PresentationValue value)
        {
            return new KeyValuePair<string, PresentationValue>(name, value);
        }

        private static PresentationSignalContextReference Context(
            string entityId)
        {
            return new PresentationSignalContextReference(
                entityId,
                entityId,
                null);
        }

        private static GameObject CreateView(
            GameObject root,
            CourseEntityViewRegistry registry,
            string entityId)
        {
            var value = new GameObject(entityId);
            value.transform.SetParent(root.transform);
            var view = value.AddComponent<CourseEntityView>();
            view.Configure(entityId);
            registry.Register(view);
            return value;
        }

        private static GameObject CreateSlot(
            GameObject entity,
            string slotId,
            PresentationSlotKind kind)
        {
            var slot = new GameObject(slotId);
            slot.transform.SetParent(entity.transform);
            slot.AddComponent<PresentationSlotMarker>().Configure(
                slotId,
                kind);
            return slot;
        }

        private sealed class RecordingResourceResolver :
            IPresentationResourceResolver
        {
            private readonly IReadOnlyDictionary<string, Object> _resources;
            private readonly List<string> _resolvedIds =
                new List<string>();

            public RecordingResourceResolver(
                IReadOnlyDictionary<string, Object> resources)
            {
                _resources = resources;
            }

            public IReadOnlyList<string> ResolvedIds => _resolvedIds;

            public bool TryResolve(
                string resourceId,
                Type expectedType,
                out Object resource)
            {
                _resolvedIds.Add(resourceId);
                return _resources.TryGetValue(resourceId, out resource)
                       && expectedType.IsInstanceOfType(resource);
            }
        }

        private sealed class RecordingMessageSink : IPresentationMessageSink
        {
            public string Text { get; private set; }

            public double Duration { get; private set; }

            public void ShowMessage(string text, double durationSeconds)
            {
                Text = text;
                Duration = durationSeconds;
            }
        }

        private sealed class RecordingHighlightSink :
            IPresentationHighlightSink
        {
            public string EntityId { get; private set; }

            public double Duration { get; private set; }

            public void Highlight(
                string entityId,
                Color color,
                double durationSeconds)
            {
                EntityId = entityId;
                Duration = durationSeconds;
            }
        }
    }
}
