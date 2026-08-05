using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 通用课程运行能力的显式模块入口。
    /// </summary>
    public sealed class CoreCourseRuntimeModule : ICourseRuntimeModule
    {
        private static readonly CourseModuleManifest ModuleManifest =
            new CourseModuleManifest(
                CourseModuleIds.Core,
                "最小课程内核",
                new Version(1, 0, 0));

        public CourseModuleManifest Manifest => ModuleManifest;

        public void Register(CourseModuleRegistrationContext context)
        {
            foreach (var reader in CoreCourseRegistrations.CreateFactReaders())
            {
                context.RegisterFactReader(reader);
            }

            context.RegisterStateOperations(
                "内核.状态操作.通用",
                registry => registry.RegisterBuiltInOperations());
        }
    }

    /// <summary>
    /// 注册跨学科事实和通用状态操作，不定义任何具体课程实体或阈值。
    /// </summary>
    public static class CoreCourseRegistrations
    {
        private const string 对象正在接触Parameter = "空间接触";
        private const string 对象间距离Parameter = "空间距离米";

        public static ConfigDrivenCourseSession CreateSession(
            ExperimentWorld world,
            IEnumerable<ConfiguredActionDefinition> actions)
        {
            return new CourseRuntimeDefinition(
                    CreateModuleScope(),
                    actions,
                    Array.Empty<CourseActionAssessmentDefinition>(),
                    0)
                .CreateSession(world);
        }

        public static ConfigDrivenCourseSession CreateSession(
            ExperimentWorld world,
            CompiledCourseDefinition course)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var modules = CreateModuleScope();
            modules.ValidateRequiredModules(course.RequiredModuleIds);
            return new CourseRuntimeDefinition(
                    modules,
                    course.ConfiguredActions,
                    course.ActionAssessments,
                    course.Assessments.Sum(value => value.MaximumScore))
                .CreateSession(world);
        }

        public static CourseRuntimeFacade CreateRuntimeFacade(
            ExperimentWorld world,
            CompiledCourseDefinition course)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var modules = CreateModuleScope();
            modules.ValidateRequiredModules(course.RequiredModuleIds);
            var session = new CourseRuntimeDefinition(
                    modules,
                    course.ConfiguredActions,
                    course.ActionAssessments,
                    course.Assessments.Sum(value => value.MaximumScore))
                .CreateSession(world);
            return new CourseRuntimeFacade(
                world,
                session,
                modules.FactReaders,
                course.GoalRules);
        }

        public static CourseRuntimeModuleScope CreateModuleScope()
        {
            return CourseRuntimeModuleScope.Create(new CoreCourseRuntimeModule());
        }

        public static IReadOnlyList<IStructuredFactReader>
            CreateFactReaders()
        {
            return new IStructuredFactReader[]
            {
                new EntityExistsFactReader(
                    StructuredFactField.操作者存在,
                    context => context.Request.ActorEntityId),
                new EntityExistsFactReader(
                    StructuredFactField.来源对象存在,
                    context => context.Request.SourceEntityId),
                new EntityExistsFactReader(
                    StructuredFactField.目标对象存在,
                    context => context.Request.TargetEntityId),
                new CapabilityFactReader(
                    StructuredFactField.操作者能力,
                    context => context.Request.ActorEntityId),
                new CapabilityFactReader(
                    StructuredFactField.来源对象能力,
                    context => context.Request.SourceEntityId),
                new CapabilityFactReader(
                    StructuredFactField.目标对象能力,
                    context => context.Request.TargetEntityId),
                new 来源对象持有者FactReader(),
                new 来源对象已被操作者拿起FactReader(),
                new HeldByActorFactReader(
                    StructuredFactField.目标对象已被操作者拿起,
                    context => context.Request.TargetEntityId),
                new ProgressFactReader(
                    StructuredFactField.来源对象进度,
                    context => context.Request.SourceEntityId),
                new ProgressFactReader(
                    StructuredFactField.目标对象进度,
                    context => context.Request.TargetEntityId),
                new PortOccupancyFactReader(
                    StructuredFactField.来源连接点占用状态,
                    true),
                new PortOccupancyFactReader(
                    StructuredFactField.目标连接点占用状态,
                    false),
                new PortCompatibilityGroupFactReader(
                    StructuredFactField.来源连接标签,
                    true),
                new PortCompatibilityGroupFactReader(
                    StructuredFactField.目标连接标签,
                    false),
                new 连接标签相匹配FactReader(),
                new RequestParameterFactReader(
                    StructuredFactField.对象正在接触,
                    对象正在接触Parameter,
                    StructuredValue.FromBoolean(false)),
                new RequestParameterFactReader(
                    StructuredFactField.对象间距离,
                    对象间距离Parameter,
                    StructuredValue.FromNumber(double.MaxValue))
            };
        }

        private static StructuredValue CapabilityIds(
            ExperimentEntity entity)
        {
            if (entity == null)
            {
                return StructuredValue.FromTextList(Array.Empty<string>());
            }

            var ids = entity.Capabilities
                .Select(CapabilityId)
                .OrderBy(value => value, StringComparer.Ordinal);
            return StructuredValue.FromTextList(ids);
        }

        private static string CapabilityId(ICapability capability)
        {
            return capability switch
            {
                GrabbableCapability _ => CoreCapabilityIds.Grabbable,
                ContainerCapability _ => CoreCapabilityIds.Container,
                ConnectorCapability _ => CoreCapabilityIds.Connector,
                IConfiguredCapability configured =>
                    configured.CapabilityId,
                ObservableCapability _ => CoreCapabilityIds.Observable,
                ClampableCapability _ => CoreCapabilityIds.Clampable,
                CoverableCapability _ => CoreCapabilityIds.Coverable,
                BreakableCapability _ => CoreCapabilityIds.Breakable,
                _ => capability.GetType().FullName
            };
        }

        private static ExperimentEntity FindEntity(
            StructuredRuleContext context,
            string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return null;
            }

            return context.World.TryGetEntity(
                new EntityId(entityId),
                out var entity)
                ? entity
                : null;
        }

        private sealed class EntityExistsFactReader :
            IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public EntityExistsFactReader(
                StructuredFactField field,
                Func<StructuredRuleContext, string> idSelector)
            {
                Field = field;
                _idSelector = idSelector;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return StructuredValue.FromBoolean(
                    FindEntity(context, _idSelector(context)) != null);
            }
        }

        private sealed class CapabilityFactReader :
            IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public CapabilityFactReader(
                StructuredFactField field,
                Func<StructuredRuleContext, string> idSelector)
            {
                Field = field;
                _idSelector = idSelector;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return CapabilityIds(
                    FindEntity(context, _idSelector(context)));
            }
        }

        private sealed class 来源对象持有者FactReader :
            IStructuredFactReader
        {
            public StructuredFactField Field =>
                StructuredFactField.来源对象持有者;

            public StructuredValue Read(StructuredRuleContext context)
            {
                var source = new EntityId(context.Request.SourceEntityId);
                var holder = context.World.Relations.FirstOrDefault(
                    value => value.Kind == RelationKind.由对象持有
                        && value.Source == source);
                return holder == null
                    ? StructuredValue.Null()
                    : StructuredValue.FromText(holder.Target.Value);
            }
        }

        private sealed class 来源对象已被操作者拿起FactReader :
            IStructuredFactReader
        {
            public StructuredFactField Field =>
                StructuredFactField.来源对象已被操作者拿起;

            public StructuredValue Read(StructuredRuleContext context)
            {
                var source = new EntityId(context.Request.SourceEntityId);
                var actor = new EntityId(context.Request.ActorEntityId);
                return StructuredValue.FromBoolean(
                    context.World.Relations.Any(
                        value => value.Kind == RelationKind.由对象持有
                            && value.Source == source
                            && value.Target == actor));
            }
        }

        private sealed class HeldByActorFactReader :
            IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public HeldByActorFactReader(
                StructuredFactField field,
                Func<StructuredRuleContext, string> idSelector)
            {
                Field = field;
                _idSelector = idSelector;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                var selectedId = _idSelector(context);
                if (string.IsNullOrWhiteSpace(selectedId))
                {
                    return StructuredValue.FromBoolean(false);
                }

                var entity = new EntityId(selectedId);
                var actor = new EntityId(context.Request.ActorEntityId);
                return StructuredValue.FromBoolean(
                    context.World.Relations.Any(value =>
                        value.Kind == RelationKind.由对象持有
                        && value.Source == entity
                        && value.Target == actor));
            }
        }

        private sealed class ProgressFactReader :
            IStructuredFactReader
        {
            private readonly Func<StructuredRuleContext, string> _idSelector;

            public ProgressFactReader(
                StructuredFactField field,
                Func<StructuredRuleContext, string> idSelector)
            {
                Field = field;
                _idSelector = idSelector;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                var selectedId = _idSelector(context);
                if (string.IsNullOrWhiteSpace(selectedId))
                {
                    return StructuredValue.FromNumber(0d);
                }

                var key = selectedId + ".课程进度";
                return context.World.TryGetScalar(key, out var progress)
                    ? StructuredValue.FromNumber(progress.Value)
                    : StructuredValue.FromNumber(0d);
            }
        }

        private sealed class PortOccupancyFactReader :
            IStructuredFactReader
        {
            private readonly bool _sourceEndpoint;

            public PortOccupancyFactReader(
                StructuredFactField field,
                bool sourceEndpoint)
            {
                Field = field;
                _sourceEndpoint = sourceEndpoint;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                if (!ConnectionPortResolver.TryResolveForFacts(
                        context.World,
                        context.Request,
                        out var ports))
                {
                    return StructuredValue.Null();
                }

                var entityId = _sourceEndpoint
                    ? ports.SourceEntityId
                    : ports.TargetEntityId;
                var portId = _sourceEndpoint
                    ? ports.SourcePort.PortId
                    : ports.TargetPort.PortId;
                var connection = ConnectionPortResolver.FindConnection(
                    context.World,
                    entityId,
                    portId);
                if (connection == null)
                {
                    return StructuredValue.Null();
                }

                var other = connection.Source == entityId
                    ? connection.Target
                    : connection.Source;
                return StructuredValue.FromText(other.Value);
            }
        }

        private sealed class PortCompatibilityGroupFactReader :
            IStructuredFactReader
        {
            private readonly bool _sourceEndpoint;

            public PortCompatibilityGroupFactReader(
                StructuredFactField field,
                bool sourceEndpoint)
            {
                Field = field;
                _sourceEndpoint = sourceEndpoint;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                if (!ConnectionPortResolver.TryResolveForFacts(
                        context.World,
                        context.Request,
                        out var ports))
                {
                    return StructuredValue.Null();
                }

                var group = (_sourceEndpoint
                    ? ports.SourcePort
                    : ports.TargetPort).CompatibilityGroup;
                return group == null
                    ? StructuredValue.Null()
                    : StructuredValue.FromText(group);
            }
        }

        private sealed class 连接标签相匹配FactReader :
            IStructuredFactReader
        {
            public StructuredFactField Field =>
                StructuredFactField.连接标签相匹配;

            public StructuredValue Read(StructuredRuleContext context)
            {
                return StructuredValue.FromBoolean(
                    ConnectionPortResolver.TryResolve(
                        context.World,
                        context.Request,
                        out _));
            }
        }

        private sealed class RequestParameterFactReader :
            IStructuredFactReader
        {
            private readonly string _parameterName;
            private readonly StructuredValue _defaultValue;

            public RequestParameterFactReader(
                StructuredFactField field,
                string parameterName,
                StructuredValue defaultValue)
            {
                Field = field;
                _parameterName = parameterName;
                _defaultValue = defaultValue;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return context.Request.Parameters.TryGetValue(
                    _parameterName,
                    out var value)
                    ? value
                    : _defaultValue;
            }
        }
    }

    internal sealed class ConnectionPortPair
    {
        public ConnectionPortPair(
            EntityId sourceEntityId,
            ConnectionPortDefinition sourcePort,
            EntityId targetEntityId,
            ConnectionPortDefinition targetPort)
        {
            SourceEntityId = sourceEntityId;
            SourcePort = sourcePort;
            TargetEntityId = targetEntityId;
            TargetPort = targetPort;
        }

        public EntityId SourceEntityId { get; }
        public ConnectionPortDefinition SourcePort { get; }
        public EntityId TargetEntityId { get; }
        public ConnectionPortDefinition TargetPort { get; }
    }

    /// <summary>
    /// 根据语义请求和实体能力选择具体端口。输入设备可以通过锚点 ID 明确指定端口；
    /// 未指定时按兼容组和端口 ID 确定性选择，保证测试、鼠标和 VR 得到相同结果。
    /// </summary>
    internal static class ConnectionPortResolver
    {
        private const string SourceAnchorId = "来源锚点ID";
        private const string TargetAnchorId = "目标锚点ID";

        public static bool TryResolve(
            ExperimentWorld world,
            SemanticActionRequest request,
            out ConnectionPortPair pair)
        {
            if (!TryResolveEndpoints(world, request, out pair))
            {
                return false;
            }

            if (pair.SourcePort.CompatibilityGroup != null
                && string.Equals(
                    pair.SourcePort.CompatibilityGroup,
                    pair.TargetPort.CompatibilityGroup,
                    StringComparison.Ordinal))
            {
                return true;
            }

            // 未指定端口时，需要继续寻找兼容组合，不能只比较各端首个端口。
            return TryResolveCompatiblePair(world, request, out pair);
        }

        /// <summary>
        /// 为占用和兼容组事实解析候选端口。即使两端不兼容也返回候选，
        /// 让规则引擎能够同时报告占用、不兼容和距离等全部拒绝原因。
        /// </summary>
        public static bool TryResolveForFacts(
            ExperimentWorld world,
            SemanticActionRequest request,
            out ConnectionPortPair pair)
        {
            return TryResolve(world, request, out pair)
                || TryResolveEndpoints(world, request, out pair);
        }

        private static bool TryResolveEndpoints(
            ExperimentWorld world,
            SemanticActionRequest request,
            out ConnectionPortPair pair)
        {
            pair = null;
            if (world == null || request == null
                || string.IsNullOrWhiteSpace(request.TargetEntityId))
            {
                return false;
            }

            var sourceId = new EntityId(request.SourceEntityId);
            var targetId = new EntityId(request.TargetEntityId);
            if (!world.TryGetEntity(sourceId, out var source)
                || !world.TryGetEntity(targetId, out var target)
                || !source.HasCapability<ConnectorCapability>()
                || !target.HasCapability<ConnectorCapability>())
            {
                return false;
            }

            var sourcePortId = PreferredPortId(request, SourceAnchorId);
            var targetPortId = PreferredPortId(request, TargetAnchorId);
            var sourcePorts = source.GetCapability<ConnectorCapability>()
                .Ports
                .Where(value => sourcePortId == null || string.Equals(
                    value.PortId,
                    sourcePortId,
                    StringComparison.Ordinal))
                .OrderBy(value => value.PortId, StringComparer.Ordinal);
            var targetPorts = target.GetCapability<ConnectorCapability>()
                .Ports
                .Where(value => targetPortId == null || string.Equals(
                    value.PortId,
                    targetPortId,
                    StringComparison.Ordinal))
                .OrderBy(value => value.PortId, StringComparer.Ordinal)
                .ToArray();
            var sourcePort = sourcePorts.FirstOrDefault();
            var targetPort = targetPorts.FirstOrDefault();
            if (sourcePort == null || targetPort == null)
            {
                return false;
            }

            pair = new ConnectionPortPair(
                sourceId,
                sourcePort,
                targetId,
                targetPort);
            return true;
        }

        private static bool TryResolveCompatiblePair(
            ExperimentWorld world,
            SemanticActionRequest request,
            out ConnectionPortPair pair)
        {
            pair = null;
            var sourceId = new EntityId(request.SourceEntityId);
            var targetId = new EntityId(request.TargetEntityId);
            if (!world.TryGetEntity(sourceId, out var source)
                || !world.TryGetEntity(targetId, out var target))
            {
                return false;
            }

            var sourcePortId = PreferredPortId(request, SourceAnchorId);
            var targetPortId = PreferredPortId(request, TargetAnchorId);
            var sourcePorts = source.GetCapability<ConnectorCapability>()
                .Ports
                .Where(value => sourcePortId == null || string.Equals(
                    value.PortId,
                    sourcePortId,
                    StringComparison.Ordinal))
                .OrderBy(value => value.PortId, StringComparer.Ordinal);
            var targetPorts = target.GetCapability<ConnectorCapability>()
                .Ports
                .Where(value => targetPortId == null || string.Equals(
                    value.PortId,
                    targetPortId,
                    StringComparison.Ordinal))
                .OrderBy(value => value.PortId, StringComparer.Ordinal)
                .ToArray();
            foreach (var sourcePort in sourcePorts)
            {
                var targetPort = targetPorts.FirstOrDefault(value =>
                    sourcePort.CompatibilityGroup != null
                    && string.Equals(
                        sourcePort.CompatibilityGroup,
                        value.CompatibilityGroup,
                        StringComparison.Ordinal));
                if (targetPort != null)
                {
                    pair = new ConnectionPortPair(
                        sourceId,
                        sourcePort,
                        targetId,
                        targetPort);
                    return true;
                }
            }

            return false;
        }

        public static EntityRelation FindConnection(
            ExperimentWorld world,
            EntityId entityId,
            string portId)
        {
            return world.Relations.FirstOrDefault(value =>
                value.Kind == RelationKind.连接对象
                && ((!value.HasPortEndpoints
                        && (value.Source == entityId
                            || value.Target == entityId))
                    || (value.Source == entityId
                        && string.Equals(
                            value.SourcePortId,
                            portId,
                            StringComparison.Ordinal))
                    || (value.Target == entityId
                        && string.Equals(
                            value.TargetPortId,
                            portId,
                            StringComparison.Ordinal))));
        }

        private static string PreferredPortId(
            SemanticActionRequest request,
            string key)
        {
            if (!request.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                return null;
            }

            return value.Text.Trim();
        }
    }
}
