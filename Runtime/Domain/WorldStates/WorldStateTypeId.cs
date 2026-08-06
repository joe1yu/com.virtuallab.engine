using System;

namespace VirtualLab.Domain.WorldStates
{
    /// <summary>
    /// 模块拥有的稳定世界状态类型标识。标识直接进入运行时注册，不经过枚举或词典转换。
    /// </summary>
    public readonly struct WorldStateTypeId : IEquatable<WorldStateTypeId>
    {
        public WorldStateTypeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("世界状态类型标识不能为空。", nameof(value));
            }

            Value = value.Trim();
        }

        public string Value { get; }

        public bool Equals(WorldStateTypeId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) =>
            obj is WorldStateTypeId other && Equals(other);

        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(WorldStateTypeId left, WorldStateTypeId right) =>
            left.Equals(right);

        public static bool operator !=(WorldStateTypeId left, WorldStateTypeId right) =>
            !left.Equals(right);
    }
}
