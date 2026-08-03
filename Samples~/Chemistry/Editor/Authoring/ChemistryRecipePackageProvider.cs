using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Domain.Matter;
using VirtualLab.Kernel;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Chemistry.Authoring
{
    /// <summary>
    /// 定位已导入的化学 Sample。通过脚本资产反查根目录，避免绑定包名、版本号或
    /// Package Manager 的安装位置。
    /// </summary>
    public static class ChemistrySamplePaths
    {
        private const string MarkerSuffix =
            "/Editor/Authoring/ChemistryRecipePackageProvider.cs";

        public static string Root
        {
            get
            {
                var matches = AssetDatabase.FindAssets(
                        "ChemistryRecipePackageProvider t:MonoScript")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(value => value.Replace('\\', '/'))
                    .Where(value => value.EndsWith(
                        MarkerSuffix,
                        StringComparison.Ordinal))
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (matches.Length != 1)
                {
                    throw new InvalidOperationException(
                        matches.Length == 0
                            ? "无法定位已导入的化学实验 Sample。"
                            : "检测到多个化学实验 Sample，请只保留一份导入副本。");
                }

                return matches[0].Substring(
                    0,
                    matches[0].Length - MarkerSuffix.Length);
            }
        }
    }

    public sealed class ChemistryRecipePackageProvider :
        IRecipePackageProvider
    {
        private const string Id = "化学基础";

        public string PackageId => Id;

        public RecipePackage Load()
        {
            var authoringRoot = Path.Combine(
                ChemistrySamplePaths.Root,
                "Editor",
                "Authoring");
            var knowledge = ChemistryKnowledgeCatalogLoader.Load(
                Path.Combine(
                    authoringRoot,
                    "Knowledge",
                    "化学知识.csv"));
            return new RecipePackageCsvLoader().Load(
                Id,
                RecipeLayer.Discipline,
                Path.Combine(
                    authoringRoot,
                    "Recipes",
                    "化学基础"),
                new ChemistryDisciplineRecordCompiler(knowledge));
        }
    }

    internal sealed class ChemistryDisciplineRecordCompiler :
        IDisciplineRecordCompiler
    {
        private readonly ChemistryKnowledgeCatalog _knowledge;

        public ChemistryDisciplineRecordCompiler(
            ChemistryKnowledgeCatalog knowledge = null)
        {
            _knowledge = knowledge
                         ?? new ChemistryKnowledgeCatalog(
                             Array.Empty<CourseDisciplineProcessBlueprint>());
        }

        public DisciplineRecordCompilationResult Compile(
            CourseBlueprint blueprint)
        {
            var mutationOverrides = CompileMutationOverrides(blueprint);
            var mutationAdditions = CompileMutationAdditions(blueprint);
            var courseRecords = blueprint.DisciplineProcesses
                .Where(value => value.DisciplineRecipeId == "化学.运行配置")
                .ToArray();
            var records = MergeKnowledge(
                _knowledge.Records,
                courseRecords);
            if (records.Length == 0)
            {
                return new DisciplineRecordCompilationResult(
                    Array.Empty<NormalizedItem<GeneratedCourseArtifact>>(),
                    Array.Empty<CourseCompilationDiagnostic>(),
                    mutationOverrides,
                    mutationAdditions);
            }

            var substances = records
                .Where(value => value.RecordType == "物质")
                .Select(value => new ChemistrySubstanceDefinition(
                    value.SubjectEntityId,
                    Parameter(value, "显示名称"),
                    Phase(Parameter(value, "物态")),
                    Decimal(Parameter(value, "摩尔质量")),
                    ChemistryMolarMassUnit.GramPerMole))
                .ToList();
            var terms = records
                .Where(value => value.RecordType == "反应项")
                .GroupBy(value => value.SubjectEntityId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.ToArray(),
                    StringComparer.Ordinal);
            var reactions = records
                .Where(value => value.RecordType == "反应")
                .Select(value =>
                {
                    var reactionId = value.SubjectEntityId;
                    var reactionTerms = terms.TryGetValue(
                            reactionId,
                            out var found)
                        ? found
                        : Array.Empty<CourseDisciplineProcessBlueprint>();
                    return new ChemistryReactionDefinition(
                        reactionId,
                        Terms(reactionTerms, "反应物"),
                        Terms(reactionTerms, "产物"),
                        ProcessKind(Parameter(value, "过程类型")),
                        Decimal(Parameter(value, "最低温度")),
                        Decimal(Parameter(value, "每刻反应量")),
                        Boolean(Parameter(value, "需要点燃")));
                })
                .ToList();
            var initial = records
                .Where(value => value.RecordType == "初始物质")
                .Select(value => new ChemistryInitialSubstance(
                    value.SubjectEntityId,
                    value.SourceEntityId,
                    Decimal(Parameter(value, "数量")),
                    UnitValue(Parameter(value, "单位")),
                    Phase(Parameter(value, "物态")),
                    Decimal(Parameter(value, "温度")),
                    ChemistryTemperatureUnit.Celsius))
                .ToList();
            var configuration = new ChemistryRuntimeConfiguration(
                substances,
                reactions,
                initial,
                new List<ChemistryEntityCapabilityBinding>());
            var payload = new ChemistryConfigurationCodec()
                .EncodeCourseConfiguration(
                    configuration,
                    blueprint.Objects.Select(value => value.EntityId));
            var identity = new GeneratedItemIdentity(
                "化学.运行配置",
                blueprint.Course.CourseId,
                string.Empty,
                "学科产物",
                ChemistryConfigurationKeys.Artifacts.RuntimeConfiguration);
            var sources = records
                .Select(value => value.Source)
                .Concat(new[]
                {
                    new ConfigurationSource(
                        ConfigurationLayer.Discipline,
                        "化学基础",
                        "学科记录编译器",
                        1,
                        1,
                        ChemistryConfigurationKeys.Artifacts.RuntimeConfiguration)
                });
            return new DisciplineRecordCompilationResult(
                new[]
                {
                    new NormalizedItem<GeneratedCourseArtifact>(
                        identity,
                        new GeneratedCourseArtifact(
                            ChemistryConfigurationKeys.Artifacts.RuntimeConfiguration,
                            "化学运行配置.json",
                            Encoding.UTF8.GetBytes(payload),
                            CreateOverviewEntries(configuration, records)),
                        sources)
                },
                Array.Empty<CourseCompilationDiagnostic>(),
                mutationOverrides,
                mutationAdditions);
        }

        private static IReadOnlyList<GeneratedCourseOverviewEntry>
            CreateOverviewEntries(
                ChemistryRuntimeConfiguration configuration,
                IEnumerable<CourseDisciplineProcessBlueprint> records)
        {
            var substanceNames = configuration.Substances.ToDictionary(
                value => value.Id,
                value => value.DisplayName,
                StringComparer.Ordinal);
            var reactionNames = records
                .Where(value => value.RecordType == "反应")
                .GroupBy(value => value.SubjectEntityId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => ReadableKnowledgeName(
                        value.Last().DefinitionId,
                        value.Key),
                    StringComparer.Ordinal);
            var entries = new List<GeneratedCourseOverviewEntry>();
            entries.AddRange(configuration.Substances.Select(value =>
                new GeneratedCourseOverviewEntry(
                    "物质",
                    value.Id,
                    value.DisplayName,
                    Fields(
                        ("物态", PhaseName(value.Phase)),
                        ("摩尔质量", Number(value.MolarMassValue) + " 克/摩尔")))));
            entries.AddRange(configuration.InitialSubstances.Select(value =>
                new GeneratedCourseOverviewEntry(
                    "初始内容物",
                    value.EntityId + "." + value.SubstanceId,
                    value.EntityId + "中的" + SubstanceName(
                        substanceNames,
                        value.SubstanceId),
                    Fields(
                        ("数量", Number(value.QuantityValue) + " "
                                 + UnitName(value.QuantityUnit)),
                        ("物态", PhaseName(value.Phase)),
                        ("温度", Number(value.TemperatureValue) + " ℃")),
                    value.EntityId)));
            entries.AddRange(configuration.Reactions.Select(value =>
                new GeneratedCourseOverviewEntry(
                    "化学反应",
                    value.Id,
                    reactionNames.TryGetValue(value.Id, out var name)
                        ? name
                        : value.Id,
                    Fields(
                        ("过程", ProcessName(value.ProcessKind)),
                        ("反应物", Terms(value.Reactants, substanceNames)),
                        ("产物", Terms(value.Products, substanceNames)),
                        ("发生条件", "最低 "
                                     + Number(value.MinimumTemperatureCelsius)
                                     + " ℃"
                                     + (value.RequiresIgnition
                                         ? "，需要点燃"
                                         : string.Empty))))));
            return entries;
        }

        private static IReadOnlyList<KeyValuePair<string, string>> Fields(
            params (string Name, string Value)[] values) =>
            values.Select(value =>
                    new KeyValuePair<string, string>(
                        value.Name,
                        value.Value))
                .ToArray();

        private static string Terms(
            IEnumerable<ChemistryReactionTerm> terms,
            IReadOnlyDictionary<string, string> substanceNames) =>
            string.Join(
                " + ",
                terms.Select(value =>
                    SubstanceName(substanceNames, value.SubstanceId)
                    + " " + Number(value.QuantityValue)
                    + " " + UnitName(value.QuantityUnit)));

        private static string SubstanceName(
            IReadOnlyDictionary<string, string> names,
            string substanceId) =>
            names.TryGetValue(substanceId, out var name)
                ? name
                : substanceId;

        private static string ReadableKnowledgeName(
            string definitionId,
            string fallback)
        {
            var value = (definitionId ?? string.Empty).Trim();
            var separator = value.LastIndexOf('.');
            return separator >= 0 && separator + 1 < value.Length
                ? value.Substring(separator + 1)
                : string.IsNullOrEmpty(value)
                    ? fallback
                    : value;
        }

        private static string Number(decimal value) =>
            value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string UnitName(Unit value) => value switch
        {
            Unit.Gram => "克",
            Unit.Millilitre => "毫升",
            _ => value.ToString()
        };

        private static string PhaseName(MatterPhase value) => value switch
        {
            MatterPhase.Solid => "固体",
            MatterPhase.Liquid => "液体",
            MatterPhase.Gas => "气体",
            _ => value.ToString()
        };

        private static string ProcessName(
            ChemistryReactionProcessKind value) => value switch
        {
            ChemistryReactionProcessKind.ThermalDecomposition => "热分解",
            ChemistryReactionProcessKind.Combustion => "燃烧",
            ChemistryReactionProcessKind.Mixing => "混合",
            _ => value.ToString()
        };

        private static CourseDisciplineProcessBlueprint[] MergeKnowledge(
            IEnumerable<CourseDisciplineProcessBlueprint> shared,
            IEnumerable<CourseDisciplineProcessBlueprint> course)
        {
            var sharedDefinitions = (shared
                                     ?? Array.Empty<CourseDisciplineProcessBlueprint>())
                .Where(IsKnowledgeRecord);
            var courseArray = (course
                               ?? Array.Empty<CourseDisciplineProcessBlueprint>())
                .ToArray();
            var courseDefinitions = courseArray.Where(IsKnowledgeRecord);
            var selectedKnowledge = sharedDefinitions
                .Concat(courseDefinitions)
                .GroupBy(KnowledgeKey, StringComparer.Ordinal)
                // 课程记录位于共享记录之后，因此同 ID 时自然覆盖学科默认知识。
                .Select(group => group.Last());
            return selectedKnowledge
                .Concat(courseArray.Where(value => !IsKnowledgeRecord(value)))
                .ToArray();
        }

        private static bool IsKnowledgeRecord(
            CourseDisciplineProcessBlueprint value) =>
            value.RecordType == "物质"
            || value.RecordType == "反应"
            || value.RecordType == "反应项";

        private static string KnowledgeKey(
            CourseDisciplineProcessBlueprint value) =>
            string.Join(
                "\u001f",
                value.RecordType,
                value.SubjectEntityId,
                value.SourceEntityId,
                value.TargetEntityId);

        private static IReadOnlyList<DisciplineMutationAddition>
            CompileMutationAdditions(CourseBlueprint blueprint)
        {
            return blueprint.DisciplineProcesses
                .Where(value => value.RecordType == "过程操作")
                .GroupBy(
                    value => string.Join(
                        "\u001f",
                        value.DisciplineRecipeId,
                        value.SourceEntityId,
                        value.TargetEntityId,
                        value.SubjectEntityId,
                        value.StartInteractionId,
                        value.StopInteractionId),
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    var rows = group.ToArray();
                    var first = rows[0];
                    if (string.IsNullOrWhiteSpace(first.StopInteractionId))
                    {
                        throw new InvalidOperationException(
                            $"过程操作“{first.DefinitionId}”缺少停止交互列中的操作协议。");
                    }

                    return new DisciplineMutationAddition(
                        first.DisciplineRecipeId,
                        first.SourceEntityId,
                        first.TargetEntityId,
                        first.SubjectEntityId,
                        first.StartInteractionId,
                        first.StopInteractionId,
                        CompileParameters(rows),
                        rows.Select(value => value.Source));
                })
                .ToArray();
        }

        private static IReadOnlyList<DisciplineMutationOverride>
            CompileMutationOverrides(CourseBlueprint blueprint)
        {
            return blueprint.DisciplineProcesses
                .Where(value => value.RecordType == "过程参数")
                .GroupBy(
                    value => string.Join(
                        "\u001f",
                        value.DisciplineRecipeId,
                        value.SourceEntityId,
                        value.TargetEntityId,
                        value.SubjectEntityId),
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    var rows = group.ToArray();
                    var parameters = CompileParameters(rows);

                    var first = rows[0];
                    var operationIds = rows
                        .Select(value => OptionalParameter(value, "操作协议"))
                        .Where(value => !string.IsNullOrEmpty(value))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (operationIds.Length > 1)
                    {
                        throw new InvalidOperationException(
                            $"过程参数“{first.DefinitionId}”设置了互相冲突的操作协议。");
                    }

                    return new DisciplineMutationOverride(
                        first.DisciplineRecipeId,
                        first.SourceEntityId,
                        first.TargetEntityId,
                        first.SubjectEntityId,
                        operationIds.SingleOrDefault() ?? string.Empty,
                        parameters,
                        rows.Select(value => value.Source));
                })
                .ToArray();
        }

        private static IReadOnlyDictionary<string, string> CompileParameters(
            IEnumerable<CourseDisciplineProcessBlueprint> records)
        {
            var parameters = new Dictionary<string, string>(
                StringComparer.Ordinal);
            foreach (var row in records)
            {
                foreach (var pair in row.Parameters.OrderBy(
                             value => value.Key,
                             StringComparer.Ordinal))
                {
                    if (pair.Key == "参数.操作协议")
                    {
                        continue;
                    }

                    var name = pair.Key.StartsWith(
                        "参数.",
                        StringComparison.Ordinal)
                        ? pair.Key.Substring("参数.".Length)
                        : pair.Key;
                    var value = pair.Value.RawValue.Trim();
                    if (string.IsNullOrWhiteSpace(name)
                        || string.IsNullOrWhiteSpace(value))
                    {
                        throw new InvalidOperationException(
                            $"学科过程“{row.DefinitionId}”包含空参数名或空参数值。");
                    }

                    if (!parameters.TryAdd(name, value))
                    {
                        throw new InvalidOperationException(
                            $"学科过程“{row.DefinitionId}”重复设置参数“{name}”。");
                    }
                }
            }

            return parameters;
        }

        private static List<ChemistryReactionTerm> Terms(
            IEnumerable<CourseDisciplineProcessBlueprint> records,
            string role) =>
            records
                .Where(value => value.TargetEntityId == role)
                .Select(value => new ChemistryReactionTerm(
                    value.SourceEntityId,
                    Decimal(Parameter(value, "数量")),
                    UnitValue(Parameter(value, "单位")),
                    Phase(Parameter(value, "物态")),
                    Decimal(Parameter(value, "每单位克数"))))
                .ToList();

        private static string Parameter(
            CourseDisciplineProcessBlueprint record,
            string name)
        {
            var key = "参数." + name;
            if (!record.Parameters.TryGetValue(key, out var value)
                || string.IsNullOrWhiteSpace(value.RawValue))
            {
                throw new InvalidOperationException(
                    $"学科记录“{record.DefinitionId}”缺少参数“{name}”。");
            }

            return value.RawValue.Trim();
        }

        private static string OptionalParameter(
            CourseDisciplineProcessBlueprint record,
            string name)
        {
            var key = "参数." + name;
            return record.Parameters.TryGetValue(key, out var value)
                ? value.RawValue.Trim()
                : string.Empty;
        }

        private static decimal Decimal(string value) =>
            decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"“{value}”不是有效数字。");

        private static bool Boolean(string value) =>
            value switch
            {
                "是" => true,
                "否" => false,
                _ => throw new InvalidOperationException(
                    $"“{value}”必须填写“是”或“否”。")
            };

        private static MatterPhase Phase(string value)
        {
            switch (value)
            {
                case "固体":
                    return MatterPhase.Solid;
                case "液体":
                    return MatterPhase.Liquid;
                case "气体":
                    return MatterPhase.Gas;
            }

            return Enum.TryParse(value, true, out MatterPhase parsed)
                   && Enum.IsDefined(typeof(MatterPhase), parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"物态“{value}”未注册。");
        }

        private static Unit UnitValue(string value)
        {
            switch (value)
            {
                case "克":
                    return Unit.Gram;
                case "毫升":
                    return Unit.Millilitre;
            }

            return Enum.TryParse(value, true, out Unit parsed)
                   && Enum.IsDefined(typeof(Unit), parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"单位“{value}”未注册。");
        }

        private static ChemistryReactionProcessKind ProcessKind(
            string value)
        {
            switch (value)
            {
                case "热分解":
                    return ChemistryReactionProcessKind.ThermalDecomposition;
                case "燃烧":
                    return ChemistryReactionProcessKind.Combustion;
                case "混合":
                    return ChemistryReactionProcessKind.Mixing;
            }

            return Enum.TryParse(
                value,
                true,
                out ChemistryReactionProcessKind parsed)
                   && Enum.IsDefined(
                       typeof(ChemistryReactionProcessKind),
                       parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"反应过程类型“{value}”未注册。");
        }
    }
}
