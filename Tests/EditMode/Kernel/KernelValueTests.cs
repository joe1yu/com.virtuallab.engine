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
            var grams = new Quantity(2m, new Unit("克"));
            var millilitres = new Quantity(3m, new Unit("毫升"));

            Assert.That(() => grams.Add(millilitres), Throws.InvalidOperationException);
        }

        [Test]
        public void Quantity_adds_and_subtracts_values_with_the_same_unit()
        {
            var gram = new Unit("克");
            var grams = new Quantity(5m, gram);
            var additionalGrams = new Quantity(2m, gram);

            Assert.That(grams.Add(additionalGrams), Is.EqualTo(new Quantity(7m, gram)));
            Assert.That(grams.Subtract(additionalGrams), Is.EqualTo(new Quantity(3m, gram)));
        }

        [Test]
        public void Quantity支持模块声明新的类型化单位()
        {
            var mole = new Unit("摩尔");

            Assert.That(
                new Quantity(1m, mole).Add(new Quantity(2m, mole)),
                Is.EqualTo(new Quantity(3m, mole)));
            Assert.That(mole.ToString(), Is.EqualTo("摩尔"));
        }

        [Test]
        public void 类型化单位拒绝空标识()
        {
            Assert.That(() => new Unit(" "), Throws.ArgumentException);
            Assert.That(
                () => new Quantity(1m, default),
                Throws.ArgumentException);
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
