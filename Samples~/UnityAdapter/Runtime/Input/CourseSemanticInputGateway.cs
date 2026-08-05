using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Physics;

namespace VirtualLab.UnityAdapters.Input
{
    public sealed class SemanticActionCandidate
    {
        public SemanticActionCandidate(
            string actionId,
            string operationId,
            SemanticActionLifecycle lifecycle,
            string executionModeId,
            SemanticActionPhase phase,
            string sourceEntityId,
            string targetEntityId,
            int priority)
        {
            ActionId = actionId;
            OperationId = operationId;
            Lifecycle = lifecycle;
            ExecutionModeId = executionModeId;
            Phase = phase;
            SourceEntityId = sourceEntityId;
            TargetEntityId = targetEntityId;
            Priority = priority;
        }

        public string ActionId { get; }
        public string OperationId { get; }
        public SemanticActionLifecycle Lifecycle { get; }
        public string ExecutionModeId { get; }
        public SemanticActionPhase Phase { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public int Priority { get; }
    }

    /// <summary>
    /// 课程场景统一的语义输入入口。鼠标、触控、VR 和自动化层只负责识别
    /// 操作者、来源、目标及动作，不直接修改 Transform 或领域状态。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConfigDrivenCourseBootstrap))]
    [DefaultExecutionOrder(100)]
    public sealed class CourseSemanticInputGateway : MonoBehaviour
    {
        private ConfigDrivenCourseBootstrap _bootstrap;
        private SemanticPointerInputAdapter _adapter;

        public bool IsInitialized =>
            _bootstrap != null
            && _bootstrap.IsInitialized
            && _adapter != null;

        public void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            _bootstrap = GetComponent<ConfigDrivenCourseBootstrap>();
            _bootstrap.Initialize();
            if (_bootstrap.SceneAssembly?.CourseViews == null)
            {
                throw new InvalidOperationException(
                    "语义输入网关只能用于已装配实体视图的课程场景。");
            }

            _adapter = GetComponent<SemanticPointerInputAdapter>();
            if (_adapter == null)
            {
                _adapter = gameObject.AddComponent<
                    SemanticPointerInputAdapter>();
            }

            _adapter.Configure(
                new SemanticActionGestureMapper(),
                new UnitySpatialFactProvider(
                    _bootstrap.SceneAssembly.CourseViews));
        }

        public SemanticActionRequest CreateRequest(
            string commandId,
            string actionId,
            string operationInstanceId,
            SemanticActionPhase phase,
            double occurredAtSeconds,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId = null,
            IEnumerable<KeyValuePair<string, StructuredValue>>
                parameters = null)
        {
            EnsureInitialized();
            return _adapter.CreateRequest(
                commandId,
                actionId,
                operationInstanceId,
                phase,
                occurredAtSeconds,
                actorEntityId,
                sourceEntityId,
                targetEntityId,
                parameters ?? Array.Empty<
                    KeyValuePair<string, StructuredValue>>());
        }

        public CourseAvailabilityResult QueryAvailability(
            string commandId,
            string actionId,
            string operationInstanceId,
            SemanticActionPhase phase,
            double occurredAtSeconds,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId = null,
            IEnumerable<KeyValuePair<string, StructuredValue>>
                parameters = null)
        {
            return _bootstrap.QueryAvailability(CreateRequest(
                commandId,
                actionId,
                operationInstanceId,
                phase,
                occurredAtSeconds,
                actorEntityId,
                sourceEntityId,
                targetEntityId,
                parameters));
        }

        /// <summary>
        /// 返回课程为当前来源和目标实际生成的动作。输入设备只在这些候选中
        /// 解释手势，避免在鼠标、触控或 VR 适配器中复制课程对象关系。
        /// </summary>
        public IReadOnlyList<SemanticActionCandidate> FindCandidates(
            string manipulatedEntityId,
            string contactedEntityId)
        {
            EnsureInitialized();
            var values = _bootstrap.Domain.ConfiguredActions
                .Where(value =>
                    value.Effect == ConfiguredActionPolicyEffect.Allow
                    && _bootstrap.OperationExecutors.TryGet(
                        value.ExecutionModeId,
                        out _)
                    && !value.MatchesAnyEntities
                    && IsManipulationPair(
                        value,
                        manipulatedEntityId,
                        contactedEntityId))
                .GroupBy(
                    value => new
                    {
                        value.ActionId,
                        value.OperationId,
                        value.Lifecycle,
                        value.ExecutionModeId,
                        value.Phase,
                        value.SourceEntityId,
                        value.TargetEntityId
                    })
                .Select(value => new SemanticActionCandidate(
                    value.Key.ActionId,
                    value.Key.OperationId,
                    value.Key.Lifecycle,
                    value.Key.ExecutionModeId,
                    value.Key.Phase,
                    value.Key.SourceEntityId,
                    value.Key.TargetEntityId,
                    value.Max(item => item.Priority)))
                .OrderByDescending(value => value.Priority)
                .ThenBy(value => value.ActionId, StringComparer.Ordinal)
                .ThenBy(value => value.SourceEntityId, StringComparer.Ordinal)
                .ThenBy(value => value.TargetEntityId, StringComparer.Ordinal)
                .ToArray();
            return new ReadOnlyCollection<SemanticActionCandidate>(values);
        }

