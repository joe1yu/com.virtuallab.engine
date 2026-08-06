using System;
using System.Collections.Generic;

namespace VirtualLab.Presentation
{
    public enum PresentationValueKind
    {
        Null,
        Boolean,
        Number,
        Text
    }

    public sealed class PresentationValue
    {
        private PresentationValue(
            PresentationValueKind kind,
            bool boolean,
            double number,
            string text)
        {
            Kind = kind;
            Boolean = boolean;
            Number = number;
            Text = text;
        }

        public PresentationValueKind Kind { get; }

        public bool Boolean { get; }

        public double Number { get; }

        public string Text { get; }

        public static PresentationValue Null()
        {
            return new PresentationValue(
                PresentationValueKind.Null,
                false,
                0d,
                null);
        }

        public static PresentationValue FromBoolean(bool value)
        {
            return new PresentationValue(
                PresentationValueKind.Boolean,
                value,
                0d,
                null);
        }

        public static PresentationValue FromNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "表现参数数值必须是有限数。");
            }

            return new PresentationValue(
                PresentationValueKind.Number,
                false,
                value,
                null);
        }

        public static PresentationValue FromText(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    "表现文本不能为 null；请显式使用 Null。");
            }

            return new PresentationValue(
                PresentationValueKind.Text,
                false,
                0d,
                value);
        }
    }

    public enum PresentationEffectLifecycle
    {
        OneShot,
        WhileActive,
        UntilReplaced
    }

    /// <summary>
    /// 表现层执行的标准效果命令，不包含动画控制器或状态机细节。
    /// </summary>
    public sealed class PresentationEffectCommand
    {
        public PresentationEffectCommand(
            string commandId,
            string effectId,
            PresentationTargetReference target,
            PresentationSignalContextReference signalContext,
            string channel,
            int priority,
            PresentationEffectLifecycle lifecycle,
            IEnumerable<KeyValuePair<string, PresentationValue>> parameters)
        {
            CommandId = PresentationContractGuard.Required(
                commandId,
                "表现命令 ID");
            EffectId = PresentationContractGuard.Required(effectId, "效果 ID");
            Target = target ??
                throw new ArgumentNullException(nameof(target));
            SignalContext = signalContext ??
                throw new ArgumentNullException(nameof(signalContext));
            Channel = PresentationContractGuard.Required(
                channel,
                $"效果“{EffectId}”的通道");
            Priority = priority;
            Lifecycle = lifecycle;
            Parameters = PresentationContractGuard.CopyValues(
                parameters,
                $"效果“{EffectId}”");
        }

        public string CommandId { get; }

        public string EffectId { get; }

        public PresentationTargetReference Target { get; }

        public PresentationSignalContextReference SignalContext { get; }

        public string Channel { get; }

        public int Priority { get; }

        public PresentationEffectLifecycle Lifecycle { get; }

        public IReadOnlyDictionary<string, PresentationValue> Parameters
        {
            get;
        }
    }
}
