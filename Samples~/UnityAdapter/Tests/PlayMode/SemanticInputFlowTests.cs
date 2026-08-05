using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Interaction.Courses;
using VirtualLab.Kernel;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Input;
using VirtualLab.UnityAdapters.Physics;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Engine.PlayModeTests
{
    public sealed class SemanticInputFlowTests
    {
        [UnityTest]
        public IEnumerator Pointer与测试输入对同一意图生成字段一致的语义命令()
        {
            var adapterObject = new GameObject("语义输入适配器");
            try
            {
                var mapper = new SemanticActionGestureMapper();
                var facts = new FixedSpatialFactProvider(new SpatialFactSet(
                    new[]
                    {
                        Pair(
                            "空间距离米",
                            StructuredValue.FromNumber(0.02d)),
                        Pair(
                            "空间接触",
                            StructuredValue.FromBoolean(true))
                    }));
                var pointer =
                    adapterObject.AddComponent<SemanticPointerInputAdapter>();
                pointer.Configure(mapper, facts);
                var parameters = new[]
                {
                    Pair(
                        "抓取点",
                        StructuredValue.FromText("管口"))
                };
                var intent = new SemanticInputIntent(
                    "抓取",
                    "学生",
                    "器材.试管",
                    null,
                    parameters);

                var pointerRequest = pointer.CreateRequest(
                    "命令.0001",
                    intent.ActionId,
                    intent.ActorEntityId,
                    intent.SourceEntityId,
                    intent.TargetEntityId,
                    intent.Parameters);
                var testRequest = mapper.Map(
                    "命令.0001",
                    intent,
                    facts.Measure(intent));

                Assert.That(pointerRequest.ActionId,
                    Is.EqualTo(testRequest.ActionId));
                Assert.That(pointerRequest.ActorEntityId,
                    Is.EqualTo(testRequest.ActorEntityId));
                Assert.That(pointerRequest.SourceEntityId,
                    Is.EqualTo(testRequest.SourceEntityId));
                Assert.That(pointerRequest.TargetEntityId,
                    Is.EqualTo(testRequest.TargetEntityId));
                Assert.That(
                    pointerRequest.Parameters.Keys,
                    Is.EquivalentTo(testRequest.Parameters.Keys));
                Assert.That(
                    pointerRequest.Parameters["空间距离米"].Number,
                    Is.EqualTo(0.02d));
            }
            finally
            {
                UnityEngine.Object.Destroy(adapterObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator 创建语义请求不会直接改变场景Transform()
        {
            var adapterObject = new GameObject("只读输入适配器");
            var source = new GameObject("试管");
            try
            {
                source.transform.position = new Vector3(1f, 2f, 3f);
                var original = source.transform.localToWorldMatrix;
                var pointer =
                    adapterObject.AddComponent<SemanticPointerInputAdapter>();
                pointer.Configure(
                    new SemanticActionGestureMapper(),
                    new FixedSpatialFactProvider(SpatialFactSet.Empty));

                pointer.CreateRequest(
                    "命令.0002",
                    "抓取",
                    "学生",
                    "器材.试管",
                    null,
                    Array.Empty<
                        KeyValuePair<string, StructuredValue>>());

                Assert.That(source.transform.localToWorldMatrix,
                    Is.EqualTo(original));
            }
            finally
            {
                UnityEngine.Object.Destroy(adapterObject);
                UnityEngine.Object.Destroy(source);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator 语义输入经过内核审核后才触发表现命令()
        {
            var bootstrapObject = new GameObject("配置课程启动器");
            try
            {
                var world = new ExperimentWorld();
                world.AddEntity(new ExperimentEntity(new EntityId("学生")));
                world.AddEntity(
                    new ExperimentEntity(new EntityId("器材.试管")));
                var session = InteractionCourseRegistrations.CreateSession(
                    world,
                    new[]
                    {
                        ConfiguredActionDefinition.CreateGeneric(
                            "抓取",
                            Array.Empty<StructuredRuleDefinition>(),
                            Array.Empty<ConfiguredMutationDefinition>())
                    });
                var reactions = new PresentationReactionEngine(
                    new[]
                    {
                        new PresentationRuleDefinition(
                            "表现规则.抓取成功",
                            PresentationTriggerKind.ActionAccepted,
                            "抓取",
                            new[] { "效果定义.抓取成功" }),
                        new PresentationRuleDefinition(
                            "表现规则.抓取可操作性",
                            PresentationTriggerKind.ActionAvailabilityChanged,
                            "抓取",
                            new[] { "效果定义.抓取可操作性" })
                    },
                    new[]
                    {
                        new PresentationEffectDefinition(
                            "效果定义.抓取成功",
                            "ui.message",
                            new PresentationTargetSelector(
                                PresentationEntitySelectorKind.Global,
                                null,
                                PresentationLocationKind.GlobalReceiver,
                                null),
                            "ui.message",
                            0,
                            PresentationEffectLifecycle.OneShot,
                            new[]
                            {
                                new PresentationParameterBinding(
                                    "文案",
                                    PresentationParameterSource.Constant,
                                    PresentationValue.FromText("允许抓取"),
                                    null)
                            }),
                        new PresentationEffectDefinition(
                            "效果定义.抓取可操作性",
                            "interaction.affordance",
                            new PresentationTargetSelector(
                                PresentationEntitySelectorKind.Global,
                                null,
                                PresentationLocationKind.GlobalReceiver,
                                null),
                            "interaction.affordance",
                            0,
                            PresentationEffectLifecycle.OneShot,
                            new[]
                            {
                                PayloadBinding("是否允许"),
                                PayloadBinding("可操作性分类"),
                                PayloadBinding("拒绝代码"),
                                PayloadBinding("文案ID")
                            })
                    });
                var messages = new RecordingMessageSink();
                var affordances = new RecordingAffordanceSink();
                var presenter =
                    UnityPresentationDispatcher.CreateDefault(
                        new CourseEntityViewRegistry(),
                        messages: messages,
                        affordances: affordances);
                var bootstrap =
                    bootstrapObject.AddComponent<
                        ConfigDrivenCourseBootstrap>();
                bootstrap.ConfigureRuntime(
                    new CourseRuntimeFacade(session),
                    reactions,
                    presenter);
                var request = new SemanticActionGestureMapper().Map(
                    "命令.抓取",
                    new SemanticInputIntent(
                        "抓取",
                        "学生",
                        "器材.试管",
                        null,
                        Array.Empty<
                            KeyValuePair<string, StructuredValue>>()),
                    SpatialFactSet.Empty);

                var availability = bootstrap.QueryAvailability(request);

                Assert.That(
                    availability.Availability.Kind,
                    Is.EqualTo(ActionAvailabilityKind.Allowed));
                Assert.That(
                    availability.PresentationCommands.Single().EffectId,
                    Is.EqualTo("interaction.affordance"));
                Assert.That(affordances.SourceEntityId, Is.EqualTo("器材.试管"));
                Assert.That(affordances.CanExecute, Is.True);
                Assert.That(
                    affordances.Kind,
                    Is.EqualTo(ActionAvailabilityKind.Allowed.ToString()));

                var result = bootstrap.Dispatch(request);
                Assert.That(result.Outcome.IsAccepted, Is.True);
                Assert.That(
                    result.PresentationCommands.Single().EffectId,
                    Is.EqualTo("ui.message"));
                Assert.That(messages.LastMessage, Is.EqualTo("允许抓取"));
            }
            finally
            {
                UnityEngine.Object.Destroy(bootstrapObject);
            }

            yield return null;
        }

        private static PresentationParameterBinding PayloadBinding(
            string name)
        {
            return new PresentationParameterBinding(
                name,
                PresentationParameterSource.SignalPayload,
                null,
                name);
        }

        [UnityTest]
        public IEnumerator 场景装配器只实例化实验总预制体并注册内部实体()
        {
            var parent = new GameObject("课程场景根");
            var prefab = new GameObject("实验总Prefab");
            try
            {
                var entityObject = new GameObject("器材.试管");
                entityObject.transform.SetParent(prefab.transform, false);
                entityObject.AddComponent<CourseEntityView>()
                    .Configure("器材.试管");
                var definition = CompiledCourseDefinition.CreateBasic(
                    "任意课程ID",
                    new[]
                    {
                        new CourseEntityDefinition(
                            "器材.试管",
                            Array.Empty<string>())
                    },
                    Array.Empty<ActionPolicyDefinition>(),
                    Array.Empty<CourseGoalDefinition>(),
                    Array.Empty<CourseAssessmentDefinition>(),
                    new[]
                    {
                        new CourseResourceDefinition(
                            "预制体.实验",
                            "测试资源/实验.prefab")
                    },
                    "预制体.实验");
                var resources = new CourseRuntimeResourceResolver(
                    definition,
                    new FixedCourseResourceLoader(
                        "测试资源/实验.prefab",
                        prefab));

                var assembly = new CourseSceneAssembler().Assemble(
                    definition,
                    resources,
                    parent.transform);

                Assert.That(
                    assembly.CourseViews.TryGet(
                        "器材.试管",
                        out var assembled),
                    Is.True);
                Assert.That(assembled.name, Is.EqualTo("器材.试管"));
                Assert.That(assembled.transform.parent,
                    Is.SameAs(assembly.ExperimentRoot.transform));
                Assert.That(assembly.ExperimentRoot.transform.parent,
                    Is.SameAs(parent.transform));
            }
            finally
            {
                UnityEngine.Object.Destroy(parent);
                UnityEngine.Object.Destroy(prefab);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator 实验总预制体缺少声明实体时拒绝装配并清理实例()
        {
            var parent = new GameObject("课程场景根");
            var prefab = new GameObject("不完整实验总Prefab");
            try
            {
                var definition = CompiledCourseDefinition.CreateBasic(
                    "不完整课程",
                    new[]
                    {
                        new CourseEntityDefinition(
                            "器材.试管",
                            Array.Empty<string>())
                    },
                    Array.Empty<ActionPolicyDefinition>(),
                    Array.Empty<CourseGoalDefinition>(),
                    Array.Empty<CourseAssessmentDefinition>(),
                    new[]
                    {
                        new CourseResourceDefinition(
                            "预制体.实验",
                            "测试资源/不完整实验.prefab")
                    },
                    "预制体.实验");
                var resources = new CourseRuntimeResourceResolver(
                    definition,
                    new FixedCourseResourceLoader(
                        "测试资源/不完整实验.prefab",
                        prefab));

                Assert.Throws<InvalidOperationException>(() =>
                    new CourseSceneAssembler().Assemble(
                        definition,
                        resources,
                        parent.transform));

                // 运行模式下 Destroy 延迟到帧末执行。
                yield return null;
                Assert.That(parent.transform.childCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.Destroy(parent);
                UnityEngine.Object.Destroy(prefab);
            }

            yield return null;
        }

        private static KeyValuePair<string, StructuredValue> Pair(
            string key,
            StructuredValue value)
        {
            return new KeyValuePair<string, StructuredValue>(key, value);
        }

        private sealed class FixedCourseResourceLoader : ICourseResourceLoader
        {
            private readonly string _path;
            private readonly UnityEngine.Object _resource;

            public FixedCourseResourceLoader(
                string path,
                UnityEngine.Object resource)
            {
                _path = path;
                _resource = resource;
            }

            public bool TryLoad(
                string resourcePath,
                Type expectedType,
                out UnityEngine.Object resource)
            {
                resource = string.Equals(
                               resourcePath,
                               _path,
                               StringComparison.Ordinal)
                           && expectedType.IsInstanceOfType(_resource)
                    ? _resource
                    : null;
                return resource != null;
            }
        }

        private sealed class FixedSpatialFactProvider :
            ISpatialFactProvider
        {
            private readonly SpatialFactSet _facts;

            public FixedSpatialFactProvider(SpatialFactSet facts)
            {
                _facts = facts;
            }

            public SpatialFactSet Measure(SemanticInputIntent intent)
            {
                return _facts;
            }
        }

        private sealed class RecordingMessageSink : IPresentationMessageSink
        {
            public string LastMessage { get; private set; }

            public void ShowMessage(string text, double durationSeconds)
            {
                LastMessage = text;
            }
        }

        private sealed class RecordingAffordanceSink :
            IInteractionAffordanceSink
        {
            public string SourceEntityId { get; private set; }

            public bool CanExecute { get; private set; }

            public string Kind { get; private set; }

            public void ApplyActionAvailability(
                string sourceEntityId,
                string targetEntityId,
                bool canExecute,
                string kind,
                string rejectionCode,
                string messageId)
            {
                SourceEntityId = sourceEntityId;
                CanExecute = canExecute;
                Kind = kind;
            }
        }
    }
}
