using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Input;

namespace VirtualLab.UnityAdapters.Physics
{
    public sealed class SpatialFactSet
    {
        public SpatialFactSet(
            IEnumerable<KeyValuePair<string, StructuredValue>> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var copy = new Dictionary<string, StructuredValue>(
                StringComparer.Ordinal);
            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Value == null ||
                    !copy.TryAdd(pair.Key.Trim(), pair.Value))
                {
                    throw new ArgumentException(
                        "空间事实不能为空或重复。",
                        nameof(values));
                }
            }

            Values = new ReadOnlyDictionary<string, StructuredValue>(copy);
        }

        public IReadOnlyDictionary<string, StructuredValue> Values { get; }

        public static SpatialFactSet Empty { get; } = new SpatialFactSet(
            Array.Empty<KeyValuePair<string, StructuredValue>>());
    }

    public interface ISpatialFactProvider
    {
        SpatialFactSet Measure(SemanticInputIntent intent);
    }
}
