using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Courses;
using VirtualLab.Infrastructure.Persistence;
using VirtualLab.Infrastructure.Reporting;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Infrastructure
{
    public sealed class CurrentPersistenceTests
    {
        [Test]
        public void 当前课程存档往返后保持结构化状态相等()
        {
            var original = Archive();

            var json = SessionJson.Serialize(original);
            var restored = SessionJson.Deserialize(json);

            Assert.That(restored.CourseId, Is.EqualTo(original.CourseId));
            Assert.That(restored.SessionId, Is.EqualTo(original.SessionId));
            Assert.That(restored.RandomSeed, Is.EqualTo(original.RandomSeed));
            Assert.That(restored.State, Is.EqualTo(original.State));
            Assert.That(json, Does.Not.Contain("schemaVersion"));
            Assert.That(json, Does.Not.Contain("engineVersion"));
            Assert.That(json, Does.Not.Contain("configurationHash"));
            Assert.That(json, Does.Not.Contain("stateHash"));
        }

        [Test]
        public void 当前课程存档拒绝未知字段和损坏实体引用()
        {
            var json = SessionJson.Serialize(Archive());

            Assert.That(
                SessionJson.TryDeserialize(
                    json.Replace(
                        "\"courseId\":",
                        "\"unknown\":1,\"courseId\":"),
                    out _,
                    out var unknownError),
                Is.False);
            Assert.That(unknownError, Is.EqualTo("session.data.invalid"));

            Assert.That(
                SessionJson.TryDeserialize(
                    json.Replace(
                        "\"relations\": []",
                        "\"relations\": [{\"typeId\":\"交互.关系.位于容器内\","
                        + "\"sourceEntityId\":\"学生\","
                        + "\"targetEntityId\":\"不存在\"}]"),
                    out _,
                    out var referenceError),
                Is.False);
            Assert.That(
                referenceError,
                Is.EqualTo("session.reference.invalid"));
        }

        [Test]
        public void 本地存档保留路径安全和原子读写()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "VirtualLab.CurrentPersistence."
                    + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LocalSessionStore(root);

                var saved = store.Save("课程/会话.json", Archive());
                var loaded = store.Load("课程/会话.json");

                Assert.That(saved.IsSuccess, Is.True, saved.ErrorCode);
                Assert.That(loaded.IsSuccess, Is.True, loaded.ErrorCode);
                Assert.That(loaded.Archive.State, Is.EqualTo(Archive().State));
                Assert.That(
                    store.Save("../越界.json", Archive()).ErrorCode,
                    Is.EqualTo("session.path.invalid"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void 当前报告往返后不产生版本和指纹字段()
        {
            var report = new ExperimentReport(
                new ReportMetadata(
                    "氧气的实验室制取与性质",
                    "会话.一",
                    42,
                    3),
                new ReportScore(90, 80, 100, 85),
                new[] { new ReportGoal("收集两瓶氧气", 1, true) },
                new[]
                {
                    new ReportObservation(
                        "氧气气流稳定",
                        2,
                        "气体.已生成")
                },
                Array.Empty<ReportRisk>(),
                Array.Empty<ReportDeduction>(),
                Array.Empty<ReportHint>());

            var json = new JsonReportWriter().Write(report);
            var restored = new ExperimentReportJsonReader().TryParse(json);

            Assert.That(restored.IsSuccess, Is.True, restored.ErrorCode);
            Assert.That(
                restored.Report.Metadata.CourseId,
                Is.EqualTo("氧气的实验室制取与性质"));
            Assert.That(json, Does.Not.Contain("schemaVersion"));
            Assert.That(json, Does.Not.Contain("engineVersion"));
            Assert.That(json, Does.Not.Contain("configurationHash"));
            Assert.That(json, Does.Not.Contain("stateHash"));
        }

        private static SessionArchive Archive()
        {
            var request = new SemanticActionRequest(
                "命令.拒绝",
                "抓取",
                "学生",
                "学生",
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>());
            var state = CourseSessionState.RestoreCurrent(
                new[]
                {
                    new CourseEntityState(
                        "学生",
                        Array.Empty<CourseCapabilityState>())
                },
                Array.Empty<CourseRelationState>(),
                Array.Empty<CourseMatterState>(),
                Array.Empty<KeyValuePair<string, Unit>>(),
                Array.Empty<CourseScalarState>(),
                Array.Empty<CourseProcessState>(),
                Array.Empty<CourseEventState>(),
                new[]
                {
                    new CourseExecutedCommandState(
                        request,
                        CommandResult.Rejected(
                            "course.action.not-configured"))
                },
                1,
                new CourseGoalEvaluationResult(
                    Array.Empty<string>()),
                new CourseAssessmentEvaluationResult(
                    80,
                    new[] { "风险.样品损坏" },
                    new[]
                    {
                        new CourseAssessmentEvidence(
                            "评价.样品损坏",
                            "风险.样品损坏",
                            -20,
                            "样品已经损坏，需要重新开始实验。",
                            "命令.拒绝",
                            CourseConsequenceSeverity
                                .EquipmentOrSampleDamage,
                            CourseConsequenceRecoverability.RestartRequired,
                            new[] { "目标.完成实验" })
                    }),
                new[] { "尚未开始实验" },
                new[]
                {
                    new CourseSpatialPoseState(
                        "学生",
                        1.25,
                        2.5,
                        -3.75,
                        10,
                        20,
                        30)
                });
            return new SessionArchive(
                "氧气的实验室制取与性质",
                "会话.一",
                42,
                0,
                0,
                state);
        }
    }
}
