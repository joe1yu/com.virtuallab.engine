using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace VirtualLab.Engine.Tests.Architecture
{
    public sealed class AssemblyDependencyBoundaryTests
    {
        private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["VirtualLab.Kernel"] = Array.Empty<string>(),
                ["VirtualLab.Domain"] = new[]
                {
                    "VirtualLab.Kernel"
                },
                ["VirtualLab.Interaction"] = new[]
                {
                    "VirtualLab.Domain"
                },
                ["VirtualLab.Interaction.Courses"] = new[]
                {
                    "VirtualLab.Kernel",
                    "VirtualLab.Domain",
                    "VirtualLab.Interaction",
                    "VirtualLab.Application"
                },
                ["VirtualLab.Teaching.Courses"] = new[]
                {
                    "VirtualLab.Domain",
                    "VirtualLab.Application"
                },
                ["VirtualLab.Spatial.Courses"] = new[]
                {
                    "VirtualLab.Application"
                },
                ["VirtualLab.Application"] = new[]
                {
                    "VirtualLab.Kernel",
                    "VirtualLab.Domain",
                    "VirtualLab.Interaction"
                },
                ["VirtualLab.Presentation"] = Array.Empty<string>(),
                ["VirtualLab.Infrastructure"] = new[]
                {
                    "VirtualLab.Kernel",
                    "VirtualLab.Domain",
                    "VirtualLab.Application"
                },
                ["VirtualLab.Chemistry"] = new[]
                {
                    "VirtualLab.Kernel",
                    "VirtualLab.Domain",
                    "VirtualLab.Application",
                    "VirtualLab.Interaction",
                    "VirtualLab.Interaction.Courses",
                    "VirtualLab.Spatial.Courses",
                    "VirtualLab.Teaching.Courses"
                },
                ["VirtualLab.UnityAdapters"] = new[]
                {
                    "VirtualLab.Kernel",
                    "VirtualLab.Domain",
                    "VirtualLab.Application",
                    "VirtualLab.Interaction",
                    "VirtualLab.Interaction.Courses",
                    "VirtualLab.Spatial.Courses",
                    "VirtualLab.Presentation",
                    "Unity.ugui"
                },
                ["VirtualLab.Chemistry.UnityAdapters"] = new[]
                {
                    "VirtualLab.Kernel",
                    "VirtualLab.Domain",
                    "VirtualLab.Application",
                    "VirtualLab.Presentation",
                    "VirtualLab.UnityAdapters",
                    "VirtualLab.Chemistry"
                }
            };

        [Test]
        public void 生产程序集依赖符合冻结的边界方向()
        {
            var definitions = ReadAssemblyDefinitions();
            var errors = new List<string>();

            foreach (var boundary in AllowedReferences)
            {
                if (!definitions.TryGetValue(boundary.Key, out var definition))
                {
                    errors.Add($"缺少受边界保护的程序集：{boundary.Key}");
                    continue;
                }

                var unexpected = definition.References
                    .Except(boundary.Value, StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (unexpected.Length > 0)
                {
                    errors.Add(
                        $"{boundary.Key} 出现越界依赖：{string.Join("、", unexpected)}");
                }
            }

            Assert.That(
                errors,
                Is.Empty,
                "程序集依赖边界被破坏："
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors));
        }

        [Test]
        public void 氧气课程程序集不能被生产程序集反向引用()
        {
            var errors = ReadAssemblyDefinitions()
                .Values
                .Where(value => !value.Name.Contains("Tests"))
                .Where(value => value.References.Any(reference =>
                    reference.StartsWith(
                        "VirtualLab.OxygenCourse",
                        StringComparison.Ordinal)))
                .Select(value => $"{value.Name} 引用了氧气课程程序集")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                errors,
                Is.Empty,
                "课程实例反向进入了生产模块："
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors));
        }

        private static IReadOnlyDictionary<string, AssemblyDefinition> ReadAssemblyDefinitions()
        {
            var packageRoot = Path.Combine(
                Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")),
                "Packages",
                "com.virtuallab.engine");

            // 同名程序集会使 Unity 的引用解析产生歧义，因此读取时一并拒绝。
            return Directory.GetFiles(
                    packageRoot,
                    "*.asmdef",
                    SearchOption.AllDirectories)
                .Select(path => JsonConvert.DeserializeObject<AssemblyDefinition>(
                    File.ReadAllText(path)))
                .Where(value => value != null)
                .ToDictionary(value => value.Name, StringComparer.Ordinal);
        }

        [Serializable]
        private sealed class AssemblyDefinition
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("references")]
            public string[] References { get; set; } = Array.Empty<string>();
        }
    }
}
