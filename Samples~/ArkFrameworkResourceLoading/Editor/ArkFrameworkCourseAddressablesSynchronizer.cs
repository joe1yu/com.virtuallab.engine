using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using VirtualLab.ArkFrameworkAdapters.Resources;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.ArkFrameworkAdapters.Editor
{
    /// <summary>
    /// 将课程资源注册为 Addressables，并使用运行时加载器采用的稳定地址。
    /// </summary>
    public static class ArkFrameworkCourseAddressablesSynchronizer
    {
        public const string CourseGroupName = "虚拟实验课程";

        [MenuItem(
            "Virtual Lab/课程/资源/同步选中课程到 ArkFramework Addressables")]
        private static void SyncSelectedCourse()
        {
            var course = Selection.activeObject as CompiledCourseAsset;
            if (course == null)
            {
                throw new InvalidOperationException(
                    "请先在 Project 窗口选择一个编译课程资产。");
            }

            var count = Sync(course);
            Debug.Log(
                $"课程“{course.CourseId}”已同步 {count} 个 Addressables 资源。");
        }

        [MenuItem(
            "Virtual Lab/课程/资源/同步选中课程到 ArkFramework Addressables",
            true)]
        private static bool CanSyncSelectedCourse() =>
            Selection.activeObject is CompiledCourseAsset;

        public static int Sync(CompiledCourseAsset course)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            var settings =
                AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                throw new InvalidOperationException(
                    "无法创建或读取 Addressables 设置。");
            }

            var group = settings.FindGroup(CourseGroupName)
                        ?? settings.CreateGroup(
                            CourseGroupName,
                            false,
                            false,
                            false,
                            null,
                            typeof(BundledAssetGroupSchema),
                            typeof(ContentUpdateGroupSchema));
            if (group == null)
            {
                throw new InvalidOperationException(
                    "无法创建虚拟实验课程 Addressables 分组。");
            }

            var domain = CourseAssetDecoder.DecodeDomain(course);
            var resourcesByAddress = domain.Resources
                .GroupBy(
                    value => ArkFrameworkCourseResourceAddress
                        .FromConfiguredPath(value.AssetPath),
                    StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.First(),
                    StringComparer.Ordinal);
            var assetPathsByAddress = FindResourceAssetPaths(
                resourcesByAddress.Keys);

            foreach (var pair in resourcesByAddress)
            {
                if (!assetPathsByAddress.TryGetValue(
                        pair.Key,
                        out var paths)
                    || paths.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"课程资源“{pair.Value.ResourceId}”找不到工程资产，" +
                        $"运行时地址为“{pair.Key}”。");
                }

                if (paths.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"运行时地址“{pair.Key}”对应多个工程资产：" +
                        string.Join("、", paths));
                }

                AddOrMoveEntry(settings, group, paths[0], pair.Key);
            }

            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return resourcesByAddress.Count;
        }

        private static Dictionary<string, List<string>>
            FindResourceAssetPaths(IEnumerable<string> requiredAddresses)
        {
            var result = requiredAddresses
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    value => value,
                    _ => new List<string>(),
                    StringComparer.Ordinal);
            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                var address = TryGetResourcesAddress(assetPath);
                if (address != null
                    && result.TryGetValue(address, out var matches))
                {
                    matches.Add(assetPath);
                }
            }

            return result;
        }

        private static string TryGetResourcesAddress(string assetPath)
        {
            const string marker = "/Resources/";
            var normalized = (assetPath ?? string.Empty).Replace('\\', '/');
            var markerIndex = normalized.IndexOf(
                marker,
                StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return null;
            }

            var relative = normalized.Substring(markerIndex + marker.Length);
            var extension = Path.GetExtension(relative);
            return string.IsNullOrEmpty(extension)
                ? relative
                : relative.Substring(0, relative.Length - extension.Length);
        }

        private static void AddOrMoveEntry(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            string assetPath,
            string address)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                throw new InvalidOperationException(
                    $"工程资产“{assetPath}”没有可用 GUID。");
            }

            var entry = settings.CreateOrMoveEntry(
                guid,
                group,
                false,
                false);
            if (entry == null)
            {
                throw new InvalidOperationException(
                    $"无法为工程资产“{assetPath}”创建 Addressables 条目。");
            }

            entry.SetAddress(address, false);
        }
    }
}
