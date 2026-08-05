using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Commands;
using VirtualLab.Domain;

namespace VirtualLab.Application.Courses
{
    /// <summary>
    /// 后果严重度描述已经发生的影响，不决定动作是否允许执行。
    /// 枚举顺序从轻到重，供课程结局聚合时比较。
    /// </summary>
    public enum CourseConsequenceSeverity
    {
        PhenomenonDeviation,
        ExperimentRisk,
        SafetyIncident
    }

    /// <summary>
    /// 表示后果发生后实验如何继续，与后果严重度相互独立。
    /// </summary>
    public enum CourseContinuationMode
    {
        CanContinue,
        ContinueAfterCorrection,
        ContinueAfterReplacement,
        RestartRequired,
        CannotContinue
    }

    public enum CourseRunStatus
    {
        InProgress,
        Completed,
        Failed
    }

    public enum CourseResultQuality
    {
        Normal,
        Degraded,
        RecoveryRequired
    }

    public enum CourseAssessmentTriggerKind
    {
        ActionRejected,
        DomainEvent
    }

    public sealed class CourseAssessmentRuleDefinition
    {
        public CourseAssessmentRuleDefinition(
            string assessmentId,
            string actorEntityId,
            string sourceEntityId,
            string targetEntityId,
            IEnumerable<StructuredRuleDefinition> rules,
            string riskId,
            int scoreDelta,
            string prompt)
        {
            AssessmentId = CourseContractGuard.Required(assessmentId, "评价 ID");
            Condition = new CourseConditionDefinition(
                "条件." + AssessmentId,
                actorEntityId,
                sourceEntityId,
                targetEntityId,
                Array.Empty<KeyValuePair<string, StructuredValue>>(),
                rules);
            RiskId = CourseContractGuard.Required(riskId, "风险 ID");
            ScoreDelta = scoreDelta;
            Prompt = CourseContractGuard.Required(prompt, "评价提示");
        }

        public string AssessmentId { get; }
        public CourseConditionDefinition Condition { get; }
        public string RiskId { get; }
        public int ScoreDelta { get; }
        public string Prompt { get; }
    }

    /// <summary>
    /// 根据语义动作的裁决结果记录风险证据，不依赖输入设备或表现层状态。
    /// </summary>
    public sealed class CourseActionAssessmentDefinition
    {
        public CourseActionAssessmentDefinition(
            string assessmentId,
            string actionId,
            string rejectionCode,
            string riskId,
            int scoreDelta,
            string prompt,
            IEnumerable<CourseConditionDefinition> conditions = null,
            CourseAssessmentTriggerKind triggerKind =
                CourseAssessmentTriggerKind.ActionRejected,
            string triggerValue = null,
            CourseConsequenceSeverity severity =
                CourseConsequenceSeverity.ExperimentRisk,
            CourseContinuationMode continuation =
                CourseContinuationMode.ContinueAfterCorrection,
            IEnumerable<string> affectedTargetIds = null)
        {
            if (!Enum.IsDefined(typeof(CourseAssessmentTriggerKind), triggerKind))
            {
                throw new ArgumentOutOfRangeException(nameof(triggerKind));
            }

            AssessmentId = CourseContractGuard.Required(
                assessmentId,
                "评价 ID");
            TriggerKind = triggerKind;
            ActionId = triggerKind == CourseAssessmentTriggerKind.ActionRejected
                ? CourseContractGuard.Required(actionId, "动作 ID")
                : CourseContractGuard.Optional(actionId) ?? string.Empty;
            RejectionCode = triggerKind
                    == CourseAssessmentTriggerKind.ActionRejected
                ? CourseContractGuard.Required(rejectionCode, "拒绝原因")
                : CourseContractGuard.Optional(rejectionCode) ?? string.Empty;
            RiskId = CourseContractGuard.Required(riskId, "风险 ID");
            ScoreDelta = scoreDelta;
            Prompt = CourseContractGuard.Required(prompt, "评价提示");
            Conditions = CourseContractGuard.CopyUnique(
                conditions ?? Array.Empty<CourseConditionDefinition>(),
                value => value.ConditionId,
                $"动作评价“{AssessmentId}”的条件");
            TriggerValue = triggerKind
                    == CourseAssessmentTriggerKind.ActionRejected
                ? RejectionCode
                : CourseContractGuard.Required(
                    triggerValue,
                    "风险领域事件类型");
            if (!Enum.IsDefined(typeof(CourseConsequenceSeverity), severity))
            {
                throw new ArgumentOutOfRangeException(nameof(severity));
            }

            if (!Enum.IsDefined(
                    typeof(CourseContinuationMode),
                    continuation))
            {
                throw new ArgumentOutOfRangeException(nameof(continuation));
            }

            Severity = severity;
            Continuation = continuation;
            AffectedTargetIds = CourseContractGuard.CopyStrings(
                affectedTargetIds ?? Array.Empty<string>(),
                $"动作评价“{AssessmentId}”的受影响目标");
        }

