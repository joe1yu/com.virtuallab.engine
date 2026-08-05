using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Interaction.Courses;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseSessionStateTests
    {
        [Test]
        public void 权威状态导出恢复后结构完全相等且不含版本或哈希()
        {
            var runtime = RuntimeDefinition();
            var world = World();
            var session = runtime.CreateSession(world);
            session.Execute(Request("命令.记录状态"));
            var rejectedRequest = new SemanticActionRequest(
                "命令.未配置",
                "测试.未配置",
                "学生",
                "器材.试管",
                null,
                Parameters());
            session.Execute(rejectedRequest);
            session.RecordEvaluation(
                new CourseGoalEvaluationResult(new[] { "目标.已连接" }),
                new CourseAssessmentEvaluationResult(
                    90,
                    new[] { "风险.倒吸" },
                    new[]
                    {
                        new CourseAssessmentEvidence(
                            "评价.倒吸",
                            "风险.倒吸",
                            -10,
                            "先移导管。")
                    }),
                new[] { "观察.产生气泡" });

            var state = session.ExportState();
            var restored = ConfigDrivenCourseSession.Restore(runtime, state);
            var replay = restored.Execute(Request("命令.记录状态"));
            var rejectedReplay = restored.Execute(rejectedRequest);
            var conflict = restored.Execute(new SemanticActionRequest(
                "命令.未配置",
                "测试.记录状态",
                "学生",
                "端口.来源",
                "端口.目标",
                Parameters()));

            Assert.That(restored.ExportState(), Is.EqualTo(state));
            Assert.That(
                restored.ExportState().Relations.Single().SourcePortId,
                Is.EqualTo("默认端口"));
            Assert.That(
                restored.ExportState().Entities
                    .Single(value => value.EntityId == "端口.来源")
                    .Capabilities.Single().TextProperties.Single().Key,
                Is.EqualTo("默认端口"));
            Assert.That(replay.IsAccepted, Is.True);
            Assert.That(replay.Events, Has.Count.EqualTo(1));
            Assert.That(rejectedReplay.RejectionCode,
                Is.EqualTo("动作未配置"));
            Assert.That(conflict.RejectionCode, Is.EqualTo("命令标识冲突"));
            Assert.That(typeof(CourseSessionState).GetProperty("CourseVersion"), Is.Null);
            Assert.That(typeof(CourseSessionState).GetProperty("SchemaVersion"), Is.Null);
            Assert.That(typeof(CourseSessionState).GetProperty("ContentHash"), Is.Null);
        }

        [Test]
        public void 恢复拒绝未知实体引用和非当前结构()
        {
            var runtime = RuntimeDefinition();
            var state = runtime.CreateSession(World()).ExportState();

            var unknownRelation = state.WithRelations(
                new[]
                {
                    new CourseRelationState(
                        InteractionRelationTypeIds.Connection,
                        "端口.不存在",
                        "端口.目标")
                });
            var referenceError = Assert.Throws<CourseStateRestoreException>(
                () => ConfigDrivenCourseSession.Restore(runtime, unknownRelation));
            var formatError = Assert.Throws<CourseStateRestoreException>(
                () => ConfigDrivenCourseSession.Restore(runtime, "{}"));
            Assert.That(referenceError.Code, Is.EqualTo("session.reference.invalid"));
            Assert.That(formatError.Code, Is.EqualTo("session.format.unsupported"));
        }

        [Test]
        public void 恢复保持能力decimal精度并统一报告非法物质引用()
        {
            const decimal capacity = 0.1234567890123456789012345678m;
            var world = new ExperimentWorld();
            Add(world, "器材.精密容器", new ContainerCapability(capacity));
            var runtime = RuntimeDefinition();
            var state = runtime.CreateSession(world).ExportState();
            var restored = ConfigDrivenCourseSession.Restore(runtime, state);
            var capability = restored.ExportState()
                .Entities.Single()
                .Capabilities.Single();

            Assert.That(capability.NumberValue, Is.EqualTo(capacity));

            var invalidMatter = state.WithMatter(
                new[]
                {
                    new CourseMatterState(
                        "器材.不存在",
                        "水",
                        1m,
                        Unit.Millilitre,
                        MatterPhase.Liquid,
                        20m)
                });
            var error = Assert.Throws<CourseStateRestoreException>(
                () => ConfigDrivenCourseSession.Restore(runtime, invalidMatter));
            Assert.That(error.Code, Is.EqualTo("session.state.invalid"));
        }

        private static CourseRuntimeDefinition RuntimeDefinition()
        {
            var action = ConfiguredActionDefinition.CreateGeneric(
                "测试.记录状态",
                Array.Empty<StructuredRuleDefinition>(),
                new[]
                {
                    new ConfiguredMutationDefinition(
                        "变更.建立连接",
                        ConfiguredStateOperationIds.RelationSet,
                        Parameters(
                            ("关系类型", StructuredValue.FromText("交互.关系.连接")))),
                    new ConfiguredMutationDefinition(
                        "变更.温度",
                        ConfiguredStateOperationIds.ScalarSet,
                        Parameters(
                            ("状态键", StructuredValue.FromText("器材.试管.温度")),
                            ("数值", StructuredValue.FromNumber(80d)),
                            ("单位", StructuredValue.FromText("摄氏度")))),
                    new ConfiguredMutationDefinition(
                        "变更.过程",
                        ConfiguredStateOperationIds.ProcessStart,
                        Parameters(
                            ("过程标识", StructuredValue.FromText("过程.观察")),
                            ("实体ID", StructuredValue.FromText("器材.试管")))),
                    new ConfiguredMutationDefinition(
                        "变更.事件",
                        ConfiguredStateOperationIds.EventEmit,
                        Parameters(
                            ("事件类型", StructuredValue.FromText("课程.状态已记录"))))
                });
            return new CourseRuntimeDefinition(
                InteractionCourseRegistrations.CreateModuleScope(),
                new[] { action },
                Array.Empty<CourseActionAssessmentDefinition>(),
                0);
        }

        private static SemanticActionRequest Request(string commandId)
        {
            return new SemanticActionRequest(
                commandId,
                "测试.记录状态",
                "学生",
                "端口.来源",
                "端口.目标",
                Parameters());
        }

        private static ExperimentWorld World()
        {
            var world = new ExperimentWorld();
            Add(world, "学生");
            Add(world, "端口.来源", new ConnectorCapability("导气"));
            Add(world, "端口.目标", new ConnectorCapability("导气"));
            Add(world, "器材.试管", new ContainerCapability(50m));
            world.Matter.Add(
                new EntityId("器材.试管"),
                new SubstanceBatch(
                    "水",
                    new Quantity(10m, Unit.Millilitre),
                    MatterPhase.Liquid,
                    new Temperature(25m)));
            return world;
        }

        private static void Add(
            ExperimentWorld world,
            string id,
            params ICapability[] capabilities)
        {
            var entity = new ExperimentEntity(new EntityId(id));
            foreach (var capability in capabilities)
            {
                entity.AddCapability(capability);
            }

            world.AddEntity(entity);
        }

        private static IReadOnlyDictionary<string, StructuredValue> Parameters(
            params (string Key, StructuredValue Value)[] values)
        {
            var result = new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                result.Add(value.Key, value.Value);
            }

            return result;
        }
    }
}
