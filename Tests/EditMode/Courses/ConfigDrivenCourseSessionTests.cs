using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class ConfigDrivenCourseSessionTests
    {
        [Test]
        public void 可操作性查询与实际执行共享裁决且查询不改变世界()
        {
            var world = WorldWith("器材.试管", "学生");
            var operation = new CountingOperation();
            var registry = new ConfiguredStateOperationRegistry();
            registry.Register(operation);
            var session = SessionWithPolicies(
                world,
                new StructuredRuleEvaluator(
                    Array.Empty<IStructuredFactReader>()),
                registry,
                AllowPolicy(
                    "策略.抓取试管",
                    "器材.试管",
                    Array.Empty<StructuredRuleDefinition>(),
                    new ConfiguredMutationDefinition(
                        "变化.记录抓取",
                        CountingOperation.Id,
                        EmptyParameters())));
            var request = Request("命令.查询后抓取");

            var availability = session.QueryAvailability(request);
            var afterQuery = session.ExportState();

            Assert.That(
                availability.Kind,
                Is.EqualTo(ActionAvailabilityKind.Allowed));
            Assert.That(availability.IsAllowed, Is.True);
            Assert.That(availability.ActionId, Is.EqualTo("抓取"));
            Assert.That(availability.ActorEntityId, Is.EqualTo("学生"));
            Assert.That(
                availability.SourceEntityId,
                Is.EqualTo("器材.试管"));
            Assert.That(availability.TargetEntityId, Is.EqualTo("学生"));
            Assert.That(availability.RejectionCode, Is.Null);
            Assert.That(availability.RejectionCodes, Is.Empty);
            Assert.That(afterQuery.Commands, Is.Empty);
            Assert.That(afterQuery.Events, Is.Empty);
            Assert.That(operation.ApplyCount, Is.Zero);

            var outcome = session.Execute(request);

            Assert.That(outcome.IsAccepted, Is.True);
            Assert.That(operation.ApplyCount, Is.EqualTo(1));
        }

        [Test]
        public void 可操作性区分不支持暂时阻止和课程禁用()
        {
            var world = WorldWith(
                "器材.试管",
                "器材.铁架台",
                "器材.墙壁",
                "学生");
            var rule = new StructuredRuleDefinition(
                "规则.允许抓取",
                10,
                CoreStructuredFactFields.来源对象存在,
                StructuredRuleOperator.等于,
                StructuredValue.FromBoolean(true),
                "器材正在受热");
            var session = SessionWithPolicies(
                world,
                new StructuredRuleEvaluator(new IStructuredFactReader[]
                {
                    new FixedFactReader(
                        CoreStructuredFactFields.来源对象存在,
                        StructuredValue.FromBoolean(false))
                }),
                new ConfiguredStateOperationRegistry(),
                AllowPolicy(
                    "策略.抓取试管",
                    "器材.试管",
                    new[] { rule }),
                AllowPolicy(
                    "策略.抓取铁架台",
                    "器材.铁架台",
                    Array.Empty<StructuredRuleDefinition>()),
                DenyPolicy(
                    "策略.禁止抓取铁架台",
                    "器材.铁架台",
                    1000,
                    "课程.铁架台固定",
                    "文案.铁架台固定"));

            var unsupported = session.QueryAvailability(
                Request("查询.墙壁", "器材.墙壁"));
            var blocked = session.QueryAvailability(
                Request("查询.试管", "器材.试管"));
            var disabled = session.QueryAvailability(
                Request("查询.铁架台", "器材.铁架台"));

            Assert.That(
                unsupported.Kind,
                Is.EqualTo(ActionAvailabilityKind.Unsupported));
            Assert.That(
                blocked.Kind,
                Is.EqualTo(ActionAvailabilityKind.TemporarilyBlocked));
            Assert.That(
                blocked.RejectionCodes,
                Is.EqualTo(new[] { "器材正在受热" }));
            Assert.That(
                disabled.Kind,
                Is.EqualTo(ActionAvailabilityKind.Disabled));
            Assert.That(
                disabled.RejectionCode,
                Is.EqualTo("课程.铁架台固定"));
            Assert.That(
                disabled.MessageId,
                Is.EqualTo("文案.铁架台固定"));
        }

        [Test]
        public void 多个允许候选任一通过即可执行且拒绝原因稳定去重()
        {
            var world = WorldWith("器材.试管", "学生");
            var evaluator = new StructuredRuleEvaluator(
                new IStructuredFactReader[]
                {
                    new FixedFactReader(
                        CoreStructuredFactFields.来源对象存在,
                        StructuredValue.FromBoolean(false))
                });
            var firstRule = RejectedRule(20, "条件.乙");
            var duplicateRule = RejectedRule(10, "条件.乙");
            var thirdRule = RejectedRule(30, "条件.甲");
            var blocked = SessionWithPolicies(
                world,
                evaluator,
                new ConfiguredStateOperationRegistry(),
                AllowPolicy(
                    "策略.失败甲",
                    "器材.试管",
                    new[] { firstRule, duplicateRule }),
                AllowPolicy(
                    "策略.失败乙",
                    "器材.试管",
                    new[] { thirdRule }));

            var rejected = blocked.QueryAvailability(
                Request("查询.全部失败"));

            Assert.That(
                rejected.Kind,
                Is.EqualTo(ActionAvailabilityKind.TemporarilyBlocked));
            Assert.That(
                rejected.RejectionCodes,
                Is.EqualTo(new[] { "条件.甲", "条件.乙" }));

            var allowed = SessionWithPolicies(
                world,
                evaluator,
                new ConfiguredStateOperationRegistry(),
                AllowPolicy(
                    "策略.失败",
                    "器材.试管",
                    new[] { thirdRule }),
                AllowPolicy(
                    "策略.成功",
                    "器材.试管",
                    Array.Empty<StructuredRuleDefinition>()));

            Assert.That(
                allowed.QueryAvailability(Request("查询.任一成功")).Kind,
                Is.EqualTo(ActionAvailabilityKind.Allowed));
        }

        [Test]
        public void 禁用策略序列化往返保留裁决字段()
        {
            var original = DenyPolicy(
                "策略.禁止抓取铁架台",
                "器材.铁架台",
                1000,
                "课程.铁架台固定",
                "文案.铁架台固定");

            var json = JsonConvert.SerializeObject(original);
            var restored =
                JsonConvert.DeserializeObject<ConfiguredActionDefinition>(
                    json);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Priority, Is.EqualTo(1000));
            Assert.That(
                restored.Effect,
                Is.EqualTo(ConfiguredActionPolicyEffect.Deny));
            Assert.That(
                restored.RejectionCode,
                Is.EqualTo("课程.铁架台固定"));
            Assert.That(
                restored.MessageId,
                Is.EqualTo("文案.铁架台固定"));
        }

        [Test]
        public void 任一状态操作失败时世界和事件都不改变()
        {
            var world = WorldWith("器材.试管", "学生");
            var registry = new ConfiguredStateOperationRegistry();
            registry.Register(new ThrowingOperation());
            registry.Register(new MatterAddingOperation());
            var session = Session(
                world,
                registry,
                new ConfiguredMutationDefinition(
                    "变更.建立持有关系",
                    "设置关系",
                    Parameters(
                        ("关系类型", StructuredValue.FromText("由对象持有")))),
                new ConfiguredMutationDefinition(
                    "变更.记录温度",
                    "设置标量",
                    Parameters(
                        ("状态键", StructuredValue.FromText("试管.温度")),
                        ("单位", StructuredValue.FromText("摄氏度")),
                        ("数值", StructuredValue.FromNumber(25d)))),
                new ConfiguredMutationDefinition(
                    "变更.缓冲事件",
                    "发布事件",
                    Parameters(
                        ("事件类型", StructuredValue.FromText(
                            "课程.测试事件")))),
                new ConfiguredMutationDefinition(
                    "变更.模拟加入物质",
                    MatterAddingOperation.Id,
                    EmptyParameters()),
                new ConfiguredMutationDefinition(
                    "变更.模拟失败",
                    ThrowingOperation.Id,
                    EmptyParameters()));

            var result = session.Execute(Request("命令.事务失败"));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.RejectionCode, Is.EqualTo("状态操作失败"));
            Assert.That(
                result.RejectionCodes,
                Is.EqualTo(new[]
                {
                    "状态操作失败",
                    "状态操作失败.test.throw"
                }));
            Assert.That(result.Events, Is.Empty);
            Assert.That(world.Relations, Is.Empty);
            Assert.That(world.Matter.Entries, Is.Empty);
            Assert.That(world.Matter.KnownUnits, Is.Empty);
            Assert.That(
                world.TryGetScalar("试管.温度", out _),
                Is.False);
        }

        [Test]
        public void 相同命令标识只执行一次并返回同一结果()
        {
            var world = WorldWith("器材.试管", "学生");
            var operation = new CountingOperation();
            var registry = new ConfiguredStateOperationRegistry();
            registry.Register(operation);
            var session = Session(
                world,
                registry,
                new ConfiguredMutationDefinition(
                    "变更.计数",
                    CountingOperation.Id,
                    EmptyParameters()));
            var request = Request("命令.幂等");

            var first = session.Execute(request);
            var second = session.Execute(request);

            Assert.That(first.IsAccepted, Is.True);
            Assert.That(second, Is.SameAs(first));
            Assert.That(operation.ApplyCount, Is.EqualTo(1));

            var conflicting = new SemanticActionRequest(
                request.CommandId,
                request.ActionId,
                request.ActorEntityId,
                request.SourceEntityId,
                null,
                EmptyParameters());
            var conflictResult = session.Execute(conflicting);

            Assert.That(conflictResult.IsAccepted, Is.False);
            Assert.That(conflictResult.RejectionCode, Is.EqualTo("命令标识冲突"));
            Assert.That(operation.ApplyCount, Is.EqualTo(1));
        }

        [Test]
        public void 领域事件只在全部状态操作成功后发布()
        {
            var world = WorldWith("器材.试管", "学生");
            var registry = new ConfiguredStateOperationRegistry();
            var session = Session(
                world,
                registry,
                new ConfiguredMutationDefinition(
                    "变更.发布事件",
                    "发布事件",
                    Parameters(
                        ("事件类型", StructuredValue.FromText(
                            "课程.操作已接受")))));

            var result = session.Execute(Request("命令.事件成功"));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Events, Has.Count.EqualTo(1));
            Assert.That(
                result.Events[0].EventType,
                Is.EqualTo("课程.操作已接受"));
            var domainEvent =
                result.Events[0].Event as ConfiguredCourseDomainEvent;
            Assert.That(domainEvent, Is.Not.Null);
            Assert.That(
                domainEvent.Payload["来源实体标识"].Text,
                Is.EqualTo("器材.试管"));
            Assert.That(
                domainEvent.Payload["目标实体标识"].Text,
                Is.EqualTo("学生"));
        }

        [Test]
        public void 共享操作注册表不会让两个世界串状态()
        {
            var registry = new ConfiguredStateOperationRegistry();
            var firstWorld = WorldWith("器材.试管", "学生");
            var secondWorld = WorldWith("器材.试管", "学生");
            var mutation = new ConfiguredMutationDefinition(
                "变更.温度",
                "设置标量",
                Parameters(
                    ("状态键", StructuredValue.FromText("试管.温度")),
                    ("单位", StructuredValue.FromText("摄氏度")),
                    ("数值", StructuredValue.FromNumber(25d))));
            var first = Session(firstWorld, registry, mutation);
            Session(secondWorld, registry, mutation);

            first.Execute(Request("命令.第一世界"));

            Assert.That(
                firstWorld.TryGetScalar("试管.温度", out var value),
                Is.True);
            Assert.That(value.Value, Is.EqualTo(25d));
            Assert.That(
                secondWorld.TryGetScalar("试管.温度", out _),
                Is.False);
        }

        [Test]
        public void 标量拒绝单位漂移和非有限运算结果()
        {
            var registry = new ConfiguredStateOperationRegistry();
            var world = WorldWith("器材.试管", "学生");
            var request = Request("命令.标量");
            registry.ApplyAtomically(
                request,
                world,
                new[]
                {
                    new ConfiguredMutationDefinition(
                        "变更.初始值",
                        "设置标量",
                        Parameters(
                            ("状态键", StructuredValue.FromText("状态.值")),
                            ("单位", StructuredValue.FromText("克")),
                            ("数值", StructuredValue.FromNumber(
                                double.MaxValue))))
                });

            Assert.Throws<ConfiguredStateOperationException>(() =>
                registry.ApplyAtomically(
                    request,
                    world,
                    new[]
                    {
                        new ConfiguredMutationDefinition(
                            "变更.单位漂移",
                            "设置标量",
                            Parameters(
                                ("状态键", StructuredValue.FromText("状态.值")),
                                ("单位", StructuredValue.FromText("毫升")),
                                ("数值", StructuredValue.FromNumber(1d))))
                    }));
            Assert.Throws<ConfiguredStateOperationException>(() =>
                registry.ApplyAtomically(
                    request,
                    world,
                    new[]
                    {
                        new ConfiguredMutationDefinition(
                            "变更.溢出",
                            "增加标量",
                            Parameters(
                                ("状态键", StructuredValue.FromText("状态.值")),
                                ("单位", StructuredValue.FromText("克")),
                                ("增量", StructuredValue.FromNumber(
                                    double.MaxValue))))
                    }));
        }

        [Test]
        public void 关系操作允许重复设置同一关系并拒绝冲突持有者()
        {
            var registry = new ConfiguredStateOperationRegistry();
            var world = WorldWith("器材.试管", "学生", "另一学生");
            var request = Request("命令.关系");
            Assert.Throws<ConfiguredStateOperationException>(() =>
                registry.ApplyAtomically(
                    request,
                    world,
                    new[]
                    {
                        new ConfiguredMutationDefinition(
                            "变更.非法关系",
                            "设置关系",
                            Parameters(
                                ("关系类型", StructuredValue.FromText("999"))))
                    }));

            registry.ApplyAtomically(
                request,
                world,
                new[]
                {
                    new ConfiguredMutationDefinition(
                        "变更.首次持有",
                        "设置关系",
                        Parameters(
                            ("关系类型", StructuredValue.FromText("由对象持有"))))
                });
            Assert.DoesNotThrow(() => registry.ApplyAtomically(
                request,
                world,
                new[]
                {
                    new ConfiguredMutationDefinition(
                        "变更.重复设置首次持有",
                        "设置关系",
                        Parameters(
                            ("关系类型", StructuredValue.FromText("由对象持有"))))
                }));
            Assert.Throws<ConfiguredStateOperationException>(() =>
                registry.ApplyAtomically(
                    request,
                    world,
                    new[]
                    {
                        new ConfiguredMutationDefinition(
                            "变更.第二持有者",
                            "设置关系",
                            Parameters(
                                ("关系类型", StructuredValue.FromText("由对象持有")),
                                ("目标实体标识", StructuredValue.FromText(
                                    "另一学生"))))
                    }));
        }

        [Test]
        public void 权威表现状态评估只读世界并稳定排序()
        {
            var world = WorldWith("器材.试管", "学生");
            var session = Session(
                world,
                new ConfiguredStateOperationRegistry());
            var active = session.EvaluatePresentationStates(new[]
            {
                new CoursePresentationStateDefinition(
                    "状态.乙",
                    "器材.试管",
                    "器材.试管",
                    "学生",
                    Array.Empty<StructuredRuleDefinition>()),
                new CoursePresentationStateDefinition(
                    "状态.甲",
                    "器材.试管",
                    "器材.试管",
                    "学生",
                    Array.Empty<StructuredRuleDefinition>())
            });

            Assert.That(active, Is.EqualTo(new[] { "状态.乙", "状态.甲" }));
            var after = session.ExportState();
            Assert.That(after.Commands, Is.Empty);
            Assert.That(after.Events, Is.Empty);
            Assert.That(world.Relations, Is.Empty);
        }

        private static ConfigDrivenCourseSession Session(
            ExperimentWorld world,
            ConfiguredStateOperationRegistry registry,
            params ConfiguredMutationDefinition[] mutations)
        {
            return new ConfigDrivenCourseSession(
                world,
                new StructuredRuleEvaluator(
                    Array.Empty<IStructuredFactReader>()),
                registry,
                new[]
                {
                    ConfiguredActionDefinition.CreateGeneric(
                        "抓取",
                        Array.Empty<StructuredRuleDefinition>(),
                        mutations)
                });
        }

        private static ConfigDrivenCourseSession SessionWithPolicies(
            ExperimentWorld world,
            StructuredRuleEvaluator evaluator,
            ConfiguredStateOperationRegistry registry,
            params ConfiguredActionDefinition[] policies)
        {
            return new ConfigDrivenCourseSession(
                world,
                evaluator,
                registry,
                policies);
        }

        private static ConfiguredActionDefinition AllowPolicy(
            string policyId,
            string sourceEntityId,
            IEnumerable<StructuredRuleDefinition> rules,
            params ConfiguredMutationDefinition[] mutations)
        {
            return ConfiguredActionDefinition.CreatePolicy(
                policyId,
                "抓取",
                sourceEntityId,
                "学生",
                100,
                ConfiguredActionPolicyEffect.Allow,
                null,
                rules,
                mutations);
        }

        private static ConfiguredActionDefinition DenyPolicy(
            string policyId,
            string sourceEntityId,
            int priority,
            string rejectionCode,
            string messageId)
        {
            return ConfiguredActionDefinition.CreatePolicy(
                policyId,
                "抓取",
                sourceEntityId,
                "学生",
                priority,
                ConfiguredActionPolicyEffect.Deny,
                messageId,
                Array.Empty<StructuredRuleDefinition>(),
                Array.Empty<ConfiguredMutationDefinition>(),
                rejectionCode);
        }

        private static StructuredRuleDefinition RejectedRule(
            int order,
            string rejectionCode)
        {
            return new StructuredRuleDefinition(
                "规则." + order + "." + rejectionCode,
                order,
                CoreStructuredFactFields.来源对象存在,
                StructuredRuleOperator.等于,
                StructuredValue.FromBoolean(true),
                rejectionCode);
        }

        private static SemanticActionRequest Request(
            string commandId,
            string sourceEntityId = "器材.试管")
        {
            return new SemanticActionRequest(
                commandId,
                "抓取",
                "学生",
                sourceEntityId,
                "学生",
                EmptyParameters());
        }

        private static ExperimentWorld WorldWith(params string[] entityIds)
        {
            var world = new ExperimentWorld();
            foreach (var entityId in entityIds)
            {
                world.AddEntity(new ExperimentEntity(new EntityId(entityId)));
            }

            return world;
        }

        private static IReadOnlyDictionary<string, StructuredValue> Parameters(
            params (string Key, StructuredValue Value)[] values)
        {
            var result = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var value in values)
            {
                result.Add(value.Key, value.Value);
            }

            return result;
        }

        private static IReadOnlyDictionary<string, StructuredValue>
            EmptyParameters()
        {
            return new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
        }

        private sealed class ThrowingOperation : IConfiguredStateOperation
        {
            public const string Id = "test.throw";

            public string OperationId => Id;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                throw new InvalidOperationException("模拟状态操作失败。");
            }
        }

        private sealed class CountingOperation : IConfiguredStateOperation
        {
            public const string Id = "test.count";

            public int ApplyCount { get; private set; }

            public string OperationId => Id;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                ApplyCount++;
            }
        }

        private sealed class MatterAddingOperation : IConfiguredStateOperation
        {
            public const string Id = "test.matter-add";

            public string OperationId => Id;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                world.Matter.RegisterUnit("测试液体", Unit.Millilitre);
                world.Matter.Add(
                    new EntityId(request.SourceEntityId),
                    new SubstanceBatch(
                        "测试液体",
                        new Quantity(10m, Unit.Millilitre),
                        MatterPhase.Liquid,
                        new Temperature(20m)));
            }
        }

        private sealed class FixedFactReader : IStructuredFactReader
        {
            private readonly StructuredValue _value;

            public FixedFactReader(
                StructuredFactField field,
                StructuredValue value)
            {
                Field = field;
                _value = value;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return _value;
            }
        }
    }
}
