using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Application.Events;
using VirtualLab.Application.Goals;
using VirtualLab.Domain.Events;

namespace VirtualLab.Application.Assessment
{
    /// <summary>Optional event detail used by generic assessment rules.</summary>
    public interface IHazardEvent : IDomainEvent
    {
        string HazardId { get; }
    }

    public sealed class AssessmentRule
    {
        public AssessmentRule(
            string ruleId,
            string hazardId,
            int scientificResultDelta,
            int operationQualityDelta,
            int safetyDelta,
            int efficiencyDelta,
            string reason)
        {
            RuleId = GoalDefinition.RequireStableId(ruleId, nameof(ruleId), "assessment rule");
            HazardId = GoalDefinition.RequireStableId(hazardId, nameof(hazardId), "hazard");
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("An assessment reason cannot be blank.", nameof(reason));
            }

            ScientificResultDelta = scientificResultDelta;
            OperationQualityDelta = operationQualityDelta;
            SafetyDelta = safetyDelta;
            EfficiencyDelta = efficiencyDelta;
            Reason = reason.Trim();
        }

        public string RuleId { get; }

        public string HazardId { get; }

        public int ScientificResultDelta { get; }

        public int OperationQualityDelta { get; }

        public int SafetyDelta { get; }

        public int EfficiencyDelta { get; }

        public string Reason { get; }

        public bool Matches(DomainEventEnvelope domainEvent)
        {
            if (domainEvent == null)
            {
                throw new ArgumentNullException(nameof(domainEvent));
            }

            if (domainEvent.Event is IHazardEvent hazardEvent)
            {
                return string.Equals(hazardEvent.HazardId, HazardId, StringComparison.Ordinal);
            }

            return false;
        }
    }

    public sealed class AssessmentChange
    {
        public AssessmentChange(
            string ruleId,
            string hazardId,
            long eventSequence,
            string reason,
            ScoreCard before,
            ScoreCard after)
        {
            RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
            HazardId = hazardId ?? throw new ArgumentNullException(nameof(hazardId));
            if (eventSequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(eventSequence));
            }

            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
            Before = before ?? throw new ArgumentNullException(nameof(before));
            After = after ?? throw new ArgumentNullException(nameof(after));
            EventSequence = eventSequence;
        }

        public string RuleId { get; }
        public string HazardId { get; }
        public long EventSequence { get; }
        public string Reason { get; }
        public ScoreCard Before { get; }
        public ScoreCard After { get; }
    }

    public sealed class AssessmentEngine
    {
        private readonly IReadOnlyList<AssessmentRule> _rules;
        private readonly HashSet<long> _handledEventSequences = new HashSet<long>();
        private readonly HashSet<string> _appliedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<AssessmentChange> _changes = new List<AssessmentChange>();
        private readonly ReadOnlyCollection<AssessmentChange> _readOnlyChanges;

        public AssessmentEngine(IReadOnlyList<AssessmentRule> rules)
        {
            if (rules == null)
            {
                throw new ArgumentNullException(nameof(rules));
            }

            var copy = new List<AssessmentRule>(rules.Count);
            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                if (rule == null)
                {
                    throw new ArgumentException("Assessment rules cannot contain null.", nameof(rules));
                }

                if (!ruleIds.Add(rule.RuleId))
                {
                    throw new ArgumentException("Assessment rule IDs must be unique.", nameof(rules));
                }

                copy.Add(rule);
            }

            _rules = new ReadOnlyCollection<AssessmentRule>(copy);
            _readOnlyChanges = new ReadOnlyCollection<AssessmentChange>(_changes);
            Score = ScoreCard.Perfect;
        }

        public IReadOnlyList<AssessmentRule> Rules => _rules;

        public ScoreCard Score { get; private set; }

        public IReadOnlyList<AssessmentChange> Changes => _readOnlyChanges;

        /// <summary>Each rule is intentionally a one-time deduction, even if a hazard is replayed.</summary>
        public void Handle(DomainEventEnvelope domainEvent)
        {
            if (domainEvent == null)
            {
                throw new ArgumentNullException(nameof(domainEvent));
            }

            if (!_handledEventSequences.Add(domainEvent.Sequence))
            {
                return;
            }

            foreach (var rule in _rules)
            {
                if (!_appliedRuleIds.Contains(rule.RuleId) && rule.Matches(domainEvent))
                {
                    var before = Score;
                    var after = before.Apply(
                        rule.ScientificResultDelta,
                        rule.OperationQualityDelta,
                        rule.SafetyDelta,
                        rule.EfficiencyDelta);
                    Score = after;
                    _appliedRuleIds.Add(rule.RuleId);
                    _changes.Add(new AssessmentChange(
                        rule.RuleId,
                        rule.HazardId,
                        domainEvent.Sequence,
                        rule.Reason,
                        before,
                        after));
                }
            }
        }
    }
}
