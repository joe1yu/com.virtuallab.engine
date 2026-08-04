using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseContractTests
    {
        [Test]
        public void 语义动作请求不暴露输入设备细节并复制参数()
        {
            var parameters = new Dictionary<string, StructuredValue>
            {
                ["抓取点"] = StructuredValue.FromText("管口")
            };

            var request = new SemanticActionRequest(
                "命令.0001",
                "抓取",
                "学生",
                "器材.试管",
                null,
                parameters);
            parameters["抓取点"] = StructuredValue.FromText("管底");

            Assert.That(request.Parameters["抓取点"].Text, Is.EqualTo("管口"));
            Assert.That(
                typeof(SemanticActionRequest).GetProperties()
                    .Select(value => value.Name),
                Has.None.Contains("Mouse")
                    .And.None.Contains("Screen")
                    .And.None.Contains("Controller")
                    .And.None.Contains("GameObject")
                    .And.None.Contains("Transform"));
        }

        [Test]
        public void 课程定义复制集合并拒绝重复ID()
        {
            var entities = new List<CourseEntityDefinition>
            {
                new CourseEntityDefinition(
                    "器材.试管",
                    new[] { "可抓取" })
            };
            var course = CompiledCourseDefinition.CreateBasic(
                "课程.氧气制取",
                entities,
                Array.Empty<ActionPolicyDefinition>(),
                Array.Empty<CourseGoalDefinition>(),
                Array.Empty<CourseAssessmentDefinition>());

            entities.Clear();

            Assert.That(course.Entities, Has.Count.EqualTo(1));
            Assert.That(
                typeof(CourseEntityDefinition).GetProperty(
                    "PrefabReference"),
                Is.Null,
                "操作对象不应再持有独立模型预制体引用。");
            Assert.Throws<ArgumentException>(() =>
                CompiledCourseDefinition.CreateBasic(
                    "课程.重复实体",
                    new[]
                    {
                        new CourseEntityDefinition(
                            "器材.试管",
                            Array.Empty<string>()),
                        new CourseEntityDefinition(
                            "器材.试管",
                            Array.Empty<string>())
                    },
                    Array.Empty<ActionPolicyDefinition>(),
                    Array.Empty<CourseGoalDefinition>(),
                    Array.Empty<CourseAssessmentDefinition>()));
        }

    }
}