        /// <summary>
        /// 返回当前实体可执行的单对象语义动作。设备适配器可以将振荡、观察等
        /// 手势映射到这些候选，而不需要了解课程实体组合。
        /// </summary>
        public IReadOnlyList<SemanticActionCandidate> FindUnaryCandidates(
            string manipulatedEntityId)
        {
            EnsureInitialized();
            var values = _bootstrap.Domain.ConfiguredActions
                .Where(value =>
                    value.Effect == ConfiguredActionPolicyEffect.Allow
                    && _bootstrap.OperationExecutors.TryGet(
                        value.ExecutionModeId,
                        out _)
                    && value.TargetEntityId == null
                    && (value.MatchesAnyEntities
                        || string.Equals(
                            value.SourceEntityId,
                            manipulatedEntityId,
                            StringComparison.Ordinal)))
                .GroupBy(
                    value => new
                    {
                        value.ActionId,
                        value.OperationId,
                        value.Lifecycle,
                        value.ExecutionModeId,
                        value.Phase,
                        value.SourceEntityId,
                        value.TargetEntityId
                    })
                .Select(value => new SemanticActionCandidate(
                    value.Key.ActionId,
                    value.Key.OperationId,
                    value.Key.Lifecycle,
                    value.Key.ExecutionModeId,
                    value.Key.Phase,
                    value.Key.SourceEntityId ?? manipulatedEntityId,
                    null,
                    value.Max(item => item.Priority)))
                .OrderByDescending(value => value.Priority)
                .ThenBy(value => value.ActionId, StringComparer.Ordinal)
                .ToArray();
            return new ReadOnlyCollection<SemanticActionCandidate>(values);
        }

        public string ActorEntityId
        {
            get
            {
                EnsureInitialized();
                return _bootstrap.Domain.ActorEntityId;
            }
        }

        /// <summary>
        /// 只读探测，不产生可用性表现。控制器可先比较多个候选，再把最终
        /// 候选通过 QueryAvailability 投影给表现层。
        /// </summary>
        public ActionAvailability ProbeAvailability(
            string commandId,
            string actionId,
            string operationInstanceId,
            SemanticActionPhase phase,
            double occurredAtSeconds,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId = null,
            IEnumerable<KeyValuePair<string, StructuredValue>>
                parameters = null)
        {
            EnsureInitialized();
            return _bootstrap.Runtime.QueryAvailability(CreateRequest(
                commandId,
                actionId,
                operationInstanceId,
                phase,
                occurredAtSeconds,
                actorEntityId,
                sourceEntityId,
                targetEntityId,
                parameters));
        }

        public CourseDispatchResult Dispatch(
            string commandId,
            string actionId,
            string operationInstanceId,
            SemanticActionPhase phase,
            double occurredAtSeconds,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId = null,
            IEnumerable<KeyValuePair<string, StructuredValue>>
                parameters = null)
        {
            return _bootstrap.Dispatch(CreateRequest(
                commandId,
                actionId,
                operationInstanceId,
                phase,
                occurredAtSeconds,
                actorEntityId,
                sourceEntityId,
                targetEntityId,
                parameters));
        }

        private void Start()
        {
            Initialize();
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                Initialize();
            }
        }

        private static bool IsManipulationPair(
            ConfiguredActionDefinition action,
            string manipulatedEntityId,
            string contactedEntityId)
        {
            return (string.Equals(
                        action.SourceEntityId,
                        manipulatedEntityId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        action.TargetEntityId,
                        contactedEntityId,
                        StringComparison.Ordinal))
                   || (string.Equals(
                           action.TargetEntityId,
                           manipulatedEntityId,
                           StringComparison.Ordinal)
                       && string.Equals(
                           action.SourceEntityId,
                           contactedEntityId,
                           StringComparison.Ordinal));
        }
    }
}
