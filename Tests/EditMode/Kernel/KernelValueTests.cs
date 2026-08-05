using NUnit.Framework;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Engine.Tests.Kernel
{
    public sealed class KernelValueTests
    {
        [Test]
        public void Entity_id_rejects_blank_value()
        {
            Assert.That(() => new EntityId(" "), Throws.ArgumentException);
        }

        [Test]
        public void Entity_ids_with_the_same_value_are_equal()
        {
            Assert.That(new EntityId("tube-1"), Is.EqualTo(new EntityId("tube-1")));
        }

        [Test]
        public void Quantity_rejects_different_units_when_adding()
        {
            var grams = new Quantity(2m, Unit.Gram);
            var millilitres = new Quantity(3m, Unit.Millilitre);

            Assert.That(() => grams.Add(millilitres), Throws.InvalidOperationException);
        }

        [Test]
        public void Quantity_adds_and_subtracts_values_with_the_same_unit()
        {
            var grams = new Quantity(5m, Unit.Gram);
            var additionalGrams = new Quantity(2m, Unit.Gram);

            Assert.That(grams.Add(additionalGrams), Is.EqualTo(new Quantity(7m, Unit.Gram)));
            Assert.That(grams.Subtract(additionalGrams), Is.EqualTo(new Quantity(3m, Unit.Gram)));
        }

        [Test]
        public void Simulation_tick_advances_by_one()
        {
            Assert.That(new SimulationTick(7).Next(), Is.EqualTo(new SimulationTick(8)));
        }

        [Test]
        public void Temperature_converts_celsius_to_kelvin()
        {
            var temperature = new Temperature(25m);

            Assert.That(temperature.Celsius, Is.EqualTo(25m));
            Assert.That(temperature.Kelvin, Is.EqualTo(298.15m));
        }
    }
}
