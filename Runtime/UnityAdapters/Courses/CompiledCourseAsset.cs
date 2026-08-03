using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 当前课程的本地直接引用资产，不携带版本号或内容指纹。
    /// </summary>
    public sealed class CompiledCourseAsset : ScriptableObject
    {
        [SerializeField] private string courseId;
        [SerializeField, TextArea] private string domainJson;
        [SerializeField, TextArea] private string presentationJson;
        [SerializeField] private CourseResourceBinding[] resourceBindings =
            Array.Empty<CourseResourceBinding>();
        [SerializeField] private CourseTextArtifactBinding[] textArtifacts =
            Array.Empty<CourseTextArtifactBinding>();
        [SerializeField] private GameObject environmentPrefab;

        public string CourseId => courseId;
        public string DomainJson => domainJson;
        public string PresentationJson => presentationJson;
        public IReadOnlyList<CourseResourceBinding> ResourceBindings =>
            resourceBindings;
        public IReadOnlyList<CourseTextArtifactBinding> TextArtifacts =>
            textArtifacts;
        public GameObject EnvironmentPrefab => environmentPrefab;

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

            courseId = newCourseId.Trim();
            domainJson = newDomainJson;
            presentationJson = newPresentationJson;
            resourceBindings = bindings
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .ToArray();
            textArtifacts = artifacts
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .ToArray();
            environmentPrefab = newEnvironmentPrefab;
        }

        public bool TryGetTextArtifact(string key, out string content)
        {
            var binding = textArtifacts.FirstOrDefault(value =>
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
