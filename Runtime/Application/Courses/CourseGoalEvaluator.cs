using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Domain;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 一个只读取权威世界状态的结构化条件。
    /// </summary>
    public sealed class CourseConditionDefinition
    {
        public CourseConditionDefinition(
            string conditionId,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId,
            IEnumerable<KeyValuePair<string, StructuredValue>> parameters,
            IEnumerable<StructuredRuleDefinition> rules)
        {
            ConditionId = CourseContractGuard.Required(conditionId, "条件 ID");
            ActorEntityId = CourseContractGuard.Required(actorEntityId, "条件操作者");
            SourceEntityId = CourseContractGuard.Required(sourceEntityId, "条件来源实体");
            TargetEntityId = CourseContractGuard.Optional(targetEntityId);
            Parameters = new ReadOnlyDictionary<string, StructuredValue>(
                parameters.ToDictionary(
                    value => CourseContractGuard.Required(value.Key, "条件参数名"),
                    value => value.Value,
                    StringComparer.Ordinal));
            Rules = CourseContractGuard.CopyUnique(
                rules,
                value => value.RuleId,
                $"条件“{ConditionId}”的规则");
        }

        public string ConditionId { get; }
        public string ActorEntityId { get; }
        public string SourceEntityId { get; }
        public string TargetEntityId { get; }
        public IReadOnlyDictionary<string, StructuredValue> Parameters { get; }
        public IReadOnlyList<StructuredRuleDefinition> Rules { get; }
    }

    public sealed class CourseGoalRuleDefinition
    {
        public CourseGoalRuleDefinition(
            string goalId,
            IEnumerable<CourseConditionDefinition> conditions)
        {
            GoalId = CourseContractGuard.Required(goalId, "目标 ID");
            Conditions = CourseContractGuard.CopyUnique(
                conditions,
                value => value.ConditionId,
                $"目标“{GoalId}”的条件");
        }

        public string GoalId { get; }
        public IReadOnlyList<CourseConditionDefinition> Conditions { get; }
    }

    public sealed class CourseGoalEvaluationResult :
        IEquatable<CourseGoalEvaluationResult>
    {
        public CourseGoalEvaluationResult(
            IEnumerable<string> completedGoalIds)
        {
            CompletedGoalIds = CourseContractGuard.CopyStrings(
                completedGoalIds,
                "已完成目标");
        }

        public IReadOnlyList<string> CompletedGoalIds { get; }

        public bool Equals(CourseGoalEvaluationResult other)
        {
            return other != null
                && CompletedGoalIds.SequenceEqual(
                    other.CompletedGoalIds,
                    StringComparer.Ordinal);
        }

        public override bool Equals(object obj) =>
            Equals(obj as CourseGoalEvaluationResult);

        public override int GetHashCode() => CompletedGoalIds.Count;
    }

    public sealed class CourseGoalEvaluator
    {
        private readonly StructuredRuleEvaluator _rules;

        public CourseGoalEvaluator(StructuredRuleEvaluator rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public CourseGoalEvaluationResult Evaluate(
            ExperimentWorld world,
            IEnumerable<CourseGoalRuleDefinition> goals)
        {
            var completed = new List<string>();
            foreach (var goal in goals.OrderBy(
                value => value.GoalId,
                StringComparer.Ordinal))
            {
                if (goal.Conditions.All(
                    condition => EvaluateCondition(world, condition)))
                {
                    completed.Add(goal.GoalId);
                }
            }

            return new CourseGoalEvaluationResult(completed);
        }

        internal bool EvaluateCondition(
            ExperimentWorld world,
            CourseConditionDefinition condition)
        {
            var request = new SemanticActionRequest(
                "评价." + condition.ConditionId,
                "course.evaluate",
                "评价操作." + condition.ConditionId,
                SemanticActionPhase.Complete,
                0d,
                condition.ActorEntityId,
                condition.SourceEntityId,
                condition.TargetEntityId,
                condition.Parameters);
            return _rules.Evaluate(
                condition.Rules,
                new StructuredRuleContext(request, world)).IsAccepted;
        }
    }
}
