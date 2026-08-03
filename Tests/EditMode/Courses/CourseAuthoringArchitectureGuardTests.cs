using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseAuthoringArchitectureGuardTests
    {
        private static readonly string[] AllowedCourseTables =
        {
            "课程.csv",
            "实验对象.csv",
            "交互规则.csv",
            "学科过程.csv",
            "教学评价.csv",
            "表现覆盖.csv",
            "验收场景.csv",
            "实验流程.csv",
            "高级覆盖.csv"
        };

        [Test]
        public void 课程作者目录只允许受支持的高层表且新旧流程表不能混用()
        {
            var errors = new List<string>();
            foreach (var directory in CourseAuthoringDirectories())
            {
                var csvNames = Directory.GetFiles(directory, "*.csv")
                    .Select(Path.GetFileName)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                var unknown = csvNames.Except(
                    AllowedCourseTables,
                    StringComparer.Ordinal);
                errors.AddRange(unknown.Select(name =>
                    $"{Relative(directory)} 包含旧表或未知表：{name}"));

                if (!csvNames.Contains("课程.csv")
                    || !csvNames.Contains("实验对象.csv"))
                {
                    errors.Add(
                        $"{Relative(directory)} 缺少必需表：课程.csv 或 实验对象.csv");
                }

                if (csvNames.Contains("实验流程.csv")
                    && (csvNames.Contains("教学评价.csv")
                        || csvNames.Contains("验收场景.csv")))
                {
                    errors.Add(
                        $"{Relative(directory)} 同时包含实验流程表和旧教学/验收表。");
                }
            }

            Assert.That(
                errors,
                Is.Empty,
                string.Join(Environment.NewLine, errors));
        }

        [Test]
        public void 生产课程学科过程使用一行一配置简表()
        {
            const string expectedHeader =
                "配置标识,类型,配方,主体,来源,目标,操作名称,协议,参数";
            var errors = new List<string>();
            foreach (var directory in CourseAuthoringDirectories())
            {
                var path = Path.Combine(directory, "学科过程.csv");
                if (!File.Exists(path))
                {
                    continue;
                }

                var header = File.ReadLines(path).FirstOrDefault()
                             ?? string.Empty;
                if (!string.Equals(
                        header.TrimStart('\uFEFF'),
                        expectedHeader,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        $"{Relative(path)} 未使用固定九列学科过程简表。");
                }

                var content = File.ReadAllText(path);
                if (content.Contains("参数.参数名")
                    || content.Contains("参数.参数值")
                    || content.Contains("启动交互")
                    || content.Contains("停止交互"))
                {
                    errors.Add(
                        $"{Relative(path)} 仍包含旧的逐参数或内部交互列。");
                }
            }

            Assert.That(
                errors,
                Is.Empty,
                string.Join(Environment.NewLine, errors));
        }

        [Test]
        public void 编辑器和运行时依赖方向保持单向()
        {
            var errors = new List<string>();
            errors.AddRange(FindText(
                Path.Combine(ProjectRoot, "Packages/com.virtuallab.engine/Editor"),
                "VirtualLab.Chemistry",
                "*.cs",
                "*.asmdef"));
            errors.AddRange(FindText(
                Path.Combine(ProjectRoot, "Packages/com.virtuallab.engine/Runtime"),
                "VirtualLab.Unity.Authoring",
                "*.cs",
                "*.asmdef"));
            errors.AddRange(FindText(
                Path.Combine(ProjectRoot, "Packages/com.virtuallab.engine/Runtime"),
                "Oxygen",
                "*.cs"));
            errors.AddRange(FindText(
                Path.Combine(ProjectRoot, "Packages/com.virtuallab.engine/Runtime"),
                "氧气",
                "*.cs"));

            Assert.That(
                errors,
                Is.Empty,
                "发现反向依赖或课程专用运行时代码："
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors));
        }

        [Test]
        public void 配方不使用脚本模板且示例组标识可读()
        {
            var recipeRoot = Path.Combine(
                ProjectRoot,
                "Packages/com.virtuallab.engine/Editor/Authoring/Recipes");
            var templateUsages = FindText(recipeRoot, "${", "*.cs", "*.csv");
            var actions = File.ReadAllText(Path.Combine(
                recipeRoot,
                "平台通用/操作.csv"));
            var stateChanges = File.ReadAllText(Path.Combine(
                recipeRoot,
                "平台通用/状态变化.csv"));
            var presentations = File.ReadAllText(Path.Combine(
                recipeRoot,
                "平台通用/表现.csv"));

            Assert.That(templateUsages, Is.Empty);
            StringAssert.StartsWith(
                "配方标识,操作名称,操作指令,审核结果,优先级,状态变化,表现反馈",
                actions);
            StringAssert.DoesNotContain("生成项类型", actions);
            StringAssert.DoesNotContain("core.", actions);
            StringAssert.DoesNotContain("chemistry.", actions);
            StringAssert.Contains("状态变化方式", stateChanges);
            StringAssert.DoesNotContain("domain.", stateChanges);
            StringAssert.DoesNotContain("chemistry.", stateChanges);
            StringAssert.Contains("表现方式", presentations);
            StringAssert.DoesNotContain("interaction.", presentations);
            StringAssert.DoesNotContain("transform.", presentations);
            StringAssert.DoesNotContain("renderer.", presentations);
            StringAssert.Contains("通用.抓取", actions);
            StringAssert.Contains("结果.建立持有", actions);
        }

        [Test]
        public void 生产配置除资源定位路径外只使用自然中文()
        {
            var roots = new[]
            {
                Path.Combine(
                    ProjectRoot,
                    "Packages/com.virtuallab.engine/Samples~/OxygenCourse/Courses"),
                Path.Combine(
                    ProjectRoot,
                    "Packages/com.virtuallab.engine/Editor/Authoring"),
                Path.Combine(
                    ProjectRoot,
                    "Packages/com.virtuallab.engine/Samples~/Chemistry/Editor/Authoring")
            };
            var violations = roots
                .Where(Directory.Exists)
                .SelectMany(root => Directory.GetFiles(
                    root,
                    "*.csv",
                    SearchOption.AllDirectories))
                .Select(path => new
                {
                    Path = path,
                    Content = Regex.Replace(
                        File.ReadAllText(path),
                        @"(?:Assets|Packages|课程目录)/[^,\r\n""]+\.[A-Za-z0-9]+",
                        string.Empty)
                })
                .Where(value => Regex.IsMatch(value.Content, "[A-Za-z]"))
                .Select(value => Relative(value.Path))
                .ToArray();

            Assert.That(
                violations,
                Is.Empty,
                "作者维护的配置表仍包含英文或内部协议："
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void 只读诊断不会成为生产输入()
        {
            var violations = ProductionCodeFiles()
                .Where(path =>
                {
                    var content = File.ReadAllText(path);
                    return content.Contains("Generated/Diagnostics")
                           || content.Contains(@"Generated\Diagnostics");
                })
                .Select(Relative)
                .ToArray();

            Assert.That(violations, Is.Empty);
        }

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));

        private static IEnumerable<string> CourseAuthoringDirectories()
        {
            var coursesRoot = Path.Combine(
                ProjectRoot,
                "Packages/com.virtuallab.engine/Samples~/OxygenCourse/Courses");
            return Directory.Exists(coursesRoot)
                ? Directory.GetDirectories(
                    coursesRoot,
                    "Authoring",
                    SearchOption.AllDirectories)
                : Array.Empty<string>();
        }

        private static IEnumerable<string> ProductionCodeFiles()
        {
            foreach (var rootName in new[] { "Assets", "Packages" })
            {
                var root = Path.Combine(ProjectRoot, rootName);
                foreach (var path in Directory.GetFiles(
                             root,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    var normalized = path.Replace('\\', '/');
                    if (!normalized.Contains("/Tests/")
                        && !normalized.Contains("/Library/"))
                    {
                        yield return path;
                    }
                }
            }
        }

        private static IEnumerable<string> FindText(
            string root,
            string text,
            params string[] patterns)
        {
            if (!Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            return patterns
                .SelectMany(pattern => Directory.GetFiles(
                    root,
                    pattern,
                    SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => File.ReadAllText(path).Contains(text))
                .Select(path => $"{Relative(path)}: {text}")
                .ToArray();
        }

        private static string Relative(string path) =>
            path.Substring(ProjectRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
    }
}
