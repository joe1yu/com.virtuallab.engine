using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Kernel;

namespace VirtualLab.Domain.Entities
{
    public sealed class ExperimentEntity
    {
        private readonly List<ICapability> _capabilities =
            new List<ICapability>();

        public ExperimentEntity(EntityId id)
        {
            Id = id;
        }

        public EntityId Id { get; }

        public IReadOnlyCollection<ICapability> Capabilities
        {
            get
            {
                return new ReadOnlyCollection<ICapability>(
                    new List<ICapability>(_capabilities));
            }
        }

        public void AddCapability(ICapability capability)
        {
            if (capability == null)
            {
                throw new ArgumentNullException(nameof(capability));
            }

            if (string.IsNullOrWhiteSpace(capability.CapabilityId))
            {
                throw new ArgumentException(
                    "能力协议 ID 不能为空。",
                    nameof(capability));
            }

            var duplicate = _capabilities.Any(value => string.Equals(
                value.CapabilityId,
                capability.CapabilityId,
                StringComparison.Ordinal));
            if (duplicate)
            {
                throw new InvalidOperationException(
                    "同一实体不能重复注册相同能力协议。");
            }

            _capabilities.Add(capability);
        }

        /// <summary>
        /// 学科运行时将课程声明的占位能力提升为有行为的具体能力。
        /// 只有稳定协议 ID 相同的占位项会被替换。
        /// </summary>
        public void PromoteCapability(IConfiguredCapability capability)
        {
            if (capability == null)
            {
                throw new ArgumentNullException(nameof(capability));
            }

            var placeholder = _capabilities
                .OfType<ConfiguredCapability>()
                .FirstOrDefault(value => string.Equals(
                    value.CapabilityId,
                    capability.CapabilityId,
                    StringComparison.Ordinal));
            if (placeholder != null)
            {
                _capabilities.Remove(placeholder);
            }

            AddCapability(capability);
        }

        public bool HasCapability<TCapability>()
            where TCapability : class, ICapability
        {
            return _capabilities.OfType<TCapability>().Any();
        }

        public TCapability GetCapability<TCapability>()
            where TCapability : class, ICapability
        {
            var capability = _capabilities.OfType<TCapability>().FirstOrDefault();
            if (capability == null)
            {
                throw new InvalidOperationException("The requested capability is not registered on this entity.");
            }

            return capability;
        }
    }
}
