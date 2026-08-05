using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public sealed class CourseFeatureParameterDescriptor
    {
        internal CourseFeatureParameterDescriptor(
            string name,
            RecipeParameterType type,
            bool isRequired,
            string unit,
            double? minimum,
            double? maximum,
            string defaultValue,
            string purpose)
        {
            Name = name;
            Type = type;
            IsRequired = isRequired;
            Unit = unit ?? string.Empty;
            Minimum = minimum;
            Maximum = maximum;
            DefaultValue = defaultValue ?? string.Empty;
            Purpose = purpose ?? string.Empty;
        }

        public string Name { get; }
        public string ColumnName => "参数." + Name;
        public RecipeParameterType Type { get; }
        public bool IsRequired { get; }
        public string Unit { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }
        public string DefaultValue { get; }
        public string Purpose { get; }
    }

    public sealed class CourseFeatureDescriptor
    {
        internal CourseFeatureDescriptor(
            string featureId,
            IEnumerable<string> recipeIds,
            IEnumerable<string> packageIds,
            IEnumerable<CourseFeatureParameterDescriptor> parameters,
            bool isRegisteredCapability)
        {
            FeatureId = featureId;
            RecipeIds = recipeIds
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            PackageIds = packageIds
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            Parameters = parameters
                .GroupBy(value => value.Name, StringComparer.Ordinal)
                .Select(value => value.First())
                .OrderBy(value => value.Name, StringComparer.Ordinal)
                .ToArray();
            IsRegisteredCapability = isRegisteredCapability;
        }

        public string FeatureId { get; }
        public IReadOnlyList<string> RecipeIds { get; }
        public IReadOnlyList<string> PackageIds { get; }
        public IReadOnlyList<CourseFeatureParameterDescriptor> Parameters { get; }
        public bool IsRegisteredCapability { get; }
        public bool IsCapabilityOnly =>
            IsRegisteredCapability && RecipeIds.Count == 0;
    }

    /// <summary>
    /// 从有效共享配方反推课程作者可选择的对象特征及其结构化参数。
    /// 工作台因此不维护第二份硬编码特征清单；新增学科配方后会自动出现在界面中。
    /// </summary>
    public sealed class CourseFeatureCatalog
    {
        private readonly IReadOnlyDictionary<string, CourseFeatureDescriptor>
            _features;

        private CourseFeatureCatalog(
            IReadOnlyDictionary<string, CourseFeatureDescriptor> features)
        {
            _features = features;
        }

        public IReadOnlyList<CourseFeatureDescriptor> Features =>
            _features.Values
                .OrderBy(value => value.FeatureId, StringComparer.Ordinal)
                .ToArray();

        public bool TryGet(
            string featureId,
            out CourseFeatureDescriptor descriptor) =>
            _features.TryGetValue(featureId, out descriptor);

        public static CourseFeatureCatalog Create(RecipeCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var builders = new Dictionary<string, FeatureBuilder>(
                StringComparer.Ordinal);
            var parameters = catalog.Packages
                .SelectMany(package => package.Parameters)
                .GroupBy(value => value.RecipeId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.ToArray(),
                    StringComparer.Ordinal);
            var recipesById = catalog.Recipes
                .GroupBy(value => value.RecipeId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.First(),
                    StringComparer.Ordinal);

            foreach (var recipe in catalog.EffectiveRecipes)
            {
                var packageId = recipe.Source?.PackageId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(recipe.MatchFeatureId))
                {
                    var builder = Builder(builders, recipe.MatchFeatureId);
                    builder.AddRecipe(recipe.RecipeId, packageId);
                    AddRecipeParameters(
                        builder,
                        recipe,
                        parameters,
                        recipesById);
                }

                var pair = recipe.CompatiblePair;
                if (pair == null)
                {
                    continue;
                }

                var sourceBuilder = AddPairSide(
                    builders,
                    pair.SourceFeatureId,
                    pair.SourcePortParameter,
                    recipe,
                    packageId,
                    "来源对象使用的配对标签；与目标对象标签相同时才生成操作");
                // 双实体操作中的实体参数以来源对象为当前实体解析。
                AddRecipeParameters(
                    sourceBuilder,
                    recipe,
                    parameters,
                    recipesById);
                AddPairSide(
                    builders,
                    pair.TargetFeatureId,
                    pair.TargetPortParameter,
                    recipe,
                    packageId,
                    "目标对象使用的配对标签；与来源对象标签相同时才生成操作");
            }

            // “容器”等基础能力不会单独生成动作，因此不会作为配方匹配特征出现。
            // 它们仍是合法课程特征，必须进入工作台目录，不能被误报成未知标签。
            foreach (var capabilityName in InteractionCapabilityIds.All)
            {
                Builder(builders, capabilityName).MarkRegisteredCapability();
            }

            return new CourseFeatureCatalog(
                new ReadOnlyDictionary<string, CourseFeatureDescriptor>(
                    builders.ToDictionary(
                        value => value.Key,
                        value => value.Value.Build(),
                        StringComparer.Ordinal)));
        }

        private static void AddRecipeParameters(
            FeatureBuilder builder,
            RecipeDefinition recipe,
            IReadOnlyDictionary<string, RecipeParameterContract[]> parameters,
            IReadOnlyDictionary<string, RecipeDefinition> recipesById)
        {
            if (builder == null)
            {
                return;
            }

            var current = recipe;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current != null && visited.Add(current.RecipeId))
            {
                if (parameters.TryGetValue(current.RecipeId, out var contracts))
                {
                    foreach (var contract in contracts)
                    {
                        builder.AddParameter(new CourseFeatureParameterDescriptor(
                            contract.ParameterName,
                            contract.ParameterType,
                            contract.IsRequired,
                            contract.Unit,
                            contract.Minimum,
                            contract.Maximum,
                            contract.DefaultValue,
                            $"配方 {current.RecipeId} 的参数"));
                    }
                }

                current = string.IsNullOrWhiteSpace(current.ExtendsRecipeId)
                    || !recipesById.TryGetValue(
                        current.ExtendsRecipeId,
                        out var parent)
                        ? null
                        : parent;
            }
        }

        private static FeatureBuilder AddPairSide(
            IDictionary<string, FeatureBuilder> builders,
            string featureId,
            string parameterName,
            RecipeDefinition recipe,
            string packageId,
            string purpose)
        {
            if (string.IsNullOrWhiteSpace(featureId))
            {
                return null;
            }

            var builder = Builder(builders, featureId);
            builder.AddRecipe(recipe.RecipeId, packageId);
            if (!string.IsNullOrWhiteSpace(parameterName))
            {
                builder.AddParameter(new CourseFeatureParameterDescriptor(
                    parameterName,
                    RecipeParameterType.Text,
                    false,
                    string.Empty,
                    null,
                    null,
                    string.Empty,
                    purpose));
            }

            return builder;
        }

        private static FeatureBuilder Builder(
            IDictionary<string, FeatureBuilder> builders,
            string featureId)
        {
            if (!builders.TryGetValue(featureId, out var builder))
            {
                builder = new FeatureBuilder(featureId);
                builders.Add(featureId, builder);
            }

            return builder;
        }

        private sealed class FeatureBuilder
        {
            private readonly List<string> _recipeIds = new List<string>();
            private readonly List<string> _packageIds = new List<string>();
            private readonly List<CourseFeatureParameterDescriptor> _parameters =
                new List<CourseFeatureParameterDescriptor>();
            private bool _isRegisteredCapability;

            public FeatureBuilder(string featureId)
            {
                FeatureId = featureId;
            }

            public string FeatureId { get; }

            public void AddRecipe(string recipeId, string packageId)
            {
                _recipeIds.Add(recipeId);
                _packageIds.Add(packageId);
            }

            public void AddParameter(CourseFeatureParameterDescriptor parameter) =>
                _parameters.Add(parameter);

            public void MarkRegisteredCapability() =>
                _isRegisteredCapability = true;

            public CourseFeatureDescriptor Build() =>
                new CourseFeatureDescriptor(
                    FeatureId,
                    _recipeIds,
                    _packageIds,
                    _parameters,
                    _isRegisteredCapability);
        }
    }
}
