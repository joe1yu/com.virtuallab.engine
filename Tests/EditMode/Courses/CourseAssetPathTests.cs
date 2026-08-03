using NUnit.Framework;
using VirtualLab.Unity.Authoring.Blueprints;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseAssetPathTests
    {
        [Test]
        public void 课程目录路径随课程根目录解析()
        {
            var resolved = CourseAssetPath.Resolve(
                "课程目录/预制体/试管.prefab",
                "Assets/Samples/化学实验/课程");

            Assert.That(
                resolved,
                Is.EqualTo("Assets/Samples/化学实验/课程/预制体/试管.prefab"));
        }

        [Test]
        public void 课程目录路径不能越出课程范围()
        {
            Assert.That(
                () => CourseAssetPath.Resolve(
                    "课程目录/../其它课程/资源.prefab",
                    "Assets/Samples/化学实验/课程"),
                Throws.ArgumentException);
        }
    }
}
