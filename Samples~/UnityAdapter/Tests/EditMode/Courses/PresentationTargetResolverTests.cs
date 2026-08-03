using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class PresentationTargetResolverTests
    {
        private GameObject _root;
        private CourseEntityViewRegistry _views;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("表现目标解析测试");
            _views = new CourseEntityViewRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        [TestCase(PresentationLocationKind.EntityRoot)]
        [TestCase(PresentationLocationKind.PresentationSlot)]
        [TestCase(PresentationLocationKind.SemanticAnchor)]
        public void 可精确解析实体根节点插槽和语义锚点(
            PresentationLocationKind locationKind)
        {
            var entity = CreateEntity("器材.试管");
            var locationId = default(string);
            Transform expected = entity.transform;
            if (locationKind == PresentationLocationKind.PresentationSlot)
            {
                var slot = new GameObject("液体插槽");
                slot.transform.SetParent(entity.transform);
                slot.AddComponent<PresentationSlotMarker>().Configure(
                    "插槽.液体",
                    PresentationSlotKind.Liquid);
                locationId = "插槽.液体";
                expected = slot.transform;
            }
            else if (locationKind == PresentationLocationKind.SemanticAnchor)
            {
                var anchor = new GameObject("连接端口");
                anchor.transform.SetParent(entity.transform);
                anchor.AddComponent<SemanticAnchorMarker>().Configure(
                    "端口.连接",
                    SemanticAnchorKind.ConnectionPort);
                locationId = "端口.连接";
                expected = anchor.transform;
            }

            var descriptor = Descriptor(
                locationKind,
                locationKind == PresentationLocationKind.PresentationSlot
                    ? new[] { PresentationSlotKind.Liquid }
                    : Array.Empty<PresentationSlotKind>(),
                locationKind == PresentationLocationKind.SemanticAnchor
                    ? new[] { SemanticAnchorKind.ConnectionPort }
                    : Array.Empty<SemanticAnchorKind>());
            var result = new PresentationTargetResolver(_views)
                .ResolveTarget(
                    Command(locationKind, locationId),
                    descriptor);

            Assert.That(result.Transform, Is.SameAs(expected));
            Assert.That(result.LocationKind, Is.EqualTo(locationKind));
        }

        [Test]
        public void 可解析全局接收器()
        {
            var receiver = new object();
            var resolver = new PresentationTargetResolver(
                _views,
                _ => receiver);
            var descriptor = Descriptor(
                PresentationLocationKind.GlobalReceiver,
                Array.Empty<PresentationSlotKind>(),
                Array.Empty<SemanticAnchorKind>());

            var result = resolver.ResolveTarget(
                Command(PresentationLocationKind.GlobalReceiver, null),
                descriptor);

            Assert.That(result.GlobalReceiver, Is.SameAs(receiver));
            Assert.That(result.Transform, Is.Null);
        }

        [Test]
        public void 默认全局接收器按目录协议注册而不是由上下文分支判断()
        {
            var messages = new RecordingMessageSink();
            var highlights = new RecordingHighlightSink();
            var affordances = new RecordingAffordanceSink();
            var context = new PresentationExecutionContext(
                _views,
                null,
                messages,
                highlights,
                affordances);
            var catalog = BuiltInPresentationEffectCatalog.Create();

            Assert.That(
                context.ResolveGlobalReceiver(catalog.RequireByProtocolId(
                    BuiltInPresentationEffectCatalog.UiMessageProtocolId)),
                Is.SameAs(messages));
            Assert.That(
                context.ResolveGlobalReceiver(catalog.RequireByProtocolId(
                    BuiltInPresentationEffectCatalog.UiHighlightProtocolId)),
                Is.SameAs(highlights));
            Assert.That(
                context.ResolveGlobalReceiver(catalog.RequireByProtocolId(
                    BuiltInPresentationEffectCatalog
                        .InteractionAffordanceProtocolId)),
                Is.SameAs(affordances));
        }

        [TestCase("缺少实体")]
        [TestCase("重复插槽")]
        [TestCase("插槽类型不符")]
        public void 非法场景绑定在准备阶段给出中文错误(string scenario)
        {
            var descriptor = Descriptor(
                PresentationLocationKind.PresentationSlot,
                new[] { PresentationSlotKind.Liquid },
                Array.Empty<SemanticAnchorKind>());
            var entityId = "器材.试管";
            if (scenario != "缺少实体")
            {
                var entity = CreateEntity(entityId);
                var count = scenario == "重复插槽" ? 2 : 1;
                for (var index = 0; index < count; index++)
                {
                    var slot = new GameObject("液体插槽" + index);
                    slot.transform.SetParent(entity.transform);
                    slot.AddComponent<PresentationSlotMarker>().Configure(
                        "插槽.液体",
                        scenario == "插槽类型不符"
                            ? PresentationSlotKind.Content
                            : PresentationSlotKind.Liquid);
                }
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PresentationTargetResolver(_views).ResolveTarget(
                    Command(
                        PresentationLocationKind.PresentationSlot,
                        "插槽.液体",
                        entityId),
                    descriptor));

            Assert.That(exception.Message, Does.Contain(
                scenario == "缺少实体" ? "找不到目标实体" :
                scenario == "重复插槽" ? "存在重复表现插槽" :
                "不符合原语"));
        }

        [Test]
        public void 按描述符要求解析动作来源和动作目标()
        {
            var source = CreateEntity("器材.导管");
            var target = CreateEntity("器材.集气瓶");
            var anchor = new GameObject("连接端口");
            anchor.transform.SetParent(target.transform);
            anchor.AddComponent<SemanticAnchorMarker>().Configure(
                "端口.进气",
                SemanticAnchorKind.ConnectionPort);
            var descriptor = new PresentationEffectDescriptor(
                "test.anchor",
                "测试锚点",
                "test.channel",
                PresentationEffectLifecycle.OneShot,
                new[] { PresentationEffectLifecycle.OneShot },
                new PresentationTargetContract(
                    new[] { PresentationLocationKind.SemanticAnchor },
                    Array.Empty<PresentationSlotKind>(),
                    new[] { SemanticAnchorKind.ConnectionPort },
                    new[]
                    {
                        PresentationContextEntityKind.ActionSource,
                        PresentationContextEntityKind.ActionTarget
                    }),
                Array.Empty<PresentationParameterDescriptor>(),
                () => new RecordingExecutor());
            var command = new PresentationEffectCommand(
                "命令.连接",
                "test.anchor",
                new PresentationTargetReference(
                    "器材.集气瓶",
                    PresentationLocationKind.SemanticAnchor,
                    "端口.进气"),
                new PresentationSignalContextReference(
                    null,
                    "器材.导管",
                    "器材.集气瓶"),
                "test.channel",
                0,
                PresentationEffectLifecycle.OneShot,
                Array.Empty<KeyValuePair<string, PresentationValue>>());

            var context = new PresentationTargetResolver(_views)
                .ResolveSignalContext(command, descriptor);

            Assert.That(context.ActionSourceView, Is.SameAs(
                source.GetComponent<CourseEntityView>()));
            Assert.That(context.ActionTargetView, Is.SameAs(
                target.GetComponent<CourseEntityView>()));
        }

        [Test]
        public void 准备失败不会进入持续通道仲裁()
        {
            CreateEntity("器材.试管");
            var arbiter = new PresentationChannelArbiter();
            var catalog = BuiltInPresentationEffectCatalog.Create();
            var dispatcher = new UnityPresentationDispatcher(
                new PresentationExecutionContext(
                    _views,
                    null,
                    null,
                    null),
                catalog,
                catalog.CreateExecutors(),
                arbiter,
                PresentationFailurePolicy.RecordAndContinue);
            var command = new PresentationEffectCommand(
                "权威状态:液面",
                "liquid.set-level",
                new PresentationTargetReference(
                    "器材.试管",
                    PresentationLocationKind.PresentationSlot,
                    "插槽.液体"),
                new PresentationSignalContextReference(null, null, null),
                "liquid.level",
                0,
                PresentationEffectLifecycle.UntilReplaced,
                new[]
                {
                    new KeyValuePair<string, PresentationValue>(
                        "液面比例",
                        PresentationValue.FromNumber(0.5))
                });

            var result = dispatcher.Dispatch(command);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("找不到表现插槽"));
            Assert.That(arbiter.CurrentCommands, Is.Empty);
        }

        private GameObject CreateEntity(string entityId)
        {
            var value = new GameObject(entityId);
            value.transform.SetParent(_root.transform);
            var view = value.AddComponent<CourseEntityView>();
            view.Configure(entityId);
            _views.Register(view);
            return value;
        }

        private sealed class RecordingMessageSink : IPresentationMessageSink
        {
            public void ShowMessage(string text, double durationSeconds)
            {
            }
        }

        private sealed class RecordingHighlightSink :
            IPresentationHighlightSink
        {
            public void Highlight(
                string entityId,
                Color color,
                double durationSeconds)
            {
            }
        }

        private sealed class RecordingAffordanceSink :
            IInteractionAffordanceSink
        {
            public void ApplyActionAvailability(
                string sourceEntityId,
                string targetEntityId,
                bool canExecute,
                string kind,
                string rejectionCode,
                string messageId)
            {
            }
        }

        private static PresentationEffectCommand Command(
            PresentationLocationKind kind,
            string locationId,
            string entityId = "器材.试管")
        {
            return new PresentationEffectCommand(
                "命令.测试",
                "test.effect",
                new PresentationTargetReference(
                    kind == PresentationLocationKind.GlobalReceiver
                        ? null
                        : entityId,
                    kind,
                    locationId),
                new PresentationSignalContextReference(null, null, null),
                "test.channel",
                0,
                PresentationEffectLifecycle.OneShot,
                Array.Empty<KeyValuePair<string, PresentationValue>>());
        }

        private static PresentationEffectDescriptor Descriptor(
            PresentationLocationKind kind,
            IReadOnlyList<PresentationSlotKind> slotKinds,
            IReadOnlyList<SemanticAnchorKind> anchorKinds)
        {
            return new PresentationEffectDescriptor(
                "test.effect",
                "测试效果",
                "test.channel",
                PresentationEffectLifecycle.OneShot,
                new[] { PresentationEffectLifecycle.OneShot },
                new PresentationTargetContract(
                    new[] { kind },
                    slotKinds,
                    anchorKinds,
                    Array.Empty<PresentationContextEntityKind>()),
                Array.Empty<PresentationParameterDescriptor>(),
                () => new RecordingExecutor());
        }

        private sealed class RecordingExecutor : IPresentationEffectExecutor
        {
            public string EffectId => "test.effect";

            public void Execute(
                PreparedPresentationEffect effect,
                PresentationExecutionContext context)
            {
            }

            public void Stop(
                PreparedPresentationEffect effect,
                PresentationExecutionContext context)
            {
            }
        }
    }
}
