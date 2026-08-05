using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using VirtualLab.Application.Courses;
using VirtualLab.Domain.Relations;

namespace VirtualLab.UnityAdapters.Courses
{
    public static class CourseAssetDecoder
    {
        private static readonly JsonSerializerSettings Settings =
            new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                TypeNameHandling = TypeNameHandling.None,
                Converters =
                {
                    new StructuredFactFieldConverter(),
                    new RelationTypeIdConverter(),
                    new StructuredValueDictionaryConverter()
                }
            };

        private sealed class StructuredFactFieldConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) =>
                objectType == typeof(StructuredFactField);

            public override void WriteJson(
                JsonWriter writer,
                object value,
                JsonSerializer serializer)
            {
                writer.WriteValue(((StructuredFactField)value).Id);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    return new StructuredFactField((string)reader.Value);
                }

                throw new JsonSerializationException(
                    "课程事实字段必须是中文字符串标识。");
            }
        }

        private sealed class RelationTypeIdConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) =>
                objectType == typeof(RelationTypeId);

            public override void WriteJson(
                JsonWriter writer,
                object value,
                JsonSerializer serializer)
            {
                writer.WriteValue(((RelationTypeId)value).Value);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    return new RelationTypeId((string)reader.Value);
                }

                throw new JsonSerializationException(
                    "课程关系类型必须是模块注册的中文字符串标识。");
            }
        }

        public static string EncodeDomain(
            CompiledCourseDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return JsonConvert.SerializeObject(definition, Settings);
        }

        private sealed class StructuredValueDictionaryConverter :
            JsonConverter
        {
            private static readonly Type PairEnumerableType =
                typeof(IEnumerable<KeyValuePair<string, StructuredValue>>);

            public override bool CanConvert(Type objectType) =>
                PairEnumerableType.IsAssignableFrom(objectType);

            public override void WriteJson(
                JsonWriter writer,
                object value,
                JsonSerializer serializer)
            {
                var entries =
                    ((IEnumerable<KeyValuePair<string, StructuredValue>>)value)
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => new StructuredValueEntry
                    {
                        Key = item.Key,
                        Value = item.Value
                    })
                    .ToArray();
                serializer.Serialize(writer, entries);
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                var entries =
                    serializer.Deserialize<StructuredValueEntry[]>(reader)
                    ?? Array.Empty<StructuredValueEntry>();
                return entries.ToDictionary(
                    value => value.Key,
                    value => value.Value,
                    StringComparer.Ordinal);
            }

            private sealed class StructuredValueEntry
            {
                public string Key { get; set; }
                public StructuredValue Value { get; set; }
            }
        }

        public static CompiledCourseDefinition DecodeDomain(
            CompiledCourseAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            var definition =
                JsonConvert.DeserializeObject<CompiledCourseDefinition>(
                    asset.DomainJson,
                    Settings);
            return definition
                ?? throw new InvalidOperationException("课程领域 JSON 为空。");
        }

        public static string EncodePresentation(
            CoursePresentationDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return JsonConvert.SerializeObject(definition, Settings);
        }

        public static CoursePresentationDefinition DecodePresentation(
            CompiledCourseAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            var definition =
                JsonConvert.DeserializeObject<CoursePresentationDefinition>(
                    asset.PresentationJson,
                    Settings);
            return definition
                ?? throw new InvalidOperationException("课程表现 JSON 为空。");
        }
    }
}
