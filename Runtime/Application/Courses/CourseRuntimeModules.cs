using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Domain;
using VirtualLab.Domain.Processes;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 内置模块的稳定标识集中定义在此处，课程和模块组合入口不再散落字符串。
    /// </summary>
    public static class CourseModuleIds
    {
        public const string Core = "内核";
    }

    /// <summary>
    /// 模块对另一个模块的最低版本要求。版本只用于安装期检查，不进入协议标识。
    /// </summary>
    public sealed class CourseModuleDependency
    {
        public CourseModuleDependency(string moduleId, Version minimumVersion)
        {
            ModuleId = CourseContractGuard.Required(moduleId, "依赖模块标识");
            MinimumVersion = minimumVersion
                ?? throw new ArgumentNullException(nameof(minimumVersion));
        }

        public string ModuleId { get; }

        public Version MinimumVersion { get; }
    }

    /// <summary>
    /// 模块的稳定身份、当前包版本及显式依赖。
    /// </summary>
    public sealed class CourseModuleManifest
    {
        public CourseModuleManifest(
            string moduleId,
            string displayName,
            Version version,
            IEnumerable<CourseModuleDependency> dependencies = null)
        {
            ModuleId = CourseContractGuard.Required(moduleId, "模块标识");
            DisplayName = CourseContractGuard.Required(displayName, "模块显示名称");
            Version = version ?? throw new ArgumentNullException(nameof(version));

            var copy = (dependencies ?? Array.Empty<CourseModuleDependency>())
                .ToArray();
            if (copy.Any(value => value == null))
            {
                throw new ArgumentException("模块依赖不能包含空项。", nameof(dependencies));
            }

            if (copy.Any(value => string.Equals(
                    value.ModuleId,
                    ModuleId,
                    StringComparison.Ordinal)))
            {
                throw new ArgumentException("模块不能依赖自身。", nameof(dependencies));
            }

            var duplicate = copy
                .GroupBy(value => value.ModuleId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new ArgumentException(
                    $"模块依赖“{duplicate.Key}”重复。",
                    nameof(dependencies));
            }

            Dependencies = new ReadOnlyCollection<CourseModuleDependency>(copy);
        }

        public string ModuleId { get; }

        public string DisplayName { get; }

        public Version Version { get; }

        public IReadOnlyList<CourseModuleDependency> Dependencies { get; }
    }

    /// <summary>
    /// 运行时模块必须显式提供清单和注册入口，禁止通过程序集扫描决定课程行为。
    /// </summary>
    public interface ICourseRuntimeModule
    {
        CourseModuleManifest Manifest { get; }

        void Register(CourseModuleRegistrationContext context);
    }

    /// <summary>
    /// 单个模块的受限注册入口。注册内容在全部模块完成后统一校验并冻结。
    /// </summary>
    public sealed class CourseModuleRegistrationContext
    {
        private readonly CourseRuntimeModuleScopeBuilder _builder;

        internal CourseModuleRegistrationContext(
            string moduleId,
            CourseRuntimeModuleScopeBuilder builder)
        {
            ModuleId = moduleId;
            _builder = builder;
        }

        public string ModuleId { get; }

        public void RegisterFactReader(IStructuredFactReader reader)
        {
            _builder.RegisterFactReader(ModuleId, reader);
        }

        public void RegisterRelationSchema(RelationSchema schema)
        {
            _builder.RegisterRelationSchema(ModuleId, schema);
        }

        public void RegisterCapabilityStateCodec(
            ICourseCapabilityStateCodec codec)
        {
            _builder.RegisterCapabilityStateCodec(ModuleId, codec);
        }

        public void RegisterStateOperations(
            string registrationId,
            Action<ConfiguredStateOperationRegistry> registration)
        {
            _builder.RegisterStateOperations(
                ModuleId,
                registrationId,
                registration);
        }

        public void RegisterEventProjector(
            string registrationId,
            ICourseEventProjector projector)
        {
            _builder.RegisterEventProjector(
                ModuleId,
                registrationId,
                projector);
        }

        public void RegisterProcessAdvancer(
            string registrationId,
            Func<ExperimentWorld, ICourseProcessAdvancer> factory)
        {
            _builder.RegisterProcessAdvancer(
                ModuleId,
                registrationId,
                factory);
        }
    }

    /// <summary>
    /// 已完成依赖解析和冲突校验的不可变模块作用域。
    /// 每个课程编译或运行会话应持有自己的作用域。
    /// </summary>
    public sealed class CourseRuntimeModuleScope
    {
        private readonly IReadOnlyList<
            Action<ConfiguredStateOperationRegistry>> _stateRegistrations;
        private readonly IReadOnlyList<
            Func<ExperimentWorld, ICourseProcessAdvancer>>
            _processFactories;

        internal CourseRuntimeModuleScope(
            IEnumerable<CourseModuleManifest> manifests,
            IEnumerable<RelationSchema> relationSchemas,
            IEnumerable<IStructuredFactReader> factReaders,
            IEnumerable<ICourseCapabilityStateCodec> capabilityStateCodecs,
            IEnumerable<Action<ConfiguredStateOperationRegistry>> stateRegistrations,
            IEnumerable<ICourseEventProjector> eventProjectors,
            IEnumerable<Func<ExperimentWorld, ICourseProcessAdvancer>>
                processFactories)
        {
            Manifests = new ReadOnlyCollection<CourseModuleManifest>(
                manifests.ToArray());
            RelationSchemas = new ReadOnlyCollection<RelationSchema>(
                relationSchemas.ToArray());
            FactReaders = new ReadOnlyCollection<IStructuredFactReader>(
                factReaders.ToArray());
            CapabilityStateCodecs = new CourseCapabilityStateCodecRegistry(
                capabilityStateCodecs);
            EventProjectors = new ReadOnlyCollection<ICourseEventProjector>(
                eventProjectors.ToArray());
            _stateRegistrations = new ReadOnlyCollection<
                Action<ConfiguredStateOperationRegistry>>(
                stateRegistrations.ToArray());
            _processFactories = new ReadOnlyCollection<
                Func<ExperimentWorld, ICourseProcessAdvancer>>(
                processFactories.ToArray());
        }

        public IReadOnlyList<CourseModuleManifest> Manifests { get; }

        public IReadOnlyList<RelationSchema> RelationSchemas { get; }

        public IReadOnlyList<IStructuredFactReader> FactReaders { get; }

        public CourseCapabilityStateCodecRegistry CapabilityStateCodecs
        {
            get;
        }

        public IReadOnlyList<ICourseEventProjector> EventProjectors { get; }

        public static CourseRuntimeModuleScope Create(
            params ICourseRuntimeModule[] modules)
        {
            return CourseRuntimeModuleScopeBuilder.Build(modules);
        }

        public ConfiguredStateOperationRegistry CreateStateOperationRegistry()
        {
            var registry = new ConfiguredStateOperationRegistry(false);
            foreach (var registration in _stateRegistrations)
            {
                registration(registry);
            }

            registry.Freeze();
            return registry;
        }

        /// <summary>
        /// 把模块已冻结的关系模式安装到世界，并冻结关系模式与世界状态类型注册表。
        /// 世界工厂可以提前安装同一关系模式和模块状态以建立初始世界。
        /// </summary>
        public void PrepareWorld(ExperimentWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.RelationSchemasFrozen)
            {
                world.RegisterRelationSchemas(RelationSchemas);
                world.FreezeRelationSchemas();
            }
            else
            {
                foreach (var schema in RelationSchemas)
                {
                    if (!world.RequireRelationSchema(schema.TypeId).Equals(schema))
                    {
                        throw new InvalidOperationException(
                            $"世界中的关系模式“{schema.TypeId}”与模块注册不一致。");
                    }
                }
            }

            if (!world.WorldStateTypesFrozen)
            {
                world.FreezeWorldStateTypes();
            }
        }

        public ICourseProcessAdvancer CreateProcessAdvancer(
            ExperimentWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var advancers = _processFactories
                .Select(factory => factory(world)
                    ?? throw new InvalidOperationException(
                        "过程推进器工厂返回了空值。"))
                .ToArray();
            return advancers.Length == 0
                ? null
                : new CompositeCourseProcessAdvancer(advancers);
        }

        public void ValidateRequiredModules(IEnumerable<string> requiredModuleIds)
        {
            var required = CourseContractGuard.CopyStrings(
                requiredModuleIds,
                "课程所需模块");
            if (required.Count == 0)
            {
                throw new InvalidOperationException("课程没有声明所需模块。");
            }

            var installed = new HashSet<string>(
                Manifests.Select(value => value.ModuleId),
                StringComparer.Ordinal);
            var missing = required
                .Where(value => !installed.Contains(value))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"课程缺少运行时模块：{string.Join("、", missing)}。");
            }
        }

        private sealed class CompositeCourseProcessAdvancer :
            ICourseProcessAdvancer
        {
            private readonly IReadOnlyList<ICourseProcessAdvancer> _advancers;

            public CompositeCourseProcessAdvancer(
                IEnumerable<ICourseProcessAdvancer> advancers)
            {
                _advancers = new ReadOnlyCollection<ICourseProcessAdvancer>(
                    advancers.ToArray());
            }

            public void AdvanceProcesses(
                double elapsedSeconds,
                SimulationTick tick,
                IProcessEventCollector events)
            {
                foreach (var advancer in _advancers)
                {
                    advancer.AdvanceProcesses(elapsedSeconds, tick, events);
                }
            }
        }
    }

    internal sealed class CourseRuntimeModuleScopeBuilder
    {
        private readonly Dictionary<StructuredFactField, OwnedFactReader>
            _factReaders = new Dictionary<StructuredFactField, OwnedFactReader>();
        private readonly Dictionary<RelationTypeId, OwnedRelationSchema>
            _relationSchemas = new Dictionary<RelationTypeId, OwnedRelationSchema>();
        private readonly Dictionary<string, OwnedCapabilityStateCodec>
            _capabilityStateCodecs = new Dictionary<
                string,
                OwnedCapabilityStateCodec>(StringComparer.Ordinal);
        private readonly Dictionary<string, OwnedStateRegistration>
            _stateRegistrations = new Dictionary<string, OwnedStateRegistration>(
                StringComparer.Ordinal);
        private readonly Dictionary<string, OwnedEventProjector>
            _eventProjectors = new Dictionary<string, OwnedEventProjector>(
                StringComparer.Ordinal);
        private readonly Dictionary<string, OwnedProcessAdvancerFactory>
            _processFactories = new Dictionary<
                string,
                OwnedProcessAdvancerFactory>(
                StringComparer.Ordinal);
        private bool _isFrozen;

        public static CourseRuntimeModuleScope Build(
            IEnumerable<ICourseRuntimeModule> modules)
        {
            if (modules == null)
            {
                throw new ArgumentNullException(nameof(modules));
            }

            var moduleList = modules.ToArray();
            if (moduleList.Any(value => value == null || value.Manifest == null))
            {
                throw new ArgumentException(
                    "模块集合及模块清单不能包含空项。",
                    nameof(modules));
            }

            var byId = new Dictionary<string, ICourseRuntimeModule>(
                StringComparer.Ordinal);
            foreach (var module in moduleList)
            {
                if (!byId.TryAdd(module.Manifest.ModuleId, module))
                {
                    throw new ArgumentException(
                        $"模块“{module.Manifest.ModuleId}”重复。",
                        nameof(modules));
                }
            }

            var ordered = ResolveOrder(byId);
            var builder = new CourseRuntimeModuleScopeBuilder();
            foreach (var module in ordered)
            {
                module.Register(new CourseModuleRegistrationContext(
                    module.Manifest.ModuleId,
                    builder));
            }

            // 用独立注册表预演一次，确保操作 ID 冲突在创建课程作用域时就失败。
            var validationRegistry = new ConfiguredStateOperationRegistry(false);
            foreach (var registration in builder._stateRegistrations
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => value.Value))
            {
                registration.Registration(validationRegistry);
            }

            validationRegistry.Freeze();
            builder._isFrozen = true;
            return new CourseRuntimeModuleScope(
                ordered.Select(value => value.Manifest),
                builder._relationSchemas
                    .OrderBy(value => value.Key.Value, StringComparer.Ordinal)
                    .Select(value => value.Value.Schema),
                builder._factReaders
                    .OrderBy(value => value.Key.Id, StringComparer.Ordinal)
                    .Select(value => value.Value.Reader),
                builder._capabilityStateCodecs
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => value.Value.Codec),
                builder._stateRegistrations
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => value.Value.Registration),
                builder._eventProjectors
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => value.Value.Projector),
                builder._processFactories
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => value.Value.Factory));
        }

        public void RegisterFactReader(
            string moduleId,
            IStructuredFactReader reader)
        {
            EnsureMutable();
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (!_factReaders.TryAdd(
                    reader.Field,
                    new OwnedFactReader(moduleId, reader)))
            {
                var owner = _factReaders[reader.Field].ModuleId;
                throw new InvalidOperationException(
                    $"事实“{reader.Field}”已由模块“{owner}”注册，"
                    + $"模块“{moduleId}”不能重复注册。");
            }
        }

        public void RegisterRelationSchema(string moduleId, RelationSchema schema)
        {
            EnsureMutable();
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (!_relationSchemas.TryAdd(
                    schema.TypeId,
                    new OwnedRelationSchema(moduleId, schema)))
            {
                var owner = _relationSchemas[schema.TypeId].ModuleId;
                throw new InvalidOperationException(
                    $"关系模式“{schema.TypeId}”已由模块“{owner}”注册，"
                    + $"模块“{moduleId}”不能重复注册。");
            }
        }

        public void RegisterCapabilityStateCodec(
            string moduleId,
            ICourseCapabilityStateCodec codec)
        {
            EnsureMutable();
            if (codec == null)
            {
                throw new ArgumentNullException(nameof(codec));
            }

            var capabilityId = CourseContractGuard.Required(
                codec.CapabilityId,
                "能力状态编解码器的能力标识");
            if (!_capabilityStateCodecs.TryAdd(
                    capabilityId,
                    new OwnedCapabilityStateCodec(moduleId, codec)))
            {
                var owner = _capabilityStateCodecs[capabilityId].ModuleId;
                throw new InvalidOperationException(
                    $"能力“{capabilityId}”的状态编解码器已由模块“{owner}”注册，"
                    + $"模块“{moduleId}”不能重复注册。");
            }
        }

        public void RegisterStateOperations(
            string moduleId,
            string registrationId,
            Action<ConfiguredStateOperationRegistry> registration)
        {
            EnsureMutable();
            RegisterOwned(
                _stateRegistrations,
                moduleId,
                registrationId,
                new OwnedStateRegistration(moduleId, registration),
                "状态操作组");
        }

        public void RegisterEventProjector(
            string moduleId,
            string registrationId,
            ICourseEventProjector projector)
        {
            EnsureMutable();
            if (projector == null)
            {
                throw new ArgumentNullException(nameof(projector));
            }

            RegisterOwned(
                _eventProjectors,
                moduleId,
                registrationId,
                new OwnedEventProjector(moduleId, projector),
                "事件投影器");
        }

        public void RegisterProcessAdvancer(
            string moduleId,
            string registrationId,
            Func<ExperimentWorld, ICourseProcessAdvancer> factory)
        {
            EnsureMutable();
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            RegisterOwned(
                _processFactories,
                moduleId,
                registrationId,
                new OwnedProcessAdvancerFactory(moduleId, factory),
                "过程推进器");
        }

        private void EnsureMutable()
        {
            if (_isFrozen)
            {
                throw new InvalidOperationException(
                    "模块注册作用域已冻结，不能继续注册协议。");
            }
        }

        private static IReadOnlyList<ICourseRuntimeModule> ResolveOrder(
            IReadOnlyDictionary<string, ICourseRuntimeModule> modules)
        {
            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            var ordered = new List<ICourseRuntimeModule>();
            foreach (var moduleId in modules.Keys.OrderBy(
                value => value,
                StringComparer.Ordinal))
            {
                Visit(moduleId, modules, states, ordered);
            }

            return ordered;
        }

        private static void Visit(
            string moduleId,
            IReadOnlyDictionary<string, ICourseRuntimeModule> modules,
            IDictionary<string, int> states,
            ICollection<ICourseRuntimeModule> ordered)
        {
            if (states.TryGetValue(moduleId, out var state))
            {
                if (state == 1)
                {
                    throw new InvalidOperationException(
                        $"模块依赖存在循环，循环包含“{moduleId}”。");
                }

                return;
            }

            states[moduleId] = 1;
            var module = modules[moduleId];
            foreach (var dependency in module.Manifest.Dependencies.OrderBy(
                value => value.ModuleId,
                StringComparer.Ordinal))
            {
                if (!modules.TryGetValue(dependency.ModuleId, out var installed))
                {
                    throw new InvalidOperationException(
                        $"模块“{moduleId}”缺少依赖“{dependency.ModuleId}”。");
                }

                if (installed.Manifest.Version < dependency.MinimumVersion)
                {
                    throw new InvalidOperationException(
                        $"模块“{moduleId}”要求“{dependency.ModuleId}”至少为"
                        + $" {dependency.MinimumVersion}，当前为"
                        + $" {installed.Manifest.Version}。");
                }

                Visit(dependency.ModuleId, modules, states, ordered);
            }

            states[moduleId] = 2;
            ordered.Add(module);
        }

        private static void RegisterOwned<T>(
            IDictionary<string, T> registrations,
            string moduleId,
            string registrationId,
            T registration,
            string category)
            where T : IOwnedRegistration
        {
            var id = CourseContractGuard.Required(registrationId, $"{category}标识");
            if (registrations.ContainsKey(id))
            {
                throw new InvalidOperationException(
                    $"{category}“{id}”已由模块“{registrations[id].ModuleId}”注册，"
                    + $"模块“{moduleId}”不能重复注册。");
            }

            registrations.Add(id, registration);
        }

        private interface IOwnedRegistration
        {
            string ModuleId { get; }
        }

        private sealed class OwnedFactReader
        {
            public OwnedFactReader(string moduleId, IStructuredFactReader reader)
            {
                ModuleId = moduleId;
                Reader = reader;
            }

            public string ModuleId { get; }

            public IStructuredFactReader Reader { get; }
        }

        private sealed class OwnedRelationSchema
        {
            public OwnedRelationSchema(string moduleId, RelationSchema schema)
            {
                ModuleId = moduleId;
                Schema = schema;
            }

            public string ModuleId { get; }
            public RelationSchema Schema { get; }
        }

        private sealed class OwnedCapabilityStateCodec
        {
            public OwnedCapabilityStateCodec(
                string moduleId,
                ICourseCapabilityStateCodec codec)
            {
                ModuleId = moduleId;
                Codec = codec;
            }

            public string ModuleId { get; }
            public ICourseCapabilityStateCodec Codec { get; }
        }

        private sealed class OwnedStateRegistration : IOwnedRegistration
        {
            public OwnedStateRegistration(
                string moduleId,
                Action<ConfiguredStateOperationRegistry> registration)
            {
                ModuleId = moduleId;
                Registration = registration
                    ?? throw new ArgumentNullException(nameof(registration));
            }

            public string ModuleId { get; }

            public Action<ConfiguredStateOperationRegistry> Registration { get; }
        }

        private sealed class OwnedEventProjector : IOwnedRegistration
        {
            public OwnedEventProjector(
                string moduleId,
                ICourseEventProjector projector)
            {
                ModuleId = moduleId;
                Projector = projector;
            }

            public string ModuleId { get; }

            public ICourseEventProjector Projector { get; }
        }

        private sealed class OwnedProcessAdvancerFactory : IOwnedRegistration
        {
            public OwnedProcessAdvancerFactory(
                string moduleId,
                Func<ExperimentWorld, ICourseProcessAdvancer> factory)
            {
                ModuleId = moduleId;
                Factory = factory;
            }

            public string ModuleId { get; }

            public Func<ExperimentWorld, ICourseProcessAdvancer> Factory
            {
                get;
            }
        }
    }
}
