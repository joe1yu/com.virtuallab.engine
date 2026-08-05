using System;
using VirtualLab.Domain;

namespace VirtualLab.Chemistry.Matter
{
    /// <summary>
    /// 化学模块对实验世界的物质库存访问入口。
    /// 显式调用该扩展即声明调用方依赖化学模块，通用世界不再暴露化学属性。
    /// </summary>
    public static class ChemistryWorldExtensions
    {
        public static MatterInventory RequireMatterInventory(
            this ExperimentWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (world.TryGetWorldState<MatterInventory>(
                MatterWorldStateTypeIds.Inventory,
                out var inventory))
            {
                return inventory;
            }

            inventory = new MatterInventory(world.ContainsEntity);
            world.RegisterWorldState(inventory);
            return inventory;
        }
    }
}
