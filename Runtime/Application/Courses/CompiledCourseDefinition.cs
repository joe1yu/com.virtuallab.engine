using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 编译后的课程定义。该类型只描述运行时所需事实，不携带版本或内容指纹。
    /// </summary>
    public sealed class CompiledCourseDefinition
    {
        public static CompiledCourseDefinition CreateBasic(
            string courseId,
            IEnumerable<CourseEntityDefinition> entities,
            IEnumerable<ActionPolicyDefinition> actionPolicies,
            IEnumerable<CourseGoalDefinition> goals,
            IEnumerable<CourseAssessmentDefinition> assessments,
            IEnumerable<CourseResourceDefinition> resources = null,
            string experimentPrefabResourceId = null)
        {
            return new CompiledCourseDefinition(
                courseId,
                "学生",
                Array.Empty<string>(),
                new[] { CourseModuleIds.Core },
                experimentPrefabResourceId,
                entities,
                actionPolicies,
                goals,
                assessments,
                Array.Empty<StructuredRuleDefinition>(),
                Array.Empty<ConfiguredActionDefinition>(),
                resources ?? Array.Empty<CourseResourceDefinition>(),
                Array.Empty<CoursePortDefinition>(),
                Array.Empty<CourseInitialRelationDefinition>(),
                Array.Empty<ConfiguredMutationDefinition>(),
                Array.Empty<CourseDomainEventDefinition>(),
                Array.Empty<CourseContinuousProcessDefinition>(),
                Array.Empty<CourseActionResultGroupDefinition>(),
                Array.Empty<CourseGoalRuleDefinition>(),
                Array.Empty<CourseActionAssessmentDefinition>(),
                Array.Empty<CourseSceneLayoutDefinition>(),
                Array.Empty<CoursePrefabContractDefinition>());
        }

        public CompiledCourseDefinition(
            string courseId,
            string actorEntityId,
            IEnumerable<string> disciplinePackageIds,
            IEnumerable<string> requiredModuleIds,
            string experimentPrefabResourceId,
            IEnumerable<CourseEntityDefinition> entities,
            IEnumerable<ActionPolicyDefinition> actionPolicies,
            IEnumerable<CourseGoalDefinition> goals,
            IEnumerable<CourseAssessmentDefinition> assessments,
            IEnumerable<StructuredRuleDefinition> rules,
            IEnumerable<ConfiguredActionDefinition> configuredActions,
            IEnumerable<CourseResourceDefinition> resources,
            IEnumerable<CoursePortDefinition> ports,
            IEnumerable<CourseInitialRelationDefinition> initialRelations,
            IEnumerable<ConfiguredMutationDefinition> stateChanges,
            IEnumerable<CourseDomainEventDefinition> domainEvents,
            IEnumerable<CourseContinuousProcessDefinition> continuousProcesses,
            IEnumerable<CourseActionResultGroupDefinition> actionResultGroups,
            IEnumerable<CourseGoalRuleDefinition> goalRules,
            IEnumerable<CourseActionAssessmentDefinition> actionAssessments,
            IEnumerable<CourseSceneLayoutDefinition> sceneLayouts,
            IEnumerable<CoursePrefabContractDefinition> prefabContracts)
        {
            CourseId = CourseContractGuard.Required(courseId, "课程 ID");
            // “学生”只作为旧资产迁移的兼容值；新课程由课程.csv 明确声明。
            ActorEntityId = CourseContractGuard.Required(
                actorEntityId ?? "学生",
                "操作者实体 ID");
            DisciplinePackageIds = new ReadOnlyCollection<string>(
                (disciplinePackageIds ?? Array.Empty<string>())
                .Select(value => CourseContractGuard.Required(
                    value,
                    "学科配方包 ID"))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
            RequiredModuleIds = new ReadOnlyCollection<string>(
                CourseContractGuard.CopyStrings(
                        requiredModuleIds,
                        "课程所需模块")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());
            if (RequiredModuleIds.Count == 0)
            {
                throw new ArgumentException("课程至少需要声明一个运行时模块。",
                    nameof(requiredModuleIds));
            }

            ExperimentPrefabResourceId = CourseContractGuard.Optional(
                experimentPrefabResourceId);
            Entities = CourseContractGuard.CopyUnique(
                entities,
                value => value.EntityId,
                "课程实体");
            ActionPolicies = CourseContractGuard.CopyUnique(
                actionPolicies,
                value => value.PolicyId,
                "动作策略");
            Goals = CourseContractGuard.CopyUnique(
                goals,
                value => value.GoalId,
                "课程目标");
            Assessments = CourseContractGuard.CopyUnique(
                assessments,
                value => value.AssessmentId,
                "评分定义");
            Rules = CourseContractGuard.CopyUnique(
                rules,
                value => value.RuleId,
                "结构化规则");
            ConfiguredActions = CourseContractGuard.CopyUnique(
                configuredActions,
                value => value.PolicyId,
                "可执行语义动作");
            Resources = CourseContractGuard.CopyUnique(
                resources,
                value => value.ResourceId,
                "课程资源");
            Ports = CourseContractGuard.CopyUnique(
                ports,
                value => value.PortId,
                "课程端口");
            InitialRelations = CourseContractGuard.CopyUnique(
                initialRelations,
                value => value.RelationId,
                "初始关系");
            StateChanges = CourseContractGuard.CopyUnique(
                stateChanges,
                value => value.MutationId,
                "状态变化");
            DomainEvents = CourseContractGuard.CopyUnique(
                domainEvents,
                value => value.EventId,
                "领域事件");
            ContinuousProcesses = CourseContractGuard.CopyUnique(
                continuousProcesses,
                value => value.ProcessId,
                "持续过程");
            ActionResultGroups = CourseContractGuard.CopyUnique(
                actionResultGroups,
                value => value.GroupId,
                "动作结果组");
            GoalRules = CourseContractGuard.CopyUnique(
                goalRules,
                value => value.GoalId,
                "目标规则");
            ActionAssessments = CourseContractGuard.CopyUnique(
                actionAssessments,
                value => value.AssessmentId,
                "动作评价");
            SceneLayouts = CourseContractGuard.CopyUnique(
                sceneLayouts,
                value => value.EntityId,
                "场景布局");
            PrefabContracts = CourseContractGuard.CopyUnique(
                prefabContracts,
                value => value.EntityId,
                "实体视图契约");
        }

        public string CourseId { get; }

        public string ActorEntityId { get; }

        public IReadOnlyList<string> DisciplinePackageIds { get; }

        public IReadOnlyList<string> RequiredModuleIds { get; }

        public string ExperimentPrefabResourceId { get; }

        public IReadOnlyList<CourseEntityDefinition> Entities { get; }

        public IReadOnlyList<ActionPolicyDefinition> ActionPolicies { get; }

        public IReadOnlyList<CourseGoalDefinition> Goals { get; }

        public IReadOnlyList<CourseAssessmentDefinition> Assessments { get; }

        public IReadOnlyList<StructuredRuleDefinition> Rules { get; }

        public IReadOnlyList<ConfiguredActionDefinition> ConfiguredActions
        {
            get;
        }

        public IReadOnlyList<CourseResourceDefinition> Resources { get; }
        public IReadOnlyList<CoursePortDefinition> Ports { get; }
        public IReadOnlyList<CourseInitialRelationDefinition> InitialRelations { get; }
        public IReadOnlyList<ConfiguredMutationDefinition> StateChanges { get; }
        public IReadOnlyList<CourseDomainEventDefinition> DomainEvents { get; }
        public IReadOnlyList<CourseContinuousProcessDefinition>
            ContinuousProcesses { get; }
        public IReadOnlyList<CourseActionResultGroupDefinition>
            ActionResultGroups { get; }
        public IReadOnlyList<CourseGoalRuleDefinition> GoalRules { get; }
        public IReadOnlyList<CourseActionAssessmentDefinition>
            ActionAssessments { get; }
        public IReadOnlyList<CourseSceneLayoutDefinition> SceneLayouts { get; }
        public IReadOnlyList<CoursePrefabContractDefinition>
            PrefabContracts { get; }
    }

    /// <summary>
    /// 课程中的语义实体。实体的 Unity 视图由实验总预制体中的同 ID 节点提供。
    /// </summary>
    public sealed class CourseEntityDefinition
    {
        public CourseEntityDefinition(
            string entityId,
            IEnumerable<string> capabilityIds)
        {
            EntityId = CourseContractGuard.Required(entityId, "实体 ID");
            CapabilityIds = CourseContractGuard.CopyStrings(
                capabilityIds,
                $"实体“{EntityId}”的能力 ID");
        }

        public string EntityId { get; }

        public IReadOnlyList<string> CapabilityIds { get; }
    }

    /// <summary>
    /// 某类语义动作适用的实体与规则集合。
    /// </summary>
    public sealed class ActionPolicyDefinition
    {
        public ActionPolicyDefinition(
            string policyId,
            string actionId,
            string sourceEntityId,
            string targetEntityId,
            IEnumerable<string> ruleIds)
        {
            PolicyId = CourseContractGuard.Required(policyId, "动作策略 ID");
            ActionId = CourseContractGuard.Required(
                actionId,
                $"动作策略“{PolicyId}”的动作 ID");
            SourceEntityId = CourseContractGuard.Required(
                sourceEntityId,
                $"动作策略“{PolicyId}”的来源实体 ID");
            TargetEntityId = CourseContractGuard.Optional(targetEntityId);
            RuleIds = CourseContractGuard.CopyStrings(
                ruleIds,
                $"动作策略“{PolicyId}”的规则 ID");
        }

        public string PolicyId { get; }

        public string ActionId { get; }

        public string SourceEntityId { get; }

        public string TargetEntityId { get; }

        public IReadOnlyList<string> RuleIds { get; }
    }

    public sealed class CourseGoalDefinition
    {
        public CourseGoalDefinition(string goalId, string displayName)
        {
            GoalId = CourseContractGuard.Required(goalId, "课程目标 ID");
            DisplayName = CourseContractGuard.Required(
                displayName,
                $"课程目标“{GoalId}”的显示名称");
        }

        public string GoalId { get; }

        public string DisplayName { get; }
    }

    public sealed class CourseAssessmentDefinition
    {
        public CourseAssessmentDefinition(
            string assessmentId,
            string dimensionId,
            int maximumScore)
        {
            AssessmentId = CourseContractGuard.Required(
                assessmentId,
                "评分定义 ID");
            DimensionId = CourseContractGuard.Required(
                dimensionId,
                $"评分定义“{AssessmentId}”的维度 ID");

            if (maximumScore < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumScore),
                    maximumScore,
                    $"评分定义“{AssessmentId}”的最高分不能小于 0。");
            }

            MaximumScore = maximumScore;
        }

        public string AssessmentId { get; }

        public string DimensionId { get; }

        public int MaximumScore { get; }
    }

    internal static class CourseContractGuard
    {
        public static string Required(string value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    $"{context}不能为空。",
                    nameof(value));
            }

            return value.Trim();
        }

        public static string Optional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static IReadOnlyList<T> CopyUnique<T>(
            IEnumerable<T> source,
            Func<T, string> idSelector,
            string context)
        {
            if (source == null)
            {
                throw new ArgumentNullException(
                    nameof(source),
                    $"{context}集合不能为空。");
            }

            var copy = source.ToArray();
            if (copy.Any(value => value == null))
            {
                throw new ArgumentException(
                    $"{context}集合不能包含空项。",
                    nameof(source));
            }

            var duplicateId = copy
                .GroupBy(idSelector, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1)
                ?.Key;
            if (duplicateId != null)
            {
                throw new ArgumentException(
                    $"{context}包含重复 ID“{duplicateId}”。",
                    nameof(source));
            }

            return new ReadOnlyCollection<T>(copy);
        }

        public static IReadOnlyList<string> CopyStrings(
            IEnumerable<string> source,
            string context)
        {
            if (source == null)
            {
                throw new ArgumentNullException(
                    nameof(source),
                    $"{context}集合不能为空。");
            }

            var copy = source
                .Select(value => Required(value, context))
                .ToArray();
            var duplicate = copy
                .GroupBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1)
                ?.Key;
            if (duplicate != null)
            {
                throw new ArgumentException(
                    $"{context}包含重复值“{duplicate}”。",
                    nameof(source));
            }

            return new ReadOnlyCollection<string>(copy);
        }
    }
}
