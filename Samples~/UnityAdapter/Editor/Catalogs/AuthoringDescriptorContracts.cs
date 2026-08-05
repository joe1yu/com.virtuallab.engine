using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Unity.Authoring.Catalogs
{
    public enum AuthoringParameterType
    {
        Text,
        Boolean,
        Integer,
        Number,
        Entity,
        Port,
        Choice
    }

    public enum AuthoringOperationLifecycle
    {
        Instant,
        Continuous,
        Manipulation
    }

    public enum AuthoringOptionKind
    {
        RelationType,
        FactField,
        PresentationSignal,
        ConsequenceTemplate
    }

    public sealed class AuthoringParameterDescriptor
    {
        public AuthoringParameterDescriptor(
            string parameterId,
            string displayName,
            string description,
            AuthoringParameterType type,
            bool isRequired,
            string unitId,
            double? minimum,
            double? maximum,
            string defaultValue,
            IEnumerable<string> choices = null)
        {
            ParameterId = Normalize(parameterId);
            DisplayName = Normalize(displayName);
            Description = Normalize(description);
            Type = type;
            IsRequired = isRequired;
            UnitId = Normalize(unitId);
            Minimum = minimum;
            Maximum = maximum;
            DefaultValue = Normalize(defaultValue);
            Choices = CopyStrings(choices);
        }

        public string ParameterId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public AuthoringParameterType Type { get; }
        public bool IsRequired { get; }
        public string UnitId { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }
        public string DefaultValue { get; }
        public IReadOnlyList<string> Choices { get; }

        internal static IReadOnlyList<string> CopyStrings(
            IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Select(Normalize)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        internal static string Normalize(string value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }

    public sealed class AuthoringPortDescriptor
    {
        public AuthoringPortDescriptor(
            string portId,
            string displayName,
            string description)
        {
            PortId = AuthoringParameterDescriptor.Normalize(portId);
            DisplayName = AuthoringParameterDescriptor.Normalize(displayName);
            Description = AuthoringParameterDescriptor.Normalize(description);
        }

        public string PortId { get; }
        public string DisplayName { get; }
        public string Description { get; }
    }

    public sealed class AuthoringCategoryDescriptor
    {
        public AuthoringCategoryDescriptor(
            string categoryId,
            string displayName,
            string description,
            int displayOrder)
        {
            CategoryId = AuthoringParameterDescriptor.Normalize(categoryId);
            DisplayName = AuthoringParameterDescriptor.Normalize(displayName);
            Description = AuthoringParameterDescriptor.Normalize(description);
            DisplayOrder = displayOrder;
        }

        public string CategoryId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int DisplayOrder { get; }
    }

    public sealed class AuthoringComponentDescriptor
    {
        public AuthoringComponentDescriptor(
            string componentId,
            string displayName,
            string description,
            IEnumerable<AuthoringParameterDescriptor> parameters,
            IEnumerable<AuthoringPortDescriptor> ports)
        {
            ComponentId = AuthoringParameterDescriptor.Normalize(componentId);
            DisplayName = AuthoringParameterDescriptor.Normalize(displayName);
            Description = AuthoringParameterDescriptor.Normalize(description);
            Parameters = Copy(parameters);
            Ports = Copy(ports);
        }

        public string ComponentId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public IReadOnlyList<AuthoringParameterDescriptor> Parameters { get; }
        public IReadOnlyList<AuthoringPortDescriptor> Ports { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
        {
            return (values ?? Array.Empty<T>()).ToArray();
        }
    }

    public sealed class AuthoringOperationDescriptor
    {
        public AuthoringOperationDescriptor(
            string operationId,
            string displayName,
            string description,
            AuthoringOperationLifecycle lifecycle,
            string executionModeId,
            string startActionId,
            string observationActionId,
            string completionActionId,
            string cancellationActionId)
        {
            OperationId = AuthoringParameterDescriptor.Normalize(operationId);
            DisplayName = AuthoringParameterDescriptor.Normalize(displayName);
            Description = AuthoringParameterDescriptor.Normalize(description);
            Lifecycle = lifecycle;
            ExecutionModeId = AuthoringParameterDescriptor.Normalize(
                executionModeId);
            StartActionId = AuthoringParameterDescriptor.Normalize(startActionId);
            ObservationActionId = AuthoringParameterDescriptor.Normalize(
                observationActionId);
            CompletionActionId = AuthoringParameterDescriptor.Normalize(
                completionActionId);
            CancellationActionId = AuthoringParameterDescriptor.Normalize(
                cancellationActionId);
        }

        public string OperationId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public AuthoringOperationLifecycle Lifecycle { get; }
        public string ExecutionModeId { get; }
        public string StartActionId { get; }
        public string ObservationActionId { get; }
        public string CompletionActionId { get; }
        public string CancellationActionId { get; }
    }

    public sealed class AuthoringProcessDescriptor
    {
        public AuthoringProcessDescriptor(
            string processId,
            string displayName,
            string description,
            IEnumerable<AuthoringParameterDescriptor> parameters)
        {
            ProcessId = AuthoringParameterDescriptor.Normalize(processId);
            DisplayName = AuthoringParameterDescriptor.Normalize(displayName);
            Description = AuthoringParameterDescriptor.Normalize(description);
            Parameters = (parameters ?? Array.Empty<AuthoringParameterDescriptor>())
                .ToArray();
        }

        public string ProcessId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public IReadOnlyList<AuthoringParameterDescriptor> Parameters { get; }
    }

    public sealed class AuthoringOptionDescriptor
    {
        public AuthoringOptionDescriptor(
            AuthoringOptionKind kind,
            string optionId,
            string displayName,
            string description)
        {
            Kind = kind;
            OptionId = AuthoringParameterDescriptor.Normalize(optionId);
            DisplayName = AuthoringParameterDescriptor.Normalize(displayName);
            Description = AuthoringParameterDescriptor.Normalize(description);
        }

        public AuthoringOptionKind Kind { get; }
        public string OptionId { get; }
        public string DisplayName { get; }
        public string Description { get; }
    }

    public sealed class AuthoringItemTemplateDescriptor
    {
        public AuthoringItemTemplateDescriptor(
            string templateId,
            string categoryId,
            string displayName,
            string description,
            int displayOrder,
            IEnumerable<string> componentIds,
            IEnumerable<KeyValuePair<string, string>> defaultParameters,
            IEnumerable<string> operationIds,
            IEnumerable<string> suggestedRoles,
            IEnumerable<string> suggestedTags)
        {
            TemplateId = AuthoringParameterDescriptor.Normalize(templateId);
            CategoryId = AuthoringParameterDescriptor.Normalize(categoryId);
            DisplayName = AuthoringParameterDescriptor.Normalize(displayName);
            Description = AuthoringParameterDescriptor.Normalize(description);
            DisplayOrder = displayOrder;
            ComponentIds = AuthoringParameterDescriptor.CopyStrings(componentIds);
            DefaultParameters = CopyParameters(defaultParameters);
            OperationIds = AuthoringParameterDescriptor.CopyStrings(operationIds);
            SuggestedRoles = AuthoringParameterDescriptor.CopyStrings(
                suggestedRoles);
            SuggestedTags = AuthoringParameterDescriptor.CopyStrings(
                suggestedTags);
        }

        public string TemplateId { get; }
        public string CategoryId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int DisplayOrder { get; }
        public IReadOnlyList<string> ComponentIds { get; }
        public IReadOnlyDictionary<string, string> DefaultParameters { get; }
        public IReadOnlyList<string> OperationIds { get; }
        public IReadOnlyList<string> SuggestedRoles { get; }
        public IReadOnlyList<string> SuggestedTags { get; }

        private static IReadOnlyDictionary<string, string> CopyParameters(
            IEnumerable<KeyValuePair<string, string>> values)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values
                         ?? Array.Empty<KeyValuePair<string, string>>())
            {
                var key = AuthoringParameterDescriptor.Normalize(pair.Key);
                if (key.Length == 0 || result.ContainsKey(key))
                {
                    throw new ArgumentException(
                        "模板默认参数的名称不能为空或重复。",
                        nameof(values));
                }

                result.Add(
                    key,
                    AuthoringParameterDescriptor.Normalize(pair.Value));
            }

            return new ReadOnlyDictionary<string, string>(result);
        }
    }

    public sealed class CourseAuthoringModuleDescriptor
    {
        public CourseAuthoringModuleDescriptor(
            string packageId,
            IEnumerable<AuthoringCategoryDescriptor> categories,
            IEnumerable<AuthoringComponentDescriptor> components,
            IEnumerable<AuthoringItemTemplateDescriptor> templates,
            IEnumerable<AuthoringOperationDescriptor> operations,
            IEnumerable<AuthoringProcessDescriptor> processes,
            IEnumerable<AuthoringOptionDescriptor> options)
        {
            PackageId = AuthoringParameterDescriptor.Normalize(packageId);
            Categories = Copy(categories);
            Components = Copy(components);
            Templates = Copy(templates);
            Operations = Copy(operations);
            Processes = Copy(processes);
            Options = Copy(options);
        }

        public string PackageId { get; }
        public IReadOnlyList<AuthoringCategoryDescriptor> Categories { get; }
        public IReadOnlyList<AuthoringComponentDescriptor> Components { get; }
        public IReadOnlyList<AuthoringItemTemplateDescriptor> Templates { get; }
        public IReadOnlyList<AuthoringOperationDescriptor> Operations { get; }
        public IReadOnlyList<AuthoringProcessDescriptor> Processes { get; }
        public IReadOnlyList<AuthoringOptionDescriptor> Options { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
        {
            return (values ?? Array.Empty<T>()).ToArray();
        }
    }

    public interface ICourseAuthoringCatalogProvider
    {
        string PackageId { get; }
        CourseAuthoringModuleDescriptor Load();
    }
}
