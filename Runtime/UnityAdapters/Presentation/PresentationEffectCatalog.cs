using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.UnityAdapters.Presentation
{
    public enum PresentationParameterKind
    {
        Boolean,
        Number,
        Text,
        EnumText,
        Resource
    }

    public enum PresentationContextEntityKind
    {
        SignalSubject,
        ActionSource,
        ActionTarget
    }

    /// <summary>
    /// 单个表现参数的编译期和运行时共同契约。
    /// </summary>
    public sealed class PresentationParameterDescriptor
    {
        public PresentationParameterDescriptor(
            string name,
            PresentationParameterKind kind,
            bool required,
            PresentationValue defaultValue,
            double? minimum,
            double? maximum,
            IEnumerable<string> allowedValues,
            CourseResourceKind? resourceKind)
        {
            Name = RequireText(name, "表现参数名");
            Kind = kind;
            Required = required;
            DefaultValue = defaultValue;
            Minimum = minimum;
            Maximum = maximum;
            AllowedValues = CopyDistinct(
                allowedValues,
                $"表现参数“{Name}”的允许值");
            ResourceKind = resourceKind;
            ValidateContract();
        }

        public string Name { get; }

        public PresentationParameterKind Kind { get; }

        public bool Required { get; }

        public PresentationValue DefaultValue { get; }

        public double? Minimum { get; }

        public double? Maximum { get; }

        public IReadOnlyList<string> AllowedValues { get; }

        public CourseResourceKind? ResourceKind { get; }

        public bool Matches(PresentationValue value, out string reason)
        {
            reason = null;
            switch (Kind)
            {
                case PresentationParameterKind.Boolean:
                    return RequireKind(
                        value,
                        PresentationValueKind.Boolean,
                        out reason);
                case PresentationParameterKind.Number:
                    if (!RequireKind(
                            value,
                            PresentationValueKind.Number,
                            out reason))
                    {
                        return false;
                    }

                    if ((Minimum.HasValue &&
                         value.Number < Minimum.Value) ||
                        (Maximum.HasValue &&
                         value.Number > Maximum.Value))
                    {
                        reason = "数值超出允许范围。";
                        return false;
                    }

                    return true;
                case PresentationParameterKind.Text:
                case PresentationParameterKind.Resource:
                    return RequireKind(
                        value,
                        PresentationValueKind.Text,
                        out reason);
                case PresentationParameterKind.EnumText:
                    if (!RequireKind(
                            value,
                            PresentationValueKind.Text,
                            out reason))
                    {
                        return false;
                    }

                    if (!AllowedValues.Contains(
                            value.Text,
                            StringComparer.Ordinal))
                    {
                        reason = "文本不在允许值集合中。";
                        return false;
                    }

                    return true;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ValidateContract()
        {
            if (Minimum.HasValue &&
                Maximum.HasValue &&
                Minimum.Value > Maximum.Value)
            {
                throw new ArgumentException(
                    $"参数“{Name}”的最小值不能大于最大值。");
            }

            if (Kind == PresentationParameterKind.EnumText &&
                AllowedValues.Count == 0)
            {
                throw new ArgumentException(
                    $"枚举参数“{Name}”必须声明允许值。");
            }

            if (Kind == PresentationParameterKind.Resource &&
                !ResourceKind.HasValue)
            {
                throw new ArgumentException(
                    $"资源参数“{Name}”必须声明资源种类。");
            }

            if (DefaultValue != null &&
                !Matches(DefaultValue, out var reason))
            {
                throw new ArgumentException(
                    $"参数“{Name}”的默认值非法：{reason}");
            }
        }

        private static bool RequireKind(
            PresentationValue value,
            PresentationValueKind expected,
            out string reason)
        {
            if (value != null && value.Kind == expected)
            {
                reason = null;
                return true;
            }

            reason = $"参数类型必须是 {expected}。";
            return false;
        }

        private static IReadOnlyList<string> CopyDistinct(
            IEnumerable<string> values,
            string context)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var result = values
                .Select(value => RequireText(value, context))
                .ToArray();
            if (result.Distinct(StringComparer.Ordinal).Count() !=
                result.Length)
            {
                throw new ArgumentException(context + "不能重复。");
            }

            return new ReadOnlyCollection<string>(result);
        }

        private static string RequireText(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(context + "不能为空。");
            }

            return value.Trim();
        }
    }

    /// <summary>
    /// 描述一个原语允许作用的位置，以及准备阶段必须解析的语义实体。
    /// </summary>
    public sealed class PresentationTargetContract
    {
        public PresentationTargetContract(
            IEnumerable<PresentationLocationKind> allowedLocations,
            IEnumerable<PresentationSlotKind> allowedSlotKinds,
            IEnumerable<SemanticAnchorKind> allowedAnchorKinds,
            IEnumerable<PresentationContextEntityKind>
                requiredContextEntities)
        {
            AllowedLocations = CopyDistinct(allowedLocations);
            AllowedSlotKinds = CopyDistinct(allowedSlotKinds);
            AllowedAnchorKinds = CopyDistinct(allowedAnchorKinds);
            RequiredContextEntities = CopyDistinct(
                requiredContextEntities);
            if (AllowedLocations.Count == 0)
            {
                throw new ArgumentException(
                    "目标契约必须允许至少一种作用位置。");
            }

            if (AllowedSlotKinds.Count > 0 &&
                !AllowedLocations.Contains(
                    PresentationLocationKind.PresentationSlot))
            {
                throw new ArgumentException(
                    "声明表现插槽种类时必须允许表现插槽位置。");
            }

            if (AllowedAnchorKinds.Count > 0 &&
                !AllowedLocations.Contains(
                    PresentationLocationKind.SemanticAnchor))
            {
                throw new ArgumentException(
                    "声明语义锚点种类时必须允许语义锚点位置。");
            }
        }

        public IReadOnlyList<PresentationLocationKind> AllowedLocations
        {
            get;
        }

        public IReadOnlyList<PresentationSlotKind> AllowedSlotKinds { get; }

        public IReadOnlyList<SemanticAnchorKind> AllowedAnchorKinds { get; }

        public IReadOnlyList<PresentationContextEntityKind>
            RequiredContextEntities { get; }

        private static IReadOnlyList<T> CopyDistinct<T>(
            IEnumerable<T> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var result = values.ToArray();
            if (result.Distinct().Count() != result.Length)
            {
                throw new ArgumentException("目标契约不能包含重复项。");
            }

            return new ReadOnlyCollection<T>(result);
        }
    }

    /// <summary>
    /// 一个表现原语的唯一协议事实，课程不得覆盖通道或参数契约。
    /// </summary>
    public sealed class PresentationEffectDescriptor
    {
        public PresentationEffectDescriptor(
            string protocolId,
            string chineseName,
            string channel,
            PresentationEffectLifecycle defaultLifecycle,
            IEnumerable<PresentationEffectLifecycle> allowedLifecycles,
            PresentationTargetContract targetContract,
            IEnumerable<PresentationParameterDescriptor> parameters,
            Func<IPresentationEffectExecutor> createExecutor)
        {
            ProtocolId = Required(protocolId, "表现协议 ID");
            ChineseName = Required(chineseName, "表现原语中文名");
            Channel = Required(channel, "表现通道");
            DefaultLifecycle = defaultLifecycle;
            AllowedLifecycles = CopyDistinct(
                allowedLifecycles,
                $"表现原语“{ChineseName}”的生命周期");
            TargetContract = targetContract ??
                throw new ArgumentNullException(nameof(targetContract));
            Parameters = CopyParameters(parameters, ChineseName);
            CreateExecutor = createExecutor ??
                throw new ArgumentNullException(nameof(createExecutor));

            if (AllowedLifecycles.Count == 0 ||
                !AllowedLifecycles.Contains(DefaultLifecycle))
            {
                throw new ArgumentException(
                    $"表现原语“{ChineseName}”的允许生命周期必须包含默认生命周期。");
            }
        }

        public string ProtocolId { get; }

        public string ChineseName { get; }

        public string Channel { get; }

        public PresentationEffectLifecycle DefaultLifecycle { get; }

        public IReadOnlyList<PresentationEffectLifecycle>
            AllowedLifecycles { get; }

        public PresentationTargetContract TargetContract { get; }

        public IReadOnlyList<PresentationParameterDescriptor> Parameters
        {
            get;
        }

        public Func<IPresentationEffectExecutor> CreateExecutor { get; }

        private static IReadOnlyList<PresentationParameterDescriptor>
            CopyParameters(
                IEnumerable<PresentationParameterDescriptor> parameters,
                string chineseName)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            var result = parameters.ToArray();
            if (result.Any(value => value == null))
            {
                throw new ArgumentException(
                    $"表现原语“{chineseName}”的参数不能包含空项。");
            }

            if (result.Select(value => value.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != result.Length)
            {
                throw new ArgumentException(
                    $"表现原语“{chineseName}”包含重复参数名。");
            }

            return new ReadOnlyCollection<PresentationParameterDescriptor>(
                result);
        }

        private static IReadOnlyList<T> CopyDistinct<T>(
            IEnumerable<T> values,
            string context)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var result = values.ToArray();
            if (result.Distinct().Count() != result.Length)
            {
                throw new ArgumentException(context + "不能重复。");
            }

            return new ReadOnlyCollection<T>(result);
        }

        private static string Required(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(context + "不能为空。");
            }

            return value.Trim();
        }
    }

    /// <summary>
    /// 表现原语唯一目录，同时提供中文配表索引和稳定协议索引。
    /// </summary>
    public sealed class PresentationEffectCatalog
    {
        private readonly IReadOnlyDictionary<
            string,
            PresentationEffectDescriptor> _byProtocolId;
        private readonly IReadOnlyDictionary<
            string,
            PresentationEffectDescriptor> _byChineseName;

        public PresentationEffectCatalog(
            IEnumerable<PresentationEffectDescriptor> descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            var values = descriptors.ToArray();
            _byProtocolId = BuildUnique(
                values,
                value => value.ProtocolId,
                "表现协议 ID");
            _byChineseName = BuildUnique(
                values,
                value => value.ChineseName,
                "表现原语中文名");

            foreach (var descriptor in values)
            {
                RequireMatchingExecutor(descriptor);
            }
        }

        public IReadOnlyList<PresentationEffectDescriptor> Descriptors =>
            new ReadOnlyCollection<PresentationEffectDescriptor>(
                _byProtocolId.Values
                    .OrderBy(
                        value => value.ProtocolId,
                        StringComparer.Ordinal)
                    .ToArray());

        public PresentationEffectDescriptor RequireByProtocolId(
            string protocolId)
        {
            return Require(
                _byProtocolId,
                protocolId,
                "表现协议 ID");
        }

        public PresentationEffectDescriptor RequireByChineseName(
            string chineseName)
        {
            return Require(
                _byChineseName,
                chineseName,
                "表现原语中文名");
        }

        public IReadOnlyList<IPresentationEffectExecutor> CreateExecutors()
        {
            return new ReadOnlyCollection<IPresentationEffectExecutor>(
                Descriptors
                    .Select(RequireMatchingExecutor)
                    .ToArray());
        }

        private static IPresentationEffectExecutor RequireMatchingExecutor(
            PresentationEffectDescriptor descriptor)
        {
            var executor = descriptor.CreateExecutor();
            if (executor == null ||
                !string.Equals(
                    executor.EffectId,
                    descriptor.ProtocolId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"表现原语“{descriptor.ChineseName}”的执行器协议不一致。");
            }

            return executor;
        }

        private static IReadOnlyDictionary<
            string,
            PresentationEffectDescriptor> BuildUnique(
                IEnumerable<PresentationEffectDescriptor> values,
                Func<PresentationEffectDescriptor, string> keySelector,
                string context)
        {
            var result =
                new Dictionary<string, PresentationEffectDescriptor>(
                    StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (value == null ||
                    !result.TryAdd(keySelector(value), value))
                {
                    throw new ArgumentException(
                        context + "不能为空或重复。");
                }
            }

            return new ReadOnlyDictionary<
                string,
                PresentationEffectDescriptor>(result);
        }

        private static PresentationEffectDescriptor Require(
            IReadOnlyDictionary<
                string,
                PresentationEffectDescriptor> source,
            string key,
            string context)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                !source.TryGetValue(key.Trim(), out var value))
            {
                throw new KeyNotFoundException(
                    $"未注册的{context}“{key}”。");
            }

            return value;
        }
    }
}
