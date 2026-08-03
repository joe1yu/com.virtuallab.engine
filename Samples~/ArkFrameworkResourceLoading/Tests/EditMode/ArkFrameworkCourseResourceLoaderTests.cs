using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArkFramework;
using NUnit.Framework;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.ArkFrameworkAdapters.Resources;
using Object = UnityEngine.Object;

namespace VirtualLab.ArkFrameworkAdapters.Tests
{
    public sealed class ArkFrameworkCourseResourceLoaderTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
        }

        [Test]
        public void 预加载复用稳定地址且销毁时释放Ark资源租约()
        {
            var service = new FakeResourceService();
            var asset = Track(ScriptableObject.CreateInstance<TestAsset>());
            const string configuredPath = "课程资源/测试/材料.asset";
            const string expectedAddress =
                "VirtualLab/课程资源/测试/材料";
            service.Register(expectedAddress, asset);
            var loader = CreateLoader(service);

            loader.PreloadAsync(
                    new[]
                    {
                        new CourseResourceDefinition("资源.材料一", configuredPath),
                        new CourseResourceDefinition("资源.材料二", configuredPath)
                    })
                .AsTask()
                .GetAwaiter()
                .GetResult();

            Assert.That(service.LoadedKeys, Is.EqualTo(new[] { expectedAddress }));
            Assert.That(loader.LoadedResourceCount, Is.EqualTo(1));
            Assert.That(
                loader.TryLoad(configuredPath, typeof(TestAsset), out var loaded),
                Is.True);
            Assert.That(loaded, Is.SameAs(asset));
            Assert.That(
                loader.TryLoad(configuredPath, typeof(Material), out _),
                Is.False);

            loader.Dispose();

            Assert.That(service.ReleaseCount, Is.EqualTo(1));
            Assert.That(loader.LoadedResourceCount, Is.Zero);
        }

        [Test]
        public void 任一资源加载失败会回滚本轮已取得的租约()
        {
            var service = new FakeResourceService();
            var asset = Track(ScriptableObject.CreateInstance<TestAsset>());
            service.Register("地址.存在", asset);
            var loader = CreateLoader(service);
            loader.ConfigureAddressResolver(path => path);

            Assert.Throws<InvalidOperationException>(() =>
                loader.PreloadAsync(
                        new[]
                        {
                            new CourseResourceDefinition(
                                "资源.存在",
                                "地址.存在"),
                            new CourseResourceDefinition(
                                "资源.缺失",
                                "地址.缺失")
                        })
                    .AsTask()
                    .GetAwaiter()
                    .GetResult());

            Assert.That(service.ReleaseCount, Is.EqualTo(1));
            Assert.That(loader.LoadedResourceCount, Is.Zero);
            Assert.That(
                loader.TryLoad("地址.存在", typeof(TestAsset), out _),
                Is.False);
        }

        private ArkFrameworkCourseResourceLoader CreateLoader(
            IResourceService service)
        {
            var owner = Track(new GameObject("ArkFrameworkResourceLoaderTests"));
            var loader = owner.AddComponent<ArkFrameworkCourseResourceLoader>();
            loader.ConfigureResourceService(service);
            return loader;
        }

        private T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }

        private sealed class TestAsset : ScriptableObject
        {
        }

        private sealed class FakeResourceService : IResourceService
        {
            private readonly Dictionary<string, Object> _assets =
                new Dictionary<string, Object>(StringComparer.Ordinal);

            public List<string> LoadedKeys { get; } = new List<string>();
            public int ReleaseCount { get; private set; }
            public ResourceDiagnostics Diagnostics => null;

            public void Register(string key, Object asset)
            {
                _assets.Add(key, asset);
            }

            public ValueTask<IAssetLease<T>> LoadAsync<T>(
                ResourceKey key,
                CancellationToken token = default)
                where T : Object
            {
                token.ThrowIfCancellationRequested();
                LoadedKeys.Add(key.Value);
                if (!_assets.TryGetValue(key.Value, out var asset)
                    || !(asset is T typed))
                {
                    throw new InvalidOperationException(
                        $"测试资源“{key.Value}”不存在。");
                }

                return new ValueTask<IAssetLease<T>>(
                    new FakeAssetLease<T>(
                        key,
                        typed,
                        () => ReleaseCount++));
            }

            public ValueTask<IInstanceLease> InstantiateAsync(
                ResourceKey key,
                Transform parent = null,
                CancellationToken token = default) =>
                throw new NotSupportedException();

            public ValueTask<IReadOnlyList<IAssetLease<T>>>
                LoadByLabelAsync<T>(
                    string label,
                    CancellationToken token = default)
                where T : Object =>
                throw new NotSupportedException();
        }

        private sealed class FakeAssetLease<T> : IAssetLease<T>
            where T : Object
        {
            private readonly Action _release;
            private bool _disposed;

            public FakeAssetLease(ResourceKey key, T asset, Action release)
            {
                Key = key;
                Asset = asset;
                _release = release;
                CreatedUtc = DateTime.UtcNow;
            }

            public long LeaseId => 1;
            public ResourceKey Key { get; }
            public string Label => null;
            public string KeyOrLabel => Key.Value;
            public DateTime CreatedUtc { get; }
            public T Asset { get; }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _release();
            }
        }
    }
}
