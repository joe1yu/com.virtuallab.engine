using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Physics;

namespace VirtualLab.UnityAdapters.Input
{
    /// <summary>
    /// 鼠标、VR 和自动化测试共用的设备无关观测。观测只描述操作者、
    /// 来源、目标和输入参数，不声明科学结果。
    /// </summary>
    public sealed class SemanticInputObservation
    {
        public SemanticInputObservation(
            string actionId,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId,
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters)
        {
            ActionId = Required(actionId, "动作 ID");
            ActorEntityId = Required(actorEntityId, "操作者实体 ID");
            SourceEntityId = Required(sourceEntityId, "来源实体 ID");
            TargetEntityId = string.IsNullOrWhiteSpace(targetEntityId)
                ? null
                : targetEntityId.Trim();
            var copy = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var pair in parameters ??
                                 throw new ArgumentNullException(
                                     nameof(parameters)))
            {
                if (pair.Value == null || !copy.TryAdd(pair.Key, pair.Value))
                {
                    throw new ArgumentException(
                        "语义输入参数不能为空或重复。",
                        nameof(parameters));
                }
            }

            Parameters =
                new ReadOnlyDictionary<string, StructuredValue>(copy);
        }

        public string ActionId { get; }
        public string ActorEntityId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public IReadOnlyDictionary<string, StructuredValue> Parameters { get; }

        private static string Required(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(context + "不能为空。");
            }

            return value.Trim();
        }
    }

    public sealed class SemanticActionGestureMapper
    {
        public SemanticActionRequest Map(
            string commandId,
            string operationInstanceId,
            SemanticActionPhase phase,
            double occurredAtSeconds,
            SemanticInputObservation observation,
            SpatialFactSet spatialFacts)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            if (spatialFacts == null)
            {
                throw new ArgumentNullException(nameof(spatialFacts));
            }

            var parameters = new Dictionary<string, StructuredValue>(
                observation.Parameters,
                StringComparer.Ordinal);
            foreach (var fact in spatialFacts.Values)
            {
                parameters[fact.Key] = fact.Value;
            }

            return new SemanticActionRequest(
                commandId,
                observation.ActionId,
                operationInstanceId,
                phase,
                occurredAtSeconds,
                observation.ActorEntityId,
                observation.SourceEntityId,
                observation.TargetEntityId,
                parameters);
        }
    }
}
