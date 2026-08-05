using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Application.Events;
using VirtualLab.Domain;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;

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
        public void 不同模块不能重复注册同一事实或状态操作()
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
