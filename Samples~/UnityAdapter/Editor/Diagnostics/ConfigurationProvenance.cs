using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualLab.Unity.Authoring.Diagnostics
{
    public enum ConfigurationLayer
    {
        Platform,
        Discipline,
        Course,
        Override
    }

    /// <summary>
    /// 定位一项配置的原始来源；后续生成阶段会把多个来源串成完整来源链。
    /// </summary>
    public sealed class ConfigurationSource
    {
        public ConfigurationSource(
            ConfigurationLayer layer,
            string packageId,
            string fileName,
            int line,
            int column,
            string configurationId)
        {
            if (line < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(line));
            }

            if (column < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            Layer = layer;
            PackageId = packageId ?? string.Empty;
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Line = line;
            Column = column;
            ConfigurationId = configurationId ?? string.Empty;
        }

        public ConfigurationLayer Layer { get; }
        public string PackageId { get; }
        public string FileName { get; }
        public int Line { get; }
        public int Column { get; }
        public string ConfigurationId { get; }
    }

    /// <summary>
    /// 一个生成项从平台、学科、课程到覆盖的完整来源链。
    /// </summary>
    public sealed class ConfigurationProvenance
    {
        public ConfigurationProvenance(
            string generatedItemId,
            IEnumerable<ConfigurationSource> sources,
            string overrideOperation = null)
        {
            GeneratedItemId = generatedItemId
                ?? throw new ArgumentNullException(nameof(generatedItemId));
            Sources = (sources ?? throw new ArgumentNullException(nameof(sources)))
                .Where(value => value != null)
                .ToArray();
            OverrideOperation = overrideOperation ?? string.Empty;
        }

        public string GeneratedItemId { get; }
        public IReadOnlyList<ConfigurationSource> Sources { get; }
        public string OverrideOperation { get; }
    }
}
