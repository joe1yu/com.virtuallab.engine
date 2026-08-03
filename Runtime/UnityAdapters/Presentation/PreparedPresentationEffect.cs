using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class PreparedPresentationParameters
    {
        private readonly IReadOnlyDictionary<string, PresentationValue> _values;

        public PreparedPresentationParameters(
            IReadOnlyDictionary<string, PresentationValue> values)
        {
            _values = values ??
                throw new ArgumentNullException(nameof(values));
        }

        public IReadOnlyDictionary<string, PresentationValue> Values => _values;

        public double RequireNumber(string name)
        {
            var value = Require(name, PresentationValueKind.Number);
            return value.Number;
        }

        public string RequireText(string name)
        {
            var value = Require(name, PresentationValueKind.Text);
            return value.Text;
        }

        public bool RequireBoolean(string name)
        {
            var value = Require(name, PresentationValueKind.Boolean);
            return value.Boolean;
        }

        private PresentationValue Require(
            string name,
            PresentationValueKind kind)
        {
            if (!_values.TryGetValue(name, out var value) ||
                value == null ||
                value.Kind != kind)
            {
                throw new InvalidOperationException(
                    $"已准备表现参数“{name}”不存在或类型不是 {kind}。");
            }

            return value;
        }
    }

    public sealed class PreparedPresentationResources
    {
        private readonly IReadOnlyDictionary<string, UnityEngine.Object>
            _values;

        public PreparedPresentationResources(
            IReadOnlyDictionary<string, UnityEngine.Object> values)
        {
            _values = values ??
                throw new ArgumentNullException(nameof(values));
        }

        public T Require<T>(string parameterName)
            where T : UnityEngine.Object
        {
            if (!_values.TryGetValue(parameterName, out var value) ||
                !(value is T typed))
            {
                throw new InvalidOperationException(
                    $"已准备表现资源“{parameterName}”不是 {typeof(T).Name}。");
            }

            return typed;
        }
    }

    /// <summary>
    /// 表现执行器只接收已经通过协议、目标、上下文、参数和资源检查的准备产物。
    /// </summary>
    public sealed class PreparedPresentationEffect
    {
        public PreparedPresentationEffect(
            PresentationEffectCommand command,
            PresentationEffectDescriptor descriptor,
            IPresentationEffectExecutor executor,
            ResolvedPresentationTarget target,
            ResolvedPresentationSignalContext signalContext,
            IReadOnlyDictionary<string, PresentationValue> parameters,
            IReadOnlyDictionary<string, UnityEngine.Object> resources)
        {
            Command = command ??
                throw new ArgumentNullException(nameof(command));
            Descriptor = descriptor ??
                throw new ArgumentNullException(nameof(descriptor));
            Executor = executor ??
                throw new ArgumentNullException(nameof(executor));
            Target = target ??
                throw new ArgumentNullException(nameof(target));
            SignalContext = signalContext ??
                throw new ArgumentNullException(nameof(signalContext));
            Parameters = new PreparedPresentationParameters(
                parameters ??
                throw new ArgumentNullException(nameof(parameters)));
            Resources = new PreparedPresentationResources(
                resources ??
                throw new ArgumentNullException(nameof(resources)));
        }

        public PresentationEffectCommand Command { get; }

        public PresentationEffectDescriptor Descriptor { get; }

        public IPresentationEffectExecutor Executor { get; }

        public ResolvedPresentationTarget Target { get; }

        public ResolvedPresentationSignalContext SignalContext { get; }

        public PreparedPresentationParameters Parameters { get; }

        public PreparedPresentationResources Resources { get; }
    }

    public sealed class PresentationPreparationResult
    {
        private PresentationPreparationResult(
            PreparedPresentationEffect value,
            PresentationDispatchResult failure)
        {
            Value = value;
            Failure = failure;
        }

        public bool Succeeded => Value != null;

        public PreparedPresentationEffect Value { get; }

        public PresentationDispatchResult Failure { get; }

        public static PresentationPreparationResult Success(
            PreparedPresentationEffect value)
        {
            return new PresentationPreparationResult(
                value ?? throw new ArgumentNullException(nameof(value)),
                null);
        }

        public static PresentationPreparationResult Failed(
            PresentationDispatchResult failure)
        {
            return new PresentationPreparationResult(
                null,
                failure ??
                throw new ArgumentNullException(nameof(failure)));
        }
    }
}
