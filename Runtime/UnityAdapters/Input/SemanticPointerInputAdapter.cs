using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Physics;

namespace VirtualLab.UnityAdapters.Input
{
    /// <summary>
    /// Pointer 只负责识别实体和提交语义意图，不直接移动 Transform 或改写实验状态。
    /// </summary>
    public sealed class SemanticPointerInputAdapter : MonoBehaviour
    {
        private SemanticActionGestureMapper _mapper;
        private ISpatialFactProvider _spatialFacts;

        public void Configure(
            SemanticActionGestureMapper mapper,
            ISpatialFactProvider spatialFacts)
        {
            _mapper = mapper ??
                throw new ArgumentNullException(nameof(mapper));
            _spatialFacts = spatialFacts ??
                throw new ArgumentNullException(nameof(spatialFacts));
        }

        public SemanticActionRequest CreateRequest(
            string commandId,
            string actionId,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId,
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters)
        {
            if (_mapper == null || _spatialFacts == null)
            {
                throw new InvalidOperationException("语义输入适配器尚未配置。");
            }

            var intent = new SemanticInputIntent(
                actionId,
                actorEntityId,
                sourceEntityId,
                targetEntityId,
                parameters);
            return _mapper.Map(
                commandId,
                intent,
                _spatialFacts.Measure(intent));
        }
    }
}
