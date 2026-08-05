using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Engine.Tests.Courses
{
    /// <summary>
    /// 为配方和编译器行为测试直接构造内存蓝图，不再借用已删除的旧表读取协议。
    /// </summary>
    internal static class CourseBlueprintTestFactory
    {
        public static CourseBlueprint Blueprint(
            params CourseObjectBlueprint[] objects) =>
            new CourseBlueprint(
                new CourseBlueprintCourse(
                    "测试课程",
                    "测试课程",
                    Array.Empty<string>(),
                    "学生",
                    "环境.prefab",
                    Source("课程.csv", 2, "测试课程")),
                objects,
                Array.Empty<CourseInitialRelationBlueprint>(),
                Array.Empty<CourseInteractionRuleBlueprint>(),
                Array.Empty<CourseDisciplineProcessBlueprint>(),
                Array.Empty<CourseTeachingEvaluationBlueprint>(),
                Array.Empty<CoursePresentationOverrideBlueprint>(),
                Array.Empty<CourseAcceptanceRecordBlueprint>(),
                Array.Empty<CourseAdvancedOverrideBlueprint>());

        public static CourseObjectBlueprint Object(
            string entityId,
            IEnumerable<string> features,
            params KeyValuePair<string, string>[] extensions) =>
            new CourseObjectBlueprint(
                entityId,
                entityId,
                features ?? Array.Empty<string>(),
                new BlueprintVector3(0, 0, 0),
                new BlueprintVector3(0, 0, 0),
                Values(Source("实验对象.csv", 2, entityId), extensions),
                Source("实验对象.csv", 2, entityId));

        public static IReadOnlyDictionary<string, BlueprintValue> Values(
            ConfigurationSource source,
            params KeyValuePair<string, string>[] values) =>
            new ReadOnlyDictionary<string, BlueprintValue>(
                (values ?? Array.Empty<KeyValuePair<string, string>>())
                .ToDictionary(
                    value => value.Key,
                    value => new BlueprintValue(
                        value.Key,
                        value.Value,
                        source),
                    StringComparer.Ordinal));

        public static ConfigurationSource Source(
            string fileName,
            int line,
            string id) =>
            new ConfigurationSource(
                ConfigurationLayer.Course,
                "测试课程",
                fileName,
                line,
                1,
                id);
    }
}
