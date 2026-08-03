using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.UnityAdapters.Presentation
{
    public interface IPresentationResourceResolver
    {
        bool TryResolve(string resourceId, out UnityEngine.Object resource);
    }

    public interface IPresentationMessageSink
    {
        void ShowMessage(string text, double durationSeconds);
    }

    public interface IPresentationHighlightSink
    {
        void Highlight(
            string entityId,
            Color color,
            double durationSeconds);
    }

    public interface IInteractionAffordanceSink
    {
        void ApplyActionAvailability(
            string sourceEntityId,
            string targetEntityId,
            bool canExecute,
            string kind,
            string rejectionCode,
            string messageId);
    }

    public interface IPresentationEffectExecutor
    {
        string EffectId { get; }

        void Execute(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context);

        void Stop(
            PreparedPresentationEffect effect,
            PresentationExecutionContext context);
    }

    public sealed class PresentationExecutionContext
    {
        public PresentationExecutionContext(
            CourseEntityViewRegistry views,
            IPresentationResourceResolver resources,
            IPresentationMessageSink messages,
            IPresentationHighlightSink highlights,
            IInteractionAffordanceSink affordances = null)
        {
            CourseViews = views ??
                throw new ArgumentNullException(nameof(views));
            Resources = resources;
            Messages = messages;
            Highlights = highlights;
            Affordances = affordances;
            GlobalReceivers =
                PresentationGlobalReceiverRegistry.CreateDefault(
                    messages,
                    highlights,
                    affordances);
        }

        public CourseEntityViewRegistry CourseViews { get; }

        public IPresentationResourceResolver Resources { get; }

        public IPresentationMessageSink Messages { get; }

        public IPresentationHighlightSink Highlights { get; }

        public IInteractionAffordanceSink Affordances { get; }

        public PresentationGlobalReceiverRegistry GlobalReceivers { get; }

        public object ResolveGlobalReceiver(
            PresentationEffectDescriptor descriptor)
        {
            return GlobalReceivers.Resolve(descriptor);
        }
    }

    /// <summary>
    /// 先准备并验证所有 Unity 依赖，再进入通道仲裁和执行。
    /// 表现技术错误只被记录或抛出，不会修改科学状态。
    /// </summary>
    public sealed class UnityPresentationDispatcher :
        IPresentationCommandDispatcher
    {
        private readonly PresentationExecutionContext _context;
        private readonly PresentationEffectPreparer _preparer;
        private readonly PresentationChannelArbiter _arbiter;
        private readonly PresentationFailurePolicy _failurePolicy;
        private readonly Dictionary<string, PreparedPresentationEffect>
            _preparedByCommandId =
                new Dictionary<string, PreparedPresentationEffect>(
                    StringComparer.Ordinal);
        private readonly List<PresentationDispatchResult> _results =
            new List<PresentationDispatchResult>();

        public UnityPresentationDispatcher(
            PresentationExecutionContext context,
            PresentationEffectCatalog catalog,
            IEnumerable<IPresentationEffectExecutor> executors,
            PresentationChannelArbiter arbiter = null,
            PresentationFailurePolicy failurePolicy =
                PresentationFailurePolicy.ThrowAfterRecording)
        {
            _context = context ??
                throw new ArgumentNullException(nameof(context));
            _preparer = new PresentationEffectPreparer(
                context,
                catalog,
                executors);
            _arbiter = arbiter ?? new PresentationChannelArbiter();
            _failurePolicy = failurePolicy;
            RegisteredEffectIds = _preparer.RegisteredEffectIds;
        }

        public IReadOnlyList<string> RegisteredEffectIds { get; }

        public IReadOnlyList<PresentationDispatchResult> Results =>
            new ReadOnlyCollection<PresentationDispatchResult>(
                _results.ToArray());

        public static UnityPresentationDispatcher CreateDefault(
            CourseEntityViewRegistry views,
            IPresentationResourceResolver resources = null,
            IPresentationMessageSink messages = null,
            IPresentationHighlightSink highlights = null,
            PresentationFailurePolicy? failurePolicy = null,
            IInteractionAffordanceSink affordances = null)
        {
            var catalog = BuiltInPresentationEffectCatalog.Create();
            return new UnityPresentationDispatcher(
                new PresentationExecutionContext(
                    views,
                    resources,
                    messages,
                    highlights,
                    affordances),
                catalog,
                catalog.CreateExecutors(),
                null,
                failurePolicy ?? DefaultFailurePolicy());
        }

        public PresentationDispatchResult Dispatch(
            PresentationEffectCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var prepared = _preparer.Prepare(command);
            if (!prepared.Succeeded)
            {
                return HandleFailure(prepared.Failure);
            }

            if (command.Lifecycle == PresentationEffectLifecycle.OneShot)
            {
                try
                {
                    prepared.Value.Executor.Execute(
                        prepared.Value,
                        _context);
                    return Record(PresentationDispatchResult.Success(command));
                }
                catch (Exception exception)
                {
                    return HandleFailure(
                        PresentationDispatchResult.Failure(
                            command,
                            "表现执行.执行器异常",
                            exception.Message));
                }
            }

            var snapshot = _arbiter.Capture(
                command.Target,
                command.Channel);
            try
            {
                _preparedByCommandId[command.CommandId] = prepared.Value;
                var transition =
                    command.Lifecycle ==
                    PresentationEffectLifecycle.WhileActive
                        ? _arbiter.BeginTemporary(command)
                        : _arbiter.ReplaceTemporary(command);
                ApplyTransition(transition);
                return Record(PresentationDispatchResult.Success(command));
            }
            catch (Exception exception)
            {
                _preparedByCommandId.Remove(command.CommandId);
                _arbiter.Remove(command.CommandId);
                TryRestore(snapshot);
                return HandleFailure(
                    PresentationDispatchResult.Failure(
                        command,
                        "表现执行.执行器异常",
                        exception.Message));
            }
        }

        public PresentationDispatchResult End(
            PresentationEffectCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            try
            {
                ApplyTransition(_arbiter.EndTemporary(
                    command.CommandId,
                    command.Target,
                    command.Channel));
                _preparedByCommandId.Remove(command.CommandId);
                return Record(PresentationDispatchResult.Success(command));
            }
            catch (Exception exception)
            {
                return HandleFailure(
                    PresentationDispatchResult.Failure(
                        command,
                        "表现执行.停止异常",
                        exception.Message));
            }
        }

        public IReadOnlyList<PresentationDispatchResult>
            SynchronizeAuthoritativeState(
                IEnumerable<PresentationEffectCommand> commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            var prepared = new List<PreparedPresentationEffect>();
            var results = new List<PresentationDispatchResult>();
            foreach (var command in commands)
            {
                var preparation = _preparer.Prepare(command);
                if (!preparation.Succeeded)
                {
                    results.Add(HandleFailure(preparation.Failure));
                    continue;
                }

                prepared.Add(preparation.Value);
                _preparedByCommandId[command.CommandId] = preparation.Value;
            }

            foreach (var transition in
                     _arbiter.SynchronizeAuthoritativeState(
                         prepared.Select(value => value.Command)))
            {
                try
                {
                    ApplyTransition(transition);
                }
                catch (Exception exception)
                {
                    var failed = transition.Current ??
                                 transition.Previous;
                    if (failed != null)
                    {
                        results.Add(HandleFailure(
                            PresentationDispatchResult.Failure(
                                failed,
                                "表现执行.执行器异常",
                                exception.Message)));
                    }
                }
            }

            results.AddRange(prepared.Select(value =>
                Record(PresentationDispatchResult.Success(value.Command))));
            return new ReadOnlyCollection<PresentationDispatchResult>(
                results);
        }

        private void ApplyTransition(
            PresentationChannelTransition transition)
        {
            if (transition == null ||
                ReferenceEquals(transition.Previous, transition.Current))
            {
                return;
            }

            if (transition.Previous != null)
            {
                RequirePrepared(transition.Previous).Executor.Stop(
                    RequirePrepared(transition.Previous),
                    _context);
            }

            if (transition.Current != null)
            {
                RequirePrepared(transition.Current).Executor.Execute(
                    RequirePrepared(transition.Current),
                    _context);
            }
        }

        private PreparedPresentationEffect RequirePrepared(
            PresentationEffectCommand command)
        {
            if (_preparedByCommandId.TryGetValue(
                    command.CommandId,
                    out var prepared))
            {
                return prepared;
            }

            var result = _preparer.Prepare(command);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    result.Failure.ErrorMessage);
            }

            _preparedByCommandId[command.CommandId] = result.Value;
            return result.Value;
        }

        private void TryRestore(PresentationChannelSnapshot snapshot)
        {
            if (snapshot?.Previous == null)
            {
                return;
            }

            try
            {
                _arbiter.Restore(snapshot);
                var previous = RequirePrepared(snapshot.Previous);
                previous.Executor.Execute(previous, _context);
            }
            catch (Exception exception)
            {
                Record(PresentationDispatchResult.Failure(
                    snapshot.Previous,
                    "表现执行.恢复失败",
                    exception.Message));
            }
        }

        private PresentationDispatchResult HandleFailure(
            PresentationDispatchResult failure)
        {
            Record(failure);
            if (_failurePolicy ==
                PresentationFailurePolicy.ThrowAfterRecording)
            {
                throw new InvalidOperationException(
                    failure.ErrorMessage);
            }

            return failure;
        }

        private PresentationDispatchResult Record(
            PresentationDispatchResult result)
        {
            _results.Add(result);
            return result;
        }

        private static PresentationFailurePolicy DefaultFailurePolicy()
        {
#if UNITY_EDITOR
            return PresentationFailurePolicy.ThrowAfterRecording;
#else
            return PresentationFailurePolicy.RecordAndContinue;
#endif
        }

    }

}
