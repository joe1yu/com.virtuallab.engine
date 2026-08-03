using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.UnityAdapters.Courses
{
    public interface IConfiguredCourseSessionFactory
    {
        ConfigDrivenCourseSession CreateSession(
            ExperimentWorld world,
            CompiledCourseDefinition course);
    }

    public interface IConfiguredCourseRuntimeFactory
    {
        CourseRuntimeFacade CreateRuntimeFacade(
            ExperimentWorld world,
            CompiledCourseDefinition course);
    }

    /// <summary>
    /// 学科适配器通过该接口读取课程资产中的具名编译产物，不再要求场景逐个
    /// 绑定 TextAsset。资产内容仍由编辑期学科编译器生成。
    /// </summary>
    public interface IConfiguredCourseAssetConsumer
    {
        void ConfigureCourseAsset(CompiledCourseAsset courseAsset);
    }

    /// <summary>
    /// 学科包到 Unity 运行时适配器的注册表。课程只声明学科配方包，启动器
    /// 根据声明补齐组件，不按课程 ID 或具体实验名称分支。
    /// </summary>
    public static class ConfiguredCourseRuntimeCatalog
    {
        private static readonly Dictionary<string, Func<GameObject, MonoBehaviour>>
            Installers = new Dictionary<string, Func<GameObject, MonoBehaviour>>(
                StringComparer.Ordinal);

        public static void Register(
            string disciplinePackageId,
            Func<GameObject, MonoBehaviour> installer)
        {
            if (string.IsNullOrWhiteSpace(disciplinePackageId))
            {
                throw new ArgumentException(
                    "学科配方包 ID 不能为空。",
                    nameof(disciplinePackageId));
            }

            var key = disciplinePackageId.Trim();
            Installers[key] = installer
                ?? throw new ArgumentNullException(nameof(installer));
        }

        internal static void EnsureInstalled(
            GameObject owner,
            IEnumerable<string> disciplinePackageIds)
        {
            foreach (var packageId in disciplinePackageIds
                         ?? Array.Empty<string>())
            {
                if (Installers.TryGetValue(packageId, out var installer)
                    && installer(owner) == null)
                {
                    throw new InvalidOperationException(
                        $"学科配方包“{packageId}”未能安装运行时适配器。");
                }
            }
        }
    }

    /// <summary>
    /// 学科适配器通过该端口把持续过程产生的领域事实投影为通用表现信号。
    /// 内核事件不包含动画概念，具体效果仍由课程表现配置决定。
    /// </summary>
    public interface IConfiguredCoursePresentationSignalSource
    {
        event Action<PresentationSignal> PresentationSignalProduced;
    }

    public interface IConfiguredCourseTickDriver
    {
        CourseTickResult Advance(double elapsedSeconds);
    }

    public sealed class CourseDispatchResult
    {
        public CourseDispatchResult(
            CommandResult outcome,
            IEnumerable<PresentationEffectCommand> presentationCommands)
        {
            Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
            PresentationCommands =
                new ReadOnlyCollection<PresentationEffectCommand>(
                    presentationCommands.ToArray());
        }

        public CommandResult Outcome { get; }
        public IReadOnlyList<PresentationEffectCommand>
            PresentationCommands { get; }
    }

    public sealed class CourseAvailabilityResult
    {
        public CourseAvailabilityResult(
            ActionAvailability availability,
            IEnumerable<PresentationEffectCommand> presentationCommands)
        {
            Availability = availability ??
                throw new ArgumentNullException(nameof(availability));
            PresentationCommands =
                new ReadOnlyCollection<PresentationEffectCommand>(
                    presentationCommands?.ToArray() ??
                    throw new ArgumentNullException(
                        nameof(presentationCommands)));
        }

        public ActionAvailability Availability { get; }

        public IReadOnlyList<PresentationEffectCommand>
            PresentationCommands { get; }
    }

    /// <summary>
    /// 通用课程启动器。只依赖编译资产和可插拔会话工厂，不按课程 ID 分支。
    /// </summary>
    public sealed class ConfigDrivenCourseBootstrap : MonoBehaviour
    {
        [SerializeField] private CompiledCourseAsset course;
        [SerializeField] private bool uiSimulationMode;

        private CourseRuntimeFacade _runtime;
        private CompiledCourseDefinition _domain;
        private CoursePresentationCoordinator _presentationCoordinator;
        private IConfiguredCoursePresentationSignalSource _signalSource;

        public CompiledCourseAsset Course => course;
        public CompiledCourseDefinition Domain => _domain;
        public CourseRuntimeFacade Runtime => _runtime;
        public CourseSceneAssembly SceneAssembly { get; private set; }
        public bool IsInitialized =>
            _runtime != null && _presentationCoordinator != null;

        public void ConfigureCourse(CompiledCourseAsset value)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("课程已经初始化。");
            }

            course = value ?? throw new ArgumentNullException(nameof(value));
        }

        public void ConfigureUiSimulationMode(bool enabled = true)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("课程已经初始化。");
            }

            uiSimulationMode = enabled;
        }

        public void ConfigureRuntime(
            CourseRuntimeFacade runtime,
            PresentationReactionEngine reactions,
            UnityPresentationDispatcher presenter)
        {
            _runtime = runtime ??
                throw new ArgumentNullException(nameof(runtime));
            _presentationCoordinator =
                new CoursePresentationCoordinator(
                    _runtime,
                    Array.Empty<CoursePresentationStateDefinition>(),
                    reactions ??
                    throw new ArgumentNullException(nameof(reactions)),
                    presenter ??
                    throw new ArgumentNullException(nameof(presenter)));
            _presentationCoordinator.InitializeOrRestore();
        }

        public void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            if (course == null)
            {
                throw new InvalidOperationException("未配置编译课程资产。");
            }

            var domain = CourseAssetDecoder.DecodeDomain(course);
            if (!string.Equals(
                    course.CourseId,
                    domain.CourseId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "课程资产 ID 与领域配置 ID 不一致。");
            }

            _domain = domain;

            var presentation =
                CourseAssetDecoder.DecodePresentation(course);
            SceneAssembly = uiSimulationMode
                ? new CourseSceneAssembly(
                    null,
                    new CourseEntityViewRegistry())
                : new CourseSceneAssembler().Assemble(
                    course,
                    domain,
                    transform);
            var world = CourseWorldFactory.Create(domain);
            ConfiguredCourseRuntimeCatalog.EnsureInstalled(
                gameObject,
                domain.DisciplinePackageIds);
            foreach (var consumer in GetComponents<MonoBehaviour>()
                         .OfType<IConfiguredCourseAssetConsumer>())
            {
                consumer.ConfigureCourseAsset(course);
            }

            var runtimeFactory = GetComponents<MonoBehaviour>()
                .OfType<IConfiguredCourseRuntimeFactory>()
                .SingleOrDefault();
            var sessionFactory = GetComponents<MonoBehaviour>()
                .OfType<IConfiguredCourseSessionFactory>()
                .SingleOrDefault();
            _runtime = runtimeFactory != null
                ? runtimeFactory.CreateRuntimeFacade(world, domain)
                : sessionFactory != null
                    ? new CourseRuntimeFacade(
                        sessionFactory.CreateSession(world, domain))
                    : CoreCourseRegistrations.CreateRuntimeFacade(
                        world,
                        domain);
            if (!uiSimulationMode)
            {
                _runtime.ConfigureSpatialStatePort(
                    new UnityCourseSpatialStatePort(
                        SceneAssembly.CourseViews));
            }

            var catalog = BuiltInPresentationEffectCatalog.Create();
            var reactions =
                CoursePresentationAdapter.CreateReactionEngine(
                    presentation,
                    catalog);
            IPresentationCommandDispatcher presenter = uiSimulationMode
                ? new TextPresentationDispatcher(
                    FindObjectsOfType<MonoBehaviour>(true)
                        .OfType<IPresentationTextSink>()
                        .SingleOrDefault())
                : UnityPresentationDispatcher.CreateDefault(
                    SceneAssembly.CourseViews,
                    new CourseAssetResourceResolver(course),
                    FindObjectsOfType<MonoBehaviour>(true)
                        .OfType<IPresentationMessageSink>()
                        .FirstOrDefault(),
                    FindObjectsOfType<MonoBehaviour>(true)
                        .OfType<IPresentationHighlightSink>()
                        .FirstOrDefault(),
                    affordances: FindObjectsOfType<MonoBehaviour>(true)
                        .OfType<IInteractionAffordanceSink>()
                        .FirstOrDefault());
            _presentationCoordinator =
                new CoursePresentationCoordinator(
                    _runtime,
                    presentation.States,
                    reactions,
                    presenter);
            _presentationCoordinator.InitializeOrRestore();
            _signalSource = GetComponents<MonoBehaviour>()
                .OfType<IConfiguredCoursePresentationSignalSource>()
                .SingleOrDefault();
            if (_signalSource != null)
            {
                _signalSource.PresentationSignalProduced +=
                    OnPresentationSignalProduced;
            }
        }

        public CourseDispatchResult Dispatch(SemanticActionRequest request)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("课程尚未初始化。");
            }

            var outcome = _runtime.Execute(request);
            _presentationCoordinator.Present(request, outcome);
            return new CourseDispatchResult(
                outcome,
                _presentationCoordinator.LastCommands);
        }

        public CourseAvailabilityResult QueryAvailability(
            SemanticActionRequest request)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("课程尚未初始化。");
            }

            var availability = _runtime.QueryAvailability(request);
            _presentationCoordinator.PresentAvailability(
                request,
                availability);
            return new CourseAvailabilityResult(
                availability,
                _presentationCoordinator.LastCommands);
        }

        private void Start()
        {
            if (!IsInitialized)
            {
                Initialize();
            }
        }

        private void OnDestroy()
        {
            if (_signalSource != null)
            {
                _signalSource.PresentationSignalProduced -=
                    OnPresentationSignalProduced;
            }
        }

        private void OnPresentationSignalProduced(PresentationSignal signal)
        {
            _presentationCoordinator.PresentSignal(signal);
        }

    }
}
