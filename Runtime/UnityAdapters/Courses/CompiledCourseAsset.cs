using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VirtualLab.UnityAdapters.Courses
{
    [Serializable]
    public sealed class CourseTextArtifactBinding
    {
        [SerializeField] private string key;
        [SerializeField, TextArea] private string content;

        public CourseTextArtifactBinding(string key, string content)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("文本产物 Key 不能为空。", nameof(key));
            }

            this.key = key.Trim();
            this.content = content
                ?? throw new ArgumentNullException(nameof(content));
        }

        public string Key => key;
        public string Content => content;
    }

    [Serializable]
    public sealed class CourseResourceBinding
    {
        [SerializeField] private string key;
        [SerializeField] private GameObject prefab;
        [SerializeField] private Material material;
        [SerializeField] private AudioClip audioClip;
        [SerializeField] private UnityEngine.Object presentationResource;

        public CourseResourceBinding(
            string key,
            GameObject prefab,
            Material material,
            AudioClip audioClip,
            UnityEngine.Object presentationResource)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("资源绑定 Key 不能为空。", nameof(key));
            }

            this.key = key.Trim();
            this.prefab = prefab;
            this.material = material;
            this.audioClip = audioClip;
            this.presentationResource = presentationResource;
        }

        public string Key => key;
        public GameObject Prefab => prefab;
        public Material Material => material;
        public AudioClip AudioClip => audioClip;
        public UnityEngine.Object PresentationResource => presentationResource;
    }

    /// <summary>
    /// 课程资产中与 Unity 对象引用无关的运行时数据快照。
    /// 领域、表现和学科文本产物统一放在这里，避免由多个序列化字段重复承担协议职责。
    /// </summary>
    internal sealed class CompiledCoursePayload
    {
        public CompiledCoursePayload(
            string domainJson,
            string presentationJson,
            CourseTextArtifactBinding[] textArtifacts)
        {
            DomainJson = domainJson;
            PresentationJson = presentationJson;
            TextArtifacts = textArtifacts;
        }

        public string DomainJson { get; }
        public string PresentationJson { get; }
        public CourseTextArtifactBinding[] TextArtifacts { get; }
    }

    /// <summary>
    /// 集中管理课程载荷的二进制布局和压缩方式。载荷属于生成结果，
    /// 不保留旧字段或版本化格式的兼容分支。
    /// </summary>
    internal static class CompiledCoursePayloadCodec
    {
        public static string Encode(CompiledCoursePayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(
                           output,
                           CompressionMode.Compress,
                           true))
                using (var writer = new BinaryWriter(
                           gzip,
                           new UTF8Encoding(false),
                           true))
                {
                    writer.Write(payload.DomainJson);
                    writer.Write(payload.PresentationJson);
                    writer.Write(payload.TextArtifacts.Length);
                    foreach (var artifact in payload.TextArtifacts)
                    {
                        writer.Write(artifact.Key);
                        writer.Write(artifact.Content);
                    }
                }

                return Convert.ToBase64String(output.ToArray());
            }
        }

        public static CompiledCoursePayload Decode(string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded))
            {
                throw new InvalidOperationException("课程编译载荷为空，请重新生成课程资产。");
            }

            try
            {
                var compressed = Convert.FromBase64String(encoded);
                using (var input = new MemoryStream(compressed, false))
                using (var gzip = new GZipStream(
                           input,
                           CompressionMode.Decompress,
                           false))
                using (var reader = new BinaryReader(
                           gzip,
                           new UTF8Encoding(false, true),
                           true))
                {
                    var domainJson = reader.ReadString();
                    var presentationJson = reader.ReadString();
                    var artifactCount = reader.ReadInt32();
                    if (artifactCount < 0)
                    {
                        throw new InvalidDataException("文本产物数量不能为负数。");
                    }

                    var artifacts = new List<CourseTextArtifactBinding>();
                    for (var index = 0; index < artifactCount; index++)
                    {
                        artifacts.Add(new CourseTextArtifactBinding(
                            reader.ReadString(),
                            reader.ReadString()));
                    }

                    if (reader.Read() != -1)
                    {
                        throw new InvalidDataException("课程编译载荷包含未识别的尾部数据。");
                    }

                    return new CompiledCoursePayload(
                        domainJson,
                        presentationJson,
                        artifacts.ToArray());
                }
            }
            catch (FormatException exception)
            {
                throw InvalidPayload(exception);
            }
            catch (IOException exception)
            {
                throw InvalidPayload(exception);
            }
            catch (ArgumentException exception)
            {
                throw InvalidPayload(exception);
            }
        }

        private static InvalidOperationException InvalidPayload(
            Exception exception) =>
            new InvalidOperationException(
                "课程编译载荷损坏，请重新生成课程资产。",
                exception);
    }

    /// <summary>
    /// 当前课程的本地直接引用资产，不携带版本号或内容指纹。
    /// </summary>
    public sealed class CompiledCourseAsset : ScriptableObject
    {
        [SerializeField] private string courseId;
        [SerializeField, HideInInspector] private string compiledPayload;
        [SerializeField] private CourseResourceBinding[] resourceBindings =
            Array.Empty<CourseResourceBinding>();
        [SerializeField] private GameObject environmentPrefab;

        [NonSerialized] private string decodedSource;
        [NonSerialized] private CompiledCoursePayload decodedPayload;

        public string CourseId => courseId;
        public string DomainJson => Payload.DomainJson;
        public string PresentationJson => Payload.PresentationJson;
        public IReadOnlyList<CourseResourceBinding> ResourceBindings =>
            resourceBindings;
        public IReadOnlyList<CourseTextArtifactBinding> TextArtifacts =>
            Payload.TextArtifacts;
        public GameObject EnvironmentPrefab => environmentPrefab;
        public bool HasCompiledPayload =>
            !string.IsNullOrWhiteSpace(compiledPayload);

        private CompiledCoursePayload Payload
        {
            get
            {
                if (decodedPayload == null
                    || !string.Equals(
                        decodedSource,
                        compiledPayload,
                        StringComparison.Ordinal))
                {
                    decodedPayload = CompiledCoursePayloadCodec.Decode(
                        compiledPayload);
                    decodedSource = compiledPayload;
                }

                return decodedPayload;
            }
        }

        public void SetData(
            string newCourseId,
            string newDomainJson,
            string newPresentationJson,
            IEnumerable<CourseResourceBinding> newResourceBindings,
            GameObject newEnvironmentPrefab)
        {
            SetData(
                newCourseId,
                newDomainJson,
                newPresentationJson,
                newResourceBindings,
                Array.Empty<CourseTextArtifactBinding>(),
                newEnvironmentPrefab);
        }

        public void SetData(
            string newCourseId,
            string newDomainJson,
            string newPresentationJson,
            IEnumerable<CourseResourceBinding> newResourceBindings,
            IEnumerable<CourseTextArtifactBinding> newTextArtifacts,
            GameObject newEnvironmentPrefab)
        {
            if (string.IsNullOrWhiteSpace(newCourseId))
            {
                throw new ArgumentException("课程 ID 不能为空。", nameof(newCourseId));
            }

            if (string.IsNullOrWhiteSpace(newDomainJson))
            {
                throw new ArgumentException("领域 JSON 不能为空。", nameof(newDomainJson));
            }

            if (newPresentationJson == null)
            {
                throw new ArgumentNullException(nameof(newPresentationJson));
            }

            var bindings = newResourceBindings?.ToArray()
                ?? throw new ArgumentNullException(nameof(newResourceBindings));
            if (bindings.Any(value => value == null))
            {
                throw new ArgumentException("资源绑定不能包含空项。");
            }

            var duplicate = bindings
                .GroupBy(value => value.Key, StringComparer.Ordinal)
                .FirstOrDefault(value => value.Count() > 1)?.Key;
            if (duplicate != null)
            {
                throw new ArgumentException($"资源绑定 Key“{duplicate}”重复。");
            }

            var artifacts = newTextArtifacts?.ToArray()
                ?? throw new ArgumentNullException(nameof(newTextArtifacts));
            if (artifacts.Any(value => value == null))
            {
                throw new ArgumentException("文本产物不能包含空项。");
            }

            var duplicateArtifact = artifacts
                .GroupBy(value => value.Key, StringComparer.Ordinal)
                .FirstOrDefault(value => value.Count() > 1)?.Key;
            if (duplicateArtifact != null)
            {
                throw new ArgumentException(
                    $"文本产物 Key“{duplicateArtifact}”重复。");
            }

            var payload = new CompiledCoursePayload(
                newDomainJson,
                newPresentationJson,
                artifacts
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .ToArray());
            var encodedPayload = CompiledCoursePayloadCodec.Encode(payload);

            courseId = newCourseId.Trim();
            compiledPayload = encodedPayload;
            decodedSource = encodedPayload;
            decodedPayload = payload;
            resourceBindings = bindings
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .ToArray();
            environmentPrefab = newEnvironmentPrefab;
        }

        public bool TryGetTextArtifact(string key, out string content)
        {
            var binding = Payload.TextArtifacts.FirstOrDefault(value =>
                string.Equals(value.Key, key, StringComparison.Ordinal));
            content = binding?.Content;
            return binding != null;
        }

        public string RequireTextArtifact(string key)
        {
            if (TryGetTextArtifact(key, out var content))
            {
                return content;
            }

            throw new InvalidOperationException(
                $"课程资产缺少必需的文本产物“{key}”。");
        }
    }
}
