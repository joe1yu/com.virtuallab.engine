using System.IO;
using VirtualLab.Unity.Authoring.Catalogs;

namespace VirtualLab.Chemistry.Authoring.Catalogs
{
    /// <summary>
    /// 以模块自有的中文 CSV 提供化学实验用品、能力、操作和过程描述。
    /// 目录不引用 Unity 资源，也不负责定位场景中的具体对象实例。
    /// </summary>
    public sealed class ChemistryCourseAuthoringProvider :
        ICourseAuthoringCatalogProvider
    {
        private const string Id = "化学";

        public string PackageId => Id;

        public CourseAuthoringModuleDescriptor Load()
        {
            var directory = Path.Combine(
                ChemistrySamplePaths.Root,
                "Editor",
                "Authoring",
                "Catalogs");
            return new CourseAuthoringCatalogCsvLoader().LoadRequired(
                PackageId,
                directory);
        }
    }
}
