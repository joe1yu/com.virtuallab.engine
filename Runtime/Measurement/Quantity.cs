using System;

namespace VirtualLab.Measurement
{
    /// <summary>
    /// 类型化计量单位标识。具体单位由业务模块声明，测量模块不维护固定单位表。
    /// </summary>
    public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
    {
        public Unit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("计量单位标识不能为空。", nameof(id));
            }

            Id = id.Trim();
        }

        public string Id { get; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Id);

        public bool Equals(Unit other) => string.Equals(
            Id,
            other.Id,
            StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is Unit other && Equals(other);

        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Id ?? string.Empty);

        public int CompareTo(Unit other) => string.Compare(
            Id,
            other.Id,
            StringComparison.Ordinal);

        public override string ToString() => Id ?? string.Empty;

        public static bool operator ==(Unit left, Unit right) =>
            left.Equals(right);

        public static bool operator !=(Unit left, Unit right) =>
            !left.Equals(right);
    }

    /// <summary>
    /// 带单位的十进制量值，运算时强制要求单位一致。
    /// </summary>
    public readonly struct Quantity :
        IEquatable<Quantity>,
        IComparable<Quantity>
    {
        public Quantity(decimal value, Unit unit)
        {
            if (unit.IsEmpty)
            {
                throw new ArgumentException(
                    "量值必须使用非空计量单位。",
                    nameof(unit));
            }

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
                return (Value.GetHashCode() * 397) ^ Unit.GetHashCode();
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
                throw new InvalidOperationException(
                    "不同单位的量值不能直接运算。");
            }
        }
    }
}
