using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Kernel;

namespace VirtualLab.Domain
{
    /// <summary>
    /// 可事务复制的权威过程参数。这里只保存领域数据，不保存动画或输入设备状态。
    /// </summary>
    public sealed class WorldProcessState
    {
        public WorldProcessState(
            string processId,
            EntityId entityId,
            IEnumerable<KeyValuePair<string, string>> textParameters,
            IEnumerable<KeyValuePair<string, double>> numberParameters)
        {
            ProcessId = RequireText(processId, "过程 ID");
            if (string.IsNullOrWhiteSpace(entityId.Value))
            {
                throw new ArgumentException(
                    "过程实体 ID 不能为空。",
                    nameof(entityId));
            }

            EntityId = entityId;
            TextParameters = CopyTextParameters(textParameters);
            NumberParameters = CopyNumberParameters(numberParameters);
        }

        public string ProcessId { get; }

        public EntityId EntityId { get; }

        public IReadOnlyDictionary<string, string> TextParameters { get; }

        public IReadOnlyDictionary<string, double> NumberParameters { get; }

        private static IReadOnlyDictionary<string, string>
            CopyTextParameters(
                IEnumerable<KeyValuePair<string, string>> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                var key = RequireText(parameter.Key, "过程文本参数名");
                var value = RequireText(parameter.Value, $"过程参数“{key}”");
                if (!copy.TryAdd(key, value))
                {
                    throw new ArgumentException(
                        $"过程文本参数“{key}”重复。",
                        nameof(parameters));
                }
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }

        private static IReadOnlyDictionary<string, double>
            CopyNumberParameters(
                IEnumerable<KeyValuePair<string, double>> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            var copy = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                var key = RequireText(parameter.Key, "过程数值参数名");
                if (double.IsNaN(parameter.Value)
                    || double.IsInfinity(parameter.Value))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(parameters),
                        $"过程数值参数“{key}”必须是有限数。");
                }

                if (!copy.TryAdd(key, parameter.Value))
                {
                    throw new ArgumentException(
                        $"过程数值参数“{key}”重复。",
                        nameof(parameters));
                }
            }

            return new ReadOnlyDictionary<string, double>(copy);
        }

        private static string RequireText(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{context}不能为空。");
            }

            return value.Trim();
        }
    }
}