        public static CourseActionAssessmentDefinition ForDomainEvent(
            string assessmentId,
            string eventType,
            string riskId,
            int scoreDelta,
            string prompt,
            IEnumerable<CourseConditionDefinition> conditions = null,
            CourseConsequenceSeverity severity =
                CourseConsequenceSeverity.ExperimentRisk,
            CourseContinuationMode continuation =
                CourseContinuationMode.ContinueAfterCorrection,
            IEnumerable<string> affectedTargetIds = null) =>
            new CourseActionAssessmentDefinition(
                assessmentId,
                string.Empty,
                string.Empty,
                riskId,
                scoreDelta,
                prompt,
                conditions,
                CourseAssessmentTriggerKind.DomainEvent,
                eventType,
                severity,
                continuation,
                affectedTargetIds);

        public string AssessmentId { get; }
        public string ActionId { get; }
        public string RejectionCode { get; }
        public string RiskId { get; }
        public int ScoreDelta { get; }
        public string Prompt { get; }
        public IReadOnlyList<CourseConditionDefinition> Conditions { get; }
        public CourseAssessmentTriggerKind TriggerKind { get; }
        public string TriggerValue { get; }
        public CourseConsequenceSeverity Severity { get; }
        public CourseContinuationMode Continuation { get; }
        public IReadOnlyList<string> AffectedTargetIds { get; }
    }

    public sealed class CourseAssessmentEvidence :
        IEquatable<CourseAssessmentEvidence>
    {
        public CourseAssessmentEvidence(
            string assessmentId,
            string riskId,
            int scoreDelta,
            string prompt,
            string commandId = null,
            CourseConsequenceSeverity severity =
                CourseConsequenceSeverity.ExperimentRisk,
            CourseContinuationMode continuation =
                CourseContinuationMode.ContinueAfterCorrection,
            IEnumerable<string> affectedTargetIds = null)
        {
            AssessmentId = assessmentId;
            RiskId = riskId;
            ScoreDelta = scoreDelta;
            Prompt = prompt;
            CommandId = CourseContractGuard.Optional(commandId);
            if (!Enum.IsDefined(typeof(CourseConsequenceSeverity), severity))
            {
                throw new ArgumentOutOfRangeException(nameof(severity));
            }

            if (!Enum.IsDefined(
                    typeof(CourseContinuationMode),
                    continuation))
            {
                throw new ArgumentOutOfRangeException(nameof(continuation));
            }

            Severity = severity;
            Continuation = continuation;
            AffectedTargetIds = CourseContractGuard.CopyStrings(
                affectedTargetIds ?? Array.Empty<string>(),
                $"评价证据“{AssessmentId}”的受影响目标");
        }

        public string AssessmentId { get; }
        public string RiskId { get; }
        public int ScoreDelta { get; }
        public string Prompt { get; }
        public string CommandId { get; }
        public CourseConsequenceSeverity Severity { get; }
        public CourseContinuationMode Continuation { get; }
        public IReadOnlyList<string> AffectedTargetIds { get; }

        public bool Equals(CourseAssessmentEvidence other) =>
            other != null
            && AssessmentId == other.AssessmentId
            && RiskId == other.RiskId
            && ScoreDelta == other.ScoreDelta
            && Prompt == other.Prompt
            && CommandId == other.CommandId
            && Severity == other.Severity
            && Continuation == other.Continuation
            && AffectedTargetIds.SequenceEqual(
                other.AffectedTargetIds,
                StringComparer.Ordinal);

        public override bool Equals(object obj) =>
            Equals(obj as CourseAssessmentEvidence);

        public override int GetHashCode()
        {
            unchecked
            {
                return (AssessmentId.GetHashCode() * 397)
                    ^ (CommandId?.GetHashCode() ?? 0);
            }
        }
    }

