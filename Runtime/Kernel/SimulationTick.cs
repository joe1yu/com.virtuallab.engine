using System;
using System.Globalization;

namespace VirtualLab.Kernel
{
    public readonly struct SimulationTick : IEquatable<SimulationTick>
    {
        public SimulationTick(long value)
        {
            Value = value;
        }

        public long Value { get; }

        public SimulationTick Next()
        {
            return new SimulationTick(Value + 1);
        }

        public bool Equals(SimulationTick other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is SimulationTick other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(SimulationTick left, SimulationTick right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SimulationTick left, SimulationTick right)
        {
            return !left.Equals(right);
        }
    }
}
