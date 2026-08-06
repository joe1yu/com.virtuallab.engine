using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Infrastructure.Reporting
{
    public sealed class ExperimentReport
    {
        public ExperimentReport(
            ReportMetadata metadata,
            ReportScore score,
            IEnumerable<ReportGoal> goals,
            IEnumerable<ReportObservation> observations,
            IEnumerable<ReportRisk> risks,
            IEnumerable<ReportDeduction> deductions)
            : this(
                metadata,
                score,
                goals,
                observations,
                risks,
                deductions,
                Array.Empty<ReportHint>())
        {
        }

        public ExperimentReport(
            ReportMetadata metadata,
            ReportScore score,
            IEnumerable<ReportGoal> goals,
            IEnumerable<ReportObservation> observations,
            IEnumerable<ReportRisk> risks,
            IEnumerable<ReportDeduction> deductions,
            IEnumerable<ReportHint> hints)
        {
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Score = score ?? throw new ArgumentNullException(nameof(score));
            Goals = Sort(goals);
            Observations = Sort(observations);
            Risks = Sort(risks);
            Deductions = Sort(deductions);
            Hints = Sort(hints);
        }

        public ReportMetadata Metadata { get; }
        public ReportScore Score { get; }
        public IReadOnlyList<ReportGoal> Goals { get; }
        public IReadOnlyList<ReportObservation> Observations { get; }
        public IReadOnlyList<ReportRisk> Risks { get; }
        public IReadOnlyList<ReportDeduction> Deductions { get; }
        public IReadOnlyList<ReportHint> Hints { get; }

        private static IReadOnlyList<T> Sort<T>(IEnumerable<T> values)
            where T : ReportEvidence
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var copy = values.ToList();
            if (copy.Any(value => value == null))
            {
                throw new ArgumentException(
                    "Report evidence cannot contain null.",
                    nameof(values));
            }

            return new ReadOnlyCollection<T>(
                copy
                    .OrderBy(value => value.Sequence)
                    .ThenBy(value => value.Id, StringComparer.Ordinal)
                    .ToList());
        }
    }

    public sealed class ReportMetadata
    {
        public ReportMetadata(
            string courseId,
            string sessionId,
            int randomSeed,
            long lastSequence)
        {
            CourseId = RequireText(courseId, nameof(courseId));
            SessionId = RequireText(sessionId, nameof(sessionId));
            if (lastSequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lastSequence));
            }

            RandomSeed = randomSeed;
            LastSequence = lastSequence;
        }

        public string CourseId { get; }
        public string SessionId { get; }
        public int RandomSeed { get; }
        public long LastSequence { get; }

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "A report value cannot be blank.",
                    parameterName);
            }

            return value.Trim();
        }
    }

    public sealed class ReportScore
    {
        public ReportScore(
            int scientificResult,
            int operationQuality,
            int safety,
            int efficiency)
        {
            ScientificResult = Validate(
                scientificResult,
                nameof(scientificResult));
            OperationQuality = Validate(
                operationQuality,
                nameof(operationQuality));
            Safety = Validate(safety, nameof(safety));
            Efficiency = Validate(efficiency, nameof(efficiency));
        }

        public int ScientificResult { get; }
        public int OperationQuality { get; }
        public int Safety { get; }
        public int Efficiency { get; }

        private static int Validate(int value, string parameterName)
        {
            if (value < 0 || value > 100)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }
    }

    public abstract class ReportEvidence
    {
        protected ReportEvidence(string id, long sequence)
        {
            Id = ReportMetadata.RequireText(id, nameof(id));
            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence));
            }

            Sequence = sequence;
        }

        public string Id { get; }
        public long Sequence { get; }
    }

    public sealed class ReportGoal : ReportEvidence
    {
        public ReportGoal(string id, long sequence, bool isSatisfied)
            : base(id, sequence)
        {
            IsSatisfied = isSatisfied;
        }

        public bool IsSatisfied { get; }
    }

    public sealed class ReportObservation : ReportEvidence
    {
        public ReportObservation(
            string id,
            long sequence,
            string eventType)
            : base(id, sequence)
        {
            EventType = ReportMetadata.RequireText(
                eventType,
                nameof(eventType));
        }

        public string EventType { get; }
    }

    public sealed class ReportRisk : ReportEvidence
    {
        public ReportRisk(
            string id,
            long sequence,
            string eventType)
            : base(id, sequence)
        {
            EventType = ReportMetadata.RequireText(
                eventType,
                nameof(eventType));
        }

        public string EventType { get; }
    }

    public sealed class ReportDeduction : ReportEvidence
    {
        public ReportDeduction(
            string id,
            long sequence,
            string reason,
            int scientificResultDelta,
            int operationQualityDelta,
            int safetyDelta,
            int efficiencyDelta)
            : base(id, sequence)
        {
            Reason = ReportMetadata.RequireText(reason, nameof(reason));
            ScientificResultDelta = scientificResultDelta;
            OperationQualityDelta = operationQualityDelta;
            SafetyDelta = safetyDelta;
            EfficiencyDelta = efficiencyDelta;
        }

        public string Reason { get; }
        public int ScientificResultDelta { get; }
        public int OperationQualityDelta { get; }
        public int SafetyDelta { get; }
        public int EfficiencyDelta { get; }
    }

    public sealed class ReportHint : ReportEvidence
    {
        public ReportHint(
            string id,
            long sequence,
            string ruleId,
            string goalId,
            int level,
            string message,
            long tick)
            : base(id, sequence)
        {
            RuleId = ReportMetadata.RequireText(
                ruleId,
                nameof(ruleId));
            GoalId = ReportMetadata.RequireText(
                goalId,
                nameof(goalId));
            if (level < 1 || level > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            Message = ReportMetadata.RequireText(
                message,
                nameof(message));
            if (tick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }

            Level = level;
            Tick = tick;
        }

        public string RuleId { get; }
        public string GoalId { get; }
        public int Level { get; }
        public string Message { get; }
        public long Tick { get; }
    }
}
