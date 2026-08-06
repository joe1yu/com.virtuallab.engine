using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Diagnostics;

namespace VirtualLab.Unity.Authoring.Recipes
{
    /// <summary>
    /// 保证面向作者的抽象操作目录与运行时共享配方使用同一套操作语义。
    /// </summary>
    public static class RecipeAuthoringOperationContractValidator
    {
        public static IReadOnlyList<CourseCompilationDiagnostic> Validate(
            CourseAuthoringCatalog authoringCatalog,
            RecipeCatalog recipeCatalog)
        {
            if (authoringCatalog == null)
            {
                throw new ArgumentNullException(nameof(authoringCatalog));
            }

            if (recipeCatalog == null)
            {
                throw new ArgumentNullException(nameof(recipeCatalog));
            }

            if (!authoringCatalog.IsValid || !recipeCatalog.IsValid)
            {
                return Array.Empty<CourseCompilationDiagnostic>();
            }

            var diagnostics = new List<CourseCompilationDiagnostic>();
            var actions = recipeCatalog.Packages
                .SelectMany(value => value.Actions)
                .OrderBy(value => value.OperationId, StringComparer.Ordinal)
                .ThenBy(value => value.Phase)
                .ThenBy(value => value.RecipeId, StringComparer.Ordinal);
            foreach (var action in actions)
            {
                if (!authoringCatalog.TryGetOperation(
                        action.OperationId,
                        out var operation))
                {
                    continue;
                }

                if (!Enum.IsDefined(
                        typeof(AuthoringOperationLifecycle),
                        operation.Lifecycle))
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.operation.lifecycle-invalid",
                        action.Source,
                        operation.OperationId,
                        $"抽象操作“{operation.OperationId}”使用了未注册的生命周期。",
                        "生命周期只能选择即时、持续或操纵。"));
                    continue;
                }

                var expectedLifecycle = Lifecycle(operation.Lifecycle);
                if (action.Lifecycle != expectedLifecycle)
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.operation.lifecycle-mismatch",
                        action.Source,
                        operation.OperationId,
                        $"抽象操作“{operation.OperationId}”在创作目录与共享配方中的生命周期不一致。",
                        "让抽象操作目录和操作配方使用相同的即时、持续或操纵生命周期。"));
                }

                if (!string.Equals(
                        action.ExecutionModeId,
                        operation.ExecutionModeId,
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.operation.execution-mode-mismatch",
                        action.Source,
                        operation.OperationId,
                        $"抽象操作“{operation.OperationId}”在创作目录与共享配方中的执行方式不一致。",
                        "让两处使用相同的设备无关执行方式标识。"));
                }

                var expectedCommand = SemanticCommand(operation, action.Phase);
                if (!string.Equals(
                        action.SemanticCommandId,
                        expectedCommand,
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "authoring.operation.phase-command-mismatch",
                        action.Source,
                        operation.OperationId,
                        $"抽象操作“{operation.OperationId}”的“{PhaseName(action.Phase)}”阶段动作在创作目录与共享配方中不一致。",
                        "让该阶段使用同一个语义动作标识；不需要的阶段应在两处同时留空。"));
                }
            }

            return diagnostics;
        }

        private static SemanticActionLifecycle Lifecycle(
            AuthoringOperationLifecycle lifecycle) => lifecycle switch
            {
                AuthoringOperationLifecycle.Instant =>
                    SemanticActionLifecycle.Instant,
                AuthoringOperationLifecycle.Continuous =>
                    SemanticActionLifecycle.Continuous,
                AuthoringOperationLifecycle.Manipulation =>
                    SemanticActionLifecycle.Manipulation,
                _ => throw new ArgumentOutOfRangeException(nameof(lifecycle))
            };

        private static string SemanticCommand(
            AuthoringOperationDescriptor operation,
            SemanticActionPhase phase) => phase switch
            {
                SemanticActionPhase.Start => operation.StartActionId,
                SemanticActionPhase.Observe => operation.ObservationActionId,
                SemanticActionPhase.Complete => operation.CompletionActionId,
                SemanticActionPhase.Cancel => operation.CancellationActionId,
                _ => throw new ArgumentOutOfRangeException(nameof(phase))
            };

        private static string PhaseName(SemanticActionPhase phase) =>
            phase switch
            {
                SemanticActionPhase.Start => "开始",
                SemanticActionPhase.Observe => "观测",
                SemanticActionPhase.Complete => "完成",
                SemanticActionPhase.Cancel => "取消",
                _ => phase.ToString()
            };

        private static CourseCompilationDiagnostic Diagnostic(
            string code,
            ConfigurationSource source,
            string configurationId,
            string reason,
            string suggestion) =>
            new CourseCompilationDiagnostic(
                code,
                source?.FileName ?? "共享配方目录",
                source?.Line ?? 1,
                source?.Column ?? 1,
                string.Empty,
                configurationId,
                reason,
                suggestion);
    }
}
