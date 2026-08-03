using VirtualLab.Application.Assessment;
using VirtualLab.Application.Events;
using VirtualLab.Application.Goals;
using VirtualLab.Domain.Events;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Fixtures
{
    public static class GoalAssessmentFixtures
    {
        public static GoalEngine CreateGoalEngine()
        {
            return new GoalEngine(new[]
            {
                new GoalDefinition(
                    "prepare-collection-bottles",
                    new string[0],
                    "集气瓶.已准备",
                    1m,
                    "collection equipment",
                    "Prepare the collection bottles."),
                new GoalDefinition(
                    "start-heating",
                    new[] { "prepare-collection-bottles" },
                    "加热.已开始",
                    2m,
                    "heat source",
                    "Start heating the reaction vessel.")
            });
        }

        public static AssessmentEngine CreateAssessment()
        {
            return new AssessmentEngine(new[]
            {
                new AssessmentRule(
                    "back-suction-safety",
                    "back-suction",
                    0,
                    0,
                    -25,
                    0,
                    "Back suction can draw liquid into hot apparatus.")
            });
        }

        public static DomainEventEnvelope Event(long sequence, string eventType)
        {
            return new DomainEventEnvelope(
                sequence,
                "command-" + sequence,
                new SimulationTick(sequence - 1),
                new TestEvent(eventType));
        }

        public static DomainEventEnvelope Hazard(long sequence, string hazardId)
        {
            return new DomainEventEnvelope(
                sequence,
                "hazard-command-" + sequence,
                new SimulationTick(sequence - 1),
                new TestHazardEvent(hazardId));
        }

        private sealed class TestEvent : IDomainEvent
        {
            public TestEvent(string eventType)
            {
                EventType = eventType;
            }

            public string EventType { get; }
        }

        private sealed class TestHazardEvent : IHazardEvent
        {
            public TestHazardEvent(string hazardId)
            {
                HazardId = hazardId;
                EventType = "实验风险.已发生";
            }

            public string HazardId { get; }

            public string EventType { get; }
        }
    }
}
