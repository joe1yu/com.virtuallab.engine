using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualLab.Application.Assessment;
using VirtualLab.Application.Events;
using VirtualLab.Application.Goals;
using VirtualLab.Application.Hints;
using VirtualLab.Engine.Tests.Fixtures;

namespace VirtualLab.Engine.Tests.Application
{
    public sealed class GoalAndAssessmentTests
    {
        [Test]
        public void 事件命名协议只接受自然中文格式()
        {
            Assert.That(EventTypeProtocol.IsStable("实验风险.装置漏气"),
                Is.True);
            Assert.That(EventTypeProtocol.IsStable("lab.risk.gas-leak"),
                Is.False);
        }

        [Test]
        public void Nonlinear_facts_are_recorded_then_re_evaluated_when_prerequisite_arrives()
        {
            var goals = GoalAssessmentFixtures.CreateGoalEngine();

            goals.Handle(GoalAssessmentFixtures.Event(1, "加热.已开始"));

            Assert.That(goals.IsSatisfied("start-heating"), Is.False);
            Assert.That(goals.Observations, Has.Count.EqualTo(1));

            goals.Handle(GoalAssessmentFixtures.Event(2, "集气瓶.已准备"));

            Assert.That(goals.IsSatisfied("prepare-collection-bottles"), Is.True);
            Assert.That(goals.IsSatisfied("start-heating"), Is.True);
        }

        [Test]
        public void Duplicate_event_sequence_does_not_create_duplicate_observations()
        {
            var goals = GoalAssessmentFixtures.CreateGoalEngine();
            var prepared = GoalAssessmentFixtures.Event(1, "集气瓶.已准备");

            goals.Handle(prepared);
            goals.Handle(prepared);

            Assert.That(goals.Observations, Has.Count.EqualTo(1));
        }

        [Test]
        public void Hints_are_stable_read_only_three_level_projections()
        {
            var goals = GoalAssessmentFixtures.CreateGoalEngine();
            var hints = new HintEngine(goals).GetHints();

            Assert.That(hints, Has.Count.EqualTo(3));
            Assert.That(hints[0].Level, Is.EqualTo(HintLevel.CurrentGoal));
            Assert.That(hints[0].GoalId, Is.EqualTo("prepare-collection-bottles"));
            Assert.That(hints[1].Level, Is.EqualTo(HintLevel.MissingConditionCategory));
            Assert.That(hints[2].Level, Is.EqualTo(HintLevel.SuggestedAction));
            Assert.That(() => ((IList<Hint>)hints).Add(hints[0]), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Replaying_the_same_hazard_event_is_idempotent()
        {
            var assessment = GoalAssessmentFixtures.CreateAssessment();
            var hazard = GoalAssessmentFixtures.Hazard(1, "back-suction");

            assessment.Handle(hazard);
            assessment.Handle(hazard);

            Assert.That(assessment.Score.Safety, Is.EqualTo(75));
            Assert.That(assessment.Changes, Has.Count.EqualTo(1));
            Assert.That(assessment.Changes[0].RuleId, Is.EqualTo("back-suction-safety"));
            Assert.That(assessment.Changes[0].Reason, Does.Contain("Back suction"));
        }

        [Test]
        public void Different_hazard_events_with_the_same_hazard_apply_a_rule_only_once()
        {
            var assessment = GoalAssessmentFixtures.CreateAssessment();

            assessment.Handle(GoalAssessmentFixtures.Hazard(1, "back-suction"));
            assessment.Handle(GoalAssessmentFixtures.Hazard(2, "back-suction"));

            Assert.That(assessment.Score.Safety, Is.EqualTo(75));
            Assert.That(assessment.Changes, Has.Count.EqualTo(1));
            Assert.That(assessment.Changes[0].RuleId, Is.EqualTo("back-suction-safety"));
            Assert.That(assessment.Changes[0].EventSequence, Is.EqualTo(1));
        }

        [Test]
        public void Non_hazard_events_are_ignored_even_when_their_type_looks_like_a_hazard()
        {
            var assessment = GoalAssessmentFixtures.CreateAssessment();

            assessment.Handle(GoalAssessmentFixtures.Event(1, "实验风险.倒吸"));

            Assert.That(assessment.Score.Safety, Is.EqualTo(100));
            Assert.That(assessment.Changes, Is.Empty);
        }

        [Test]
        public void Score_card_clamps_all_four_dimensions()
        {
            var score = new ScoreCard(-1, 101, 50, 999);

            Assert.That(score.ScientificResult, Is.EqualTo(0));
            Assert.That(score.OperationQuality, Is.EqualTo(100));
            Assert.That(score.Safety, Is.EqualTo(50));
            Assert.That(score.Efficiency, Is.EqualTo(100));
        }

        [Test]
        public void Definitions_validate_ids_and_defensively_copy_collections()
        {
            var prerequisites = new List<string> { "prepare-collection-bottles" };
            var definition = new GoalDefinition("start-heating", prerequisites, "加热.已开始", 1m);
            prerequisites.Clear();

            Assert.That(definition.PrerequisiteGoalIds, Is.EqualTo(new[] { "prepare-collection-bottles" }));
            Assert.That(() => new GoalDefinition(" ", new string[0], "加热.已开始", 1m), Throws.ArgumentException);
            Assert.That(() => new GoalDefinition("goal", new string[0], "heating.started", 1m), Throws.ArgumentException);
            Assert.That(() => new AssessmentRule(" ", "hazard", 0, 0, 0, 0, "reason"), Throws.ArgumentException);
        }

        [Test]
        public void Goal_engine_rejects_duplicate_missing_and_cyclic_prerequisites()
        {
            var eventType = "目标.已完成";

            Assert.That(
                () => new GoalEngine(new[]
                {
                    new GoalDefinition("goal", new string[0], eventType, 1m),
                    new GoalDefinition("goal", new string[0], eventType, 1m)
                }),
                Throws.ArgumentException);
            Assert.That(
                () => new GoalEngine(new[]
                {
                    new GoalDefinition("goal", new[] { "missing" }, eventType, 1m)
                }),
                Throws.ArgumentException);
            Assert.That(
                () => new GoalEngine(new[]
                {
                    new GoalDefinition("first", new[] { "second" }, eventType, 1m),
                    new GoalDefinition("second", new[] { "first" }, eventType, 1m)
                }),
                Throws.ArgumentException);
        }

        [Test]
        public void Engines_reject_duplicate_rules_and_defensively_copy_definition_lists()
        {
            var definitions = new List<GoalDefinition>
            {
                new GoalDefinition("goal", new string[0], "目标.已完成", 1m)
            };
            var goals = new GoalEngine(definitions);
            definitions.Clear();

            var rule = new AssessmentRule("rule", "hazard", 0, 0, -1, 0, "reason");
            var rules = new List<AssessmentRule> { rule };
            var assessment = new AssessmentEngine(rules);
            rules.Clear();

            Assert.That(goals.Definitions, Has.Count.EqualTo(1));
            Assert.That(assessment.Rules, Has.Count.EqualTo(1));
            Assert.That(
                () => new AssessmentEngine(new[] { rule, rule }),
                Throws.ArgumentException);
        }
    }
}
