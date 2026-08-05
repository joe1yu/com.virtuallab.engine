using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.Relations;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Kernel;

namespace VirtualLab.Interaction.Courses
{
    /// <summary>
    /// 根据课程定义建立带通用交互能力的权威世界。
    /// 具体学科参数仍由对应模块的结构化配置和状态操作提供。
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

            foreach (var relation in course.InitialRelations)
            {
                world.SetRelation(new EntityRelation(
                    relation.TypeId,
                    new EntityId(relation.SourceEntityId),
                    new EntityId(relation.TargetEntityId)));
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
