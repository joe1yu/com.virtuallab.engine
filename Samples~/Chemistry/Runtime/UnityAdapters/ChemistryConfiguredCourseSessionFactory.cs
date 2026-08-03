using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.Application.Events;
using VirtualLab.Chemistry;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Domain;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.Chemistry.UnityAdapters
{
    /// <summary>
    /// Unity 场景中的化学课程适配器。它只负责装载化学配置与推进过程，
    /// 具体课程规则、实体组合和数值仍由课程资产及化学配置提供。
    /// </summary>
    public sealed class ChemistryConfiguredCourseSessionFactory :
        MonoBehaviour,
        IConfiguredCourseSessionFactory,
        IConfiguredCourseRuntimeFactory,
        IConfiguredCourseAssetConsumer,
        IConfiguredCoursePresentationSignalSource,
        IConfiguredCourseTickDriver
    {
        [SerializeField] private TextAsset courseConfiguration;
        [SerializeField] private bool automaticTicking = true;

        private ChemistryCourseRuntime _runtime;
        private CompiledCourseAsset _courseAsset;

        public ChemistryCourseRuntime Runtime => _runtime;
        public IReadOnlyList<DomainEventEnvelope> EventHistory =>
            _runtime?.Facade.EventHistory
            ?? Array.Empty<DomainEventEnvelope>();
        public bool AutomaticTicking => automaticTicking;
        public event Action<PresentationSignal> PresentationSignalProduced;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntimeAdapter()
        {
            ConfiguredCourseRuntimeCatalog.Register(
                "化学基础",
                owner => owner.GetComponent<
                             ChemistryConfiguredCourseSessionFactory>()
                         ?? owner.AddComponent<
                             ChemistryConfiguredCourseSessionFactory>());
        }

        public void ConfigureCourseAsset(CompiledCourseAsset courseAsset)
        {
            if (_runtime != null)
            {
                throw new InvalidOperationException("化学课程会话已经创建。");
            }

            _courseAsset = courseAsset
                ?? throw new ArgumentNullException(nameof(courseAsset));
        }

        public void Configure(
            TextAsset configuration,
            bool enableAutomaticTicking = true)
        {
            if (_runtime != null)
            {
                throw new InvalidOperationException("化学课程会话已经创建。");
            }

            courseConfiguration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            automaticTicking = enableAutomaticTicking;
        }

        public ConfigDrivenCourseSession CreateSession(
            ExperimentWorld world,
            CompiledCourseDefinition course)
        {
            CreateRuntimeFacade(world, course);
            return _runtime.Session;
        }

        public CourseRuntimeFacade CreateRuntimeFacade(
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

            if (_runtime != null)
            {
                throw new InvalidOperationException(
                    "同一个化学课程工厂不能创建多个会话。");
            }

            var configurationText = courseConfiguration != null
                ? courseConfiguration.text
                : _courseAsset?.RequireTextArtifact(
                    ChemistryConfigurationKeys.Artifacts.RuntimeConfiguration);
            if (string.IsNullOrWhiteSpace(configurationText))
            {
                throw new InvalidOperationException(
                    "课程资产未包含化学运行配置。请重新编译课程。");
            }

            var configuration = new ChemistryConfigurationCodec()
                .DecodeCourseConfiguration(
                    configurationText,
                    course.Entities.Select(value => value.EntityId));
            _runtime = ChemistryCourseRuntime.Create(
                world,
                course,
                configuration);
            return _runtime.Facade;
        }

        public CourseTickResult Advance(double elapsedSeconds)
        {
            if (_runtime == null)
            {
                throw new InvalidOperationException("化学课程会话尚未创建。");
            }

            if (double.IsNaN(elapsedSeconds)
                || double.IsInfinity(elapsedSeconds)
                || elapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds),
                    "推进秒数必须是有限的非负数。");
            }

            var tick = _runtime.Facade.Tick(elapsedSeconds);
            PublishProcessEvents(tick.Events);
            return tick;
        }

        private void FixedUpdate()
        {
            if (automaticTicking && _runtime != null)
            {
                Advance(Time.fixedDeltaTime);
            }
        }

        private void PublishProcessEvents(
            IEnumerable<DomainEventEnvelope> events)
        {
            foreach (var envelope in events)
            {
                var state = _runtime.Facade.EventStates[
                    checked((int)envelope.Sequence - 1)];
                if (TryCreatePresentationSignal(
                        state,
                        out var signal))
                {
                    PresentationSignalProduced?.Invoke(signal);
                }
            }
        }

        private static bool TryCreatePresentationSignal(
            CourseEventState domainEvent,
            out PresentationSignal signal)
        {
            signal = null;
            if (!TrySubjectEntityId(domainEvent.Payload, out var subjectEntityId))
            {
                return false;
            }

            signal = new PresentationSignal(
                domainEvent.EventType,
                PresentationTriggerKind.DomainEvent,
                subjectEntityId,
                null,
                null,
                domainEvent.Payload.Select(value =>
                    new KeyValuePair<string, PresentationValue>(
                        value.Key,
                        ConvertValue(value.Value))));
            return true;
        }

        private static bool TrySubjectEntityId(
            IReadOnlyDictionary<string, StructuredValue> payload,
            out string subjectEntityId)
        {
            foreach (var key in new[]
                     {
                         CourseConfigurationKeys.EventPayload.TargetEntityId,
                         ChemistryConfigurationKeys.EventPayload
                             .LocationEntityId,
                         CourseConfigurationKeys.EventPayload.SourceEntityId
                     })
            {
                if (payload.TryGetValue(key, out var value)
                    && value.Kind == StructuredValueKind.Text)
                {
                    subjectEntityId = value.Text;
                    return true;
                }
            }

            subjectEntityId = null;
            return false;
        }

        private static PresentationValue ConvertValue(StructuredValue value)
        {
            return value.Kind switch
            {
                StructuredValueKind.Null => PresentationValue.Null(),
                StructuredValueKind.Boolean =>
                    PresentationValue.FromBoolean(value.Boolean),
                StructuredValueKind.Number =>
                    PresentationValue.FromNumber(value.Number),
                StructuredValueKind.Text =>
                    PresentationValue.FromText(value.Text),
                StructuredValueKind.TextList =>
                    PresentationValue.FromText(
                        string.Join(",", value.TextList)),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}
