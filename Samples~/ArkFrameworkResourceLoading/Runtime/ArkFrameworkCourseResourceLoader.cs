using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArkFramework;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.UnityAdapters.Courses;
using Object = UnityEngine.Object;

namespace VirtualLab.ArkFrameworkAdapters.Resources
{
    /// <summary>
    /// 课程资源地址约定。默认复用 Resources 的无扩展名运行时路径，
    /// 使同一份中文课程配置可以在 Resources 与 Addressables 之间切换。
    /// </summary>
    public static class ArkFrameworkCourseResourceAddress
    {
        public static string FromConfiguredPath(string configuredPath) =>
            CourseResourcesPath.ToRuntimePath(configuredPath);
    }

    /// <summary>
    /// 使用 ArkFramework 资源服务预加载课程资源，并持有 lease 直至组件销毁。
    /// 场景装配和表现执行阶段仍通过同步端口读取缓存，不阻塞 Unity 主线程。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConfigDrivenCourseBootstrap))]
    public sealed class ArkFrameworkCourseResourceLoader :
        MonoBehaviour,
        ICourseResourceLoader,
        ICourseResourcePreloader,
        IDisposable
    {
        private readonly Dictionary<string, string> _addressByConfiguredPath =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, IAssetLease<Object>>
            _leasesByAddress =
                new Dictionary<string, IAssetLease<Object>>(
                    StringComparer.Ordinal);
        private readonly CancellationTokenSource _lifetime =
            new CancellationTokenSource();

        private IResourceService _resourceService;
        private Func<string, string> _addressResolver =
            ArkFrameworkCourseResourceAddress.FromConfiguredPath;
        private bool _preloading;
        private bool _disposed;

        public int LoadedResourceCount => _leasesByAddress.Count;

        public void ConfigureResourceService(IResourceService resourceService)
        {
            EnsureConfigurable();
            _resourceService = resourceService
                ?? throw new ArgumentNullException(nameof(resourceService));
        }

        public void ConfigureAddressResolver(Func<string, string> resolver)
        {
            EnsureConfigurable();
            _addressResolver = resolver
                ?? throw new ArgumentNullException(nameof(resolver));
        }

        public async ValueTask PreloadAsync(
            IReadOnlyList<CourseResourceDefinition> resources,
            CancellationToken token = default)
        {
            if (resources == null)
            {
                throw new ArgumentNullException(nameof(resources));
            }

            ThrowIfDisposed();
            if (_preloading)
            {
                throw new InvalidOperationException("课程资源正在预加载。");
            }

            _preloading = true;
            var pendingLeases = new Dictionary<string, IAssetLease<Object>>(
                StringComparer.Ordinal);
            var pendingAddresses = new Dictionary<string, string>(
                StringComparer.Ordinal);
            try
            {
                using (var linked = CancellationTokenSource
                           .CreateLinkedTokenSource(token, _lifetime.Token))
                {
                    var service = await ResolveResourceServiceAsync(
                        linked.Token);
                    for (var index = 0; index < resources.Count; index++)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        var configuredPath = NormalizeConfiguredPath(
                            resources[index].AssetPath);
                        var address = ResolveAddress(configuredPath);
                        if (_addressByConfiguredPath.TryGetValue(
                                configuredPath,
                                out var existingAddress))
                        {
                            if (!string.Equals(
                                    existingAddress,
                                    address,
                                    StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException(
                                    $"课程资源“{configuredPath}”的地址映射发生变化。");
                            }

                            continue;
                        }

                        pendingAddresses[configuredPath] = address;
                        if (_leasesByAddress.ContainsKey(address)
                            || pendingLeases.ContainsKey(address))
                        {
                            continue;
                        }

                        var lease = await service.LoadAsync<Object>(
                            new ResourceKey(address),
                            linked.Token);
                        if (lease == null || lease.Asset == null)
                        {
                            lease?.Dispose();
                            throw new InvalidOperationException(
                                $"ArkFramework 未返回课程资源“{address}”。");
                        }

                        pendingLeases.Add(address, lease);
                    }

                    linked.Token.ThrowIfCancellationRequested();
                    foreach (var pair in pendingLeases)
                    {
                        _leasesByAddress.Add(pair.Key, pair.Value);
                    }

                    foreach (var pair in pendingAddresses)
                    {
                        _addressByConfiguredPath.Add(pair.Key, pair.Value);
                    }
                }
            }
            catch
            {
                DisposeLeases(pendingLeases.Values);
                throw;
            }
            finally
            {
                _preloading = false;
            }
        }

        public bool TryLoad(
            string resourcePath,
            Type expectedType,
            out Object resource)
        {
            ThrowIfDisposed();
            if (expectedType == null)
            {
                throw new ArgumentNullException(nameof(expectedType));
            }

            resource = null;
            var configuredPath = NormalizeConfiguredPath(resourcePath);
            if (!_addressByConfiguredPath.TryGetValue(
                    configuredPath,
                    out var address)
                || !_leasesByAddress.TryGetValue(address, out var lease))
            {
                return false;
            }

            var asset = lease.Asset;
            if (asset == null || !expectedType.IsInstanceOfType(asset))
            {
                return false;
            }

            resource = asset;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            // 主动释放组件时也要中止尚未完成的异步加载，避免租约在释放后被重新写入缓存。
            _lifetime.Cancel();
            DisposeLeases(_leasesByAddress.Values);
            _leasesByAddress.Clear();
            _addressByConfiguredPath.Clear();
        }

        private async ValueTask<IResourceService> ResolveResourceServiceAsync(
            CancellationToken token)
        {
            if (_resourceService != null)
            {
                return _resourceService;
            }

            var host = FrameworkHost.Current;
            if (host == null)
            {
                throw new InvalidOperationException(
                    "场景中不存在 ArkFramework FrameworkHost。");
            }

            await host.StartRuntimeAsync(token);
            var runtime = host.Runtime;
            if (runtime == null
                || !runtime.Services.TryResolve<IResourceService>(
                    out var service))
            {
                throw new InvalidOperationException(
                    "ArkFramework 尚未安装 ResourceModule。");
            }

            _resourceService = service;
            return service;
        }

        private string ResolveAddress(string configuredPath)
        {
            var address = _addressResolver(configuredPath)?.Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new InvalidOperationException(
                    $"课程资源“{configuredPath}”没有可用的 ArkFramework 地址。");
            }

            return address;
        }

        private static string NormalizeConfiguredPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("课程资源路径不能为空。", nameof(value));
            }

            return value.Trim().Replace('\\', '/');
        }

        private static void DisposeLeases(
            IEnumerable<IAssetLease<Object>> leases)
        {
            foreach (var lease in leases)
            {
                lease?.Dispose();
            }
        }

        private void EnsureConfigurable()
        {
            ThrowIfDisposed();
            if (_preloading || _leasesByAddress.Count != 0)
            {
                throw new InvalidOperationException(
                    "课程资源开始预加载后不能再修改 ArkFramework 配置。");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(ArkFrameworkCourseResourceLoader));
            }
        }

        private void OnDestroy()
        {
            Dispose();
            _lifetime.Dispose();
        }
    }
}
