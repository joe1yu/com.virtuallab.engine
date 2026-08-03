using System;

namespace VirtualLab.Kernel
{
    public enum Unit
    {
        Gram,
        Millilitre
    }

    public readonly struct Quantity : IEquatable<Quantity>, IComparable<Quantity>
    {
        public Quantity(decimal value, Unit unit)
        {
            Value = value;
            Unit = unit;
        }

        public decimal Value { get; }

        public Unit Unit { get; }

        public static Quantity Zero(Unit unit)
        {
            return new Quantity(0m, unit);
        }

        public Quantity Add(Quantity other)
        {
            EnsureSameUnit(other);
            return new Quantity(Value + other.Value, Unit);
        }

        public Quantity Subtract(Quantity other)
        {
            EnsureSameUnit(other);
            return new Quantity(Value - other.Value, Unit);
        }

        public bool Equals(Quantity other)
        {
            return Value == other.Value && Unit == other.Unit;
        }

        public override bool Equals(object obj)
        {
            return obj is Quantity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Value.GetHashCode() * 397) ^ (int)Unit;
            }
        }

        public int CompareTo(Quantity other)
        {
            EnsureSameUnit(other);
            return Value.CompareTo(other.Value);
        }

        public static bool operator ==(Quantity left, Quantity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Quantity left, Quantity right)
        {
            return !left.Equals(right);
        }

        public static bool operator <(Quantity left, Quantity right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator >(Quantity left, Quantity right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator <=(Quantity left, Quantity right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >=(Quantity left, Quantity right)
        {
            return left.CompareTo(right) >= 0;
        }

        private void EnsureSameUnit(Quantity other)
        {
            if (Unit != other.Unit)
            {
                throw new InvalidOperationException("Quantities must use the same unit.");
            }
        }
    }
}
