using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Catalogs
{
    /// <summary>
    /// 合并各模块的创作描述，并在工作台显示前一次性验证标识与引用。
    /// 作者配置错误以诊断返回；目录结果始终保持确定顺序。
    /// </summary>
    public sealed class CourseAuthoringCatalog
    {
        private readonly IReadOnlyDictionary<string, AuthoringCategoryDescriptor>
            _categories;
        private readonly IReadOnlyDictionary<string, AuthoringComponentDescriptor>
            _components;
        private readonly IReadOnlyDictionary<string, AuthoringItemTemplateDescriptor>
            _templates;
        private readonly IReadOnlyDictionary<string, AuthoringOperationDescriptor>
            _operations;
        private readonly IReadOnlyDictionary<string, AuthoringProcessDescriptor>
            _processes;

        private CourseAuthoringCatalog(
            IEnumerable<AuthoringCategoryDescriptor> categories,
            IEnumerable<AuthoringComponentDescriptor> components,
            IEnumerable<AuthoringItemTemplateDescriptor> templates,
            IEnumerable<AuthoringOperationDescriptor> operations,
            IEnumerable<AuthoringProcessDescriptor> processes,
            IEnumerable<AuthoringOptionDescriptor> options,
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            Categories = categories.ToArray();
            Components = components.ToArray();
            Templates = templates.ToArray();
            Operations = operations.ToArray();
            Processes = processes.ToArray();
            Options = options.ToArray();
            Diagnostics = diagnostics.ToArray();
            _categories = Index(Categories, value => value.CategoryId);
            _components = Index(Components, value => value.ComponentId);
            _templates = Index(Templates, value => value.TemplateId);
            _operations = Index(Operations, value => value.OperationId);
            _processes = Index(Processes, value => value.ProcessId);
        }

        public bool IsValid => Diagnostics.Count == 0;
        public IReadOnlyList<AuthoringCategoryDescriptor> Categories { get; }
        public IReadOnlyList<AuthoringComponentDescriptor> Components { get; }
        public IReadOnlyList<AuthoringItemTemplateDescriptor> Templates { get; }
        public IReadOnlyList<AuthoringOperationDescriptor> Operations { get; }
        public IReadOnlyList<AuthoringProcessDescriptor> Processes { get; }
        public IReadOnlyList<AuthoringOptionDescriptor> Options { get; }
        public IReadOnlyList<CourseCompilationDiagnostic> Diagnostics { get; }

        public bool TryGetCategory(
            string categoryId,
            out AuthoringCategoryDescriptor descriptor)
        {
            return _categories.TryGetValue(categoryId ?? string.Empty, out descriptor);
        }

        public bool TryGetComponent(
            string componentId,
            out AuthoringComponentDescriptor descriptor)
        {
            return _components.TryGetValue(componentId ?? string.Empty, out descriptor);
        }

        public bool TryGetTemplate(
            string templateId,
            out AuthoringItemTemplateDescriptor descriptor)
        {
            return _templates.TryGetValue(templateId ?? string.Empty, out descriptor);
        }

        public bool TryGetOperation(
            string operationId,
            out AuthoringOperationDescriptor descriptor)
        {
            return _operations.TryGetValue(operationId ?? string.Empty, out descriptor);
        }

        public bool TryGetProcess(
            string processId,
            out AuthoringProcessDescriptor descriptor)
        {
            return _processes.TryGetValue(processId ?? string.Empty, out descriptor);
        }

        public static CourseAuthoringCatalog Create(
            IEnumerable<ICourseAuthoringCatalogProvider> providers)
        {
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            var diagnostics = new List<CourseCompilationDiagnostic>();
            var modules = LoadModules(providers, diagnostics);
            ValidateDuplicatePackageIds(modules, diagnostics);

            var categories = Collect(
                modules,
                module => module.Descriptor.Categories,
                value => value.CategoryId,
                "category",
                diagnostics)
                .OrderBy(value => value.DisplayOrder)
                .ThenBy(value => value.CategoryId, StringComparer.Ordinal)
                .ToArray();
            var components = Collect(
                modules,
                module => module.Descriptor.Components,
                value => value.ComponentId,
                "component",
                diagnostics)
                .OrderBy(value => value.ComponentId, StringComparer.Ordinal)
                .ToArray();
            var templates = Collect(
                modules,
                module => module.Descriptor.Templates,
                value => value.TemplateId,
                "template",
                diagnostics)
                .OrderBy(value => value.DisplayOrder)
                .ThenBy(value => value.TemplateId, StringComparer.Ordinal)
                .ToArray();
            var operations = Collect(
                modules,
                module => module.Descriptor.Operations,
                value => value.OperationId,
                "operation",
                diagnostics)
                .OrderBy(value => value.OperationId, StringComparer.Ordinal)
                .ToArray();
            var processes = Collect(
                modules,
                module => module.Descriptor.Processes,
                value => value.ProcessId,
                "process",
                diagnostics)
                .OrderBy(value => value.ProcessId, StringComparer.Ordinal)
                .ToArray();
            var options = CollectOptions(modules, diagnostics);

            ValidateTemplateReferences(
                templates,
                categories,
                components,
                operations,
                diagnostics);

            return new CourseAuthoringCatalog(
                categories,
                components,
                templates,
                operations,
                processes,
                options,
                diagnostics
                    .OrderBy(value => value.Code, StringComparer.Ordinal)
                    .ThenBy(value => value.ConfigurationId, StringComparer.Ordinal)
                    .ThenBy(value => value.Reason, StringComparer.Ordinal));
        }

        private static IReadOnlyList<LoadedModule> LoadModules(
            IEnumerable<ICourseAuthoringCatalogProvider> providers,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var loaded = new List<LoadedModule>();
            foreach (var provider in providers
                         .OrderBy(value => value?.PackageId ?? string.Empty,
                             StringComparer.Ordinal)
                         .ThenBy(value => value?.GetType().FullName ?? string.Empty,
                             StringComparer.Ordinal))
            {
                if (provider == null)
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.provider.null",
                        string.Empty,
                        "课程创作目录提供者不能为空。",
                        "移除空提供者，或注册一个有效模块提供者。"));
                    continue;
                }

                try
                {
                    var descriptor = provider.Load();
                    if (descriptor == null)
                    {
                        diagnostics.Add(Diagnostic(
                            "authoring.provider.result-null",
                            provider.PackageId,
                            $"模块“{provider.PackageId}”没有返回创作目录。",
                            "让提供者返回完整的 CourseAuthoringModuleDescriptor。"));
                        continue;
                    }

                    loaded.Add(new LoadedModule(provider.PackageId, descriptor));
                    if (!string.Equals(
                            provider.PackageId,
                            descriptor.PackageId,
                            StringComparison.Ordinal))
                    {
                        diagnostics.Add(Diagnostic(
                            "authoring.provider.package-mismatch",
                            provider.PackageId,
                            $"提供者包标识“{provider.PackageId}”与目录包标识“{descriptor.PackageId}”不一致。",
                            "让提供者和目录使用完全相同的稳定包标识。"));
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.provider.load-failed",
                        provider.PackageId,
                        $"模块“{provider.PackageId}”的创作目录加载失败：{exception.Message}",
                        "检查模块描述数据和提供者构造逻辑。"));
                }
            }

            return loaded;
        }

        private static void ValidateDuplicatePackageIds(
            IEnumerable<LoadedModule> modules,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            foreach (var duplicate in modules
                         .GroupBy(value => value.ProviderPackageId,
                             StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "authoring.package.id-duplicate",
                    duplicate.Key,
                    $"创作目录包标识“{duplicate.Key}”重复。",
                    "每个模块提供者必须使用全局唯一的稳定包标识。"));
            }
        }

        private static IReadOnlyList<T> Collect<T>(
            IEnumerable<LoadedModule> modules,
            Func<LoadedModule, IEnumerable<T>> select,
            Func<T, string> identity,
            string kind,
            ICollection<CourseCompilationDiagnostic> diagnostics)
            where T : class
        {
            var values = modules
                .SelectMany(module => (select(module) ?? Array.Empty<T>())
                    .Select(value => new OwnedDescriptor<T>(module, value)))
                .ToArray();
            foreach (var item in values.Where(value => value.Descriptor == null))
            {
                diagnostics.Add(Diagnostic(
                    $"authoring.{kind}.null",
                    item.Module.ProviderPackageId,
                    $"模块“{item.Module.ProviderPackageId}”包含空的创作描述项。",
                    "删除空项，或填写完整的创作描述。"));
            }

            var nonNull = values.Where(value => value.Descriptor != null).ToArray();
            foreach (var item in nonNull.Where(value =>
                         string.IsNullOrWhiteSpace(identity(value.Descriptor))))
            {
                diagnostics.Add(Diagnostic(
                    $"authoring.{kind}.id-missing",
                    item.Module.ProviderPackageId,
                    $"模块“{item.Module.ProviderPackageId}”包含标识为空的创作描述。",
                    "填写稳定、可读且唯一的中文标识。"));
            }

            foreach (var duplicate in nonNull
                         .Where(value => !string.IsNullOrWhiteSpace(
                             identity(value.Descriptor)))
                         .GroupBy(value => identity(value.Descriptor),
                             StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    $"authoring.{kind}.id-duplicate",
                    duplicate.Key,
                    $"创作描述标识“{duplicate.Key}”被多个模块重复注册。",
                    "合并重复描述，或为不同语义使用不同稳定标识。"));
            }

            return nonNull
                .GroupBy(value => identity(value.Descriptor), StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(value => value.Module.ProviderPackageId,
                        StringComparer.Ordinal)
                    .First().Descriptor)
                .ToArray();
        }

        private static IReadOnlyList<AuthoringOptionDescriptor> CollectOptions(
            IEnumerable<LoadedModule> modules,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var options = modules
                .SelectMany(module => module.Descriptor.Options
                    .Select(value => new OwnedDescriptor<AuthoringOptionDescriptor>(
                        module,
                        value)))
                .ToArray();
            foreach (var item in options.Where(value => value.Descriptor == null))
            {
                diagnostics.Add(Diagnostic(
                    "authoring.option.null",
                    item.Module.ProviderPackageId,
                    $"模块“{item.Module.ProviderPackageId}”包含空的创作选项。",
                    "删除空项，或填写完整的创作选项。"));
            }

            var nonNull = options.Where(value => value.Descriptor != null).ToArray();
            foreach (var duplicate in nonNull
                         .GroupBy(value => OptionKey(value.Descriptor),
                             StringComparer.Ordinal)
                         .Where(value => value.Count() > 1))
            {
                diagnostics.Add(Diagnostic(
                    "authoring.option.id-duplicate",
                    duplicate.First().Descriptor.OptionId,
                    $"同类创作选项“{duplicate.First().Descriptor.OptionId}”重复注册。",
                    "每种选项类型只保留一个相同稳定标识。"));
            }

            return nonNull
                .GroupBy(value => OptionKey(value.Descriptor),
                    StringComparer.Ordinal)
                .Select(group => group
                    .OrderBy(value => value.Module.ProviderPackageId,
                        StringComparer.Ordinal)
                    .First().Descriptor)
                .OrderBy(value => value.Kind)
                .ThenBy(value => value.OptionId, StringComparer.Ordinal)
                .ToArray();
        }

        private static void ValidateTemplateReferences(
            IEnumerable<AuthoringItemTemplateDescriptor> templates,
            IEnumerable<AuthoringCategoryDescriptor> categories,
            IEnumerable<AuthoringComponentDescriptor> components,
            IEnumerable<AuthoringOperationDescriptor> operations,
            ICollection<CourseCompilationDiagnostic> diagnostics)
        {
            var categoryIds = new HashSet<string>(
                categories.Select(value => value.CategoryId),
                StringComparer.Ordinal);
            var componentIds = new HashSet<string>(
                components.Select(value => value.ComponentId),
                StringComparer.Ordinal);
            var operationIds = new HashSet<string>(
                operations.Select(value => value.OperationId),
                StringComparer.Ordinal);

            foreach (var template in templates)
            {
                if (!categoryIds.Contains(template.CategoryId))
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.template.category-missing",
                        template.TemplateId,
                        $"用品模板“{template.TemplateId}”引用的类别“{template.CategoryId}”不存在。",
                        "先注册该用品类别，或选择已经注册的类别。"));
                }

                foreach (var componentId in template.ComponentIds
                             .Where(value => !componentIds.Contains(value)))
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.template.component-missing",
                        template.TemplateId,
                        $"用品模板“{template.TemplateId}”引用的组件“{componentId}”不存在。",
                        "先注册组件，或从模板中移除该组件引用。"));
                }

                foreach (var operationId in template.OperationIds
                             .Where(value => !operationIds.Contains(value)))
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.template.operation-missing",
                        template.TemplateId,
                        $"用品模板“{template.TemplateId}”引用的操作“{operationId}”不存在。",
                        "先注册操作，或从模板中移除该操作引用。"));
                }
            }
        }

        private static IReadOnlyDictionary<string, T> Index<T>(
            IEnumerable<T> values,
            Func<T, string> identity)
        {
            return new ReadOnlyDictionary<string, T>(values
                .GroupBy(identity, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal));
        }

        private static string OptionKey(AuthoringOptionDescriptor option)
        {
            return option.Kind + "\u001f" + option.OptionId;
        }

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            string configurationId,
            string reason,
            string suggestion)
        {
            return new CourseCompilationDiagnostic(
                code,
                "模块创作目录",
                1,
                1,
                string.Empty,
                configurationId ?? string.Empty,
                reason,
                suggestion);
        }

        private sealed class LoadedModule
        {
            public LoadedModule(
                string providerPackageId,
                CourseAuthoringModuleDescriptor descriptor)
            {
                ProviderPackageId = providerPackageId ?? string.Empty;
                Descriptor = descriptor;
            }

            public string ProviderPackageId { get; }
            public CourseAuthoringModuleDescriptor Descriptor { get; }
        }

        private sealed class OwnedDescriptor<T>
            where T : class
        {
            public OwnedDescriptor(LoadedModule module, T descriptor)
            {
                Module = module;
                Descriptor = descriptor;
            }

            public LoadedModule Module { get; }
            public T Descriptor { get; }
        }
    }
}
