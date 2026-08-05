using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Presentation;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.Unity.Authoring.Recipes
{
    public sealed class CourseOverrideResult
    {
        internal CourseOverrideResult(
            NormalizedCourseModel model,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Model = model;
            Diagnostics = diagnostics.ToArray();
        }

        public bool IsSuccess => Diagnostics.All(value =>
            value.Severity != CourseDiagnosticSeverity.Error);
        public NormalizedCourseModel Model { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// 课程只能在共享配方结果上追加限制、明确禁用或替换表现。
    /// </summary>
    public sealed class CourseOverrideApplier
    {
        private readonly PresentationEffectCatalog _presentationCatalog;

        public CourseOverrideApplier(
            PresentationEffectCatalog presentationCatalog = null)
        {
            _presentationCatalog = presentationCatalog
                ?? BuiltInPresentationEffectCatalog.Create();
        }

        public CourseOverrideResult Apply(
            CourseBlueprint blueprint,
            RecipeCatalog catalog,
            NormalizedCourseModel expanded)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (expanded == null)
            {
                throw new ArgumentNullException(nameof(expanded));
            }

            var diagnostics = new List<CourseCompilationDiagnostic>();
            var actions = expanded.Actions.ToList();
            var rules = expanded.Rules.ToList();
            var effects = expanded.PresentationEffects.ToList();

            foreach (var interaction in blueprint.InteractionRules
                         .Where(value => value.HandlingMode == "收紧默认")
                         .OrderBy(value => value.Order)
                         .ThenBy(value => value.Source.Line))
            {
                Tighten(
                    interaction,
                    actions,
                    rules,
                    diagnostics);
            }

            foreach (var interaction in blueprint.InteractionRules
                         .Where(value => value.HandlingMode == "禁用默认")
                         .OrderBy(value => value.Order)
                         .ThenBy(value => value.Source.Line))
            {
                Disable(interaction, actions, diagnostics);
            }

            ApplyAdvancedOverrides(
                blueprint.AdvancedOverrides,
                catalog,
                actions,
                effects,
                diagnostics);
            ApplyPresentationOverrides(
                blueprint.PresentationOverrides,
                expanded,
                effects,
                diagnostics);

            var model = Copy(
                expanded,
                actions,
                rules,
                effects,
                effects.Select(value => value.Definition.ProtocolId));
            if (diagnostics.Count == 0)
            {
                diagnostics.AddRange(
                    new NormalizedCourseValidator().Validate(model).Diagnostics);
            }

            return new CourseOverrideResult(
                model,
                diagnostics
                    .OrderBy(value => value.FileName, StringComparer.Ordinal)
                    .ThenBy(value => value.Line)
                    .ThenBy(value => value.Code, StringComparer.Ordinal));
        }

        private void ApplyPresentationOverrides(
            IReadOnlyList<CoursePresentationOverrideBlueprint> overrides,
            NormalizedCourseModel expanded,
            ICollection<NormalizedItem<NormalizedPresentationEffectDefinition>>
                effects,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var group in overrides
                         .GroupBy(value => value.OverrideId, StringComparer.Ordinal)
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var rows = group
                    .OrderBy(value => value.Source.Line)
                    .ToArray();
                var first = rows[0];
                if (!rows.All(value =>
                        string.Equals(
                            value.ObjectOrState,
                            first.ObjectOrState,
                            StringComparison.Ordinal)
                        && string.Equals(
                            value.PresentationPrimitive,
                            first.PresentationPrimitive,
                            StringComparison.Ordinal)
                        && string.Equals(
                            value.TargetPosition,
                            first.TargetPosition,
                            StringComparison.Ordinal)
                        && string.Equals(
                            value.PositionId,
                            first.PositionId,
                            StringComparison.Ordinal)))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.metadata-conflict",
                        first.Source,
                        null,
                        $"表现覆盖“{first.OverrideId}”的对象、原语或作用位置不一致。",
                        "相同覆盖 ID 的多行只填写不同参数。"));
                    continue;
                }

                PresentationEffectDescriptor descriptor;
                try
                {
                    descriptor = _presentationCatalog.RequireByChineseName(
                        first.PresentationPrimitive);
                }
                catch (KeyNotFoundException)
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.primitive-unknown",
                        first.Source,
                        null,
                        $"表现原语“{first.PresentationPrimitive}”未注册。",
                        "使用统一表现目录中的中文原语名称。"));
                    continue;
                }

                if (!SubjectExists(first.ObjectOrState, expanded))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.subject-missing",
                        first.Source,
                        null,
                        $"表现覆盖对象或状态“{first.ObjectOrState}”不存在。",
                        "引用课程实体 ID 或配方生成的表现状态 ID。"));
                    continue;
                }

                if (!TryParseLocation(
                        first.TargetPosition,
                        out var courseLocation,
                        out var runtimeLocation))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.location-unknown",
                        first.Source,
                        null,
                        $"表现作用位置“{first.TargetPosition}”未注册。",
                        "使用实体根节点、表现插槽、语义锚点或全局接收器。"));
                    continue;
                }

                var locationValid =
                    descriptor.TargetContract.AllowedLocations.Contains(
                        runtimeLocation);
                if (!locationValid)
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.location-mismatch",
                        first.Source,
                        null,
                        $"表现原语“{first.PresentationPrimitive}”不能作用于“{first.TargetPosition}”。",
                        "按照统一表现目录选择作用位置。"));
                }

                if (runtimeLocation == PresentationLocationKind.PresentationSlot
                    && (!TryParseSlot(first.PositionId, out var slot)
                        || !descriptor.TargetContract.AllowedSlotKinds.Contains(
                            slot)))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.slot-mismatch",
                        first.Source,
                        null,
                        $"表现插槽“{first.PositionId}”不符合原语“{first.PresentationPrimitive}”的契约。",
                        "填写该原语允许的内容、液体、燃烧或高亮插槽。"));
                    locationValid = false;
                }

                if (runtimeLocation == PresentationLocationKind.SemanticAnchor
                    && (!TryInferAnchor(
                            first.PositionId,
                            expanded,
                            out var anchor)
                        || !descriptor.TargetContract.AllowedAnchorKinds.Contains(
                            anchor)))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.anchor-mismatch",
                        first.Source,
                        null,
                        $"语义锚点“{first.PositionId}”不符合原语“{first.PresentationPrimitive}”的契约。",
                        "填写已生成的端口 ID 或具有明确类型的倾倒、加热、点燃、观察锚点。"));
                    locationValid = false;
                }

                var parameters = CompilePresentationParameters(
                    rows,
                    descriptor,
                    diagnostics,
                    out var parametersValid);
                if (!locationValid || !parametersValid)
                {
                    continue;
                }

                effects.Add(
                    new NormalizedItem<
                        NormalizedPresentationEffectDefinition>(
                        new GeneratedItemIdentity(
                            "课程.表现覆盖",
                            first.ObjectOrState,
                            string.Empty,
                            "表现效果",
                            first.OverrideId),
                        new NormalizedPresentationEffectDefinition(
                            first.OverrideId,
                            descriptor.ProtocolId,
                            first.ObjectOrState,
                            courseLocation,
                            first.PositionId,
                            ConvertLifecycle(descriptor.DefaultLifecycle),
                            parameters,
                            ParsePresentationTrigger(first.TriggerType),
                            first.TriggerValue,
                            first.TriggerSourceEntityId,
                            first.TriggerTargetEntityId),
                        rows.Select(value => value.Source),
                        "替换表现"));
            }
        }

        private static IReadOnlyList<KeyValuePair<string, StructuredValue>>
            CompilePresentationParameters(
                IReadOnlyList<CoursePresentationOverrideBlueprint> rows,
                PresentationEffectDescriptor descriptor,
                ICollection<CourseCompilationDiagnostic> diagnostics,
                out bool isValid)
        {
            isValid = true;
            var values = new List<KeyValuePair<string, StructuredValue>>();
            foreach (var duplicate in rows
                         .Where(value => !string.IsNullOrWhiteSpace(
                             value.ParameterName))
                         .GroupBy(
                             value => value.ParameterName,
                             StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.presentation.parameter-duplicate",
                    duplicate.Skip(1).First().Source,
                    null,
                    $"表现参数“{duplicate.Key}”重复。",
                    "相同覆盖 ID 的每个参数只保留一行。"));
                isValid = false;
            }

            foreach (var row in rows.Where(value =>
                         !string.IsNullOrWhiteSpace(value.ParameterName)))
            {
                var parameter = descriptor.Parameters.FirstOrDefault(value =>
                    string.Equals(
                        value.Name,
                        row.ParameterName,
                        StringComparison.Ordinal));
                if (parameter == null)
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.parameter-unknown",
                        row.Source,
                        null,
                        $"原语“{descriptor.ChineseName}”没有参数“{row.ParameterName}”。",
                        "使用统一表现目录声明的参数名。"));
                    isValid = false;
                    continue;
                }

                if (!ParameterTypeMatches(
                        parameter.Kind,
                        row.ParameterType))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.parameter-type-mismatch",
                        row.Source,
                        null,
                        $"参数“{row.ParameterName}”的类型与统一表现目录不一致。",
                        ParameterTypeSuggestion(parameter.Kind)));
                    isValid = false;
                    continue;
                }

                if (!TryPresentationValue(
                        row.ParameterType,
                        row.ParameterValue,
                        out var presentationValue,
                        out var structuredValue))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.parameter-value-invalid",
                        row.Source,
                        null,
                        $"参数“{row.ParameterName}”的值“{row.ParameterValue}”无法按“{row.ParameterType}”解析。",
                        "检查数值小数点、布尔值或文本内容。"));
                    isValid = false;
                    continue;
                }

                if (!parameter.Matches(presentationValue, out var reason))
                {
                    diagnostics.Add(Diagnostic(
                        "blueprint.presentation.parameter-value-invalid",
                        row.Source,
                        null,
                        $"参数“{row.ParameterName}”不符合契约：{reason}",
                        "按照统一表现目录中的范围或允许值填写。"));
                    isValid = false;
                    continue;
                }

                values.Add(
                    new KeyValuePair<string, StructuredValue>(
                        row.ParameterName,
                        structuredValue));
            }

            var configured = new HashSet<string>(
                values.Select(value => value.Key),
                StringComparer.Ordinal);
            foreach (var missing in descriptor.Parameters.Where(value =>
                         value.Required && !configured.Contains(value.Name)))
            {
                diagnostics.Add(Diagnostic(
                    "blueprint.presentation.parameter-required",
                    rows[0].Source,
                    null,
                    $"原语“{descriptor.ChineseName}”缺少必填参数“{missing.Name}”。",
                    "在同一覆盖 ID 下增加该参数行。"));
                isValid = false;
            }

            foreach (var parameter in descriptor.Parameters.Where(value =>
                         !configured.Contains(value.Name)
                         && value.DefaultValue != null))
            {
                values.Add(
                    new KeyValuePair<string, StructuredValue>(
                        parameter.Name,
                        ToStructuredValue(parameter.DefaultValue)));
            }

            return values
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .ToArray();
        }

        private static void Tighten(
            CourseInteractionRuleBlueprint interaction,
            IList<NormalizedItem<NormalizedActionDefinition>> actions,
            ICollection<NormalizedItem<NormalizedRuleDefinition>> rules,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var matches = MatchingActions(actions, interaction).ToArray();
            if (matches.Length == 0)
            {
                diagnostics.Add(Diagnostic(
                    "override.action.missing",
                    interaction.Source,
                    null,
                    $"交互“{interaction.InteractionId}”没有匹配到默认动作策略。",
                    "检查动作、来源和目标实体是否与特征配方生成结果一致。"));
                return;
            }

            var ruleId =
                $"规则.课程限制.{interaction.InteractionId}.{interaction.Source.Line}";
            var rule = new NormalizedItem<NormalizedRuleDefinition>(
                new GeneratedItemIdentity(
                    "课程.交互限制",
                    interaction.SourceEntityId,
                    interaction.TargetEntityId,
                    "规则",
                    interaction.InteractionId + "." + interaction.Source.Line),
                new NormalizedRuleDefinition(
                    ruleId,
                    Field(interaction),
                    Operator(interaction.Comparison),
                    interaction.ExpectedValue,
                    interaction.Unit),
                new[] { interaction.Source },
                "追加条件");
            rules.Add(rule);

            foreach (var match in matches)
            {
                var index = actions.IndexOf(match);
                var definition = match.Definition;
                actions[index] = new NormalizedItem<NormalizedActionDefinition>(
                    match.Identity,
                    new NormalizedActionDefinition(
                        definition.PolicyId,
                        definition.ActionId,
                        definition.OperationId,
                        definition.Lifecycle,
                        definition.ExecutionModeId,
                        definition.Phase,
                        definition.SourceEntityId,
                        definition.TargetEntityId,
                        definition.RuleIds.Concat(new[] { ruleId }),
                        definition.ResultGroupId,
                        definition.PresentationGroupId,
                        definition.Priority,
                        definition.PolicyEffect,
                        definition.MessageId,
                        definition.RejectionCode,
                        definition.RejectionMessage),
                    Append(match.Provenance.Sources, interaction.Source),
                    "追加条件");
            }
        }

        private static void Disable(
            CourseInteractionRuleBlueprint interaction,
            ICollection<NormalizedItem<NormalizedActionDefinition>> actions,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var matches = MatchingActions(actions, interaction)
                .OrderByDescending(value => value.Definition.Priority)
                .ThenBy(value => value.Definition.PolicyId, StringComparer.Ordinal)
                .ToArray();
            if (matches.Length == 0)
            {
                diagnostics.Add(Diagnostic(
                    "override.action.missing",
                    interaction.Source,
                    null,
                    $"禁用交互“{interaction.InteractionId}”没有匹配到默认动作策略。",
                    "检查动作、来源和目标实体是否与特征配方生成结果一致。"));
                return;
            }

            var baseline = matches[0];
            var priority = matches.Max(value => value.Definition.Priority) + 1000;
            var policyId = $"策略.课程禁用.{interaction.InteractionId}";
            actions.Add(new NormalizedItem<NormalizedActionDefinition>(
                new GeneratedItemIdentity(
                    baseline.Identity.RecipeId,
                    interaction.SourceEntityId,
                    interaction.TargetEntityId,
                    "动作策略",
                    "课程禁用." + interaction.InteractionId),
                new NormalizedActionDefinition(
                    policyId,
                    baseline.Definition.ActionId,
                    baseline.Definition.OperationId,
                    baseline.Definition.Lifecycle,
                    baseline.Definition.ExecutionModeId,
                    baseline.Definition.Phase,
                    interaction.SourceEntityId,
                    interaction.TargetEntityId,
                    Array.Empty<string>(),
                    string.Empty,
                    string.Empty,
                    priority,
                    "禁用",
                    "文案." + interaction.InteractionId,
                    "课程禁用." + interaction.InteractionId,
                    interaction.RejectionMessage),
                Append(baseline.Provenance.Sources, interaction.Source),
                "禁用"));
        }

        private void ApplyAdvancedOverrides(
            IReadOnlyList<CourseAdvancedOverrideBlueprint> overrides,
            RecipeCatalog catalog,
            IList<NormalizedItem<NormalizedActionDefinition>> actions,
            IList<NormalizedItem<NormalizedPresentationEffectDefinition>> effects,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var conflictKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in overrides.GroupBy(
                         ConflictKey,
                         StringComparer.Ordinal))
            {
                if (group.Select(value => value.RawValue)
                        .Distinct(StringComparer.Ordinal)
                        .Count() <= 1)
                {
                    continue;
                }

                conflictKeys.Add(group.Key);
                var first = group.First();
                diagnostics.Add(Diagnostic(
                    "override.field.conflict",
                    first.Source,
                    new ConfigurationProvenance(
                        "覆盖冲突." + first.OverrideId,
                        group.Select(value => value.Source),
                        first.Operation),
                    $"多个高级覆盖同时修改“{first.Field}”且值不一致。",
                    "合并为一条明确覆盖，不能依赖最后一行获胜。"));
            }

            var safetyByRecipe = catalog.Recipes
                .GroupBy(value => value.RecipeId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.First().SafetyLevel,
                    StringComparer.Ordinal);
            foreach (var courseOverride in overrides
                         .OrderBy(value => value.Source.Line))
            {
                if (conflictKeys.Contains(ConflictKey(courseOverride)))
                {
                    continue;
                }

                var matchingActions = actions.Where(value =>
                    Matches(value, courseOverride)).ToArray();
                var matchingEffects = effects.Where(value =>
                    Matches(value, courseOverride)).ToArray();
                var matchingItems = matchingActions
                    .Cast<INormalizedItem>()
                    .Concat(matchingEffects)
                    .ToArray();

                if (safetyByRecipe.TryGetValue(
                        courseOverride.RecipeId,
                        out var safety)
                    && safety == RecipeSafetyLevel.NonWeakenable
                    && courseOverride.Operation == "删除")
                {
                    diagnostics.Add(Diagnostic(
                        "override.safety.non-weakenable",
                        courseOverride.Source,
                        Provenance(courseOverride, matchingItems),
                        $"不可弱化配方“{courseOverride.RecipeId}”的生成项不能删除。",
                        "保留平台/学科安全条件；课程只能追加更严格限制。"));
                    continue;
                }

                if (courseOverride.Operation == "替换表现"
                    && courseOverride.Field == "表现方式")
                {
                    if (matchingEffects.Length == 0)
                    {
                        diagnostics.Add(Diagnostic(
                            "override.target.missing",
                            courseOverride.Source,
                            Provenance(courseOverride, matchingItems),
                            $"覆盖“{courseOverride.OverrideId}”没有找到表现生成项。",
                            "使用配方 ID、来源/目标实体和生成项局部键定位。"));
                        continue;
                    }

                    PresentationEffectDescriptor descriptor;
                    try
                    {
                        descriptor = _presentationCatalog
                            .RequireByChineseName(courseOverride.RawValue);
                    }
                    catch (KeyNotFoundException)
                    {
                        diagnostics.Add(Diagnostic(
                            "override.presentation.unknown",
                            courseOverride.Source,
                            Provenance(courseOverride, matchingItems),
                            $"表现方式“{courseOverride.RawValue}”未注册。",
                            "使用统一表现目录中的中文名称。"));
                        continue;
                    }

                    foreach (var effect in matchingEffects)
                    {
                        var index = effects.IndexOf(effect);
                        effects[index] =
                            new NormalizedItem<NormalizedPresentationEffectDefinition>(
                                effect.Identity,
                                new NormalizedPresentationEffectDefinition(
                                    effect.Definition.EffectId,
                                    descriptor.ProtocolId,
                                    effect.Definition.SubjectId,
                                    effect.Definition.LocationKind,
                                    effect.Definition.LocationId,
                                    effect.Definition.Lifecycle,
                                    effect.Definition.ParameterValues,
                                    effect.Definition.TriggerKind,
                                    effect.Definition.TriggerValue,
                                    effect.Definition.TriggerSourceEntityId,
                                    effect.Definition.TriggerTargetEntityId,
                                    effect.Definition.ParameterBindings.Where(
                                        value => value.Source !=
                                            CoursePresentationParameterSource
                                                .Constant)),
                                Append(
                                    effect.Provenance.Sources,
                                    courseOverride.Source),
                                "替换表现");
                    }

                    continue;
                }

                diagnostics.Add(Diagnostic(
                    "override.operation.unsupported",
                    courseOverride.Source,
                    Provenance(courseOverride, matchingItems),
                    $"高级覆盖操作“{courseOverride.Operation}”不受支持。",
                    "使用禁用、追加条件、替换参数、追加结果、替换表现或追加评价。"));
            }
        }

        private static bool SubjectExists(
            string subjectId,
            NormalizedCourseModel model) =>
            string.Equals(
                subjectId,
                CoursePresentationSemanticSubjects.AllActionRejections,
                StringComparison.Ordinal)
            || string.Equals(
                subjectId,
                CoursePresentationSemanticSubjects.ActionTarget,
                StringComparison.Ordinal)
            || model.Entities.Any(value => string.Equals(
                value.Definition.EntityId,
                subjectId,
                StringComparison.Ordinal))
            || model.PresentationStates.Any(value => string.Equals(
                value.Definition.StateId,
                subjectId,
                StringComparison.Ordinal));

        private static CoursePresentationTriggerKind?
            ParsePresentationTrigger(string value)
        {
            return value?.Trim() switch
            {
                "" or null => null,
                "课程初始化" =>
                    CoursePresentationTriggerKind.CourseInitialized,
                "动作成功" => CoursePresentationTriggerKind.ActionAccepted,
                "动作拒绝" => CoursePresentationTriggerKind.ActionRejected,
                "领域事件" => CoursePresentationTriggerKind.DomainEvent,
                "状态进入" => CoursePresentationTriggerKind.StateEntered,
                "状态持续" => CoursePresentationTriggerKind.StateActive,
                "状态退出" => CoursePresentationTriggerKind.StateExited,
                "可用性变化" =>
                    CoursePresentationTriggerKind.ActionAvailabilityChanged,
                _ => throw new InvalidOperationException(
                    $"未注册表现触发类型“{value}”。")
            };
        }

        private static bool TryParseLocation(
            string text,
            out CoursePresentationLocationKind courseLocation,
            out PresentationLocationKind runtimeLocation)
        {
            switch (text)
            {
                case "实体根节点":
                    courseLocation = CoursePresentationLocationKind.EntityRoot;
                    runtimeLocation = PresentationLocationKind.EntityRoot;
                    return true;
                case "表现插槽":
                    courseLocation =
                        CoursePresentationLocationKind.PresentationSlot;
                    runtimeLocation =
                        PresentationLocationKind.PresentationSlot;
                    return true;
                case "语义锚点":
                    courseLocation =
                        CoursePresentationLocationKind.SemanticAnchor;
                    runtimeLocation = PresentationLocationKind.SemanticAnchor;
                    return true;
                case "全局接收器":
                    courseLocation =
                        CoursePresentationLocationKind.GlobalReceiver;
                    runtimeLocation =
                        PresentationLocationKind.GlobalReceiver;
                    return true;
                default:
                    courseLocation = default;
                    runtimeLocation = default;
                    return false;
            }
        }

        private static bool TryParseSlot(
            string locationId,
            out PresentationSlotKind kind)
        {
            switch (locationId)
            {
                case "插槽.内容":
                    kind = PresentationSlotKind.Content;
                    return true;
                case "插槽.液体":
                    kind = PresentationSlotKind.Liquid;
                    return true;
                case "插槽.燃烧":
                    kind = PresentationSlotKind.Combustion;
                    return true;
                case "插槽.高亮":
                    kind = PresentationSlotKind.Highlight;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private static bool TryInferAnchor(
            string locationId,
            NormalizedCourseModel model,
            out SemanticAnchorKind kind)
        {
            if (model.Ports.Any(value => string.Equals(
                    value.Definition.PortId,
                    locationId,
                    StringComparison.Ordinal))
                || locationId.Contains("端口"))
            {
                kind = SemanticAnchorKind.ConnectionPort;
                return true;
            }

            if (locationId.Contains("倾倒"))
            {
                kind = SemanticAnchorKind.PourOutlet;
                return true;
            }

            if (locationId.Contains("加热"))
            {
                kind = SemanticAnchorKind.HeatingPoint;
                return true;
            }

            if (locationId.Contains("点燃"))
            {
                kind = SemanticAnchorKind.IgnitionPoint;
                return true;
            }

            if (locationId.Contains("观察"))
            {
                kind = SemanticAnchorKind.ObservationFocus;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool ParameterTypeMatches(
            PresentationParameterKind kind,
            string configuredType) =>
            kind switch
            {
                PresentationParameterKind.Boolean =>
                    configuredType == "布尔",
                PresentationParameterKind.Number =>
                    configuredType == "数值",
                PresentationParameterKind.Text =>
                    configuredType == "文本",
                PresentationParameterKind.EnumText =>
                    configuredType == "文本",
                PresentationParameterKind.Resource =>
                    configuredType == "资源",
                _ => false
            };

        private static string ParameterTypeSuggestion(
            PresentationParameterKind kind) =>
            kind switch
            {
                PresentationParameterKind.Boolean => "参数类型应填写“布尔”。",
                PresentationParameterKind.Number => "参数类型应填写“数值”。",
                PresentationParameterKind.Resource => "参数类型应填写“资源”。",
                _ => "参数类型应填写“文本”。"
            };

        private static bool TryPresentationValue(
            string configuredType,
            string rawValue,
            out PresentationValue presentationValue,
            out StructuredValue structuredValue)
        {
            switch (configuredType)
            {
                case "布尔":
                    if (string.Equals(
                            rawValue,
                            "是",
                            StringComparison.Ordinal)
                        || string.Equals(
                            rawValue,
                            "true",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        presentationValue = PresentationValue.FromBoolean(true);
                        structuredValue = StructuredValue.FromBoolean(true);
                        return true;
                    }

                    if (string.Equals(
                            rawValue,
                            "否",
                            StringComparison.Ordinal)
                        || string.Equals(
                            rawValue,
                            "false",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        presentationValue =
                            PresentationValue.FromBoolean(false);
                        structuredValue = StructuredValue.FromBoolean(false);
                        return true;
                    }

                    break;
                case "数值":
                    if (double.TryParse(
                            rawValue,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var number)
                        && !double.IsNaN(number)
                        && !double.IsInfinity(number))
                    {
                        presentationValue =
                            PresentationValue.FromNumber(number);
                        structuredValue = StructuredValue.FromNumber(number);
                        return true;
                    }

                    break;
                case "文本":
                case "资源":
                    presentationValue =
                        PresentationValue.FromText(rawValue ?? string.Empty);
                    structuredValue =
                        StructuredValue.FromText(rawValue ?? string.Empty);
                    return true;
                case "空":
                    presentationValue = PresentationValue.Null();
                    structuredValue = StructuredValue.Null();
                    return true;
            }

            presentationValue = null;
            structuredValue = null;
            return false;
        }

        private static StructuredValue ToStructuredValue(
            PresentationValue value) =>
            value.Kind switch
            {
                PresentationValueKind.Null => StructuredValue.Null(),
                PresentationValueKind.Boolean =>
                    StructuredValue.FromBoolean(value.Boolean),
                PresentationValueKind.Number =>
                    StructuredValue.FromNumber(value.Number),
                PresentationValueKind.Text =>
                    StructuredValue.FromText(value.Text),
                _ => throw new ArgumentOutOfRangeException(nameof(value))
            };

        private static CoursePresentationLifecycle ConvertLifecycle(
            PresentationEffectLifecycle lifecycle) =>
            lifecycle switch
            {
                PresentationEffectLifecycle.OneShot =>
                    CoursePresentationLifecycle.OneShot,
                PresentationEffectLifecycle.WhileActive =>
                    CoursePresentationLifecycle.WhileActive,
                PresentationEffectLifecycle.UntilReplaced =>
                    CoursePresentationLifecycle.UntilReplaced,
                _ => throw new ArgumentOutOfRangeException(nameof(lifecycle))
            };

        private static bool Matches(
            INormalizedItem item,
            CourseAdvancedOverrideBlueprint courseOverride) =>
            string.Equals(
                item is NormalizedItem<NormalizedActionDefinition> action
                    ? action.Identity.RecipeId
                    : ((NormalizedItem<NormalizedPresentationEffectDefinition>)item)
                    .Identity.RecipeId,
                courseOverride.RecipeId,
                StringComparison.Ordinal)
            && IdentityMatches(item, courseOverride);

        private static bool IdentityMatches(
            INormalizedItem item,
            CourseAdvancedOverrideBlueprint courseOverride)
        {
            GeneratedItemIdentity identity;
            if (item is NormalizedItem<NormalizedActionDefinition> action)
            {
                identity = action.Identity;
            }
            else
            {
                identity =
                    ((NormalizedItem<NormalizedPresentationEffectDefinition>)item)
                    .Identity;
            }

            return string.Equals(
                       identity.SourceEntityId,
                       courseOverride.SourceEntityId,
                       StringComparison.Ordinal)
                   && string.Equals(
                       identity.TargetEntityId,
                       courseOverride.TargetEntityId,
                       StringComparison.Ordinal)
                   && (string.Equals(
                           identity.GeneratedItemType,
                           courseOverride.GeneratedItem,
                           StringComparison.Ordinal)
                       || string.Equals(
                           identity.LocalKey,
                           courseOverride.GeneratedItem,
                           StringComparison.Ordinal));
        }

        private static IEnumerable<NormalizedItem<NormalizedActionDefinition>>
            MatchingActions(
                IEnumerable<NormalizedItem<NormalizedActionDefinition>> actions,
                CourseInteractionRuleBlueprint interaction) =>
            actions.Where(value =>
                string.Equals(
                    ActionId(value.Definition.ActionId),
                    ActionId(interaction.ActionId),
                    StringComparison.Ordinal)
                && string.Equals(
                    value.Definition.SourceEntityId,
                    interaction.SourceEntityId,
                    StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(interaction.TargetEntityId)
                    || string.Equals(
                        value.Definition.TargetEntityId,
                        interaction.TargetEntityId,
                        StringComparison.Ordinal)));

        private static string ActionId(string configured)
        {
            return configured?.Trim() ?? string.Empty;
        }

        private static string Field(CourseInteractionRuleBlueprint interaction) =>
            string.IsNullOrWhiteSpace(interaction.RequirementType)
                ? interaction.Field?.Trim() ?? string.Empty
                : interaction.RequirementType + "."
                  + (interaction.Field?.Trim() ?? string.Empty);

        private static string Operator(string value) =>
            value?.Trim() ?? string.Empty;

        private static string ConflictKey(
            CourseAdvancedOverrideBlueprint value) =>
            string.Join(
                "\u001F",
                value.RecipeId,
                value.SourceEntityId,
                value.TargetEntityId,
                value.GeneratedItem,
                value.Operation,
                value.Field);

        private static ConfigurationProvenance Provenance(
            CourseAdvancedOverrideBlueprint courseOverride,
            IEnumerable<INormalizedItem> matchingItems) =>
            new ConfigurationProvenance(
                "课程覆盖." + courseOverride.OverrideId,
                matchingItems
                    .SelectMany(value => value.Provenance.Sources)
                    .Concat(new[] { courseOverride.Source }),
                courseOverride.Operation);

        private static IReadOnlyList<ConfigurationSource> Append(
            IEnumerable<ConfigurationSource> existing,
            ConfigurationSource appended) =>
            existing.Concat(new[] { appended }).ToArray();

        private static NormalizedCourseModel Copy(
            NormalizedCourseModel source,
            IEnumerable<NormalizedItem<NormalizedActionDefinition>> actions,
            IEnumerable<NormalizedItem<NormalizedRuleDefinition>> rules,
            IEnumerable<NormalizedItem<NormalizedPresentationEffectDefinition>> effects,
            IEnumerable<string> presentationProtocolIds) =>
            new NormalizedCourseModel(
                source.CourseId,
                entities: source.Entities,
                ports: source.Ports,
                actions: actions,
                rules: rules,
                stateChanges: source.StateChanges,
                domainEvents: source.DomainEvents,
                actionResultGroups: source.ActionResultGroups,
                presentationStates: source.PresentationStates,
                presentationEffects: effects,
                presentationGroups: source.PresentationGroups,
                evaluations: source.Evaluations,
                teachingGoals: source.TeachingGoals,
                teachingRisks: source.TeachingRisks,
                teachingScores: source.TeachingScores,
                teachingHints: source.TeachingHints,
                acceptanceScenarios: source.AcceptanceScenarios,
                prefabContracts: source.PrefabContracts,
                generatedArtifacts: source.GeneratedArtifacts,
                knownUnitIds: source.KnownUnitIds,
                knownOperationIds: source.KnownOperationIds,
                knownPresentationProtocolIds:
                    source.KnownPresentationProtocolIds.Concat(
                        presentationProtocolIds));

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            ConfigurationSource source,
            ConfigurationProvenance provenance,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                source.FileName,
                source.Line,
                source.Column,
                string.Empty,
                source.ConfigurationId,
                reason,
                suggestion,
                CourseDiagnosticSeverity.Error,
                provenance ?? new ConfigurationProvenance(
                    "课程覆盖." + source.ConfigurationId,
                    new[] { source }));
    }
}
