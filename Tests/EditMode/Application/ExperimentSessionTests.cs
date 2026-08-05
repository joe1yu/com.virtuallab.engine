using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VirtualLab.Application;
using VirtualLab.Application.Commands;
using VirtualLab.Application.Events;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Events;
using VirtualLab.Engine.Tests.Fixtures;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Application
{
    public sealed class ExperimentSessionTests
    {
        private const string SessionId = "session-1";

        [Test]
        public void Grab_requires_grabbable_capability()
        {
            var session = CreateSession(ExperimentWorldFixture.WithEntity("bench"));

            var result = session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("bench"), new SimulationTick(0)));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.RejectionCode, Is.EqualTo("capability.grabbable.required"));
            Assert.That(result.Events, Is.Empty);
        }

        [Test]
        public void Accepted_command_is_wrapped_in_stable_event_envelope()
        {
            var session = CreateSession(ExperimentWorldFixture.WithGrabbableEntity("tube-1"));

            var result = session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("tube-1"), new SimulationTick(0)));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(result.Events.Single().Sequence, Is.EqualTo(1));
            Assert.That(result.Events.Single().EventType, Is.EqualTo("实体.已抓取"));
            Assert.That(result.Events.Single().CommandId, Is.EqualTo("cmd-1"));
            Assert.That(result.Events.Single().Tick, Is.EqualTo(new SimulationTick(0)));
        }

        [Test]
        public void Accepted_commands_receive_monotonically_increasing_event_sequences()
        {
            var session = CreateSession(ExperimentWorldFixture.WithGrabbableEntity("tube-1"));

            var first = session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("tube-1"), new SimulationTick(0)));
            var second = session.Execute(new ReleaseCommand("cmd-2", SessionId, new EntityId("tube-1"), new SimulationTick(0)));

            Assert.That(first.Events.Single().Sequence, Is.EqualTo(1));
            Assert.That(second.Events.Single().Sequence, Is.EqualTo(2));
        }

        [Test]
        public void Injected_collector_provides_one_monotonic_sequence_for_commands_and_domain_processes()
        {
            var collector = new EventCollector();
            var session = new ExperimentSession(
                SessionId,
                ExperimentWorldFixture.WithGrabbableEntity("tube-1"),
                collector);

            var command = session.Execute(
                new GrabCommand(
                    "cmd-1",
                    SessionId,
                    new EntityId("tube-1"),
                    new SimulationTick(0)));
            var domain = collector.Append(
                "process-1",
                new SimulationTick(1),
                new MutableEvent("过程.已推进"));

            Assert.That(command.Events.Single().Sequence, Is.EqualTo(1));
            Assert.That(domain.Sequence, Is.EqualTo(2));
            Assert.That(session.Events, Is.SameAs(collector.Events));
        }

        [Test]
        public void Event_collector_appends_one_storage_operation_per_event_at_scale()
        {
            var collector = new EventCollector();
            var operationCounter = typeof(EventCollector).GetProperty(
                "AppendStorageOperationCount",
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.Public);

            Assert.That(
                operationCounter,
                Is.Not.Null,
                "The collector must expose a deterministic append-operation metric.");
            var stableView = collector.Events;
            const int eventCount = 100000;
            for (var index = 0; index < eventCount; index++)
            {
                collector.Append(
                    "process-" + index,
                    new SimulationTick(index),
                    new MutableEvent("过程.已推进"));
            }

            Assert.That(collector.Events, Is.SameAs(stableView));
            Assert.That(collector.Events.Count, Is.EqualTo(eventCount));
            Assert.That(
                (long)operationCounter.GetValue(collector),
                Is.EqualTo(eventCount));
        }

        [Test]
        public void Event_collector_rolls_back_only_the_failed_append_increment()
        {
            var collector = new EventCollector();
            collector.Append(
                "before",
                new SimulationTick(1),
                new MutableEvent("过程.已推进"));

            Assert.That(
                () => collector.CommitAtomically(
                    "failed",
                    new SimulationTick(2),
                    new MutableEvent("过程.已失败"),
                    () => throw new InvalidOperationException("failed state")),
                Throws.InvalidOperationException);
            var afterFailure = collector.Append(
                "after",
                new SimulationTick(3),
                new MutableEvent("过程.已推进"));

            Assert.That(collector.Events.Count, Is.EqualTo(2));
            Assert.That(afterFailure.Sequence, Is.EqualTo(2));
            Assert.That(
                collector.Events.Select(value => value.CommandId),
                Is.EqualTo(new[] { "before", "after" }));
        }

        [Test]
        public void Command_tick_advances_current_tick_and_is_preserved_in_event()
        {
            var session = CreateSession(ExperimentWorldFixture.WithGrabbableEntity("tube-1"));

            var result = session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("tube-1"), new SimulationTick(4)));

            Assert.That(result.IsAccepted, Is.True);
            Assert.That(session.CurrentTick, Is.EqualTo(new SimulationTick(4)));
            Assert.That(result.Events.Single().Tick, Is.EqualTo(new SimulationTick(4)));
        }

        [Test]
        public void Commands_from_an_earlier_tick_are_rejected_without_events()
        {
            var session = CreateSession(ExperimentWorldFixture.WithGrabbableEntity("tube-1"));
            session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("tube-1"), new SimulationTick(2)));

            var result = session.Execute(new ReleaseCommand("cmd-2", SessionId, new EntityId("tube-1"), new SimulationTick(1)));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.RejectionCode, Is.EqualTo("tick.out_of_order"));
            Assert.That(result.Events, Is.Empty);
            Assert.That(session.CurrentTick, Is.EqualTo(new SimulationTick(2)));
        }

        [Test]
        public void Unknown_entity_is_rejected_without_events_or_sequence_consumption()
        {
            var session = CreateSession(ExperimentWorldFixture.WithGrabbableEntity("tube-1"));

            var rejected = session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("missing"), new SimulationTick(0)));
            var accepted = session.Execute(new GrabCommand("cmd-2", SessionId, new EntityId("tube-1"), new SimulationTick(0)));

            Assert.That(rejected.IsAccepted, Is.False);
            Assert.That(rejected.RejectionCode, Is.EqualTo("entity.target.not_found"));
            Assert.That(rejected.Events, Is.Empty);
            Assert.That(accepted.Events.Single().Sequence, Is.EqualTo(1));
        }

        [Test]
        public void Future_tick_unknown_entity_rejection_does_not_advance_tick_or_consume_command_id()
        {
            var world = ExperimentWorldFixture.WithGrabbableEntity("tube-1");
            var session = CreateSession(world);

            var rejected = session.Execute(
                new GrabCommand(
                    "retry-unknown",
                    SessionId,
                    new EntityId("missing"),
                    new SimulationTick(10)));
            var earlierAccepted = session.Execute(
                new GrabCommand(
                    "earlier-valid",
                    SessionId,
                    new EntityId("tube-1"),
                    new SimulationTick(1)));
            var added = new ExperimentEntity(new EntityId("missing"));
            added.AddCapability(new GrabbableCapability());
            world.AddEntity(added);
            var retried = session.Execute(
                new GrabCommand(
                    "retry-unknown",
                    SessionId,
                    new EntityId("missing"),
                    new SimulationTick(2)));

            Assert.That(rejected.RejectionCode, Is.EqualTo("entity.target.not_found"));
            Assert.That(earlierAccepted.IsAccepted, Is.True);
            Assert.That(retried.IsAccepted, Is.True);
            Assert.That(retried.Events.Single().Sequence, Is.EqualTo(2));
            Assert.That(session.CurrentTick, Is.EqualTo(new SimulationTick(2)));
        }

        [Test]
        public void Future_tick_capability_rejection_does_not_advance_tick_or_consume_command_id()
        {
            var world = ExperimentWorldFixture.WithEntity("tube-1");
            Assert.That(world.TryGetEntity(new EntityId("tube-1"), out var tube), Is.True);
            var session = CreateSession(world);

            var rejected = session.Execute(
                new GrabCommand(
                    "retry-capability",
                    SessionId,
                    new EntityId("tube-1"),
                    new SimulationTick(10)));
            tube.AddCapability(new GrabbableCapability());
            var retried = session.Execute(
                new GrabCommand(
                    "retry-capability",
                    SessionId,
                    new EntityId("tube-1"),
                    new SimulationTick(1)));

            Assert.That(rejected.RejectionCode, Is.EqualTo("capability.grabbable.required"));
            Assert.That(retried.IsAccepted, Is.True);
            Assert.That(retried.Events.Single().Sequence, Is.EqualTo(1));
            Assert.That(session.CurrentTick, Is.EqualTo(new SimulationTick(1)));
        }

        [Test]
        public void Duplicate_command_is_rejected_before_tick_ordering_and_does_not_advance_tick()
        {
            var session = CreateSession(ExperimentWorldFixture.WithGrabbableEntity("tube-1"));
            session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("tube-1"), new SimulationTick(2)));

            var earlyReplay = session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("tube-1"), new SimulationTick(1)));
            var lateReplay = session.Execute(new GrabCommand("cmd-1", SessionId, new EntityId("tube-1"), new SimulationTick(4)));
            var next = session.Execute(new ReleaseCommand("cmd-2", SessionId, new EntityId("tube-1"), new SimulationTick(2)));

            Assert.That(earlyReplay.RejectionCode, Is.EqualTo("command.duplicate"));
            Assert.That(lateReplay.RejectionCode, Is.EqualTo("command.duplicate"));
            Assert.That(earlyReplay.Events, Is.Empty);
            Assert.That(lateReplay.Events, Is.Empty);
            Assert.That(session.CurrentTick, Is.EqualTo(new SimulationTick(2)));
            Assert.That(next.Events.Single().Sequence, Is.EqualTo(2));
        }

        [Test]
        public void Session_rejects_commands_from_a_different_session_without_events()
        {
            var session = CreateSession(ExperimentWorldFixture.WithGrabbableEntity("tube-1"));

            var result = session.Execute(new GrabCommand("cmd-1", "other-session", new EntityId("tube-1"), new SimulationTick(0)));

            Assert.That(result.IsAccepted, Is.False);
            Assert.That(result.RejectionCode, Is.EqualTo("session.mismatch"));
            Assert.That(result.Events, Is.Empty);
            Assert.That(session.CurrentTick, Is.EqualTo(new SimulationTick(0)));
        }

        [Test]
        public void All_core_commands_accept_when_their_required_capabilities_are_present()
        {
            foreach (var scenario in AcceptedCommandScenarios())
            {
                var result = CreateSession(scenario.World).Execute(scenario.Command);

                Assert.That(result.IsAccepted, Is.True, scenario.Name);
                Assert.That(result.Events.Single().EventType, Is.EqualTo(scenario.EventType), scenario.Name);
                Assert.That(scenario.Command.SessionId, Is.EqualTo(SessionId), scenario.Name);
            }
        }

        [Test]
        public void All_core_commands_reject_when_their_required_capability_is_missing_without_consuming_sequences()
        {
            foreach (var scenario in RejectedCommandScenarios())
            {
                var session = CreateSession(scenario.World);
                var rejected = session.Execute(scenario.Command);
                var accepted = session.Execute(new GrabCommand("next-" + scenario.Name, SessionId, new EntityId("next"), new SimulationTick(0)));

                Assert.That(rejected.IsAccepted, Is.False, scenario.Name);
                Assert.That(rejected.RejectionCode, Is.EqualTo(scenario.RejectionCode), scenario.Name);
                Assert.That(rejected.Events, Is.Empty, scenario.Name);
                Assert.That(accepted.Events.Single().Sequence, Is.EqualTo(1), scenario.Name);
            }
        }

        [Test]
        public void Command_and_session_ids_are_required_and_trimmed()
        {
            Assert.That(() => new ExperimentSession(" ", ExperimentWorldFixture.WithEntity("bench")), Throws.ArgumentException);
            Assert.That(() => new GrabCommand("cmd", " ", new EntityId("bench"), new SimulationTick(0)), Throws.ArgumentException);

            var session = new ExperimentSession(" session-1 ", ExperimentWorldFixture.WithGrabbableEntity("tube-1"));
            var command = new GrabCommand(" cmd-1 ", " session-1 ", new EntityId("tube-1"), new SimulationTick(0));

            Assert.That(session.SessionId, Is.EqualTo(SessionId));
            Assert.That(command.CommandId, Is.EqualTo("cmd-1"));
            Assert.That(command.SessionId, Is.EqualTo(SessionId));
        }

        [Test]
        public void Event_envelope_snapshots_a_versioned_type_and_validates_tick()
        {
            var mutableEvent = new MutableEvent("实体.首次变更");
            var envelope = new DomainEventEnvelope(1, "cmd-1", new SimulationTick(0), mutableEvent);
            mutableEvent.EventType = "实体.再次变更";

            Assert.That(envelope.EventType, Is.EqualTo("实体.首次变更"));
            Assert.That(() => new DomainEventEnvelope(1, "cmd-1", new SimulationTick(-1), new MutableEvent("实体.首次变更")), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new DomainEventEnvelope(1, "cmd-1", new SimulationTick(0), new MutableEvent("entity.changed")), Throws.ArgumentException);
        }

        [Test]
        public void Command_result_rejects_null_events_and_normalizes_rejection_codes()
        {
            Assert.That(
                () => CommandResult.AcceptedWithoutExecution(
                    new DomainEventEnvelope[] { null }),
                Throws.ArgumentException);
            Assert.That(CommandResult.Rejected(" capability.required ").RejectionCode, Is.EqualTo("capability.required"));
        }

        private static ExperimentSession CreateSession(ExperimentWorld world)
        {
            return new ExperimentSession(SessionId, world);
        }

        private static IEnumerable<CommandScenario> AcceptedCommandScenarios()
        {
            yield return Scenario("grab", World(Entity("target", new GrabbableCapability())), new GrabCommand("grab", SessionId, new EntityId("target"), new SimulationTick(0)), "实体.已抓取");
            yield return Scenario("release", World(Entity("target", new GrabbableCapability())), new ReleaseCommand("release", SessionId, new EntityId("target"), new SimulationTick(0)), "实体.已释放");
            yield return Scenario("attach", World(Entity("source", new ConnectorCapability()), Entity("target", new ConnectorCapability())), new AttachCommand("attach", SessionId, new EntityId("source"), new EntityId("target"), new SimulationTick(0)), "连接.已建立");
            yield return Scenario("detach", World(Entity("source", new ConnectorCapability()), Entity("target", new ConnectorCapability())), new DetachCommand("detach", SessionId, new EntityId("source"), new EntityId("target"), new SimulationTick(0)), "连接.已断开");
            yield return Scenario("place", World(Entity("source"), Entity("target", new ContainerCapability(1m))), new PlaceIntoCommand("place", SessionId, new EntityId("source"), new EntityId("target"), new SimulationTick(0)), "容纳关系.已变更");
            yield return Scenario("cover", World(Entity("source"), Entity("target", new CoverableCapability())), new CoverCommand("cover", SessionId, new EntityId("source"), new EntityId("target"), new SimulationTick(0)), "密封.已确认");
            yield return Scenario("observe", World(Entity("target", new ObservableCapability())), new ObserveCommand("observe", SessionId, new EntityId("target"), new SimulationTick(0)), "观察.可用");
        }

        private static IEnumerable<RejectedCommandScenario> RejectedCommandScenarios()
        {
            yield return Rejected("grab", World(Entity("target"), Entity("next", new GrabbableCapability())), new GrabCommand("grab", SessionId, new EntityId("target"), new SimulationTick(0)), "capability.grabbable.required");
            yield return Rejected("release", World(Entity("target"), Entity("next", new GrabbableCapability())), new ReleaseCommand("release", SessionId, new EntityId("target"), new SimulationTick(0)), "capability.grabbable.required");
            yield return Rejected("attach", World(Entity("source", new ConnectorCapability()), Entity("target"), Entity("next", new GrabbableCapability())), new AttachCommand("attach", SessionId, new EntityId("source"), new EntityId("target"), new SimulationTick(0)), "capability.connector.required");
            yield return Rejected("detach", World(Entity("source", new ConnectorCapability()), Entity("target"), Entity("next", new GrabbableCapability())), new DetachCommand("detach", SessionId, new EntityId("source"), new EntityId("target"), new SimulationTick(0)), "capability.connector.required");
            yield return Rejected("place", World(Entity("source"), Entity("target"), Entity("next", new GrabbableCapability())), new PlaceIntoCommand("place", SessionId, new EntityId("source"), new EntityId("target"), new SimulationTick(0)), "capability.container.required");
            yield return Rejected("cover", World(Entity("source"), Entity("target"), Entity("next", new GrabbableCapability())), new CoverCommand("cover", SessionId, new EntityId("source"), new EntityId("target"), new SimulationTick(0)), "capability.coverable.required");
            yield return Rejected("observe", World(Entity("target"), Entity("next", new GrabbableCapability())), new ObserveCommand("observe", SessionId, new EntityId("target"), new SimulationTick(0)), "capability.observable.required");
        }

        private static CommandScenario Scenario(string name, ExperimentWorld world, IExperimentCommand command, string eventType)
        {
            return new CommandScenario(name, world, command, eventType);
        }

        private static RejectedCommandScenario Rejected(string name, ExperimentWorld world, IExperimentCommand command, string rejectionCode)
        {
            return new RejectedCommandScenario(name, world, command, rejectionCode);
        }

        private static ExperimentWorld World(params TestEntity[] entities)
        {
            var world = new ExperimentWorld();
            foreach (var definition in entities)
            {
                var entity = new ExperimentEntity(new EntityId(definition.Id));
                foreach (var capability in definition.Capabilities)
                {
                    entity.AddCapability(capability);
                }

                world.AddEntity(entity);
            }

            return world;
        }

        private static TestEntity Entity(string id, params ICapability[] capabilities)
        {
            return new TestEntity(id, capabilities);
        }

        private sealed class CommandScenario
        {
            public CommandScenario(string name, ExperimentWorld world, IExperimentCommand command, string eventType)
            {
                Name = name;
                World = world;
                Command = command;
                EventType = eventType;
            }

            public string Name { get; }
            public ExperimentWorld World { get; }
            public IExperimentCommand Command { get; }
            public string EventType { get; }
        }

        private sealed class RejectedCommandScenario
        {
            public RejectedCommandScenario(string name, ExperimentWorld world, IExperimentCommand command, string rejectionCode)
            {
                Name = name;
                World = world;
                Command = command;
                RejectionCode = rejectionCode;
            }

            public string Name { get; }
            public ExperimentWorld World { get; }
            public IExperimentCommand Command { get; }
            public string RejectionCode { get; }
        }

        private sealed class TestEntity
        {
            public TestEntity(string id, IReadOnlyList<ICapability> capabilities)
            {
                Id = id;
                Capabilities = capabilities;
            }

            public string Id { get; }
            public IReadOnlyList<ICapability> Capabilities { get; }
        }

        private sealed class MutableEvent : IDomainEvent
        {
            public MutableEvent(string eventType)
            {
                EventType = eventType;
            }

            public string EventType { get; set; }
        }
    }
}
