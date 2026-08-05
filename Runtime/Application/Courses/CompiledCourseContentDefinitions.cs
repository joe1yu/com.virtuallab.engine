using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Domain.Relations;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 引擎保留的课程级资源 ID。具体表现资源仍由课程自行命名。
    /// </summary>
    public static class CourseResourceIds
    {
        public const string ExperimentPrefab = "资源.实验预制体";
    }

    /// <summary>
    /// 课程仅记录稳定资源 ID 和加载地址，资源类型由实际使用方声明。
    /// </summary>
    public sealed class CourseResourceDefinition
    {
        public CourseResourceDefinition(
            string resourceId,
            string assetPath)
        {
            ResourceId = CourseContractGuard.Required(resourceId, "资源 ID");
            AssetPath = CourseContractGuard.Required(
                assetPath,
                $"资源“{ResourceId}”的路径");
        }

        public string ResourceId { get; }
        public string AssetPath { get; }
    }

    public sealed class CoursePortDefinition
    {
        public CoursePortDefinition(
            string portId,
            string entityId,
            string compatibilityGroup)
        {
            PortId = CourseContractGuard.Required(portId, "端口 ID");
            EntityId = CourseContractGuard.Required(
                entityId,
                $"端口“{PortId}”的实体 ID");
            CompatibilityGroup = CourseContractGuard.Required(
                compatibilityGroup,
                $"端口“{PortId}”的兼容组");
        }

        public string PortId { get; }
        public string EntityId { get; }
        public string CompatibilityGroup { get; }
    }

    public sealed class CourseInitialRelationDefinition
    {
        public CourseInitialRelationDefinition(
            string relationId,
            RelationTypeId typeId,
            string sourceEntityId,
            string targetEntityId)
        {
            RelationId = CourseContractGuard.Required(
                relationId,
                "初始关系 ID");
            TypeId = typeId;
            SourceEntityId = CourseContractGuard.Required(
                sourceEntityId,
                $"初始关系“{RelationId}”的来源实体");
            TargetEntityId = CourseContractGuard.Required(
                targetEntityId,
                $"初始关系“{RelationId}”的目标实体");
        }

        public string RelationId { get; }
        public RelationTypeId TypeId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
    }

    /// <summary>
    /// 仅描述语义动作成功后写入领域世界的结果，不包含任何表现效果。
    /// </summary>
    public sealed class CourseActionResultGroupDefinition
    {
        public CourseActionResultGroupDefinition(
            string groupId,
            IEnumerable<string> mutationIds,
            IEnumerable<string> domainEventIds)
        {
            GroupId = CourseContractGuard.Required(groupId, "动作结果组 ID");
            MutationIds = CourseContractGuard.CopyStrings(
                mutationIds,
                $"动作结果组“{GroupId}”的状态变化 ID");
            DomainEventIds = CourseContractGuard.CopyStrings(
                domainEventIds,
                $"动作结果组“{GroupId}”的领域事件 ID");
        }

        public string GroupId { get; }

        public IReadOnlyList<string> MutationIds { get; }

        public IReadOnlyList<string> DomainEventIds { get; }
    }

    /// <summary>
    /// 仅描述一组可被表现规则引用的表现效果，不反向依赖领域状态变化。
    /// </summary>
    public sealed class CoursePresentationGroupDefinition
    {
        public CoursePresentationGroupDefinition(
            string groupId,
            IEnumerable<string> effectIds)
        {
            GroupId = CourseContractGuard.Required(groupId, "表现组 ID");
            EffectIds = CourseContractGuard.CopyStrings(
                effectIds,
                $"表现组“{GroupId}”的表现效果 ID");
        }

        public string GroupId { get; }

        public IReadOnlyList<string> EffectIds { get; }
    }

    public sealed class CourseDomainEventDefinition
    {
        public CourseDomainEventDefinition(
            string eventId,
            string eventType,
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters)
        {
            EventId = CourseContractGuard.Required(eventId, "领域事件 ID");
            EventType = CourseContractGuard.Required(
                eventType,
                $"领域事件“{EventId}”的类型");
            Parameters = CopyParameters(parameters, $"领域事件“{EventId}”");
        }

        public string EventId { get; }
        public string EventType { get; }
        public IReadOnlyDictionary<string, StructuredValue> Parameters { get; }

        internal static IReadOnlyDictionary<string, StructuredValue>
            CopyParameters(
                IEnumerable<KeyValuePair<string, StructuredValue>> parameters,
                string context)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            var copy = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var pair in parameters)
            {
                var key = CourseContractGuard.Required(
                    pair.Key,
                    context + "的参数名");
                if (pair.Value == null || !copy.TryAdd(key, pair.Value))
                {
                    throw new ArgumentException(
                        context + "包含空值或重复参数。",
                        nameof(parameters));
                }
            }

            return new ReadOnlyDictionary<string, StructuredValue>(copy);
        }
    }

    public sealed class CourseContinuousProcessDefinition
    {
        public CourseContinuousProcessDefinition(
            string processId,
            string startMutationId,
            string stopMutationId)
        {
            ProcessId = CourseContractGuard.Required(
                processId,
                "持续过程 ID");
            StartMutationId = CourseContractGuard.Required(
                startMutationId,
                $"持续过程“{ProcessId}”的开始状态变化");
            StopMutationId = CourseContractGuard.Required(
                stopMutationId,
                $"持续过程“{ProcessId}”的停止状态变化");
        }

        public string ProcessId { get; }
        public string StartMutationId { get; }
        public string StopMutationId { get; }
    }

    public enum CoursePresentationEntitySelectorKind
    {
        SignalSubject,
        ActionSource,
        ActionTarget,
        PayloadField,
        FixedEntity,
        Global
    }

    public enum CoursePresentationLocationKind
    {
        EntityRoot,
        PresentationSlot,
        SemanticAnchor,
        GlobalReceiver
    }

    public enum CoursePresentationParameterSource
    {
        Constant,
        SignalPayload
    }

    public enum CoursePresentationLifecycle
    {
        OneShot,
        WhileActive,
        UntilReplaced
    }

    public enum CoursePresentationTriggerKind
    {
        ActionAccepted,
        ActionRejected,
        DomainEvent,
        StateEntered,
        StateActive,
        StateExited,
        ActionAvailabilityChanged,
        CourseInitialized
    }

    public sealed class CoursePresentationTargetDefinition
    {
        public CoursePresentationTargetDefinition(
            CoursePresentationEntitySelectorKind entityKind,
            string entityValue,
            CoursePresentationLocationKind locationKind,
            string locationId)
        {
            EntityKind = entityKind;
            EntityValue = CourseContractGuard.Optional(entityValue);
            LocationKind = locationKind;
            LocationId = CourseContractGuard.Optional(locationId);
        }

        public CoursePresentationEntitySelectorKind EntityKind { get; }
        public string EntityValue { get; }
        public CoursePresentationLocationKind LocationKind { get; }
        public string LocationId { get; }
    }

    public sealed class CoursePresentationParameterBindingDefinition
    {
        public CoursePresentationParameterBindingDefinition(
            string name,
            CoursePresentationParameterSource source,
            StructuredValue constantValue,
            string payloadKey)
        {
            Name = CourseContractGuard.Required(name, "表现参数名");
            Source = source;
            ConstantValue = constantValue;
            PayloadKey = CourseContractGuard.Optional(payloadKey);
            if (source == CoursePresentationParameterSource.Constant &&
                constantValue == null)
            {
                throw new ArgumentException("固定值参数必须提供参数值。");
            }

            if (source == CoursePresentationParameterSource.SignalPayload &&
                PayloadKey == null)
            {
                throw new ArgumentException(
                    "信号载荷参数必须提供载荷字段。");
            }
        }

        public string Name { get; }
        public CoursePresentationParameterSource Source { get; }
        public StructuredValue ConstantValue { get; }
        public string PayloadKey { get; }
    }

    public sealed class CoursePresentationEffectDefinition
    {
        public CoursePresentationEffectDefinition(
            string effectId,
            string protocolId,
            CoursePresentationTargetDefinition target,
            CoursePresentationLifecycle lifecycle,
            int priority,
            IEnumerable<CoursePresentationParameterBindingDefinition>
                parameterBindings)
        {
            EffectId = CourseContractGuard.Required(effectId, "表现效果 ID");
            ProtocolId = CourseContractGuard.Required(
                protocolId,
                $"表现效果“{EffectId}”的协议 ID");
            Target = target ??
                throw new ArgumentNullException(nameof(target));
            Lifecycle = lifecycle;
            Priority = priority;
            ParameterBindings = CourseContractGuard.CopyUnique(
                parameterBindings,
                value => value.Name,
                $"表现效果“{EffectId}”的参数绑定");
        }

        public string EffectId { get; }
        public string ProtocolId { get; }
        public CoursePresentationTargetDefinition Target { get; }
        public CoursePresentationLifecycle Lifecycle { get; }
        public int Priority { get; }
        public IReadOnlyList<CoursePresentationParameterBindingDefinition>
            ParameterBindings { get; }
    }

    public sealed class CoursePresentationRuleDefinition
    {
        public CoursePresentationRuleDefinition(
            string ruleId,
            CoursePresentationTriggerKind triggerKind,
            string triggerValue,
            string presentationGroupId,
            string sourceEntityId = null,
            string targetEntityId = null)
        {
            RuleId = CourseContractGuard.Required(ruleId, "表现规则 ID");
            TriggerKind = triggerKind;
            TriggerValue = CourseContractGuard.Required(
                triggerValue,
                $"表现规则“{RuleId}”的触发值");
            PresentationGroupId = CourseContractGuard.Required(
                presentationGroupId,
                $"表现规则“{RuleId}”的表现组");
            SourceEntityId = CourseContractGuard.Optional(sourceEntityId);
            TargetEntityId = CourseContractGuard.Optional(targetEntityId);
        }

        public string RuleId { get; }
        public CoursePresentationTriggerKind TriggerKind { get; }
        public string TriggerValue { get; }
        public string PresentationGroupId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
    }

    public sealed class CoursePresentationStateDefinition
    {
        public CoursePresentationStateDefinition(
            string stateId,
            string subjectEntityId,
            string contextSourceEntityId,
            string contextTargetEntityId,
            IEnumerable<StructuredRuleDefinition> rules)
        {
            StateId = CourseContractGuard.Required(
                stateId,
                "表现状态 ID");
            SubjectEntityId = CourseContractGuard.Required(
                subjectEntityId,
                $"表现状态“{StateId}”的主体实体 ID");
            ContextSourceEntityId = CourseContractGuard.Required(
                contextSourceEntityId,
                $"表现状态“{StateId}”的上下文来源实体 ID");
            ContextTargetEntityId =
                CourseContractGuard.Optional(contextTargetEntityId);
            Rules = CourseContractGuard.CopyUnique(
                rules,
                value => value.RuleId,
                $"表现状态“{StateId}”的条件");
        }

        public string StateId { get; }
        public string SubjectEntityId { get; }
        public string ContextSourceEntityId { get; }
        public string ContextTargetEntityId { get; }
        public IReadOnlyList<StructuredRuleDefinition> Rules { get; }
    }

    /// <summary>
    /// 表现层可独立解码的完整配置，不要求反向读取领域 JSON。
    /// </summary>
    public sealed class CoursePresentationDefinition
    {
        public CoursePresentationDefinition(
            IEnumerable<CoursePresentationRuleDefinition> rules,
            IEnumerable<CoursePresentationGroupDefinition> groups,
            IEnumerable<CoursePresentationEffectDefinition> effects,
            IEnumerable<CoursePresentationStateDefinition> states,
            IEnumerable<CourseTextDefinition> texts)
        {
            Rules = CourseContractGuard.CopyUnique(
                rules,
                value => value.RuleId,
                "表现规则");
            Groups = CourseContractGuard.CopyUnique(
                groups,
                value => value.GroupId,
                "表现组");
            Effects = CourseContractGuard.CopyUnique(
                effects,
                value => value.EffectId,
                "表现效果");
            States = CourseContractGuard.CopyUnique(
                states,
                value => value.StateId,
                "表现状态");
            Texts = CourseContractGuard.CopyUnique(
                texts,
                value => value.TextId,
                "表现文案");
        }

        public IReadOnlyList<CoursePresentationRuleDefinition> Rules { get; }
        public IReadOnlyList<CoursePresentationGroupDefinition> Groups
        {
            get;
        }
        public IReadOnlyList<CoursePresentationEffectDefinition> Effects
        {
            get;
        }
        public IReadOnlyList<CoursePresentationStateDefinition> States
        {
            get;
        }
        public IReadOnlyList<CourseTextDefinition> Texts { get; }
    }

    public sealed class CourseSceneLayoutDefinition
    {
        public CourseSceneLayoutDefinition(
            string entityId,
            double positionX,
            double positionY,
            double positionZ,
            double rotationX,
            double rotationY,
            double rotationZ)
        {
            EntityId = CourseContractGuard.Required(entityId, "布局实体 ID");
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
        }

        public string EntityId { get; }
        public double PositionX { get; }
        public double PositionY { get; }
        public double PositionZ { get; }
        public double RotationX { get; }
        public double RotationY { get; }
        public double RotationZ { get; }
    }

    public sealed class CourseTextDefinition
    {
        public CourseTextDefinition(string textId, string content)
        {
            TextId = CourseContractGuard.Required(textId, "文案 ID");
            Content = CourseContractGuard.Required(
                content,
                $"文案“{TextId}”的内容");
        }

        public string TextId { get; }
        public string Content { get; }
    }

    /// <summary>
    /// 实验总预制体内单个实体视图必须满足的组件、锚点和插槽要求。
    /// </summary>
    public sealed class CoursePrefabContractDefinition
    {
        public CoursePrefabContractDefinition(
            string entityId,
            IEnumerable<string> capabilityIds,
            IEnumerable<string> portIds)
        {
            EntityId = CourseContractGuard.Required(
                entityId,
                "实体视图契约实体 ID");
            CapabilityIds = CopyOptionalStrings(
                capabilityIds,
                $"实体视图“{EntityId}”的能力");
            PortIds = CopyOptionalStrings(
                portIds,
                $"实体视图“{EntityId}”的端口");
        }

        public string EntityId { get; }
        public IReadOnlyList<string> CapabilityIds { get; }
        public IReadOnlyList<string> PortIds { get; }

        private static IReadOnlyList<string> CopyOptionalStrings(
            IEnumerable<string> values,
            string context)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var copy = values
                .Select(value => CourseContractGuard.Required(value, context))
                .ToArray();
            if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            {
                throw new ArgumentException(context + "包含重复 ID。");
            }

            return new ReadOnlyCollection<string>(copy);
        }
    }
}
