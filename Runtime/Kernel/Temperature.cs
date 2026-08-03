using System;

namespace VirtualLab.Kernel
{
    public readonly struct Temperature : IEquatable<Temperature>
    {
        private const decimal KelvinOffset = 273.15m;

        public Temperature(decimal celsius)
        {
            Celsius = celsius;
        }

        public decimal Celsius { get; }

        public decimal Kelvin => Celsius + KelvinOffset;

        public bool Equals(Temperature other)
        {
            return Celsius == other.Celsius;
        }

        public override bool Equals(object obj)
        {
            return obj is Temperature other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Celsius.GetHashCode();
        }

        public static bool operator ==(Temperature left, Temperature right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Temperature left, Temperature right)
        {
            return !left.Equals(right);
        }
    }
}
