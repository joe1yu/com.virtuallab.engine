using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Fixtures
{
    public static class ExperimentWorldFixture
    {
        public static ExperimentWorld WithEntity(string id)
        {
            var world = new ExperimentWorld();
            world.AddEntity(new ExperimentEntity(new EntityId(id)));
            return world;
        }

        public static ExperimentWorld WithGrabbableEntity(string id)
        {
            var world = new ExperimentWorld();
            var entity = new ExperimentEntity(new EntityId(id));
            entity.AddCapability(new GrabbableCapability());
            world.AddEntity(entity);
            return world;
        }
    }
}
