using VirtualLab.Application.Events;
using VirtualLab.Domain;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Entities;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Tests.Fixtures
{
    public static class ChemistryWorldFixture
    {
        public static ChemistryFixture PotassiumPermanganateInTube(decimal grams, decimal temperatureCelsius)
        {
            return PotassiumPermanganate(
                new[]
                {
                    new SubstanceBatch(
                        "potassium-permanganate",
                        new Quantity(grams, ChemistryUnits.Gram),
                        MatterPhase.Solid,
                        new Temperature(temperatureCelsius))
                },
                new EventCollector());
        }

        public static ChemistryFixture PotassiumPermanganateWithMixedTemperatures(bool hotFirst)
        {
            var hot = new SubstanceBatch(
                "potassium-permanganate",
                new Quantity(0.25m, ChemistryUnits.Gram),
                MatterPhase.Solid,
                new Temperature(260m));
            var cold = new SubstanceBatch(
                "potassium-permanganate",
                new Quantity(1.75m, ChemistryUnits.Gram),
                MatterPhase.Solid,
                new Temperature(20m));
            return PotassiumPermanganate(
                hotFirst ? new[] { hot, cold } : new[] { cold, hot },
                new EventCollector());
        }

        public static ChemistryFixture PotassiumPermanganateWithCollector(
            IProcessEventCollector events)
        {
            return PotassiumPermanganate(
                new[]
                {
                    new SubstanceBatch(
                        "potassium-permanganate",
                        new Quantity(2m, ChemistryUnits.Gram),
                        MatterPhase.Solid,
                        new Temperature(260m))
                },
                events);
        }

        private static ChemistryFixture PotassiumPermanganate(
            SubstanceBatch[] batches,
            IProcessEventCollector events)
        {
            var tube = new EntityId("test-tube-1");
            var world = WorldWithLocations(tube);
            foreach (var batch in batches)
            {
                world.RequireMatterInventory().Add(tube, batch);
            }

            var reaction = new ChemicalReactionDefinition(
                "高锰酸钾.已分解",
                new[]
                {
                    new ReactionTerm(
                        "potassium-permanganate",
                        new Quantity(1m, ChemistryUnits.Gram),
                        MatterPhase.Solid,
                        gramsPerDeclaredUnit: 1m)
                },
                new[]
                {
                    new ReactionTerm(
                        "potassium-manganate",
                        new Quantity(197.132m / 316.068m, ChemistryUnits.Gram),
                        MatterPhase.Solid,
                        gramsPerDeclaredUnit: 1m),
                    new ReactionTerm(
                        "manganese-dioxide",
                        new Quantity(86.936m / 316.068m, ChemistryUnits.Gram),
                        MatterPhase.Solid,
                        gramsPerDeclaredUnit: 1m),
                    new ReactionTerm(
                        "oxygen",
                        new Quantity(22400m / 316.068m, ChemistryUnits.Millilitre),
                        MatterPhase.Gas,
                        gramsPerDeclaredUnit: 32m / 22400m)
                });
            var scheduler = new ProcessScheduler(events);
            scheduler.Register(
                new ThermalDecompositionProcess(
                    tube,
                    reaction,
                    new Temperature(240m),
                    new ReactionRate(0.5m)));
            return new ChemistryFixture(world, scheduler, events);
        }

        public static InventoryFixture InventoryWithWater(decimal millilitres)
        {
            var source = new EntityId("beaker-source");
            var target = new EntityId("beaker-target");
            var world = WorldWithLocations(source, target);
            world.RequireMatterInventory().Add(
                source,
                new SubstanceBatch(
                    "water",
                    new Quantity(millilitres, ChemistryUnits.Millilitre),
                    MatterPhase.Liquid,
                    new Temperature(20m)));
            return new InventoryFixture(world, source, target, new EventCollector());
        }

        public static CombustionFixture CarbonCombustion(bool hasFuel, bool hasOxygen)
        {
            var bottle = new EntityId("collection-bottle-1");
            var world = WorldWithLocations(bottle);
            if (hasFuel)
            {
                world.RequireMatterInventory().Add(
                    bottle,
                    new SubstanceBatch(
                        "carbon",
                        new Quantity(1m, ChemistryUnits.Gram),
                        MatterPhase.Solid,
                        new Temperature(600m)));
            }

            if (hasOxygen)
            {
                world.RequireMatterInventory().Add(
                    bottle,
                    new SubstanceBatch(
                        "oxygen",
                        new Quantity(1000m, ChemistryUnits.Millilitre),
                        MatterPhase.Gas,
                        new Temperature(20m)));
            }

            var reaction = new ChemicalReactionDefinition(
                "碳.已燃烧",
                new[]
                {
                    new ReactionTerm(
                        "carbon",
                        new Quantity(1m, ChemistryUnits.Gram),
                        MatterPhase.Solid,
                        gramsPerDeclaredUnit: 1m),
                    new ReactionTerm(
                        "oxygen",
                        new Quantity(22400m / 12m, ChemistryUnits.Millilitre),
                        MatterPhase.Gas,
                        gramsPerDeclaredUnit: 32m / 22400m)
                },
                new[]
                {
                    new ReactionTerm(
                        "carbon-dioxide",
                        new Quantity(22400m / 12m, ChemistryUnits.Millilitre),
                        MatterPhase.Gas,
                        gramsPerDeclaredUnit: 44m / 22400m)
                });
            var process = new CombustionProcess(
                bottle,
                reaction,
                "carbon",
                "oxygen",
                new ReactionRate(0.25m));
            var events = new EventCollector();
            var scheduler = new ProcessScheduler(events);
            scheduler.Register(process);
            return new CombustionFixture(world, scheduler, events, process);
        }

        private static ExperimentWorld WorldWithLocations(params EntityId[] locations)
        {
            var world = new ExperimentWorld();
            foreach (var location in locations)
            {
                world.AddEntity(new ExperimentEntity(location));
            }

            return world;
        }
    }

    public sealed class ChemistryFixture
    {
        public ChemistryFixture(
            ExperimentWorld world,
            ProcessScheduler scheduler,
            IProcessEventCollector events)
        {
            World = world;
            Scheduler = scheduler;
            Events = events as EventCollector;
        }

        public ExperimentWorld World { get; }
        public ProcessScheduler Scheduler { get; }
        public EventCollector Events { get; }
    }

    public sealed class InventoryFixture
    {
        public InventoryFixture(ExperimentWorld world, EntityId source, EntityId target, EventCollector events)
        {
            World = world;
            Source = source;
            Target = target;
            Events = events;
        }

        public ExperimentWorld World { get; }
        public EntityId Source { get; }
        public EntityId Target { get; }
        public EventCollector Events { get; }
    }

    public sealed class CombustionFixture
    {
        public CombustionFixture(
            ExperimentWorld world,
            ProcessScheduler scheduler,
            EventCollector events,
            CombustionProcess process)
        {
            World = world;
            Scheduler = scheduler;
            Events = events;
            Process = process;
        }

        public ExperimentWorld World { get; }
        public ProcessScheduler Scheduler { get; }
        public EventCollector Events { get; }
        public CombustionProcess Process { get; }
    }
}