    public sealed class CourseAssessmentEvaluationResult :
        IEquatable<CourseAssessmentEvaluationResult>
    {
        public CourseAssessmentEvaluationResult(
            int score,
            IEnumerable<string> riskIds,
            IEnumerable<CourseAssessmentEvidence> evidence)
        {
            Score = score;
            RiskIds = CourseContractGuard.CopyStrings(riskIds, "风险记录");
            Evidence = evidence.ToArray();
        }

        public int Score { get; }
        public IReadOnlyList<string> RiskIds { get; }
        public IReadOnlyList<CourseAssessmentEvidence> Evidence { get; }

        public bool Equals(CourseAssessmentEvaluationResult other) =>
            other != null
            && Score == other.Score
            && RiskIds.SequenceEqual(other.RiskIds, StringComparer.Ordinal)
            && Evidence.SequenceEqual(other.Evidence);

        public override bool Equals(object obj) =>
            Equals(obj as CourseAssessmentEvaluationResult);

        public override int GetHashCode() => Score;
    }

    public sealed class CourseAssessmentEvaluator
    {
        private readonly CourseGoalEvaluator _conditions;

        public CourseAssessmentEvaluator(StructuredRuleEvaluator rules)
        {
            _conditions = new CourseGoalEvaluator(rules);
        }

        public CourseAssessmentEvaluationResult Evaluate(
            ExperimentWorld world,
            IEnumerable<CourseAssessmentRuleDefinition> assessments,
            int maximumScore)
        {
            if (maximumScore < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumScore));
            }

