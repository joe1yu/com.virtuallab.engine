using System;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Domain.Capabilities;

namespace VirtualLab.Chemistry.Capabilities
{
    public sealed class Heat来源对象能力 : IConfiguredCapability
    {
        public Heat来源对象能力(decimal output)
        {
            if (output < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(output),
                    "热源输出不能为负数。");
            }

            Output = output;
        }

        public decimal Output { get; }
        public string CapabilityId => ChemistryCapabilityIds.HeatSource;
        public decimal NumberValue => Output;
        public string TextValue => null;
    }

    public abstract class ChemistryMarkerCapability : IConfiguredCapability
    {
        protected ChemistryMarkerCapability(string capabilityId)
        {
            CapabilityId = capabilityId;
        }

        public string CapabilityId { get; }
        public decimal NumberValue => 0m;
        public string TextValue => null;
    }

    public sealed class HeatableCapability : ChemistryMarkerCapability
    {
        public HeatableCapability() : base(ChemistryCapabilityIds.Heatable) { }
    }

    public sealed class PourableCapability : ChemistryMarkerCapability
    {
        public PourableCapability() : base(ChemistryCapabilityIds.Pourable) { }
    }

    public sealed class IgnitableCapability : ChemistryMarkerCapability
    {
        public IgnitableCapability() : base(ChemistryCapabilityIds.Ignitable) { }
    }

    public sealed class CombustibleCapability : ChemistryMarkerCapability
    {
        public CombustibleCapability()
            : base(ChemistryCapabilityIds.Combustible) { }
    }

    public sealed class ShakeableCapability : ChemistryMarkerCapability
    {
        public ShakeableCapability() : base(ChemistryCapabilityIds.Shakeable) { }
    }
}
