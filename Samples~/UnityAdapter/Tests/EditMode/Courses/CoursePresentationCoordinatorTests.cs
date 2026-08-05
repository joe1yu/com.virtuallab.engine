using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Relations;
using VirtualLab.Interaction.Courses;
using VirtualLab.Kernel;
using VirtualLab.Presentation;
using VirtualLab.Teaching.Courses;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CoursePresentationCoordinatorTests
    {
        [Test]
        public void 可操作性投影只使用结构化结果并携带通用参数()
        {
            var dispatcher = new RecordingDispatcher(new List<string>());
            var coordinator = new CoursePresentationCoordinator(
                Session(World()),
                Array.Empty<CoursePresentationStateDefinition>(),
                AvailabilityReactions(),
                dispatcher);
            coordinator.InitializeOrRestore();
            var request = Request("查询.抓取铁架台");
            var availability = new ActionAvailability(
                request.ActionId,
                request.ActorEntityId,
                request.SourceEntityId,
                request.TargetEntityId,
                ActionAvailabilityKind.Disabled,
                "课程.铁架台固定",
                "文案.铁架台固定",
                new[] { "课程.铁架台固定" },
                null);

            coordinator.PresentAvailability(request, availability);

            var command = dispatcher.Dispatched.Single();
            Assert.That(
                command.EffectId,
                Is.EqualTo("interaction.affordance"));
            Assert.That(command.Parameters["是否允许"].Boolean, Is.False);
            Assert.That(
                command.Parameters["可操作性分类"].Text,
                Is.EqualTo("Disabled"));
            Assert.That(
                command.Parameters["拒绝代码"].Text,
                Is.EqualTo("课程.铁架台固定"));
            Assert.That(
                command.Parameters["文案ID"].Text,
                Is.EqualTo("文案.铁架台固定"));
        }

        [Test]
        public void 语义命令参数可以由表现配置按信号载荷动态绑定()
        {
            var dispatcher = new RecordingDispatcher(new List<string>());
            var reactions = new PresentationReactionEngine(
                new[]
                {
                    Rule(
                        "抓取速度表现",
                        PresentationTriggerKind.ActionAccepted,
                        "抓取",
                        "效果.动态跟随")
                },
                new[]
                {
                    new PresentationEffectDefinition(
                        "效果.动态跟随",
                        "interaction.follow-anchor",
                        new PresentationTargetSelector(
                            PresentationEntitySelectorKind.SignalSubject,
                            null,
                            PresentationLocationKind.EntityRoot,
                            null),
                        "interaction.follow",
                        0,
                        PresentationEffectLifecycle.OneShot,
                        new[]
                        {
                            new PresentationParameterBinding(
                                "跟随速度",
                                PresentationParameterSource.SignalPayload,
                                null,
                                "抓取速度")
                        })
                });
            var session = Session(World());
            var coordinator = new CoursePresentationCoordinator(
                session,
                Array.Empty<CoursePresentationStateDefinition>(),
                reactions,
                dispatcher);
            coordinator.InitializeOrRestore();
            var request = new SemanticActionRequest(
                "命令.动态抓取",
                "抓取",
                "操作.动态抓取",
                SemanticActionPhase.Complete,
                0d,
                "学生",
                "器材.试管",
                "学生",
                Parameters(("抓取速度", StructuredValue.FromNumber(2.5))));

            var result = session.Execute(request);
            coordinator.Present(request, result);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(
                dispatcher.Dispatched.Single()
                    .Parameters["跟随速度"].Number,
                Is.EqualTo(2.5));
        }

        [Test]
        public void 初始化或恢复只同步当前权威状态()
        {
            var world = World();
            world.SetRelation(new EntityRelation(
                InteractionRelationTypeIds.HeldBy,
                new EntityId("器材.试管"),
                new EntityId("学生")));
            var calls = new List<string>();
            var dispatcher = new RecordingDispatcher(calls);
            var coordinator = new CoursePresentationCoordinator(
                Session(world),
                new[] { HeldState() },
                Reactions(),
                dispatcher);

            coordinator.InitializeOrRestore();

            Assert.That(
                calls,
                Is.EqualTo(new[] { "Synchronize:权威状态" }));
            Assert.That(
                dispatcher.Synchronized.Select(value => value.CommandId),
                Has.All.StartsWith("权威状态持续"));
        }

        [Test]
        public void 课程初始化信号可以完全由表现配置响应()
        {
            var calls = new List<string>();
            var dispatcher = new RecordingDispatcher(calls);
            var reactions = new PresentationReactionEngine(
                new[]
                {
                    Rule(
                        "初始化内容表现",
                        PresentationTriggerKind.CourseInitialized,
                        PresentationSignalIds.CourseInitialized,
                        "效果.初始化内容")
                },
                new[]
                {
                    Effect(
                        "效果.初始化内容",
                        "renderer.show",
                        PresentationEffectLifecycle.UntilReplaced)
                });
            var coordinator = new CoursePresentationCoordinator(
                Session(World()),
                Array.Empty<CoursePresentationStateDefinition>(),
                reactions,
                dispatcher);

            coordinator.InitializeOrRestore();

            Assert.That(
                calls,
                Is.EqualTo(new[]
                {
                    "Dispatch:初始化内容表现",
                    "Synchronize:权威状态"
                }));
            Assert.That(
                coordinator.LastCommands.Single().EffectId,
                Is.EqualTo("renderer.show"));
        }

        [Test]
        public void 动作事件状态进入和持续同步按固定顺序发生()
        {
            var world = World();
            var session = Session(world);
            var calls = new List<string>();
            var dispatcher = new RecordingDispatcher(calls);
            var coordinator = new CoursePresentationCoordinator(
                session,
                new[] { HeldState() },
                Reactions(),
                dispatcher);
            coordinator.InitializeOrRestore();
            calls.Clear();
            var request = Request("命令.抓取");

            var result = session.Execute(request);
            coordinator.Present(request, result);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(
                calls,
                Is.EqualTo(new[]
                {
                    "Dispatch:动作已接受",
                    "Dispatch:领域事件",
                    "Dispatch:权威状态进入",
                    "Synchronize:权威状态"
                }));
            Assert.That(
                coordinator.LastCommands.Select(value => value.EffectId),
                Is.EqualTo(new[]
                {
                    "test.action",
                    "test.event",
                    "test.entered",
                    "test.active"
                }));
        }

        [Test]
        public void 表现失败不改变科学命令结果和世界状态()
        {
            var world = World();
            var session = Session(world);
            var dispatcher = new RecordingDispatcher(
                new List<string>(),
                failAll: true);
            var coordinator = new CoursePresentationCoordinator(
                session,
                new[] { HeldState() },
                Reactions(),
                dispatcher);
            coordinator.InitializeOrRestore();
            var request = Request("命令.表现失败");

            var result = session.Execute(request);
            coordinator.Present(request, result);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(
                world.Relations.Any(value =>
                    value.TypeId == InteractionRelationTypeIds.HeldBy &&
                    value.Source == new EntityId("器材.试管") &&
                    value.Target == new EntityId("学生")),
                Is.True);
            Assert.That(
                coordinator.LastDispatchResults,
                Has.Some.Matches<PresentationDispatchResult>(
                    value => !value.Succeeded));
        }

        [Test]
        public void 状态退出先分派退出效果再清空持续效果()
        {
            var world = World();
            world.SetRelation(new EntityRelation(
                InteractionRelationTypeIds.HeldBy,
                new EntityId("器材.试管"),
                new EntityId("学生")));
            var session = Session(world);
            var calls = new List<string>();
            var dispatcher = new RecordingDispatcher(calls);
            var coordinator = new CoursePresentationCoordinator(
                session,
                new[] { HeldState() },
                Reactions(),
                dispatcher);
            coordinator.InitializeOrRestore();
            calls.Clear();
            var request = new SemanticActionRequest(
                "命令.放下",
                "放下",
                "操作.放下",
                SemanticActionPhase.Complete,
                0d,
                "学生",
                "器材.试管",
                "学生",
                Array.Empty<KeyValuePair<string, StructuredValue>>());

            var result = session.Execute(request);
            coordinator.Present(request, result);

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(
                calls,
                Is.EqualTo(new[]
                {
                    "Dispatch:权威状态退出",
                    "Synchronize:权威状态"
                }));
            Assert.That(dispatcher.Synchronized, Is.Empty);
        }

        [Test]
        public void 学科持续过程信号会重新评估并同步权威状态()
        {
            var world = World();
            var session = Session(world);
            var calls = new List<string>();
            var dispatcher = new RecordingDispatcher(calls);
            var state = new CoursePresentationStateDefinition(
                "状态.外部过程已推进",
                "器材.试管",
                "器材.试管",
                null,
                new[]
                {
                    new StructuredRuleDefinition(
                        "条件.外部进展",
                        0,
                        TeachingStructuredFactFields.来源对象课程里程碑,
                        StructuredRuleOperator.包含,
                        StructuredValue.FromText("外部过程已推进"),
                        "过程尚未推进")
                });
            var coordinator = new CoursePresentationCoordinator(
                session,
                new[] { state },
                ExternalStateReactions(),
                dispatcher);
            coordinator.InitializeOrRestore();
            calls.Clear();
            world.RequireCourseMilestones().Record(
                "器材.试管",
                "外部过程已推进");

            coordinator.PresentSignal(new PresentationSignal(
                "课程.过程已推进",
                PresentationTriggerKind.DomainEvent,
                "器材.试管",
                null,
                null,
                Array.Empty<
                    KeyValuePair<string, PresentationValue>>()));

            Assert.That(
                calls,
                Is.EqualTo(new[]
                {
                    "Dispatch:权威状态进入",
                    "Synchronize:权威状态"
                }));
        }

        private static CourseRuntimeFacade Session(
            ExperimentWorld world)
        {
            return new CourseRuntimeFacade(
                new ConfigDrivenCourseSession(
                    world,
                    new StructuredRuleEvaluator(new IStructuredFactReader[]
                    {
                        new 来源对象持有者FactReader(),
                        TeachingCourseRegistrations.CreateFactReaders()
                            .Single(value => value.Field ==
                                TeachingStructuredFactFields.来源对象课程里程碑)
                    }),
                    new ConfiguredStateOperationRegistry(),
                    new[]
                    {
                    ConfiguredActionDefinition.CreateGeneric(
                        "抓取",
                        "抓取",
                        SemanticActionLifecycle.Instant,
                        "即时执行",
                        SemanticActionPhase.Complete,
                        Array.Empty<StructuredRuleDefinition>(),
                        new[]
                        {
                            new ConfiguredMutationDefinition(
                                "变更.建立持有",
                                "设置关系",
                                Parameters(
                                    ("关系类型",
                                        StructuredValue.FromText("交互.关系.持有")))),
                            new ConfiguredMutationDefinition(
                                "变更.发布持有事件",
                                "发布事件",
                                Parameters(
                                    ("事件类型",
                                            StructuredValue.FromText(
                                            "课程.已持有"))))
                        }),
                    ConfiguredActionDefinition.CreateGeneric(
                        "放下",
                        "放下",
                        SemanticActionLifecycle.Instant,
                        "即时执行",
                        SemanticActionPhase.Complete,
                        Array.Empty<StructuredRuleDefinition>(),
                        new[]
                        {
                            new ConfiguredMutationDefinition(
                                "变更.解除持有",
                                "移除关系",
                                Parameters(
                                    ("关系类型",
                                        StructuredValue.FromText("交互.关系.持有"))))
                        })
                    }));
        }

        private static CoursePresentationStateDefinition HeldState()
        {
            return new CoursePresentationStateDefinition(
                "状态.试管已持有",
                "器材.试管",
                "器材.试管",
                "学生",
                new[]
                {
                    new StructuredRuleDefinition(
                        "条件.已持有",
                        0,
                        InteractionStructuredFactFields.来源对象持有者,
                        StructuredRuleOperator.等于,
                        StructuredValue.FromText("学生"),
                        "尚未持有")
                });
        }

        private static PresentationReactionEngine Reactions()
        {
            return new PresentationReactionEngine(
                new[]
                {
                    Rule(
                        "动作已接受",
                        PresentationTriggerKind.ActionAccepted,
                        "抓取",
                        "效果.动作"),
                    Rule(
                        "领域事件",
                        PresentationTriggerKind.DomainEvent,
                        "课程.已持有",
                        "效果.事件"),
                    Rule(
                        "权威状态进入",
                        PresentationTriggerKind.StateEntered,
                        "状态.试管已持有",
                        "效果.进入"),
                    Rule(
                        "权威状态持续",
                        PresentationTriggerKind.StateActive,
                        "状态.试管已持有",
                        "效果.持续"),
                    Rule(
                        "权威状态退出",
                        PresentationTriggerKind.StateExited,
                        "状态.试管已持有",
                        "效果.退出")
                },
                new[]
                {
                    Effect("效果.动作", "test.action",
                        PresentationEffectLifecycle.OneShot),
                    Effect("效果.事件", "test.event",
                        PresentationEffectLifecycle.OneShot),
                    Effect("效果.进入", "test.entered",
                        PresentationEffectLifecycle.OneShot),
                    Effect("效果.持续", "test.active",
                        PresentationEffectLifecycle.UntilReplaced),
                    Effect("效果.退出", "test.exited",
                        PresentationEffectLifecycle.OneShot)
                });
        }

        private static PresentationReactionEngine ExternalStateReactions()
        {
            return new PresentationReactionEngine(
                new[]
                {
                    Rule(
                        "权威状态进入",
                        PresentationTriggerKind.StateEntered,
                        "状态.外部过程已推进",
                        "效果.进入"),
                    Rule(
                        "权威状态持续",
                        PresentationTriggerKind.StateActive,
                        "状态.外部过程已推进",
                        "效果.持续")
                },
                new[]
                {
                    Effect("效果.进入", "test.entered",
                        PresentationEffectLifecycle.OneShot),
                    Effect("效果.持续", "test.active",
                        PresentationEffectLifecycle.UntilReplaced)
                });
        }

        private static PresentationReactionEngine AvailabilityReactions()
        {
            return new PresentationReactionEngine(
                new[]
                {
                    Rule(
                        "可操作性变化",
                        PresentationTriggerKind.ActionAvailabilityChanged,
                        "抓取",
                        "效果.可操作性")
                },
                new[]
                {
                    new PresentationEffectDefinition(
                        "效果.可操作性",
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

        private static PresentationRuleDefinition Rule(
            string ruleId,
            PresentationTriggerKind kind,
            string value,
            string effectId)
        {
            return new PresentationRuleDefinition(
                ruleId,
                kind,
                value,
                new[] { effectId });
        }

        private static PresentationEffectDefinition Effect(
            string definitionId,
            string protocolId,
            PresentationEffectLifecycle lifecycle)
        {
            return new PresentationEffectDefinition(
                definitionId,
                protocolId,
                new PresentationTargetSelector(
                    PresentationEntitySelectorKind.Global,
                    null,
                    PresentationLocationKind.GlobalReceiver,
                    null),
                "test.channel",
                0,
                lifecycle,
                Array.Empty<PresentationParameterBinding>());
        }

        private static SemanticActionRequest Request(string commandId)
        {
            return new SemanticActionRequest(
                commandId,
                "抓取",
                "操作." + commandId,
                SemanticActionPhase.Complete,
                0d,
                "学生",
                "器材.试管",
                "学生",
                Array.Empty<KeyValuePair<string, StructuredValue>>());
        }

        private static ExperimentWorld World()
        {
            var world = new ExperimentWorld(InteractionRelationSchemas.All);
            world.AddEntity(new ExperimentEntity(
                new EntityId("器材.试管")));
            world.AddEntity(new ExperimentEntity(
                new EntityId("学生")));
            TeachingCourseRegistrations.CreateModuleScope().PrepareWorld(world);
            return world;
        }

        private static IReadOnlyDictionary<string, StructuredValue> Parameters(
            params (string Key, StructuredValue Value)[] values)
        {
            return values.ToDictionary(
                value => value.Key,
                value => value.Value,
                StringComparer.Ordinal);
        }

        private sealed class 来源对象持有者FactReader : IStructuredFactReader
        {
            public StructuredFactField Field =>
                InteractionStructuredFactFields.来源对象持有者;

            public StructuredValue Read(StructuredRuleContext context)
            {
                var holder = context.World.Relations.SingleOrDefault(value =>
                    value.TypeId == InteractionRelationTypeIds.HeldBy &&
                    value.Source ==
                    new EntityId(context.Request.SourceEntityId));
                return holder == null
                    ? StructuredValue.Null()
                    : StructuredValue.FromText(holder.Target.Value);
            }
        }

        private sealed class RecordingDispatcher :
            IPresentationCommandDispatcher
        {
            private readonly List<string> _calls;
            private readonly bool _failAll;

            public RecordingDispatcher(
                List<string> calls,
                bool failAll = false)
            {
                _calls = calls;
                _failAll = failAll;
            }

            public IReadOnlyList<PresentationEffectCommand> Synchronized
            {
                get;
                private set;
            } = Array.Empty<PresentationEffectCommand>();

            public List<PresentationEffectCommand> Dispatched { get; } =
                new List<PresentationEffectCommand>();

            public PresentationDispatchResult Dispatch(
                PresentationEffectCommand command)
            {
                Dispatched.Add(command);
                _calls.Add("Dispatch:" + command.CommandId.Split(':')[0]);
                return _failAll
                    ? PresentationDispatchResult.Failure(
                        command,
                        "测试.失败",
                        "模拟表现失败")
                    : PresentationDispatchResult.Success(command);
            }

            public IReadOnlyList<PresentationDispatchResult>
                SynchronizeAuthoritativeState(
                    IEnumerable<PresentationEffectCommand> commands)
            {
                _calls.Add("Synchronize:权威状态");
                Synchronized = commands.ToArray();
                return Synchronized
                    .Select(command => _failAll
                        ? PresentationDispatchResult.Failure(
                            command,
                            "测试.失败",
                            "模拟表现失败")
                        : PresentationDispatchResult.Success(command))
                    .ToArray();
            }
        }
    }
}
