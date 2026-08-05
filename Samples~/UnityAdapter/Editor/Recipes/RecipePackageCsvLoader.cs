using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Unity.Authoring.Recipes
{
    /// <summary>
    /// 平台和学科共享的七表加载器；所有中文枚举在此统一映射为强类型契约。
    /// </summary>
    public sealed class RecipePackageCsvLoader
    {
        private readonly PresentationEffectCatalog _presentationCatalog;

        public RecipePackageCsvLoader()
            : this(BuiltInPresentationEffectCatalog.Create())
        {
        }

        internal RecipePackageCsvLoader(
            PresentationEffectCatalog presentationCatalog)
        {
            // 表现目录同时保存中文名称、稳定协议和参数契约；加载器不维护副本。
            _presentationCatalog = presentationCatalog
                ?? throw new ArgumentNullException(
                    nameof(presentationCatalog));
        }

        public RecipePackage Load(
            string packageId,
            RecipeLayer layer,
            string directory,
            IDisciplineRecordCompiler disciplineRecordCompiler = null)
        {
            var read = RecipePackageSource.FromDirectory(directory).ReadTables();
            if (!read.IsSuccess)
            {
                throw new InvalidOperationException(string.Join(
                    Environment.NewLine,
                    read.Diagnostics.Select(value =>
                        $"{value.FileName}({value.Line},{value.Column})：{value.Reason}")));
            }

            var recipeTable = Required(read, "配方.csv");
            var parameterTable = Required(read, "参数契约.csv");
            var prefabTable = Required(
                read,
                RecipePackageSource.RecipePrefabRequirementFileName);
            var actionTable = Required(read, "操作.csv");
            var conditionTable = Required(read, "条件.csv");
            var mutationTable = Required(read, "状态变化.csv");
            var presentationTable = Required(read, "表现.csv");

            var conditions = conditionTable.Rows.Select(row =>
                new RecipeConditionDefinition(
                    row["定义ID"].Trim(),
                    recipeId: row["配方ID"].Trim(),
                    fieldId: row["字段"].Trim(),
                    operatorId: row["比较"].Trim(),
                    expectedValue: row["值"].Trim(),
                    unitId: row["单位"].Trim(),
                    rejectionCode: row["拒绝代码"].Trim(),
                    appliesToOperationName: row["适用操作"].Trim(),
                    source: Source(
                        packageId,
                        layer,
                        "条件.csv",
                        conditionTable,
                        row,
                        "定义ID",
                        row["定义ID"]))).ToArray();
            var operations = mutationTable.Rows
                .GroupBy(
                    row => row["配方ID"] + "\u001F" + row["定义ID"],
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new RecipeOperationDefinition(
                        first["定义ID"].Trim(),
                        group.Where(row =>
                                !string.IsNullOrWhiteSpace(row["绑定种类"]))
                            .Select(row => new RecipeValueBinding(
                                ParameterName(row["参数名"]),
                                Binding(row["绑定种类"]),
                                row["绑定参数"].Trim())),
                        first["配方ID"].Trim(),
                        StateOperation(first["状态变化方式"]),
                        group.Where(row =>
                                !string.IsNullOrWhiteSpace(row["常量值"]))
                            .Select(row => new KeyValuePair<string, string>(
                                ParameterName(row["参数名"]),
                                ConstantValue(
                                    ParameterName(row["参数名"]),
                                    row["常量值"]))),
                        Source(
                            packageId,
                            layer,
                            "状态变化.csv",
                            mutationTable,
                            first,
                            "定义ID",
                            first["定义ID"]));
                }).ToArray();
            var results = mutationTable.Rows
                .GroupBy(
                    row => row["配方ID"] + "\u001F" + row["结果配方"],
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new RecipeResultDefinition(
                        first["结果配方"].Trim(),
                        recipeId: first["配方ID"].Trim(),
                        operationIds: group
                            .Select(row => row["定义ID"].Trim())
                            .Distinct(StringComparer.Ordinal),
                        source: Source(
                            packageId,
                            layer,
                            "状态变化.csv",
                            mutationTable,
                            first,
                            "结果配方",
                            first["结果配方"]));
                }).ToArray();

            return new RecipePackage(
                packageId,
                layer,
                recipes: recipeTable.Rows.Select(row => new RecipeDefinition(
                    row["配方ID"].Trim(),
                    MatchKind(row["匹配类型"]),
                    row["匹配特征"].Trim(),
                    row["扩展配方"].Trim(),
                    row["替换配方"].Trim(),
                    Safety(row["安全级别"]),
                    Array.Empty<string>(),
                    Source(
                        packageId,
                        layer,
                        "配方.csv",
                        recipeTable,
                        row,
                        "配方ID",
                        row["配方ID"]),
                    Pair(row))).ToArray(),
                parameters: parameterTable.Rows.Select(row =>
                    new RecipeParameterContract(
                        row["配方ID"].Trim(),
                        ParameterName(row["参数名"]),
                        ParameterType(row["参数类型"]),
                        Yes(row["必填"]),
                        row["单位"].Trim(),
                        NullableNumber(row["最小值"]),
                        NullableNumber(row["最大值"]),
                        row["默认值"].Trim(),
                        Yes(row["课程可删除"]),
                        Safety(row["安全级别"]))).ToArray(),
                prefabContracts: prefabTable.Rows.Select(row =>
                    new RecipePrefabContract(
                        row["配方ID"].Trim(),
                        row["契约类型"].Trim(),
                        row["标识"].Trim(),
                        Source(
                            packageId,
                            layer,
                            RecipePackageSource.RecipePrefabRequirementFileName,
                            prefabTable,
                            row,
                            "标识",
                            row["标识"]))).ToArray(),
                actions: actionTable.Rows.Select(row =>
                    new RecipeActionDefinition(
                        row["配方ID"].Trim(),
                        row["操作名称"].Trim(),
                        conditions.Where(value =>
                                value.RecipeId == row["配方ID"].Trim()
                                && value.AppliesToOperationName ==
                                row["操作名称"].Trim())
                            .Select(value => value.ConditionId),
                        Split(row["状态变化"]),
                        Split(row["表现反馈"]),
                        RequiredProtocol(row["抽象操作"], "抽象操作"),
                        ActionLifecycle(row["生命周期"]),
                        RequiredProtocol(row["执行方式"], "执行方式"),
                        ActionPhase(row["阶段"]),
                        SemanticCommand(row["操作指令"]),
                        Integer(row["优先级"]),
                        row["审核结果"].Trim(),
                        Source(
                            packageId,
                            layer,
                            "操作.csv",
                            actionTable,
                            row,
                            "操作名称",
                            row["操作名称"]))).ToArray(),
                conditions: conditions,
                results: results,
                presentations: presentationTable.Rows
                    .GroupBy(
                        row => row["配方ID"] + "\u001F" + row["定义ID"],
                        StringComparer.Ordinal)
                    .Select(group =>
                    {
                        var first = group.First();
                        return new RecipePresentationDefinition(
                            first["定义ID"].Trim(),
                            bindings: group
                                .Where(row => row.Has("参数来源")
                                    && !string.IsNullOrWhiteSpace(
                                        row["参数来源"]))
                                .Select(row => new RecipeValueBinding(
                                    ParameterName(row["参数名"]),
                                    Binding(row["参数来源"]),
                                    row["载荷键"].Trim())),
                            recipeId: first["配方ID"].Trim(),
                            protocolId: PresentationProtocol(
                                first["表现方式"]),
                            createsState: Yes(first["创建状态"]),
                            stateSuffix: first["状态后缀"].Trim(),
                            stateConditionIds: Split(first["状态条件"]),
                            targetEntityBinding:
                                Binding(first["目标绑定"]),
                            locationKind:
                                PresentationLocation(first["作用位置"]),
                            locationIdBinding:
                                LocationBinding(first["位置绑定"]),
                            locationId: first["位置ID"].Trim(),
                            parameterValues: group
                                .Where(row => !string.IsNullOrWhiteSpace(
                                    row["参数名"])
                                    && (!row.Has("参数来源")
                                        || string.IsNullOrWhiteSpace(
                                            row["参数来源"])))
                                .Select(row =>
                                    new KeyValuePair<
                                        string,
                                        StructuredValue>(
                                        ParameterName(row["参数名"]),
                                        PresentationValue(
                                            row["参数类型"],
                                            row["参数值"]))),
                            lifecycle: PresentationLifecycle(
                                first["生命周期"]),
                            source: Source(
                                packageId,
                                layer,
                                "表现.csv",
                                presentationTable,
                                first,
                                "定义ID",
                                first["定义ID"]));
                    })
                    .ToArray(),
                operations: operations,
                disciplineRecordCompiler: disciplineRecordCompiler);
        }

        private static StrictCsvReadResult Required(
            RecipePackageSourceReadResult read,
            string fileName) =>
            read.Tables.TryGetValue(fileName, out var table)
                ? table
                : throw new InvalidOperationException(
                    $"配方包缺少“{fileName}”。");

        private static RecipeMatchKind MatchKind(string value) =>
            value.Trim() switch
            {
                "单实体" => RecipeMatchKind.SingleEntity,
                "兼容双实体" => RecipeMatchKind.CompatiblePair,
                "过程" => RecipeMatchKind.Process,
                _ => throw new InvalidOperationException(
                    $"未注册匹配类型“{value}”。")
            };

        private static RecipeSafetyLevel Safety(string value) =>
            value.Trim() switch
            {
                "不可弱化" => RecipeSafetyLevel.NonWeakenable,
                "课程可收紧" => RecipeSafetyLevel.CourseMayTighten,
                "课程可替换表现" =>
                    RecipeSafetyLevel.CourseMayReplacePresentation,
                _ => throw new InvalidOperationException(
                    $"未注册安全级别“{value}”。")
            };

        private static RecipeParameterType ParameterType(string value) =>
            value.Trim() switch
            {
                "文本" => RecipeParameterType.Text,
                "布尔" => RecipeParameterType.Boolean,
                "整数" => RecipeParameterType.Integer,
                "数字" => RecipeParameterType.Number,
                "实体标识" or "实体ID" => RecipeParameterType.EntityId,
                "端口标识" or "端口ID" => RecipeParameterType.PortId,
                _ => throw new InvalidOperationException(
                    $"未注册参数类型“{value}”。")
            };

        private static string SemanticCommand(string value)
        {
            var name = value.Trim();
            if (name.Length == 0)
            {
                throw new InvalidOperationException("操作指令不能为空。");
            }

            return name;
        }

        private static string RequiredProtocol(string value, string name)
        {
            var configured = value.Trim();
            if (configured.Length == 0)
            {
                throw new InvalidOperationException($"{name}不能为空。");
            }

            return configured;
        }

        private static SemanticActionLifecycle ActionLifecycle(string value) =>
            value.Trim() switch
            {
                "即时" => SemanticActionLifecycle.Instant,
                "持续" => SemanticActionLifecycle.Continuous,
                "操纵" => SemanticActionLifecycle.Manipulation,
                _ => throw new InvalidOperationException(
                    $"未注册操作生命周期“{value}”；只允许即时、持续或操纵。")
            };

        private static SemanticActionPhase ActionPhase(string value) =>
            value.Trim() switch
            {
                "开始" => SemanticActionPhase.Start,
                "观测" => SemanticActionPhase.Observe,
                "完成" => SemanticActionPhase.Complete,
                "取消" => SemanticActionPhase.Cancel,
                _ => throw new InvalidOperationException(
                    $"未注册操作阶段“{value}”；只允许开始、观测、完成或取消。")
            };

        private static string StateOperation(string value)
        {
            var name = value.Trim();
            if (name.Length == 0)
            {
                throw new InvalidOperationException("状态变化方式不能为空。");
            }

            return name;
        }

        private static string ConstantValue(
            string parameterName,
            string configured)
        {
            var value = configured?.Trim() ?? string.Empty;
            return value;
        }

        private static string ParameterName(string configured) =>
            configured?.Trim() ?? string.Empty;

        private string PresentationProtocol(string value)
        {
            var name = value.Trim();
            try
            {
                return _presentationCatalog
                    .RequireByChineseName(name)
                    .ProtocolId;
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidOperationException(
                    $"表现方式“{name}”未注册。请使用统一表现目录中的中文名称。",
                    exception);
            }
        }

        private static RecipeBindingKind Binding(string value) =>
            value.Trim() switch
            {
                "当前实体" => RecipeBindingKind.CurrentEntity,
                "动作操作者" => RecipeBindingKind.ActionActor,
                "动作来源" => RecipeBindingKind.ActionSource,
                "动作目标" => RecipeBindingKind.ActionTarget,
                "来源端口" => RecipeBindingKind.MatchedSourcePort,
                "目标端口" => RecipeBindingKind.MatchedTargetPort,
                "实体参数" => RecipeBindingKind.EntityParameter,
                "信号载荷" => RecipeBindingKind.SignalPayload,
                _ => throw new InvalidOperationException(
                    $"未注册绑定种类“{value}”。")
            };

        private static RecipeBindingKind LocationBinding(string value) =>
            value.Trim() switch
            {
                "固定" or "" => RecipeBindingKind.CurrentEntity,
                "来源端口" => RecipeBindingKind.MatchedSourcePort,
                "目标端口" => RecipeBindingKind.MatchedTargetPort,
                _ => throw new InvalidOperationException(
                    $"未注册表现位置绑定“{value}”。")
            };

        private static CoursePresentationLocationKind PresentationLocation(
            string value) =>
            value.Trim() switch
            {
                "实体根节点" => CoursePresentationLocationKind.EntityRoot,
                "表现插槽" =>
                    CoursePresentationLocationKind.PresentationSlot,
                "语义锚点" =>
                    CoursePresentationLocationKind.SemanticAnchor,
                "全局接收器" =>
                    CoursePresentationLocationKind.GlobalReceiver,
                _ => throw new InvalidOperationException(
                    $"未注册表现作用位置“{value}”。")
            };

        private static CoursePresentationLifecycle PresentationLifecycle(
            string value) =>
            value.Trim() switch
            {
                "一次性" => CoursePresentationLifecycle.OneShot,
                "激活期间" => CoursePresentationLifecycle.WhileActive,
                "持续至替换" =>
                    CoursePresentationLifecycle.UntilReplaced,
                _ => throw new InvalidOperationException(
                    $"未注册表现生命周期“{value}”。")
            };

        private static StructuredValue PresentationValue(
            string type,
            string value) =>
            type.Trim() switch
            {
                "文本" or "资源" => StructuredValue.FromText(value.Trim()),
                "数值" => StructuredValue.FromNumber(
                    double.Parse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture)),
                "布尔" => StructuredValue.FromBoolean(Yes(value)),
                _ => throw new InvalidOperationException(
                    $"未注册表现参数类型“{type}”。")
            };

        private static CompatiblePairContract Pair(StrictCsvRow row)
        {
            if (string.IsNullOrWhiteSpace(row["兼容方式"]))
            {
                return null;
            }

            var kind = row["兼容方式"].Trim() switch
            {
                "同组" => PairCompatibilityKind.EqualGroup,
                "同值" => PairCompatibilityKind.EqualValue,
                "课程允许组合" => PairCompatibilityKind.CourseAllowedPair,
                _ => throw new InvalidOperationException(
                    $"未注册兼容方式“{row["兼容方式"]}”。")
            };
            return new CompatiblePairContract(
                row["来源特征"].Trim(),
                row["目标特征"].Trim(),
                row["来源端口参数"].Trim(),
                row["目标端口参数"].Trim(),
                kind,
                !row.Has("生成端口") || Yes(row["生成端口"]));
        }

        private static bool Yes(string value) =>
            value.Trim() switch
            {
                "是" => true,
                "否" or "" => false,
                _ => throw new InvalidOperationException(
                    $"布尔值“{value}”必须是“是”或“否”。")
            };

        private static int Integer(string value) =>
            int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"“{value}”不是整数。");

        private static double? NullableNumber(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                    ? parsed
                    : throw new InvalidOperationException(
                        $"“{value}”不是数字。");

        private static IReadOnlyList<string> Split(string value) =>
            (value ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .ToArray();

        private static ConfigurationSource Source(
            string packageId,
            RecipeLayer layer,
            string fileName,
            StrictCsvReadResult table,
            StrictCsvRow row,
            string columnName,
            string configurationId)
        {
            var column = table.Headers
                .Select((value, index) => new { value, index })
                .FirstOrDefault(value => value.value == columnName)
                ?.index + 1 ?? 1;
            return new ConfigurationSource(
                layer == RecipeLayer.Platform
                    ? ConfigurationLayer.Platform
                    : ConfigurationLayer.Discipline,
                packageId,
                fileName,
                row.LineNumber,
                column,
                configurationId.Trim());
        }
    }
}
