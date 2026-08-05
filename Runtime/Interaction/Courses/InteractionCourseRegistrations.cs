using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Interaction.Courses
{
    /// <summary>
    /// 交互课程模块的稳定标识。
    /// </summary>
    public static class InteractionModuleIds
    {
        public const string Interaction = "交互";
    }

    /// <summary>
    /// 显式注册交互关系模式和事实读取器，通用应用层不再隐式安装这些协议。
    /// </summary>
    public sealed class InteractionCourseRuntimeModule : ICourseRuntimeModule
    {
        private static readonly CourseModuleManifest ModuleManifest =
            new CourseModuleManifest(
                InteractionModuleIds.Interaction,
                "通用实验交互",
                new Version(1, 0, 0),
                new[]
                {
                    new CourseModuleDependency(
                        CourseModuleIds.Core,
                        new Version(1, 0, 0))
                });

        public CourseModuleManifest Manifest => ModuleManifest;

        public void Register(CourseModuleRegistrationContext context)
        {
            foreach (var schema in InteractionRelationSchemas.All)
            {
                context.RegisterRelationSchema(schema);
            }

            foreach (var reader in InteractionCourseRegistrations
                .CreateFactReaders())
            {
                context.RegisterFactReader(reader);
            }
        }
    }

    /// <summary>
    /// 为需要抓取、放置、覆盖和连接等行为的课程组合内核与交互模块。
    /// </summary>
    public static class InteractionCourseRegistrations
    {
        private const string SourceAnchorId = "来源锚点ID";
        private const string TargetAnchorId = "目标锚点ID";

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
            return CourseRuntimeModuleScope.Create(
                new CoreCourseRuntimeModule(),
                new InteractionCourseRuntimeModule());
        }

        public static IReadOnlyList<IStructuredFactReader> CreateFactReaders()
        {
            return new IStructuredFactReader[]
            {
                new 来源对象持有者FactReader(),
                new 来源对象已被操作者拿起FactReader(),
                new HeldByActorFactReader(
                    InteractionStructuredFactFields.目标对象已被操作者拿起,
                    context => context.Request.TargetEntityId),
                new PortOccupancyFactReader(
                    InteractionStructuredFactFields.来源连接点占用状态,
                    true),
                new PortOccupancyFactReader(
                    InteractionStructuredFactFields.目标连接点占用状态,
                    false),
                new PortCompatibilityGroupFactReader(
                    InteractionStructuredFactFields.来源连接标签,
                    true),
                new PortCompatibilityGroupFactReader(
                    InteractionStructuredFactFields.目标连接标签,
                    false),
                new 连接标签相匹配FactReader()
            };
        }

        private static bool TryResolve(
            StructuredRuleContext context,
            bool allowIncompatible,
            out ConnectionPortPair pair)
        {
            pair = null;
            var request = context.Request;
            if (string.IsNullOrWhiteSpace(request.TargetEntityId))
            {
                return false;
            }

            var sourceId = new EntityId(request.SourceEntityId);
            var targetId = new EntityId(request.TargetEntityId);
            var sourcePortId = PreferredPortId(request, SourceAnchorId);
            var targetPortId = PreferredPortId(request, TargetAnchorId);
            return allowIncompatible
                ? ConnectionPortResolver.TryResolveForFacts(
                    context.World,
                    sourceId,
                    targetId,
                    sourcePortId,
                    targetPortId,
                    out pair)
                : ConnectionPortResolver.TryResolve(
                    context.World,
                    sourceId,
                    targetId,
                    sourcePortId,
                    targetPortId,
                    out pair);
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

        private sealed class 来源对象持有者FactReader : IStructuredFactReader
        {
            public StructuredFactField Field =>
                InteractionStructuredFactFields.来源对象持有者;

            public StructuredValue Read(StructuredRuleContext context)
            {
                var source = new EntityId(context.Request.SourceEntityId);
                var holder = context.World.Relations.FirstOrDefault(value =>
                    value.TypeId == InteractionRelationTypeIds.HeldBy
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
                InteractionStructuredFactFields.来源对象已被操作者拿起;

            public StructuredValue Read(StructuredRuleContext context)
            {
                var source = new EntityId(context.Request.SourceEntityId);
                var actor = new EntityId(context.Request.ActorEntityId);
                return StructuredValue.FromBoolean(
                    context.World.Relations.Any(value =>
                        value.TypeId == InteractionRelationTypeIds.HeldBy
                        && value.Source == source
                        && value.Target == actor));
            }
        }

        private sealed class HeldByActorFactReader : IStructuredFactReader
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
                        value.TypeId == InteractionRelationTypeIds.HeldBy
                        && value.Source == entity
                        && value.Target == actor));
            }
        }

        private sealed class PortOccupancyFactReader : IStructuredFactReader
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
                if (!TryResolve(context, true, out var ports))
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
                if (!TryResolve(context, true, out var ports))
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

        private sealed class 连接标签相匹配FactReader : IStructuredFactReader
        {
            public StructuredFactField Field =>
                InteractionStructuredFactFields.连接标签相匹配;

            public StructuredValue Read(StructuredRuleContext context)
            {
                return StructuredValue.FromBoolean(
                    TryResolve(context, false, out _));
            }
        }
    }
}
