using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Application.Events;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Processes;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Interaction.Courses;
using VirtualLab.Interaction.Relations;
using VirtualLab.Kernel;
using VirtualLab.Spatial.Courses;
using VirtualLab.Teaching.Courses;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseRuntimeModuleTests
    {
        [Test]
        public void 模块按照依赖顺序注册且作用域创建的注册表已经冻结()
        {
            var core = Module("基础", "基础模块", new Version(1, 0, 0));
            var extension = Module(
                "扩展",
                "扩展模块",
                new Version(1, 0, 0),
                new CourseModuleDependency("基础", new Version(1, 0, 0)));

            var scope = CourseRuntimeModuleScope.Create(extension, core);

            Assert.That(
                scope.Manifests.Select(value => value.ModuleId),
                Is.EqualTo(new[] { "基础", "扩展" }));
            Assert.That(scope.CreateStateOperationRegistry().IsFrozen, Is.True);
        }

        [Test]
        public void 缺失依赖版本不足和循环依赖都会在创建作用域时失败()
        {
            var missing = Module(
                "扩展",
                "扩展模块",
                new Version(1, 0, 0),
                new CourseModuleDependency("基础", new Version(1, 0, 0)));
            Assert.Throws<InvalidOperationException>(() =>
                CourseRuntimeModuleScope.Create(missing));

            var oldCore = Module("基础", "基础模块", new Version(1, 0, 0));
            var requiresNewCore = Module(
                "扩展",
                "扩展模块",
                new Version(1, 0, 0),
                new CourseModuleDependency("基础", new Version(2, 0, 0)));
            Assert.Throws<InvalidOperationException>(() =>
                CourseRuntimeModuleScope.Create(oldCore, requiresNewCore));

            var first = Module(
                "甲",
                "甲模块",
                new Version(1, 0, 0),
                new CourseModuleDependency("乙", new Version(1, 0, 0)));
            var second = Module(
                "乙",
                "乙模块",
                new Version(1, 0, 0),
                new CourseModuleDependency("甲", new Version(1, 0, 0)));
            Assert.Throws<InvalidOperationException>(() =>
                CourseRuntimeModuleScope.Create(first, second));
        }

        [Test]
        public void 不同模块不能重复注册同一事实状态操作关系模式或能力编解码器()
        {
            var fact = new StructuredFactField("测试事实");
            var firstFactModule = Module(
                "甲",
                "甲模块",
                new Version(1, 0, 0),
                context => context.RegisterFactReader(new FactReader(fact)));
            var secondFactModule = Module(
                "乙",
                "乙模块",
                new Version(1, 0, 0),
                context => context.RegisterFactReader(new FactReader(fact)));
            Assert.Throws<InvalidOperationException>(() =>
                CourseRuntimeModuleScope.Create(
                    firstFactModule,
                    secondFactModule));

            var firstOperationModule = Module(
                "甲",
                "甲模块",
                new Version(1, 0, 0),
                context => context.RegisterStateOperations(
                    "甲.状态操作",
                    registry => registry.Register(new Operation("测试操作"))));
            var secondOperationModule = Module(
                "乙",
                "乙模块",
                new Version(1, 0, 0),
                context => context.RegisterStateOperations(
                    "乙.状态操作",
                    registry => registry.Register(new Operation("测试操作"))));
            Assert.Throws<ArgumentException>(() =>
                CourseRuntimeModuleScope.Create(
                    firstOperationModule,
                    secondOperationModule));

            var relationSchema = new RelationSchema(
                new RelationTypeId("甲.关系.测试"));
            var firstRelationModule = Module(
                "甲",
                "甲模块",
                new Version(1, 0, 0),
                context => context.RegisterRelationSchema(relationSchema));
            var secondRelationModule = Module(
                "乙",
                "乙模块",
                new Version(1, 0, 0),
                context => context.RegisterRelationSchema(relationSchema));
            Assert.Throws<InvalidOperationException>(() =>
                CourseRuntimeModuleScope.Create(
                    firstRelationModule,
                    secondRelationModule));

            var firstCodecModule = Module(
                "甲",
                "甲模块",
                new Version(1, 0, 0),
                context => context.RegisterCapabilityStateCodec(
                    new StubCapabilityStateCodec("测试能力")));
            var secondCodecModule = Module(
                "乙",
                "乙模块",
                new Version(1, 0, 0),
                context => context.RegisterCapabilityStateCodec(
                    new StubCapabilityStateCodec("测试能力")));
            Assert.Throws<InvalidOperationException>(() =>
                CourseRuntimeModuleScope.Create(
                    firstCodecModule,
                    secondCodecModule));
        }

        [Test]
        public void 已冻结的状态操作注册表拒绝后续修改()
        {
            var registry = new ConfiguredStateOperationRegistry(false);
            registry.Register(new Operation("测试操作"));
            registry.Freeze();

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new Operation("另一个操作")));
        }

        [Test]
        public void 初始关系在所属模块安装关系模式后写入世界()
        {
            var typeId = new RelationTypeId("测试模块.关系.连接");
            var module = Module(
                "测试模块",
                "测试模块",
                new Version(1, 0, 0),
                context => context.RegisterRelationSchema(new RelationSchema(
                    typeId,
                    portPolicy: RelationPortPolicy.可选)));
            var runtime = new CourseRuntimeDefinition(
                CourseRuntimeModuleScope.Create(module),
                Array.Empty<ConfiguredActionDefinition>(),
                Array.Empty<CourseActionAssessmentDefinition>(),
                0);
            var world = new ExperimentWorld();
            world.AddEntity(new ExperimentEntity(new EntityId("来源")));
            world.AddEntity(new ExperimentEntity(new EntityId("目标")));

            runtime.CreateInitialSession(
                world,
                new[]
                {
                    new CourseInitialRelationDefinition(
                        "初始关系.连接",
                        typeId,
                        "来源",
                        "目标",
                        "出口",
                        "入口")
                });

            Assert.That(world.RelationSchemasFrozen, Is.True);
            var relation = world.Relations.Single();
            Assert.That(relation.TypeId, Is.EqualTo(typeId));
            Assert.That(relation.SourcePortId, Is.EqualTo("出口"));
            Assert.That(relation.TargetPortId, Is.EqualTo("入口"));
        }

        [Test]
        public void 通用事实字段值对象不再持有具体模块协议()
        {
            const System.Reflection.BindingFlags publicStatic =
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static;
            Assert.That(
                typeof(StructuredFactField).GetFields(publicStatic),
                Is.Empty);
            Assert.That(
                typeof(StructuredFactField).GetProperties(publicStatic),
                Is.Empty);

            var fields = new[]
                {
                    CoreStructuredFactFields.All,
                    InteractionStructuredFactFields.All,
                    SpatialStructuredFactFields.All,
                    TeachingStructuredFactFields.All
                }
                .SelectMany(value => value)
                .ToArray();
            Assert.That(
                fields.Select(value => value.Id).Distinct().Count(),
                Is.EqualTo(fields.Length),
                "不同模块不能拥有相同的事实字段标识。");
        }

        [Test]
        public void 交互空间与教学事实只由各自显式模块安装()
        {
            var core = CoreCourseRegistrations.CreateModuleScope();
            Assert.That(core.RelationSchemas, Is.Empty);
            Assert.That(
                core.FactReaders.Select(value => value.Field)
                    .Intersect(InteractionStructuredFactFields.All),
                Is.Empty);
            Assert.That(
                core.FactReaders.Select(value => value.Field)
                    .Intersect(TeachingStructuredFactFields.All),
                Is.Empty);
            Assert.That(
                core.FactReaders.Select(value => value.Field)
                    .Intersect(SpatialStructuredFactFields.All),
                Is.Empty);

            var spatial = SpatialCourseRegistrations.CreateModuleScope();
            Assert.That(
                spatial.Manifests.Select(value => value.ModuleId),
                Is.EqualTo(new[]
                {
                    CourseModuleIds.Core,
                    SpatialModuleIds.Spatial
                }));
            Assert.That(
                spatial.FactReaders.Select(value => value.Field),
                Is.SupersetOf(SpatialStructuredFactFields.All));

            var teaching = TeachingCourseRegistrations.CreateModuleScope();
            Assert.That(
                teaching.Manifests.Select(value => value.ModuleId),
                Is.EqualTo(new[]
                {
                    CourseModuleIds.Core,
                    TeachingProtocolIds.Module
                }));
            Assert.That(
                teaching.FactReaders.Select(value => value.Field),
                Is.SupersetOf(TeachingStructuredFactFields.All));
            var interaction = InteractionCourseRegistrations
                .CreateModuleScope();
            Assert.That(
                interaction.Manifests.Select(value => value.ModuleId),
                Is.EqualTo(new[]
                {
                    CourseModuleIds.Core,
                    InteractionModuleIds.Interaction
                }));
            Assert.That(
                interaction.RelationSchemas,
                Is.EquivalentTo(InteractionRelationSchemas.All));
            Assert.That(
                interaction.FactReaders.Select(value => value.Field),
                Is.SupersetOf(InteractionStructuredFactFields.All));
            Assert.That(
                interaction.CapabilityStateCodecs.CapabilityIds,
                Is.EquivalentTo(new[]
                {
                    InteractionCapabilityIds.Grabbable,
                    InteractionCapabilityIds.Container,
                    InteractionCapabilityIds.Connector,
                    InteractionCapabilityIds.Observable,
                    InteractionCapabilityIds.Clampable,
                    InteractionCapabilityIds.Coverable,
                    InteractionCapabilityIds.Breakable
                }));
        }

        [Test]
        public void 教学模块统一负责命名状态的操作事实和存档()
        {
            var scope = TeachingCourseRegistrations.CreateModuleScope();
            var world = new ExperimentWorld();
            world.AddEntity(new ExperimentEntity(new EntityId("器材")));
            scope.PrepareWorld(world);
            var request = new SemanticActionRequest(
                "命令.准备器材",
                "准备",
                "学生",
                "器材",
                null,
                Array.Empty<KeyValuePair<string, StructuredValue>>());
            var mutation = new ConfiguredMutationDefinition(
                "变化.器材已准备",
                TeachingConfiguredStateOperationIds.AddState,
                new Dictionary<string, StructuredValue>
                {
                    [TeachingConfigurationKeys.EntityId] =
                        StructuredValue.FromText("器材"),
                    [TeachingConfigurationKeys.StateId] =
                        StructuredValue.FromText("已准备")
                });

            scope.CreateStateOperationRegistry().ApplyAtomically(
                request,
                world,
                new[] { mutation });

            var reader = scope.FactReaders.Single(value =>
                value.Field == TeachingStructuredFactFields.来源对象教学状态);
            Assert.That(
                reader.Read(new StructuredRuleContext(request, world)).TextList,
                Is.EqualTo(new[] { "已准备" }));

            var snapshot = scope.WorldStateCodecs.Capture(world);
            var restored = new ExperimentWorld();
            restored.AddEntity(new ExperimentEntity(new EntityId("器材")));
            scope.PrepareWorld(restored);
            scope.WorldStateCodecs.Restore(restored, snapshot);

            Assert.That(
                restored.RequireTeachingStates().StatesOf("器材"),
                Is.EqualTo(new[] { "已准备" }));
        }

        [Test]
        public void 课程所需模块必须全部安装()
        {
            var scope = CourseRuntimeModuleScope.Create(
                Module("基础", "基础模块", new Version(1, 0, 0)));

            Assert.DoesNotThrow(() =>
                scope.ValidateRequiredModules(new[] { "基础" }));
            var exception = Assert.Throws<InvalidOperationException>(() =>
                scope.ValidateRequiredModules(new[] { "基础", "化学" }));
            Assert.That(exception.Message, Does.Contain("化学"));
        }

        [Test]
        public void 多个模块的持续过程按照依赖顺序推进()
        {
            var calls = new List<string>();
            var core = Module(
                "基础",
                "基础模块",
                new Version(1, 0, 0),
                context => context.RegisterProcessAdvancer(
                    "基础.持续过程",
                    _ => new RecordingProcessAdvancer(calls, "基础")));
            var extension = new TestModule(
                new CourseModuleManifest(
                    "扩展",
                    "扩展模块",
                    new Version(1, 0, 0),
                    new[]
                    {
                        new CourseModuleDependency(
                            "基础",
                            new Version(1, 0, 0))
                    }),
                context => context.RegisterProcessAdvancer(
                    "扩展.持续过程",
                    _ => new RecordingProcessAdvancer(calls, "扩展")));

            var advancer = CourseRuntimeModuleScope
                .Create(extension, core)
                .CreateProcessAdvancer(new ExperimentWorld());
            advancer.AdvanceProcesses(
                0.1d,
                new SimulationTick(1),
                new EventCollector());

            Assert.That(calls, Is.EqualTo(new[] { "基础", "扩展" }));
        }

        private static ICourseRuntimeModule Module(
            string id,
            string displayName,
            Version version,
            params CourseModuleDependency[] dependencies)
        {
            return new TestModule(
                new CourseModuleManifest(id, displayName, version, dependencies),
                _ => { });
        }

        private static ICourseRuntimeModule Module(
            string id,
            string displayName,
            Version version,
            Action<CourseModuleRegistrationContext> registration)
        {
            return new TestModule(
                new CourseModuleManifest(id, displayName, version),
                registration);
        }

        private sealed class TestModule : ICourseRuntimeModule
        {
            private readonly Action<CourseModuleRegistrationContext> _registration;

            public TestModule(
                CourseModuleManifest manifest,
                Action<CourseModuleRegistrationContext> registration)
            {
                Manifest = manifest;
                _registration = registration;
            }

            public CourseModuleManifest Manifest { get; }

            public void Register(CourseModuleRegistrationContext context)
            {
                _registration(context);
            }
        }

        private sealed class FactReader : IStructuredFactReader
        {
            public FactReader(StructuredFactField field)
            {
                Field = field;
            }

            public StructuredFactField Field { get; }

            public StructuredValue Read(StructuredRuleContext context)
            {
                return StructuredValue.FromBoolean(true);
            }
        }

        private sealed class Operation : IConfiguredStateOperation
        {
            public Operation(string operationId)
            {
                OperationId = operationId;
            }

            public string OperationId { get; }

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
            }
        }

        private sealed class StubCapabilityStateCodec :
            ICourseCapabilityStateCodec
        {
            public StubCapabilityStateCodec(string capabilityId)
            {
                CapabilityId = capabilityId;
            }

            public string CapabilityId { get; }

            public CourseCapabilityState Capture(ICapability capability) =>
                new CourseCapabilityState(CapabilityId);

            public ICapability Restore(CourseCapabilityState state) =>
                new ConfiguredCapability(CapabilityId);
        }

        private sealed class RecordingProcessAdvancer :
            ICourseProcessAdvancer
        {
            private readonly ICollection<string> _calls;
            private readonly string _moduleId;

            public RecordingProcessAdvancer(
                ICollection<string> calls,
                string moduleId)
            {
                _calls = calls;
                _moduleId = moduleId;
            }

            public void AdvanceProcesses(
                double elapsedSeconds,
                SimulationTick tick,
                IProcessEventCollector events)
            {
                _calls.Add(_moduleId);
            }
        }
    }
}
