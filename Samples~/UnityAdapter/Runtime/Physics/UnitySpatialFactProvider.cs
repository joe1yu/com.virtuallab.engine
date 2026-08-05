using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.Spatial.Courses;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Input;

namespace VirtualLab.UnityAdapters.Physics
{
    public sealed class UnitySpatialFactProvider : ISpatialFactProvider
    {
        private readonly CourseEntityViewRegistry _views;
        private readonly double _alignmentToleranceDegrees;

        public UnitySpatialFactProvider(
            CourseEntityViewRegistry views,
            double alignmentToleranceDegrees = 15d)
        {
            _views = views ?? throw new ArgumentNullException(nameof(views));
            if (double.IsNaN(alignmentToleranceDegrees) ||
                double.IsInfinity(alignmentToleranceDegrees) ||
                alignmentToleranceDegrees < 0d ||
                alignmentToleranceDegrees > 180d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(alignmentToleranceDegrees));
            }

            _alignmentToleranceDegrees = alignmentToleranceDegrees;
        }

        public SpatialFactSet Measure(SemanticInputObservation observation)
        {
            if (observation == null)
            {
                throw new ArgumentNullException(nameof(observation));
            }

            if (!_views.TryGet(observation.SourceEntityId, out var source))
            {
                throw new InvalidOperationException(
                    $"找不到来源实体视图“{observation.SourceEntityId}”。");
            }

            _views.TryGet(observation.TargetEntityId, out var target);
            var sourceAnchor = SelectAnchor(
                source,
                observation,
                "来源锚点ID");
            var targetAnchor = target == null
                ? null
                : SelectAnchor(target, observation, "目标锚点ID");
            var sourceTransform = sourceAnchor == null
                ? source.transform
                : sourceAnchor.transform;
            var targetTransform = targetAnchor == null
                ? target?.transform
                : targetAnchor.transform;
            var distance = targetTransform == null
                ? 0d
                : Normalize(Vector3.Distance(
                    sourceTransform.position,
                    targetTransform.position));
            var angle = targetTransform == null
                ? 0d
                : Normalize(Vector3.Angle(
                    sourceTransform.forward,
                    targetTransform.forward));
            var sourceCollider =
                source.GetComponentInChildren<Collider>(true);
            var targetCollider =
                target?.GetComponentInChildren<Collider>(true);
            var contact =
                sourceCollider != null &&
                targetCollider != null &&
                sourceCollider.bounds.Intersects(targetCollider.bounds);
            var values = new List<KeyValuePair<string, StructuredValue>>
            {
                Fact(
                    SpatialRequestParameterKeys.DistanceMeters,
                    StructuredValue.FromNumber(distance)),
                Fact(
                    SpatialRequestParameterKeys.IsContacting,
                    StructuredValue.FromBoolean(contact)),
                Fact(
                    SpatialRequestParameterKeys.PortAligned,
                    StructuredValue.FromBoolean(
                        targetTransform != null &&
                        angle <= _alignmentToleranceDegrees)),
                Fact(
                    SpatialRequestParameterKeys.OutletAligned,
                    StructuredValue.FromBoolean(
                        targetTransform != null &&
                        angle <= _alignmentToleranceDegrees)),
                Fact(
                    SpatialRequestParameterKeys.TiltAngleDegrees,
                    StructuredValue.FromNumber(
                        Normalize(Vector3.Angle(
                            source.transform.up,
                            Vector3.up))))
            };
            if (sourceAnchor != null)
            {
                values.Add(Fact(
                    "来源锚点ID",
                    StructuredValue.FromText(sourceAnchor.AnchorId)));
            }

            if (targetAnchor != null)
            {
                values.Add(Fact(
                    "目标锚点ID",
                    StructuredValue.FromText(targetAnchor.AnchorId)));
            }

            return new SpatialFactSet(values);
        }

        private static SemanticAnchorMarker SelectAnchor(
            CourseEntityView view,
            SemanticInputObservation observation,
            string preferredParameter)
        {
            if (observation.Parameters.TryGetValue(
                    preferredParameter,
                    out var preferred))
            {
                if (preferred.Kind != StructuredValueKind.Text ||
                    !view.TryGetAnchor(preferred.Text, out var selected))
                {
                    throw new InvalidOperationException(
                        $"实体“{view.EntityId}”不存在输入指定的锚点“" +
                        preferred.Text +
                        "”。");
                }

                return selected;
            }

            return view.Anchors.FirstOrDefault(value =>
                value != null &&
                value.Kind == SemanticAnchorKind.ConnectionPort);
        }

        private static double Normalize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidOperationException("Unity 空间测量产生了非有限数。");
            }

            return Math.Round(value, 6, MidpointRounding.ToEven);
        }

        private static KeyValuePair<string, StructuredValue> Fact(
            string key,
            StructuredValue value)
        {
            return new KeyValuePair<string, StructuredValue>(key, value);
        }
    }
}
