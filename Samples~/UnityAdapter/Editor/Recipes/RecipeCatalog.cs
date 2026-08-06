using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Recipes
{
    /// <summary>
    /// 显式合并平台包和学科包，并在展开前一次性报告共享配方契约错误。
    /// </summary>
    public sealed class RecipeCatalog
    {
        private RecipeCatalog(
            IEnumerable<RecipePackage> packages,
            IEnumerable<RecipeDefinition> recipes,
            IEnumerable<RecipeDefinition> effectiveRecipes,
            IEnumerable<string> registeredStateOperationIds,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Packages = packages.ToArray();
            Recipes = recipes.ToArray();
            EffectiveRecipes = effectiveRecipes.ToArray();
            RegisteredStateOperationIds = registeredStateOperationIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            Diagnostics = diagnostics.ToArray();
        }

        public bool IsValid => Diagnostics.Count == 0;
        public IReadOnlyList<RecipePackage> Packages { get; }
        public IReadOnlyList<RecipeDefinition> Recipes { get; }
        public IReadOnlyList<RecipeDefinition> EffectiveRecipes { get; }
        public IReadOnlyList<string> RegisteredStateOperationIds { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }

        public static RecipeCatalog Create(
            IRecipePackageProvider platformProvider,
            IEnumerable<IRecipePackageProvider> selectedDisciplineProviders)
        {
            if (platformProvider == null)
            {
                throw new ArgumentNullException(nameof(platformProvider));
            }

            var diagnostics = new List<CourseCompilationDiagnostic>();
            var loaded = new List<LoadedPackage>();
            LoadProvider(
                platformProvider,
                RecipeLayer.Platform,
                loaded,
                diagnostics);
            foreach (var provider in (
                         selectedDisciplineProviders
                         ?? Array.Empty<IRecipePackageProvider>())
                     .OrderBy(
                         value => value?.PackageId ?? string.Empty,
                         StringComparer.Ordinal))
            {
                LoadProvider(
                    provider,
                    RecipeLayer.Discipline,
                    loaded,
                    diagnostics);
            }

            var packages = loaded
                .Select(value => value.Package)
                .OrderBy(value => LayerOrder(value.Layer))
                .ThenBy(value => value.PackageId, StringComparer.Ordinal)
                .ToArray();
            ValidatePackages(loaded, diagnostics);

            var ownedRecipes = packages
                .SelectMany(package => package.Recipes.Select(recipe =>
                    new OwnedRecipe(package, recipe)))
                .OrderBy(value => LayerOrder(value.Package.Layer))
                .ThenBy(value => value.Package.PackageId, StringComparer.Ordinal)
                .ThenBy(value => value.Recipe.RecipeId, StringComparer.Ordinal)
                .ToArray();
            ValidateRecipes(ownedRecipes, diagnostics);
            ValidateDefinitions(packages, ownedRecipes, diagnostics);

            var replacedIds = new HashSet<string>(
                ownedRecipes
                    .Select(value => value.Recipe.ReplacesRecipeId)
                    .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
            var effective = ownedRecipes
                .Where(value => !replacedIds.Contains(value.Recipe.RecipeId))
                .Select(value => value.Recipe)
                .ToArray();
            var orderedDiagnostics = diagnostics
                .OrderBy(value => value.FileName, StringComparer.Ordinal)
                .ThenBy(value => value.Line)
                .ThenBy(value => value.Column)
                .ThenBy(value => value.ConfigurationId, StringComparer.Ordinal)
                .ThenBy(value => value.Code, StringComparer.Ordinal)
                .ToArray();

            return new RecipeCatalog(
                packages,
                ownedRecipes.Select(value => value.Recipe),
                effective,
                loaded.SelectMany(value => value.RegisteredStateOperationIds),
                orderedDiagnostics);
        }

        private static void LoadProvider(
            IRecipePackageProvider provider,
            RecipeLayer expectedLayer,
            ICollection<LoadedPackage> loaded,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (provider == null)
            {
                diagnostics.Add(Diagnostic(
                    "recipe.package.provider-null",
                    string.Empty,
                    "配方包提供者不能为空。",
                    "移除空提供者，或注册一个显式的配方包提供者。"));
                return;
            }

            try
            {
                var package = provider.Load();
                if (package == null)
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.package.load-null",
                        provider.PackageId,
                        $"配方包提供者“{provider.PackageId}”没有返回配方包。",
                        "让提供者返回经过强类型转换的 RecipePackage。"));
                    return;
                }

                loaded.Add(new LoadedPackage(
                    package,
                    expectedLayer,
                    provider.PackageId,
                    provider.RegisteredStateOperationIds));
            }
            catch (Exception exception)
            {
                diagnostics.Add(Diagnostic(
                    "recipe.package.load-failed",
                    provider.PackageId,
                    $"配方包“{provider.PackageId}”加载失败：{exception.Message}",
                    "检查共享 CSV 的编码、表头和强类型字段。"));
            }
        }

        private static void ValidatePackages(
            IReadOnlyList<LoadedPackage> packages,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var duplicate in packages
                         .GroupBy(value => value.Package.PackageId, StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "recipe.package.id.duplicate",
                    duplicate.Key,
                    $"配方包 ID“{duplicate.Key}”重复。",
                    "平台包和每个学科包必须使用全局唯一的中文包 ID。"));
            }

            foreach (var loaded in packages.OrderBy(
                         value => value.Package.PackageId,
                         StringComparer.Ordinal))
            {
                var package = loaded.Package;
                if (string.IsNullOrWhiteSpace(package.PackageId))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.package.id.missing",
                        package.PackageId,
                        "配方包 ID 不能为空。",
                        "填写稳定且可读的中文配方包 ID。"));
                }

                if (!Enum.IsDefined(typeof(RecipeLayer), package.Layer))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.layer.invalid",
                        package.PackageId,
                        $"配方包“{package.PackageId}”使用了未注册的层级值。",
                        "共享包层级只能是 Platform（平台）或 Discipline（学科）。"));
                }
                else if (package.Layer != loaded.ExpectedLayer)
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.layer.role-mismatch",
                        package.PackageId,
                        $"配方包“{package.PackageId}”的层级与装配位置不一致。",
                        loaded.ExpectedLayer == RecipeLayer.Platform
                            ? "平台入口只能装配平台包。"
                            : "学科入口只能装配学科包。"));
                }

                if (!string.Equals(
                        loaded.ProviderPackageId,
                        package.PackageId,
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.package.provider-id-mismatch",
                        package.PackageId,
                        $"提供者 ID“{loaded.ProviderPackageId}”与包 ID“{package.PackageId}”不一致。",
                        "让提供者和配方包返回完全相同的稳定 ID。"));
                }
            }
        }

        private static void ValidateRecipes(
            IReadOnlyList<OwnedRecipe> ownedRecipes,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var byId = new Dictionary<string, OwnedRecipe>(StringComparer.Ordinal);
            foreach (var owned in ownedRecipes)
            {
                var recipe = owned.Recipe;
                if (string.IsNullOrWhiteSpace(recipe.RecipeId))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.id.missing",
                        owned.Package.PackageId,
                        "配方 ID 不能为空。",
                        "填写稳定且可读的中文配方 ID。"));
                }
                else if (!byId.TryAdd(recipe.RecipeId, owned))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.id.duplicate",
                        recipe.RecipeId,
                        $"配方 ID“{recipe.RecipeId}”全局重复。",
                        "跨平台包和学科包使用全局唯一的配方 ID。"));
                }

                if (!Enum.IsDefined(typeof(RecipeMatchKind), recipe.MatchKind))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.match-kind.invalid",
                        recipe.RecipeId,
                        $"配方“{recipe.RecipeId}”使用了未注册的匹配类型。",
                        "使用单实体、兼容双实体或过程匹配。"));
                }
                else if (recipe.MatchKind == RecipeMatchKind.CompatiblePair)
                {
                    if (recipe.CompatiblePair == null)
                    {
                        diagnostics.Add(Diagnostic(
                            "recipe.pair-contract.missing",
                            recipe.RecipeId,
                            $"双实体配方“{recipe.RecipeId}”缺少兼容契约。",
                            "声明来源/目标特征、参数列和兼容方式。"));
                    }
                    else if (!Enum.IsDefined(
                                 typeof(PairCompatibilityKind),
                                 recipe.CompatiblePair.CompatibilityKind))
                    {
                        diagnostics.Add(Diagnostic(
                            "recipe.pair-compatibility.invalid",
                            recipe.RecipeId,
                            $"双实体配方“{recipe.RecipeId}”使用了未注册兼容方式。",
                            "使用同组、同值或课程允许组合。"));
                    }
                }

                if (!Enum.IsDefined(typeof(RecipeSafetyLevel), recipe.SafetyLevel))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.safety.invalid",
                        recipe.RecipeId,
                        $"配方“{recipe.RecipeId}”使用了未注册的安全级别。",
                        "使用不可弱化、课程可收紧或课程可替换表现。"));
                }

                if (recipe.ReferencedEntityIds.Any(value =>
                        !string.IsNullOrWhiteSpace(value)))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.course-entity.forbidden",
                        recipe.RecipeId,
                        $"共享配方“{recipe.RecipeId}”直接引用了课程实体。",
                        "改用强类型实体绑定，在课程展开时注入匹配实体。"));
                }

                if (owned.Package.Layer == RecipeLayer.Discipline
                    && string.IsNullOrWhiteSpace(recipe.ExtendsRecipeId)
                    && string.IsNullOrWhiteSpace(recipe.ReplacesRecipeId))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.discipline.relationship.missing",
                        recipe.RecipeId,
                        $"学科配方“{recipe.RecipeId}”未声明扩展或替换关系。",
                        "显式填写要扩展或替换的已有配方 ID。"));
                }

                if (!string.IsNullOrWhiteSpace(recipe.ExtendsRecipeId)
                    && !string.IsNullOrWhiteSpace(recipe.ReplacesRecipeId))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.relationship.ambiguous",
                        recipe.RecipeId,
                        $"配方“{recipe.RecipeId}”同时声明了扩展和替换。",
                        "每个配方只能选择一种关系。"));
                }
            }

            foreach (var owned in ownedRecipes)
            {
                ValidateRelationshipTarget(
                    owned.Recipe,
                    owned.Recipe.ExtendsRecipeId,
                    byId,
                    diagnostics);
                ValidateRelationshipTarget(
                    owned.Recipe,
                    owned.Recipe.ReplacesRecipeId,
                    byId,
                    diagnostics);
            }

            foreach (var conflict in ownedRecipes
                         .Where(value => !string.IsNullOrWhiteSpace(
                             value.Recipe.ReplacesRecipeId))
                         .GroupBy(
                             value => value.Recipe.ReplacesRecipeId,
                             StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "recipe.replacement.conflict",
                    conflict.Key,
                    $"配方“{conflict.Key}”被多个学科配方同时替换。",
                    "同一目录中只能有一个明确替换者。"));
            }

            ValidateRelationshipCycles(byId, diagnostics);
        }

        private static void ValidateRelationshipTarget(
            RecipeDefinition recipe,
            string targetId,
            IReadOnlyDictionary<string, OwnedRecipe> byId,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return;
            }

            if (!byId.ContainsKey(targetId))
            {
                diagnostics.Add(Diagnostic(
                    "recipe.relationship.target-missing",
                    recipe.RecipeId,
                    $"配方“{recipe.RecipeId}”引用的关系目标“{targetId}”不存在。",
                    "选择当前目录中已存在的配方作为扩展或替换目标。"));
            }
        }

        private static void ValidateRelationshipCycles(
            IReadOnlyDictionary<string, OwnedRecipe> byId,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var states = new Dictionary<string, VisitState>(StringComparer.Ordinal);
            var cycleReported = false;
            foreach (var id in byId.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                Visit(id);
            }

            void Visit(string id)
            {
                if (states.TryGetValue(id, out var state))
                {
                    if (state == VisitState.Visiting && !cycleReported)
                    {
                        diagnostics.Add(Diagnostic(
                            "recipe.relationship.cycle",
                            id,
                            $"配方关系从“{id}”形成循环。",
                            "调整扩展或替换关系，使依赖图保持无环。"));
                        cycleReported = true;
                    }

                    return;
                }

                states[id] = VisitState.Visiting;
                var recipe = byId[id].Recipe;
                var target = !string.IsNullOrWhiteSpace(recipe.ExtendsRecipeId)
                    ? recipe.ExtendsRecipeId
                    : recipe.ReplacesRecipeId;
                if (!string.IsNullOrWhiteSpace(target) && byId.ContainsKey(target))
                {
                    Visit(target);
                }

                states[id] = VisitState.Visited;
            }
        }

        private static void ValidateDefinitions(
            IReadOnlyList<RecipePackage> packages,
            IReadOnlyList<OwnedRecipe> ownedRecipes,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var recipeIds = new HashSet<string>(
                ownedRecipes.Select(value => value.Recipe.RecipeId),
                StringComparer.Ordinal);
            ValidateParameters(packages, recipeIds, diagnostics);

            var conditionIds = ValidateUniqueIds(
                packages.SelectMany(value => value.Conditions),
                value => value.ConditionId,
                "condition",
                diagnostics);
            var resultIds = ValidateUniqueIds(
                packages.SelectMany(value => value.Results),
                value => value.ResultId,
                "result",
                diagnostics);
            var presentationIds = ValidateUniqueIds(
                packages.SelectMany(value => value.Presentations),
                value => value.PresentationId,
                "presentation",
                diagnostics);
            var operationIds = ValidateUniqueIds(
                packages.SelectMany(value => value.Operations),
                value => value.OperationId,
                "operation",
                diagnostics);

            foreach (var package in packages)
            {
                foreach (var condition in package.Conditions)
                {
                    ValidateBindings(condition.ConditionId, condition.Bindings, diagnostics);
                }

                foreach (var result in package.Results)
                {
                    ValidateBindings(result.ResultId, result.Bindings, diagnostics);
                    ValidateReferences(
                        result.ResultId,
                        result.OperationIds,
                        operationIds,
                        "operation",
                        "操作",
                        diagnostics);
                }

                foreach (var presentation in package.Presentations)
                {
                    ValidateBindings(
                        presentation.PresentationId,
                        presentation.Bindings,
                        diagnostics);
                    ValidateReferences(
                        presentation.PresentationId,
                        presentation.StateConditionIds,
                        conditionIds,
                        "condition",
                        "状态条件",
                        diagnostics);
                    if (presentation.ScientificOperationIds.Any(value =>
                            !string.IsNullOrWhiteSpace(value)))
                    {
                        diagnostics.Add(Diagnostic(
                            "recipe.presentation.science-operation-forbidden",
                            presentation.PresentationId,
                            $"表现配方“{presentation.PresentationId}”包含科学状态操作。",
                            "把科学状态变化放入结果或状态变化定义，表现只描述视觉与交互反馈。"));
                    }
                }

                foreach (var operation in package.Operations)
                {
                    ValidateBindings(operation.OperationId, operation.Bindings, diagnostics);
                }

                foreach (var item in package.Actions)
                {
                    if (!recipeIds.Contains(item.RecipeId))
                    {
                        diagnostics.Add(Diagnostic(
                            "recipe.reference.recipe-missing",
                            item.RecipeId,
                            $"操作引用的配方“{item.RecipeId}”不存在。",
                            "把操作绑定到当前目录中存在的配方。"));
                    }

                    ValidateReferences(
                        item.RecipeId,
                        item.ConditionIds,
                        conditionIds,
                        "condition",
                        "条件",
                        diagnostics);
                    ValidateReferences(
                        item.RecipeId,
                        item.ResultIds,
                        resultIds,
                        "result",
                        "结果",
                        diagnostics);
                    ValidateReferences(
                        item.RecipeId,
                        item.PresentationIds,
                        presentationIds,
                        "presentation",
                        "表现",
                        diagnostics);
                }
            }
        }

        private static void ValidateParameters(
            IEnumerable<RecipePackage> packages,
            ISet<string> recipeIds,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in packages.SelectMany(value => value.Parameters))
            {
                var key = parameter.RecipeId + "\u001F" + parameter.ParameterName;
                if (!keys.Add(key))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.parameter.duplicate",
                        parameter.RecipeId,
                        $"配方“{parameter.RecipeId}”的参数“{parameter.ParameterName}”重复。",
                        "每个配方只声明一次同名参数。"));
                }

                if (!recipeIds.Contains(parameter.RecipeId))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.parameter.recipe-missing",
                        parameter.RecipeId,
                        $"参数“{parameter.ParameterName}”所属配方不存在。",
                        "把参数绑定到当前目录中存在的配方。"));
                }

                if (string.IsNullOrWhiteSpace(parameter.ParameterName))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.parameter.name.missing",
                        parameter.RecipeId,
                        "配方参数名不能为空。",
                        "使用稳定且可读的中文参数名。"));
                }

                if (!Enum.IsDefined(
                        typeof(RecipeParameterType),
                        parameter.ParameterType))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.parameter.type.invalid",
                        parameter.RecipeId,
                        $"参数“{parameter.ParameterName}”使用了未注册的类型。",
                        "使用文本、布尔、整数、数字、实体 ID 或端口 ID。"));
                    continue;
                }

                var numeric = parameter.ParameterType == RecipeParameterType.Integer
                              || parameter.ParameterType == RecipeParameterType.Number;
                if (!numeric
                    && (!string.IsNullOrWhiteSpace(parameter.Unit)
                        || parameter.Minimum.HasValue
                        || parameter.Maximum.HasValue))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.parameter.unit.invalid",
                        parameter.RecipeId,
                        $"非数值参数“{parameter.ParameterName}”不能声明单位或数值范围。",
                        "移除单位和范围，或把参数类型改为整数/数字。"));
                }

                if (numeric
                    && parameter.Minimum.HasValue
                    && parameter.Maximum.HasValue
                    && parameter.Minimum.Value > parameter.Maximum.Value)
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.parameter.range.invalid",
                        parameter.RecipeId,
                        $"参数“{parameter.ParameterName}”的最小值大于最大值。",
                        "调整范围，使最小值小于或等于最大值。"));
                }

                if (!string.IsNullOrWhiteSpace(parameter.DefaultValue)
                    && !DefaultValueIsValid(parameter))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.parameter.default.invalid",
                        parameter.RecipeId,
                        $"参数“{parameter.ParameterName}”的默认值“{parameter.DefaultValue}”不符合类型或范围。",
                        "填写与参数类型、单位和范围一致的默认值。"));
                }

                if (parameter.SafetyLevel == RecipeSafetyLevel.NonWeakenable
                    && parameter.CourseMayDelete)
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.safety.deletion-forbidden",
                        parameter.RecipeId,
                        $"不可弱化参数“{parameter.ParameterName}”不能声明为课程可删除。",
                        "取消课程可删除标记；课程只能追加更严格限制。"));
                }
            }
        }

        private static bool DefaultValueIsValid(RecipeParameterContract parameter)
        {
            switch (parameter.ParameterType)
            {
                case RecipeParameterType.Boolean:
                    return bool.TryParse(parameter.DefaultValue, out _)
                           || parameter.DefaultValue == "是"
                           || parameter.DefaultValue == "否";
                case RecipeParameterType.Integer:
                    if (!long.TryParse(
                            parameter.DefaultValue,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var integer))
                    {
                        return false;
                    }

                    return WithinRange(integer, parameter);
                case RecipeParameterType.Number:
                    if (!double.TryParse(
                            parameter.DefaultValue,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var number)
                        || double.IsNaN(number)
                        || double.IsInfinity(number))
                    {
                        return false;
                    }

                    return WithinRange(number, parameter);
                default:
                    return true;
            }
        }

        private static bool WithinRange(
            double value,
            RecipeParameterContract parameter) =>
            (!parameter.Minimum.HasValue || value >= parameter.Minimum.Value)
            && (!parameter.Maximum.HasValue || value <= parameter.Maximum.Value);

        private static HashSet<string> ValidateUniqueIds<T>(
            IEnumerable<T> definitions,
            Func<T, string> idSelector,
            string kind,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                var id = idSelector(definition) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    diagnostics.Add(Diagnostic(
                        $"recipe.{kind}.id-missing",
                        string.Empty,
                        "配方子定义 ID 不能为空。",
                        "填写稳定且可读的中文 ID。"));
                }
                else if (!result.Add(id))
                {
                    diagnostics.Add(Diagnostic(
                        $"recipe.{kind}.id-duplicate",
                        id,
                        $"配方子定义 ID“{id}”重复。",
                        "在整个共享目录中使用唯一 ID。"));
                }
            }

            return result;
        }

        private static void ValidateBindings(
            string definitionId,
            IEnumerable<RecipeValueBinding> bindings,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var binding in bindings ?? Array.Empty<RecipeValueBinding>())
            {
                if (binding == null)
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.binding.null",
                        definitionId,
                        $"定义“{definitionId}”包含空绑定。",
                        "删除空行或填写绑定种类和参数名。"));
                    continue;
                }

                if (!Enum.IsDefined(typeof(RecipeBindingKind), binding.Kind))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.binding.kind.invalid",
                        definitionId,
                        $"定义“{definitionId}”使用了未注册的绑定种类。",
                        "使用 RecipeBindingKind 中声明的强类型绑定。"));
                }

                if (binding.ParameterName.Contains("$" + "{")
                    || binding.ParameterName.Contains("{")
                    || binding.ParameterName.Contains("}"))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.binding.template-forbidden",
                        definitionId,
                        $"绑定参数“{binding.ParameterName}”包含自由模板语法。",
                        "使用绑定种类和纯参数名，不要使用字符串模板。"));
                }
                else if (string.IsNullOrWhiteSpace(binding.ParameterName))
                {
                    diagnostics.Add(Diagnostic(
                        "recipe.binding.parameter.missing",
                        definitionId,
                        $"定义“{definitionId}”的绑定参数名为空。",
                        "为强类型绑定填写明确的参数名。"));
                }
            }
        }

        private static void ValidateReferences(
            string recipeId,
            IEnumerable<string> references,
            ISet<string> available,
            string codePart,
            string displayName,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var reference in references)
            {
                if (!available.Contains(reference))
                {
                    diagnostics.Add(Diagnostic(
                        $"recipe.reference.{codePart}-missing",
                        recipeId,
                        $"配方“{recipeId}”引用的{displayName}“{reference}”不存在。",
                        $"先在共享配方包中定义该{displayName}，再由操作引用。"));
                }
            }
        }

        private static int LayerOrder(RecipeLayer layer) =>
            layer == RecipeLayer.Platform ? 0 : 1;

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            string configurationId,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                "共享配方目录",
                1,
                1,
                string.Empty,
                configurationId ?? string.Empty,
                reason,
                suggestion);

        private sealed class LoadedPackage
        {
            public LoadedPackage(
                RecipePackage package,
                RecipeLayer expectedLayer,
                string providerPackageId,
                IEnumerable<string> registeredStateOperationIds)
            {
                Package = package;
                ExpectedLayer = expectedLayer;
                ProviderPackageId = providerPackageId ?? string.Empty;
                RegisteredStateOperationIds =
                    (registeredStateOperationIds ?? Array.Empty<string>())
                    .ToArray();
            }

            public RecipePackage Package { get; }
            public RecipeLayer ExpectedLayer { get; }
            public string ProviderPackageId { get; }
            public IReadOnlyList<string> RegisteredStateOperationIds { get; }
        }

        private sealed class OwnedRecipe
        {
            public OwnedRecipe(
                RecipePackage package,
                RecipeDefinition recipe)
            {
                Package = package;
                Recipe = recipe;
            }

            public RecipePackage Package { get; }
            public RecipeDefinition Recipe { get; }
        }

        private enum VisitState
        {
            Visiting,
            Visited
        }
    }
}
