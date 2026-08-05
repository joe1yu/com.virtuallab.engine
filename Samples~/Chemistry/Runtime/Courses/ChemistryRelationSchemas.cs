using System.Collections.Generic;
using VirtualLab.Domain.Relations;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 化学模块拥有的关系类型标识，通用引擎不再声明化学关系。
    /// </summary>
    public static class ChemistryRelationTypeIds
    {
        public static readonly RelationTypeId HeatedBy =
            new RelationTypeId("化学.关系.加热");
    }

    /// <summary>
    /// 化学关系模式目录。受热对象在同一时刻只能由一个热源持续加热。
    /// </summary>
    public static class ChemistryRelationSchemas
    {
        public static readonly RelationSchema HeatedBy = new RelationSchema(
            ChemistryRelationTypeIds.HeatedBy,
            sourceUnique: true);

        public static IReadOnlyList<RelationSchema> All { get; } =
            new[] { HeatedBy };
    }
}
