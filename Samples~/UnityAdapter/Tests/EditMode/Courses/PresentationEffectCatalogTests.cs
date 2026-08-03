using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class PresentationEffectCatalogTests
    {
        [Test]
        public void 新原语注册一次即可按中文名和协议ID查询()
        {
            var descriptor = Descriptor(
                "测试闪光",
                "test.flash",
                () => new RecordingExecutor("test.flash"));
            var catalog = new PresentationEffectCatalog(
                new[] { descriptor });

            Assert.That(
                catalog.RequireByChineseName("测试闪光"),
                Is.SameAs(descriptor));
            Assert.That(
                catalog.RequireByProtocolId("test.flash"),
                Is.SameAs(descriptor));
            Assert.That(
                catalog.CreateExecutors().Single().EffectId,
                Is.EqualTo("test.flash"));
        }

        [Test]
        public void 目录拒绝重复中文名或协议ID()
        {
            Assert.Throws<ArgumentException>(() =>
                new PresentationEffectCatalog(new[]
                {
                    Descriptor(
                        "测试闪光",
                        "test.flash.first",
                        () => new RecordingExecutor("test.flash.first")),
                    Descriptor(
                        "测试闪光",
                        "test.flash.second",
                        () => new RecordingExecutor("test.flash.second"))
                }));
            Assert.Throws<ArgumentException>(() =>
                new PresentationEffectCatalog(new[]
                {
                    Descriptor(
                        "测试闪光一",
                        "test.flash",
                        () => new RecordingExecutor("test.flash")),
                    Descriptor(
                        "测试闪光二",
                        "test.flash",
                        () => new RecordingExecutor("test.flash"))
                }));
        }

        [Test]
        public void 描述符拒绝非法默认生命周期和重复参数()
        {
            Assert.Throws<ArgumentException>(() =>
                new PresentationEffectDescriptor(
                    "test.flash",
                    "测试闪光",
                    "test.channel",
                    PresentationEffectLifecycle.UntilReplaced,
                    new[] { PresentationEffectLifecycle.OneShot },
                    GlobalTarget(),
                    Array.Empty<PresentationParameterDescriptor>(),
                    () => new RecordingExecutor("test.flash")));

            var parameter = new PresentationParameterDescriptor(
                "持续秒数",
                PresentationParameterKind.Number,
                true,
                PresentationValue.FromNumber(1),
                0,
                2,
                Array.Empty<string>(),
                null);
            Assert.Throws<ArgumentException>(() =>
                new PresentationEffectDescriptor(
                    "test.flash",
                    "测试闪光",
                    "test.channel",
                    PresentationEffectLifecycle.OneShot,
                    new[] { PresentationEffectLifecycle.OneShot },
                    GlobalTarget(),
                    new[] { parameter, parameter },
                    () => new RecordingExecutor("test.flash")));
        }

        [Test]
        public void 参数描述符拒绝类型错误或超出范围的默认值()
        {
            Assert.Throws<ArgumentException>(() =>
                new PresentationParameterDescriptor(
                    "持续秒数",
                    PresentationParameterKind.Number,
                    false,
                    PresentationValue.FromText("一秒"),
                    0,
                    2,
                    Array.Empty<string>(),
                    null));
            Assert.Throws<ArgumentException>(() =>
                new PresentationParameterDescriptor(
                    "液面比例",
                    PresentationParameterKind.Number,
                    false,
                    PresentationValue.FromNumber(1.1),
                    0,
                    1,
                    Array.Empty<string>(),
                    null));
        }

        [Test]
        public void 目录拒绝执行器协议与描述符不一致()
        {
            Assert.Throws<ArgumentException>(() =>
                new PresentationEffectCatalog(new[]
                {
                    Descriptor(
                        "测试闪光",
                        "test.flash",
                        () => new RecordingExecutor("test.other"))
                }));
        }

        [Test]
        public void 内置目录覆盖首批全部标准效果()
        {
            var catalog = BuiltInPresentationEffectCatalog.Create();

            Assert.That(
                catalog.Descriptors.Select(value => value.ProtocolId),
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
            Assert.That(
                catalog.Descriptors.Select(value => value.ChineseName),
                Is.Unique);

            var liquid = catalog.RequireByProtocolId("liquid.set-level");
            var level = liquid.Parameters.Single(
                value => value.Name == "液面比例");
            Assert.That(level.Required, Is.True);
            Assert.That(level.Minimum, Is.EqualTo(0));
            Assert.That(level.Maximum, Is.EqualTo(1));

            var snap = catalog.RequireByProtocolId(
                "interaction.snap-to-anchor");
            Assert.That(
                snap.TargetContract.AllowedLocations,
                Is.EquivalentTo(new[]
                {
                    PresentationLocationKind.SemanticAnchor
                }));
            Assert.That(
                snap.TargetContract.RequiredContextEntities,
                Is.EquivalentTo(new[]
                {
                    PresentationContextEntityKind.ActionSource,
                    PresentationContextEntityKind.ActionTarget
                }));

            var vfx = catalog.RequireByProtocolId("vfx.play");
            Assert.That(
                vfx.AllowedLifecycles,
                Is.EquivalentTo(new[]
                {
                    PresentationEffectLifecycle.OneShot,
                    PresentationEffectLifecycle.UntilReplaced,
                    PresentationEffectLifecycle.WhileActive
                }));
        }

        private static PresentationEffectDescriptor Descriptor(
            string chineseName,
            string protocolId,
            Func<IPresentationEffectExecutor> createExecutor)
        {
            return new PresentationEffectDescriptor(
                protocolId,
                chineseName,
                "test.channel",
                PresentationEffectLifecycle.OneShot,
                new[] { PresentationEffectLifecycle.OneShot },
                GlobalTarget(),
                Array.Empty<PresentationParameterDescriptor>(),
                createExecutor);
        }

        private static PresentationTargetContract GlobalTarget()
        {
            return new PresentationTargetContract(
                new[] { PresentationLocationKind.GlobalReceiver },
                Array.Empty<PresentationSlotKind>(),
                Array.Empty<SemanticAnchorKind>(),
                Array.Empty<PresentationContextEntityKind>());
        }

        private sealed class RecordingExecutor : IPresentationEffectExecutor
        {
            public RecordingExecutor(string effectId)
            {
                EffectId = effectId;
            }

            public string EffectId { get; }

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