            var evidence = assessments
                .OrderBy(value => value.AssessmentId, StringComparer.Ordinal)
                .Where(value => _conditions.EvaluateCondition(
                    world,
                    value.Condition))
                .Select(value => new CourseAssessmentEvidence(
                    value.AssessmentId,
                    value.RiskId,
                    value.ScoreDelta,
                    value.Prompt))
                .ToArray();
            var score = Math.Max(
                0,
                Math.Min(
                    maximumScore,
                    maximumScore + evidence.Sum(value => value.ScoreDelta)));
            return new CourseAssessmentEvaluationResult(
                score,
                evidence.Select(value => value.RiskId).Distinct(),
                evidence);
        }

        public CourseAssessmentEvaluationResult EvaluateAction(
            SemanticActionRequest request,
            CommandResult actionResult,
            IEnumerable<CourseActionAssessmentDefinition> assessments,
            int maximumScore,
            CourseAssessmentEvaluationResult previous = null)
        {
            return EvaluateAction(
                null,
                request,
                actionResult,
                assessments,
                maximumScore,
                previous);
        }

        public CourseAssessmentEvaluationResult EvaluateAction(
            ExperimentWorld world,
            SemanticActionRequest request,
            CommandResult actionResult,
            IEnumerable<CourseActionAssessmentDefinition> assessments,
            int maximumScore,
            CourseAssessmentEvaluationResult previous = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (actionResult == null)
            {
                throw new ArgumentNullException(nameof(actionResult));
            }

            if (assessments == null)
            {
                throw new ArgumentNullException(nameof(assessments));
            }

            if (maximumScore < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumScore));
            }

            var evidence = previous?.Evidence.ToList()
                ?? new List<CourseAssessmentEvidence>();
            var scoreDelta = 0;
            foreach (var assessment in assessments
                         .OrderBy(
                             value => value.AssessmentId,
                             StringComparer.Ordinal))
            {
                var triggerMatches = assessment.TriggerKind switch
                {
                    CourseAssessmentTriggerKind.ActionRejected =>
                        !actionResult.IsAccepted
                        && string.Equals(
                            assessment.ActionId,
                            request.ActionId,
                            StringComparison.Ordinal)
                        && actionResult.RejectionCodes.Contains(
                            assessment.RejectionCode,
                            StringComparer.Ordinal),
                    CourseAssessmentTriggerKind.DomainEvent =>
                        actionResult.IsAccepted
                        && actionResult.Events.Any(value => string.Equals(
                            value.Event.EventType,
                            assessment.TriggerValue,
                            StringComparison.Ordinal)),
                    _ => false
                };
                if (!triggerMatches
                    || assessment.Conditions.Count > 0
                    && (world == null
                        || !assessment.Conditions.All(condition =>
                            _conditions.EvaluateCondition(world, condition))))
                {
                    continue;
                }

                evidence.Add(new CourseAssessmentEvidence(
                    assessment.AssessmentId,
                        assessment.RiskId,
                        assessment.ScoreDelta,
                        assessment.Prompt,
                        request.CommandId,
                        assessment.Severity,
                        assessment.Continuation,
                        assessment.AffectedTargetIds));
                scoreDelta += assessment.ScoreDelta;
            }

            var baseScore = previous?.Score ?? maximumScore;
            var score = Math.Max(
                0,
                Math.Min(maximumScore, baseScore + scoreDelta));
            return new CourseAssessmentEvaluationResult(
                score,
                evidence.Select(value => value.RiskId).Distinct(),
                evidence);
        }
    }

    /// <summary>
    /// 从目标完成度和已发生后果推导课程结局。失败不会冻结会话，操作者仍可继续
    /// 观察错误结果或执行清理操作；是否允许某个动作仍由事实规则单独裁决。
    /// </summary>
    public sealed class CourseOutcomeEvaluationResult :
        IEquatable<CourseOutcomeEvaluationResult>
    {
        public CourseOutcomeEvaluationResult(
            CourseRunStatus runStatus,
            CourseResultQuality quality,
            IEnumerable<string> affectedTargetIds,
            IEnumerable<string> consequenceIds)
        {
            RunStatus = runStatus;
            Quality = quality;
            AffectedTargetIds = CourseContractGuard.CopyStrings(
                affectedTargetIds,
                "课程结局受影响目标");
            ConsequenceIds = CourseContractGuard.CopyStrings(
                consequenceIds,
                "课程结局后果");
        }

        public CourseRunStatus RunStatus { get; }
        public CourseResultQuality Quality { get; }
        public IReadOnlyList<string> AffectedTargetIds { get; }
        public IReadOnlyList<string> ConsequenceIds { get; }

        public bool Equals(CourseOutcomeEvaluationResult other) =>
            other != null
            && RunStatus == other.RunStatus
            && Quality == other.Quality
            && AffectedTargetIds.SequenceEqual(
                other.AffectedTargetIds,
                StringComparer.Ordinal)
            && ConsequenceIds.SequenceEqual(
                other.ConsequenceIds,
                StringComparer.Ordinal);

        public override bool Equals(object obj) =>
            Equals(obj as CourseOutcomeEvaluationResult);

        public override int GetHashCode() =>
            ((int)RunStatus * 397) ^ (int)Quality;
    }

    public static class CourseOutcomeEvaluator
    {
        public static CourseOutcomeEvaluationResult Evaluate(
            IEnumerable<string> allGoalIds,
            CourseGoalEvaluationResult goals,
            CourseAssessmentEvaluationResult assessment)
        {
            if (goals == null)
            {
                throw new ArgumentNullException(nameof(goals));
            }

            if (assessment == null)
            {
                throw new ArgumentNullException(nameof(assessment));
            }

            var configuredGoals = CourseContractGuard.CopyStrings(
                allGoalIds ?? throw new ArgumentNullException(nameof(allGoalIds)),
                "课程结局全部目标");
            var fatal = assessment.Evidence.Where(value =>
                    value.Continuation == CourseContinuationMode.RestartRequired
                    || value.Continuation
                        == CourseContinuationMode.CannotContinue)
                .ToArray();
            var replacementRequired = assessment.Evidence.Any(value =>
                value.Continuation
                    == CourseContinuationMode.ContinueAfterReplacement);
            var degraded = assessment.Evidence.Any(value =>
                value.Severity
                    >= CourseConsequenceSeverity.PhenomenonDeviation);
            var affectedTargets = assessment.Evidence
                .SelectMany(value => value.AffectedTargetIds)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (fatal.Any(value => value.Continuation
                    == CourseContinuationMode.RestartRequired))
            {
                affectedTargets.AddRange(configuredGoals.Where(value =>
                    !goals.CompletedGoalIds.Contains(
                        value,
                        StringComparer.Ordinal)));
            }

            var runStatus = fatal.Length > 0
                ? CourseRunStatus.Failed
                : configuredGoals.Count > 0
                    && configuredGoals.All(value =>
                        goals.CompletedGoalIds.Contains(
                            value,
                            StringComparer.Ordinal))
                        ? CourseRunStatus.Completed
                        : CourseRunStatus.InProgress;
            var quality = replacementRequired
                ? CourseResultQuality.RecoveryRequired
                : degraded
                    ? CourseResultQuality.Degraded
                    : CourseResultQuality.Normal;
            return new CourseOutcomeEvaluationResult(
                runStatus,
                quality,
                affectedTargets.Distinct(StringComparer.Ordinal),
                assessment.Evidence
                    .Select(value => value.RiskId)
                    .Distinct(StringComparer.Ordinal));
        }
    }
}
