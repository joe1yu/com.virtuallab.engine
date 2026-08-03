using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VirtualLab.Unity.Authoring.Recipes;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.Unity.Authoring.Normalized
{
    public enum PrefabContractRequirementKind
    {
        Collider,
        SemanticAnchor,
        AnyPresentationSlot
    }

    /// <summary>
    /// 一条能力对应的预制体视图要求。要求由各学科包的 CSV 提供，
    /// 通用引擎只负责执行，不认识具体学科能力。
    /// </summary>
    public sealed class PrefabContractRequirement
    {
        public PrefabContractRequirement(
            string capabilityId,
            PrefabContractRequirementKind kind,
            string value,
            string diagnosticCode,
            string message)
        {
            CapabilityId = Required(capabilityId, nameof(capabilityId));
            Kind = kind;
            Value = value?.Trim() ?? string.Empty;
            DiagnosticCode = Required(
                diagnosticCode,
                nameof(diagnosticCode));
            Message = Required(message, nameof(message));
        }

        public string CapabilityId { get; }
        public PrefabContractRequirementKind Kind { get; }
        public string Value { get; }
        public string DiagnosticCode { get; }
        public string Message { get; }

        private static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("值不能为空。", parameterName);
            }

            return value.Trim();
        }
    }

    public sealed class PrefabContractRequirementCatalog
    {
        public const string ConfigFileName = "能力对应的预制体要求";

        private static PrefabContractRequirementCatalog _default;
        private readonly IReadOnlyDictionary<
            string,
            IReadOnlyList<PrefabContractRequirement>> _byCapability;

        public PrefabContractRequirementCatalog(
            IEnumerable<PrefabContractRequirement> requirements)
        {
            if (requirements == null)
            {
                throw new ArgumentNullException(nameof(requirements));
            }

            _byCapability = new ReadOnlyDictionary<
                string,
                IReadOnlyList<PrefabContractRequirement>>(
                requirements
                    .GroupBy(value => value.CapabilityId, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<PrefabContractRequirement>)
                            new ReadOnlyCollection<PrefabContractRequirement>(
                                group.ToArray()),
                        StringComparer.Ordinal));
        }

        public static PrefabContractRequirementCatalog Default =>
            _default ?? (_default = LoadFromProject());

        public IEnumerable<PrefabContractRequirement> FindFor(
            IEnumerable<string> capabilityIds)
        {
            foreach (var capabilityId in capabilityIds ??
                         Array.Empty<string>())
            {
                if (_byCapability.TryGetValue(
                        capabilityId,
                        out var requirements))
                {
                    foreach (var requirement in requirements)
                    {
                        yield return requirement;
                    }
                }
            }
        }

        public static void InvalidateDefault()
        {
            _default = null;
        }

        private static PrefabContractRequirementCatalog LoadFromProject()
        {
            var requirements = new List<PrefabContractRequirement>();
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(
                        System.IO.Path.GetFileNameWithoutExtension(path),
                        ConfigFileName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null)
                {
                    requirements.AddRange(Parse(asset.text, path));
                }
            }

            return new PrefabContractRequirementCatalog(requirements);
        }

        private static IEnumerable<PrefabContractRequirement> Parse(
            string csv,
            string source)
        {
            var lines = (csv ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
            for (var index = 1; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                var columns = ParseRow(line).ToArray();
                if (columns.Length != 5)
                {
                    throw new FormatException(
                        $"{source} 第 {index + 1} 行应包含 5 列。" );
                }

                yield return new PrefabContractRequirement(
                    columns[0],
                    ParseKind(columns[1], source, index + 1),
                    columns[2],
                    columns[3],
                    columns[4]);
            }
        }

        private static PrefabContractRequirementKind ParseKind(
            string value,
            string source,
            int line)
        {
            switch (value.Trim())
            {
                case "碰撞体":
                    return PrefabContractRequirementKind.Collider;
                case "语义锚点":
                    return PrefabContractRequirementKind.SemanticAnchor;
                case "表现插槽任一":
                    return PrefabContractRequirementKind.AnyPresentationSlot;
                default:
                    throw new FormatException(
                        $"{source} 第 {line} 行包含未知要求类型“{value}”。" );
            }
        }

        public static SemanticAnchorKind ParseAnchorKind(string value)
        {
            if (Enum.TryParse(value, true, out SemanticAnchorKind kind))
            {
                return kind;
            }

            switch (value.Trim())
            {
                case "连接端口":
                    return SemanticAnchorKind.ConnectionPort;
                case "倾倒出口":
                    return SemanticAnchorKind.PourOutlet;
                case "受热点":
                    return SemanticAnchorKind.HeatingPoint;
                case "点火点":
                    return SemanticAnchorKind.IgnitionPoint;
                case "观察点":
                    return SemanticAnchorKind.ObservationFocus;
                case "抓取点":
                    return SemanticAnchorKind.InteractionGrip;
            }

            throw new FormatException($"未知语义锚点类型“{value}”。");
        }

        public static IReadOnlyList<PresentationSlotKind> ParseSlotKinds(
            string value)
        {
            return value.Split('|')
                .Select(item =>
                {
                    if (Enum.TryParse(
                            item.Trim(),
                            true,
                            out PresentationSlotKind kind))
                    {
                        return kind;
                    }

                    switch (item.Trim())
                    {
                        case "内容":
                            return PresentationSlotKind.Content;
                        case "液体":
                            return PresentationSlotKind.Liquid;
                        case "燃烧":
                            return PresentationSlotKind.Combustion;
                        case "高亮":
                            return PresentationSlotKind.Highlight;
                    }

                    throw new FormatException(
                        $"未知表现插槽类型“{item}”。");
                })
                .ToArray();
        }

        private static IEnumerable<string> ParseRow(string row)
        {
            var value = new System.Text.StringBuilder();
            var quoted = false;
            for (var index = 0; index < row.Length; index++)
            {
                var character = row[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < row.Length &&
                        row[index + 1] == '"')
                    {
                        value.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }

                    continue;
                }

                if (character == ',' && !quoted)
                {
                    yield return value.ToString().Trim();
                    value.Clear();
                    continue;
                }

                value.Append(character);
            }

            if (quoted)
            {
                throw new FormatException("CSV 引号没有闭合。");
            }

            yield return value.ToString().Trim();
        }
    }

    internal sealed class PrefabContractRequirementCatalogPostprocessor :
        AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (importedAssets
                    .Concat(deletedAssets)
                    .Concat(movedAssets)
                    .Concat(movedFromAssetPaths)
                    .Any(IsContractConfig))
            {
                PrefabContractRequirementCatalog.InvalidateDefault();
            }
        }

        private static bool IsContractConfig(string path)
        {
            return string.Equals(
                System.IO.Path.GetFileNameWithoutExtension(path),
                PrefabContractRequirementCatalog.ConfigFileName,
                StringComparison.Ordinal);
        }
    }
}
