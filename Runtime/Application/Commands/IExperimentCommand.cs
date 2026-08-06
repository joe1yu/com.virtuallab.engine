using VirtualLab.Kernel;

namespace VirtualLab.Application.Commands
{
    public interface IExperimentCommand
    {
        string CommandId { get; }

        string SessionId { get; }

        EntityId TargetEntityId { get; }

        SimulationTick Tick { get; }
    }
}
