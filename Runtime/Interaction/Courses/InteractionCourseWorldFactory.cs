using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Kernel;

namespace VirtualLab.Interaction.Courses
{
    /// <summary>
    /// 根据课程定义建立带通用交互能力的世界实体。
    /// 初始关系必须等全部模块安装关系模式后由课程运行定义写入；具体学科参数
    /// 仍由对应模块的结构化配置和状态操作提供。
    /// </summary>
    public static class InteractionCourseWorldFactory
    {
        public static ExperimentWorld Create(CompiledCourseDefinition course)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var world = new ExperimentWorld(
                InteractionCourseRegistrations.CreateModuleScope()
                    .RelationSchemas);
            var portsByEntity = course.Ports
                .GroupBy(value => value.EntityId, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => (IReadOnlyList<ConnectionPortDefinition>)value
                        .OrderBy(port => port.PortId, StringComparer.Ordinal)
                        .Select(port => new ConnectionPortDefinition(
                            port.PortId,
                            port.CompatibilityGroup))
                        .ToArray(),
                    StringComparer.Ordinal);
            foreach (var definition in course.Entities)
            {
                var entity = new ExperimentEntity(
                    new EntityId(definition.EntityId));
                foreach (var capabilityId in definition.CapabilityIds)
                {
                    entity.AddCapability(CreateCapability(
                        capabilityId,
                        portsByEntity.TryGetValue(
                            definition.EntityId,
                            out var ports)
                            ? ports
                            : Array.Empty<ConnectionPortDefinition>()));
                }

                world.AddEntity(entity);
            }

            return world;
        }

        private static ICapability CreateCapability(
            string capabilityId,
            IReadOnlyList<ConnectionPortDefinition> ports)
        {
            return capabilityId switch
            {
                InteractionCapabilityIds.Grabbable => new GrabbableCapability(),
                InteractionCapabilityIds.Container => new ContainerCapability(0m),
                InteractionCapabilityIds.Connector =>
                    new ConnectorCapability(ports),
                InteractionCapabilityIds.Observable => new ObservableCapability(),
                InteractionCapabilityIds.Clampable => new ClampableCapability(),
                InteractionCapabilityIds.Coverable => new CoverableCapability(),
                InteractionCapabilityIds.Breakable => new BreakableCapability(),
                _ => new ConfiguredCapability(capabilityId)
            };
        }
    }
}
