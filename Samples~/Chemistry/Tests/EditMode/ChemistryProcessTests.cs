using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VirtualLab.Application.Events;
using VirtualLab.Chemistry.Commands;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Chemistry.Tests.Fixtures;
using VirtualLab.Domain;
using VirtualLab.Domain.Events;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Tests
{
    public sealed class ChemistryProcessTests
    {
        [Test]
        public void Pour_normalizes_before_validation_and_rejects_empty_units()
        {
            Assert.That(
                () => new PourCommand(
                    "cmd-1",
                    "session-1",
                    new EntityId("source"),
                    new EntityId("target"),
                    new Quantity(0.0000004m, ChemistryUnits.Gram),
                    new SimulationTick(0)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new PourCommand(
                    "cmd-2",
                    "session-1",
                    new EntityId("source"),
                    new EntityId("target"),
                    default,
                    new SimulationTick(0)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Heated_potassium_permanganate_generates_oxygen()
        {
            var fixture = ChemistryWorldFixture.PotassiumPermanganateInTube(2m, 260m);

            fixture.Scheduler.Advance(fixture.World, new SimulationTick(1));

            Assert.That(
                fixture.World.RequireMatterInventory().Total("oxygen"),
                Is.GreaterThan(Quantity.Zero(ChemistryUnits.Millilitre)));
            Assert.That(fixture.Events.Events.Any(e => e.EventType == "气体.已生成"), Is.True);
        }

        [Test]
        public void Transfer_rejects_a_negative_quantity()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);

            Assert.That(
                () => fixture.World.RequireMatterInventory().Transfer(
                    fixture.Source,
                    fixture.Target,
                    "water",
                    new Quantity(-1m, ChemistryUnits.Millilitre),
                    new SimulationTick(1),
                    fixture.Events),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Transfer_rejects_more_than_the_source_inventory()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);

            Assert.That(
                () => fixture.World.RequireMatterInventory().Transfer(
                    fixture.Source,
                    fixture.Target,
                    "water",
                    new Quantity(11m, ChemistryUnits.Millilitre),
                    new SimulationTick(1),
                    fixture.Events),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Transfer_preserves_total_quantity_and_emits_a_versioned_event()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);
            var before = fixture.World.RequireMatterInventory().Total("water");

            fixture.World.RequireMatterInventory().Transfer(
                fixture.Source,
                fixture.Target,
                "water",
                new Quantity(4m, ChemistryUnits.Millilitre),
                new SimulationTick(3),
                fixture.Events);

            Assert.That(fixture.World.RequireMatterInventory().Total("water"), Is.EqualTo(before));
            Assert.That(fixture.World.RequireMatterInventory().Total(fixture.Source, "water").Value, Is.EqualTo(6m));
            Assert.That(fixture.World.RequireMatterInventory().Total(fixture.Target, "water").Value, Is.EqualTo(4m));
            Assert.That(fixture.Events.Events.Single().EventType, Is.EqualTo("物质.已转移"));
            Assert.That(fixture.Events.Events.Single().Tick, Is.EqualTo(new SimulationTick(3)));
        }

        [Test]
        public void Mixture_transfer_and_stoichiometric_reaction_are_exact_conservative_and_repeat_safe()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);
            fixture.World.RequireMatterInventory().Add(
                fixture.Source,
                new SubstanceBatch(
                    "calcium-hydroxide",
                    new Quantity(0.74092m, ChemistryUnits.Gram),
                    MatterPhase.Solid,
                    new Temperature(20m)));
            fixture.World.RequireMatterInventory().Add(
                fixture.Target,
                new SubstanceBatch(
                    "carbon-dioxide",
                    new Quantity(44.0095m, ChemistryUnits.Gram),
                    MatterPhase.Gas,
                    new Temperature(20m)));

            new MixtureTransferExecutor().Transfer(
                fixture.World.RequireMatterInventory(),
                fixture.Source,
                fixture.Target,
                new[]
                {
                    new MixtureTransferComponent(
                        "water",
                        new Quantity(10m, ChemistryUnits.Millilitre)),
                    new MixtureTransferComponent(
                        "calcium-hydroxide",
                        new Quantity(0.74092m, ChemistryUnits.Gram))
                },
                "pour:mixture",
                new SimulationTick(1),
                fixture.Events);

            var transfer = (MixtureTransferredEvent)
                fixture.Events.Events.Single().Event;
            Assert.That(transfer.Components.Count, Is.EqualTo(2));
            Assert.That(
                transfer.Components.Select(value =>
                    value.SubstanceId + ":" + value.Quantity.Value + ":" +
                    value.Quantity.Unit),
                Is.EqualTo(new[]
                {
                    "water:10:毫升",
                    "calcium-hydroxide:0.74092:克"
                }));

            var reaction = new ChemistryReactionDefinition(
                "carbon-dioxide-limewater",
                new[]
                {
                    new ChemistryReactionTerm(
                        "carbon-dioxide", 44.0095m, ChemistryUnits.Gram,
                        MatterPhase.Gas, 1m),
                    new ChemistryReactionTerm(
                        "calcium-hydroxide", 74.092m, ChemistryUnits.Gram,
                        MatterPhase.Solid, 1m)
                },
                new[]
                {
                    new ChemistryReactionTerm(
                        "calcium-carbonate", 100.0865m, ChemistryUnits.Gram,
                        MatterPhase.Solid, 1m),
                    new ChemistryReactionTerm(
                        "water", 18.015m, ChemistryUnits.Millilitre,
                        MatterPhase.Liquid, 1m)
                },
                ChemistryReactionProcessKind.Mixing,
                0m,
                1m,
                false);
            var executor = new StoichiometricMixingReactionExecutor();

            var first = executor.Execute(
                fixture.World.RequireMatterInventory(),
                fixture.Target,
                reaction,
                1m,
                new Temperature(20m),
                "shake:first",
                new SimulationTick(2),
                fixture.Events);
            var second = executor.Execute(
                fixture.World.RequireMatterInventory(),
                fixture.Target,
                reaction,
                1m,
                new Temperature(20m),
                "shake:repeat",
                new SimulationTick(3),
                fixture.Events);

            Assert.That(first.DidReact, Is.True);
            Assert.That(first.ReactionUnits, Is.EqualTo(0.01m));
            Assert.That(second.DidReact, Is.False);
            Assert.That(
                fixture.World.RequireMatterInventory().Total(
                    fixture.Target,
                    "carbon-dioxide",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(43.569405m));
            Assert.That(
                fixture.World.RequireMatterInventory().Total(
                    fixture.Target,
                    "calcium-hydroxide",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(0m));
            Assert.That(
                fixture.World.RequireMatterInventory().Total(
                    fixture.Target,
                    "calcium-carbonate",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(1.000865m));
            Assert.That(
                fixture.World.RequireMatterInventory().Total(
                    fixture.Target,
                    "water",
                    ChemistryUnits.Millilitre).Value,
                Is.EqualTo(10.18015m));
            Assert.That(
                fixture.Events.Events.Count(value =>
                    value.Event is ReactionAdvancedEvent),
                Is.EqualTo(1));
        }

        [Test]
        public void Mixture_transfer_is_atomic_when_any_component_is_insufficient()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);
            fixture.World.RequireMatterInventory().Add(
                fixture.Source,
                new SubstanceBatch(
                    "calcium-hydroxide",
                    new Quantity(0.74092m, ChemistryUnits.Gram),
                    MatterPhase.Solid,
                    new Temperature(20m)));

            Assert.That(
                () => new MixtureTransferExecutor().Transfer(
                    fixture.World.RequireMatterInventory(),
                    fixture.Source,
                    fixture.Target,
                    new[]
                    {
                        new MixtureTransferComponent(
                            "water",
                            new Quantity(10m, ChemistryUnits.Millilitre)),
                        new MixtureTransferComponent(
                            "calcium-hydroxide",
                            new Quantity(1m, ChemistryUnits.Gram))
                    },
                    "pour:insufficient",
                    new SimulationTick(1),
                    fixture.Events),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                fixture.World.RequireMatterInventory().Total(
                    fixture.Source,
                    "water",
                    ChemistryUnits.Millilitre).Value,
                Is.EqualTo(10m));
            Assert.That(
                fixture.World.RequireMatterInventory().Total(
                    fixture.Source,
                    "calcium-hydroxide",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(0.74092m));
            Assert.That(fixture.Events.Events, Is.Empty);
        }

        [Test]
        public void Mixture_transfer_rejects_twenty_one_components_before_inventory_or_event_changes()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(0m);
            var components = Enumerable.Range(0, 21)
                .Select(index => new MixtureTransferComponent(
                    "component-" + index,
                    new Quantity(1m, ChemistryUnits.Gram)))
                .ToArray();
            foreach (var component in components)
            {
                fixture.World.RequireMatterInventory().Add(
                    fixture.Source,
                    new SubstanceBatch(
                        component.SubstanceId,
                        component.Quantity,
                        MatterPhase.Solid,
                    new Temperature(20m)));
            }

            Assert.That(
                () => new MixtureTransferredEvent(
                    fixture.Source,
                    fixture.Target,
                    components),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new MixtureTransferExecutor().Transfer(
                    fixture.World.RequireMatterInventory(),
                    fixture.Source,
                    fixture.Target,
                    components,
                    "mixture:too-many",
                    new SimulationTick(1),
                    fixture.Events),
                Throws.TypeOf<ArgumentException>());
            Assert.That(fixture.Events.Events, Is.Empty);
            foreach (var component in components)
            {
                Assert.That(
                    fixture.World.RequireMatterInventory().Total(
                        fixture.Source,
                        component.SubstanceId,
                        ChemistryUnits.Gram).Value,
                    Is.EqualTo(1m));
                Assert.That(
                    fixture.World.RequireMatterInventory().Total(
                        fixture.Target,
                        component.SubstanceId,
                        ChemistryUnits.Gram).Value,
                    Is.EqualTo(0m));
            }
        }

        [Test]
        public void Mixture_transfer_accepts_exactly_twenty_bounded_components()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(0m);
            var components = Enumerable.Range(
                    0,
                    MixtureTransferExecutor.MaxComponents)
                .Select(index => new MixtureTransferComponent(
                    "bounded-" + index,
                    new Quantity(1m, ChemistryUnits.Gram)))
                .ToArray();
            foreach (var component in components)
            {
                fixture.World.RequireMatterInventory().Add(
                    fixture.Source,
                    new SubstanceBatch(
                        component.SubstanceId,
                        component.Quantity,
                        MatterPhase.Solid,
                        new Temperature(20m)));
            }

            new MixtureTransferExecutor().Transfer(
                fixture.World.RequireMatterInventory(),
                fixture.Source,
                fixture.Target,
                components,
                "mixture:max-components",
                new SimulationTick(1),
                fixture.Events);

            Assert.That(fixture.Events.Events, Has.Count.EqualTo(1));
            Assert.That(
                ((MixtureTransferredEvent)
                    fixture.Events.Events[0].Event).Components,
                Has.Count.EqualTo(
                    MixtureTransferExecutor.MaxComponents));
            Assert.That(
                () => new MixtureTransferComponent(
                    new string(
                        's',
                        MixtureTransferExecutor
                            .MaxSubstanceIdCharacters + 1),
                    new Quantity(1m, ChemistryUnits.Gram)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new MixtureTransferredEvent(
                    new EntityId(new string(
                        'e',
                        MixtureTransferExecutor
                            .MaxEntityIdCharacters + 1)),
                    fixture.Target,
                    components),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Stoichiometric_mixing_only_consumes_reactants_at_or_above_configured_temperature()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(0m);
            fixture.World.RequireMatterInventory().Add(
                fixture.Target,
                new SubstanceBatch(
                    "source",
                    new Quantity(1m, ChemistryUnits.Gram),
                    MatterPhase.Solid,
                    new Temperature(19m)));
            var reaction = new ChemistryReactionDefinition(
                "temperature-gated-mixing",
                new[]
                {
                    new ChemistryReactionTerm(
                        "source", 1m, ChemistryUnits.Gram,
                        MatterPhase.Solid, 1m)
                },
                new[]
                {
                    new ChemistryReactionTerm(
                        "product", 1m, ChemistryUnits.Gram,
                        MatterPhase.Solid, 1m)
                },
                ChemistryReactionProcessKind.Mixing,
                20m,
                1m,
                false);
            var executor = new StoichiometricMixingReactionExecutor();

            var cold = executor.Execute(
                fixture.World.RequireMatterInventory(),
                fixture.Target,
                reaction,
                1m,
                new Temperature(20m),
                "mix:cold",
                new SimulationTick(1),
                fixture.Events);
            Assert.That(cold.DidReact, Is.False);
            Assert.That(fixture.Events.Events, Is.Empty);

            fixture.World.RequireMatterInventory().Add(
                fixture.Target,
                new SubstanceBatch(
                    "source",
                    new Quantity(1m, ChemistryUnits.Gram),
                    MatterPhase.Solid,
                    new Temperature(20m)));
            var warm = executor.Execute(
                fixture.World.RequireMatterInventory(),
                fixture.Target,
                reaction,
                1m,
                new Temperature(20m),
                "mix:warm",
                new SimulationTick(2),
                fixture.Events);

            Assert.That(warm.DidReact, Is.True);
            Assert.That(
                fixture.World.RequireMatterInventory().Total(
                    fixture.Target,
                    "source",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(1m));
            Assert.That(
                fixture.World.RequireMatterInventory().Total(
                    fixture.Target,
                    "product",
                    ChemistryUnits.Gram).Value,
                Is.EqualTo(1m));
        }

        [Test]
        public void Transfer_failure_does_not_register_the_requested_substance_unit()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);

            Assert.That(
                () => fixture.World.RequireMatterInventory().Transfer(
                    fixture.Source,
                    fixture.Target,
                    "oxygen",
                    new Quantity(1m, ChemistryUnits.Millilitre),
                    new SimulationTick(1),
                    fixture.Events),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => fixture.World.RequireMatterInventory().Total("oxygen"),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Transfer_collector_failure_leaves_inventory_unchanged()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);

            Assert.That(
                () => fixture.World.RequireMatterInventory().Transfer(
                    fixture.Source,
                    fixture.Target,
                    "water",
                    new Quantity(4m, ChemistryUnits.Millilitre),
                    new SimulationTick(1),
                    new ThrowingCollector()),
                Throws.TypeOf<RecordingException>());
            Assert.That(fixture.World.RequireMatterInventory().Total(fixture.Source, "water").Value, Is.EqualTo(10m));
            Assert.That(fixture.World.RequireMatterInventory().Total(fixture.Target, "water").Value, Is.EqualTo(0m));
        }

        [Test]
        public void Real_event_collector_does_not_append_when_state_commit_throws()
        {
            var collector = new EventCollector();

            Assert.That(
                () => collector.CommitAtomically(
                    "process:test",
                    new SimulationTick(1),
                    new TestDomainEvent("物质.已测试"),
                    () => throw new RecordingException()),
                Throws.TypeOf<RecordingException>());

            Assert.That(collector.Events, Is.Empty);
            Assert.That(
                collector.Append(
                    "next",
                    new SimulationTick(2),
                    new TestDomainEvent("物质.已测试")).Sequence,
                Is.EqualTo(1));
        }

        [TestCase("add")]
        [TestCase("transfer")]
        [TestCase("consume")]
        [TestCase("remove")]
        public void Matter_transaction_rejects_reentrant_mutation_without_losing_outer_transfer(
            string operation)
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);
            var collector = new ReentrantCollector(
                fixture.World,
                fixture.Source,
                fixture.Target,
                operation);

            fixture.World.RequireMatterInventory().Transfer(
                fixture.Source,
                fixture.Target,
                "water",
                new Quantity(4m, ChemistryUnits.Millilitre),
                new SimulationTick(1),
                collector);

            Assert.That(collector.Rejection, Is.TypeOf<InvalidOperationException>());
            Assert.That(fixture.World.ContainsEntity(fixture.Source), Is.True);
            Assert.That(fixture.World.ContainsEntity(fixture.Target), Is.True);
            Assert.That(fixture.World.RequireMatterInventory().Total(fixture.Source, "water").Value, Is.EqualTo(6m));
            Assert.That(fixture.World.RequireMatterInventory().Total(fixture.Target, "water").Value, Is.EqualTo(4m));
        }

        [Test]
        public void Matter_inventory_public_api_does_not_accept_executable_predicates()
        {
            var hasPublicFuncParameter = typeof(MatterInventory)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Any(type =>
                    type.IsGenericType
                    && type.GetGenericTypeDefinition() == typeof(Func<,>));

            Assert.That(hasPublicFuncParameter, Is.False);
        }

        [Test]
        public void Adding_matter_to_an_unknown_world_entity_is_rejected_without_state_change()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);

            Assert.That(
                () => fixture.World.RequireMatterInventory().Add(
                    new EntityId("missing"),
                    new SubstanceBatch(
                        "water",
                        new Quantity(3m, ChemistryUnits.Millilitre),
                        MatterPhase.Liquid,
                        new Temperature(20m))),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(fixture.World.RequireMatterInventory().Total("water").Value, Is.EqualTo(10m));
        }

        [Test]
        public void Transferring_to_an_unknown_world_entity_is_rejected_without_state_change()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);

            Assert.That(
                () => fixture.World.RequireMatterInventory().Transfer(
                    fixture.Source,
                    new EntityId("missing"),
                    "water",
                    new Quantity(4m, ChemistryUnits.Millilitre),
                    new SimulationTick(1),
                    fixture.Events),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(fixture.World.RequireMatterInventory().Total(fixture.Source, "water").Value, Is.EqualTo(10m));
            Assert.That(fixture.Events.Events, Is.Empty);
        }

        [Test]
        public void Removing_an_entity_removes_matter_at_that_location()
        {
            var fixture = ChemistryWorldFixture.InventoryWithWater(10m);

            fixture.World.RemoveEntity(fixture.Source);

            Assert.That(fixture.World.RequireMatterInventory().Total("water").Value, Is.EqualTo(0m));
        }

        [Test]
        public void Scheduler_invokes_handlers_in_registration_order_with_the_supplied_tick()
        {
            var calls = new List<string>();
            var events = new EventCollector();
            var scheduler = new ProcessScheduler(events);
            scheduler.Register(new RecordingProcess("first", calls));
            scheduler.Register(new RecordingProcess("second", calls));

            scheduler.Advance(new ExperimentWorld(), new SimulationTick(7));

            Assert.That(calls, Is.EqualTo(new[] { "first:7", "second:7" }));
        }

        [Test]
        public void Thermal_decomposition_does_not_run_below_its_temperature_threshold()
        {
            var fixture = ChemistryWorldFixture.PotassiumPermanganateInTube(2m, 239m);

            fixture.Scheduler.Advance(fixture.World, new SimulationTick(1));

            Assert.That(fixture.World.RequireMatterInventory().Total("potassium-permanganate").Value, Is.EqualTo(2m));
            Assert.That(fixture.World.RequireMatterInventory().Total("oxygen").Value, Is.EqualTo(0m));
        }

        [Test]
        public void Thermal_decomposition_is_limited_by_remaining_reactant()
        {
            var fixture = ChemistryWorldFixture.PotassiumPermanganateInTube(0.75m, 260m);

            fixture.Scheduler.Advance(fixture.World, new SimulationTick(1));
            fixture.Scheduler.Advance(fixture.World, new SimulationTick(2));
            fixture.Scheduler.Advance(fixture.World, new SimulationTick(3));

            Assert.That(fixture.World.RequireMatterInventory().Total("potassium-permanganate").Value, Is.EqualTo(0m));
            var oxygenAfterExhaustion = fixture.World.RequireMatterInventory().Total("oxygen");
            fixture.Scheduler.Advance(fixture.World, new SimulationTick(4));

            Assert.That(oxygenAfterExhaustion.Value, Is.GreaterThan(0m));
            Assert.That(fixture.World.RequireMatterInventory().Total("oxygen"), Is.EqualTo(oxygenAfterExhaustion));
        }

        [Test]
        public void Thermal_decomposition_consumes_only_hot_batches_regardless_of_insertion_order()
        {
            var hotFirst = ChemistryWorldFixture.PotassiumPermanganateWithMixedTemperatures(hotFirst: true);
            var coldFirst = ChemistryWorldFixture.PotassiumPermanganateWithMixedTemperatures(hotFirst: false);

            hotFirst.Scheduler.Advance(hotFirst.World, new SimulationTick(1));
            coldFirst.Scheduler.Advance(coldFirst.World, new SimulationTick(1));

            Assert.That(
                hotFirst.World.RequireMatterInventory().Total("potassium-permanganate"),
                Is.EqualTo(new Quantity(1.75m, ChemistryUnits.Gram)));
            Assert.That(
                coldFirst.World.RequireMatterInventory().Total("potassium-permanganate"),
                Is.EqualTo(new Quantity(1.75m, ChemistryUnits.Gram)));
            Assert.That(
                hotFirst.World.RequireMatterInventory().Total(
                    new EntityId("test-tube-1"),
                    MatterBatchSelection.AtOrAboveTemperature(
                        "potassium-permanganate",
                        new Temperature(240m)),
                    ChemistryUnits.Gram),
                Is.EqualTo(Quantity.Zero(ChemistryUnits.Gram)));
            Assert.That(
                coldFirst.World.RequireMatterInventory().Total("oxygen"),
                Is.EqualTo(hotFirst.World.RequireMatterInventory().Total("oxygen")));
        }

        [Test]
        public void Thermal_reaction_collector_failure_leaves_all_reactants_and_products_unchanged()
        {
            var fixture = ChemistryWorldFixture.PotassiumPermanganateWithCollector(new ThrowingCollector());

            Assert.That(
                () => fixture.Scheduler.Advance(fixture.World, new SimulationTick(1)),
                Throws.TypeOf<RecordingException>());
            Assert.That(
                fixture.World.RequireMatterInventory().Total("potassium-permanganate"),
                Is.EqualTo(new Quantity(2m, ChemistryUnits.Gram)));
            Assert.That(
                fixture.World.RequireMatterInventory().Total("oxygen", ChemistryUnits.Millilitre),
                Is.EqualTo(Quantity.Zero(ChemistryUnits.Millilitre)));
        }

        [Test]
        public void Reaction_definition_rejects_mass_that_is_not_conserved()
        {
            Assert.That(
                () => new ChemicalReactionDefinition(
                    "反应.未平衡",
                    new[]
                    {
                        new ReactionTerm(
                            "carbon",
                            new Quantity(1m, ChemistryUnits.Gram),
                            MatterPhase.Solid,
                            gramsPerDeclaredUnit: 1m)
                    },
                    new[]
                    {
                        new ReactionTerm(
                            "carbon-dioxide",
                            new Quantity(2m, ChemistryUnits.Millilitre),
                            MatterPhase.Gas,
                            gramsPerDeclaredUnit: 1m)
                    }),
                Throws.ArgumentException);
        }

        [Test]
        public void Reaction_term_rejects_non_positive_mass_equivalence()
        {
            Assert.That(
                () => new ReactionTerm(
                    "oxygen",
                    new Quantity(1m, ChemistryUnits.Millilitre),
                    MatterPhase.Gas,
                    gramsPerDeclaredUnit: 0m),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Reaction_definition_rejects_fake_gram_equivalence_used_to_disguise_mass_loss()
        {
            Assert.That(
                () => new ChemicalReactionDefinition(
                    "反应.伪平衡",
                    new[]
                    {
                        new ReactionTerm(
                            "input",
                            new Quantity(2m, ChemistryUnits.Gram),
                            MatterPhase.Solid,
                            gramsPerDeclaredUnit: 0.5m)
                    },
                    new[]
                    {
                        new ReactionTerm(
                            "output",
                            new Quantity(1m, ChemistryUnits.Gram),
                            MatterPhase.Solid,
                            gramsPerDeclaredUnit: 1m)
                    }),
                Throws.ArgumentException);
        }

        [Test]
        public void Reaction_definition_does_not_expose_configurable_mass_tolerance()
        {
            var constructorParameters = typeof(ChemicalReactionDefinition)
                .GetConstructors()
                .Single()
                .GetParameters();

            Assert.That(constructorParameters.Length, Is.EqualTo(3));
            Assert.That(
                constructorParameters.Any(parameter => parameter.Name == "massToleranceGrams"),
                Is.False);
        }

        [Test]
        public void Reaction_rate_rejects_non_positive_reaction_units_per_tick()
        {
            Assert.That(
                () => new ReactionRate(0m),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Combustion_requires_fuel()
        {
            var fixture = ChemistryWorldFixture.CarbonCombustion(hasFuel: false, hasOxygen: true);
            fixture.Process.Ignite();

            fixture.Scheduler.Advance(fixture.World, new SimulationTick(1));

            Assert.That(fixture.World.RequireMatterInventory().Total("carbon-dioxide").Value, Is.EqualTo(0m));
        }

        [Test]
        public void Combustion_requires_oxygen()
        {
            var fixture = ChemistryWorldFixture.CarbonCombustion(hasFuel: true, hasOxygen: false);
            fixture.Process.Ignite();

            fixture.Scheduler.Advance(fixture.World, new SimulationTick(1));

            Assert.That(fixture.World.RequireMatterInventory().Total("carbon").Value, Is.EqualTo(1m));
        }

        [Test]
        public void Combustion_requires_ignition()
        {
            var fixture = ChemistryWorldFixture.CarbonCombustion(hasFuel: true, hasOxygen: true);

            fixture.Scheduler.Advance(fixture.World, new SimulationTick(1));

            Assert.That(fixture.World.RequireMatterInventory().Total("carbon-dioxide").Value, Is.EqualTo(0m));
        }

        [Test]
        public void Combustion_consumption_is_limited_by_configured_rate()
        {
            var fixture = ChemistryWorldFixture.CarbonCombustion(hasFuel: true, hasOxygen: true);
            fixture.Process.Ignite();

            fixture.Scheduler.Advance(fixture.World, new SimulationTick(1));

            Assert.That(fixture.World.RequireMatterInventory().Total("carbon").Value, Is.EqualTo(0.75m));
            Assert.That(
                fixture.World.RequireMatterInventory().Total("oxygen").Value,
                Is.EqualTo(533.33333333333333333333334m).Within(0.00000000000000000000001m));
            Assert.That(
                fixture.World.RequireMatterInventory().Total("carbon-dioxide").Value,
                Is.EqualTo(466.66666666666666666666667m).Within(0.00000000000000000000001m));
        }

        private sealed class RecordingProcess : IProcessHandler
        {
            private readonly string _name;
            private readonly ICollection<string> _calls;

            public RecordingProcess(string name, ICollection<string> calls)
            {
                _name = name;
                _calls = calls;
            }

            public void Advance(ExperimentWorld world, SimulationTick tick, IProcessEventCollector events)
            {
                _calls.Add(_name + ":" + tick.Value);
            }
        }

        private sealed class ThrowingCollector : IProcessEventCollector
        {
            public void CommitAtomically(
                string commandId,
                SimulationTick tick,
                IDomainEvent domainEvent,
                Action commitState)
            {
                throw new RecordingException();
            }
        }

        private sealed class ReentrantCollector : IProcessEventCollector
        {
            private readonly ExperimentWorld _world;
            private readonly EntityId _source;
            private readonly EntityId _target;
            private readonly string _operation;

            public ReentrantCollector(
                ExperimentWorld world,
                EntityId source,
                EntityId target,
                string operation)
            {
                _world = world;
                _source = source;
                _target = target;
                _operation = operation;
            }

            public Exception Rejection { get; private set; }

            public void CommitAtomically(
                string commandId,
                SimulationTick tick,
                IDomainEvent domainEvent,
                Action commitState)
            {
                try
                {
                    AttemptMutation();
                }
                catch (Exception exception)
                {
                    Rejection = exception;
                }

                commitState();
            }

            private void AttemptMutation()
            {
                if (_operation == "add")
                {
                    _world.RequireMatterInventory().Add(
                        _target,
                        new SubstanceBatch(
                            "water",
                            new Quantity(1m, ChemistryUnits.Millilitre),
                            MatterPhase.Liquid,
                            new Temperature(20m)));
                    return;
                }

                if (_operation == "transfer")
                {
                    _world.RequireMatterInventory().Transfer(
                        _source,
                        _target,
                        "water",
                        new Quantity(1m, ChemistryUnits.Millilitre),
                        new SimulationTick(1),
                        this);
                    return;
                }

                if (_operation == "consume")
                {
                    _world.RequireMatterInventory().Consume(
                        _source,
                        MatterBatchSelection.All("water"),
                        new Quantity(1m, ChemistryUnits.Millilitre));
                    return;
                }

                _world.RemoveEntity(_target);
            }
        }

        private sealed class TestDomainEvent : IDomainEvent
        {
            public TestDomainEvent(string eventType)
            {
                EventType = eventType;
            }

            public string EventType { get; }
        }

        private sealed class RecordingException : Exception
        {
        }
    }
}
