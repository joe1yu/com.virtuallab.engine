using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Authoring;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Chemistry.UnityAdapters;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Interaction.Capabilities;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Generation;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.Unity.Authoring.Recipes;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Input;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.OxygenCourse.Authoring
{
    /// <summary>
    /// 定位已导入的氧气课程 Sample，不依赖包版本或 Package Manager 生成的目录名。
    /// </summary>
    public static class OxygenCourseSamplePaths
    {
        private const string MarkerSuffix =
            "/Courses/氧气的实验室制取与性质/Editor/OxygenCourseContentBuilder.cs";

        public static string Root
        {
            get
            {
                var matches = AssetDatabase.FindAssets(
                        "OxygenCourseContentBuilder t:MonoScript")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(value => value.Replace('\\', '/'))
                    .Where(value => value.EndsWith(
                        MarkerSuffix,
                        StringComparison.Ordinal))
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (matches.Length != 1)
                {
                    throw new InvalidOperationException(
                        matches.Length == 0
                            ? "无法定位已导入的氧气课程 Sample。"
                            : "检测到多个氧气课程 Sample，请只保留一份导入副本。");
                }

                return matches[0].Substring(
                    0,
                    matches[0].Length - MarkerSuffix.Length);
            }
        }
    }

    /// <summary>
    /// 氧气课程的编辑期资源构建入口。实验总预制体包含全部操作对象的通用视图、
    /// 碰撞体、语义锚点和表现插槽，不写入课程规则。
    /// </summary>
    public static class OxygenCourseContentBuilder
    {
        public const string CourseId = "氧气的实验室制取与性质";

        public static string CourseRoot =>
            OxygenCourseSamplePaths.Root
            + "/Courses/氧气的实验室制取与性质";

        public static string AuthoringDirectory => CourseRoot + "/Authoring";
        public static string GeneratedDirectory => CourseRoot + "/Generated";
        public static string CourseAssetDirectory =>
            CourseRoot + "/Resources/"
            + CourseRuntimeResourcePaths.CompiledCourses;
        public static string ExperimentPrefabPath =>
            CourseRoot + "/Resources/"
            + CourseRuntimeResourcePaths.CourseResources
            + "/" + CourseId + "/氧气实验.prefab";
        public static string CourseAssetPath =>
            CourseAssetDirectory + "/氧气实验课程.asset";
        public static string UiScenarioPath =>
            CourseRoot + "/UI/UI模拟步骤.csv";
        public static string ProductionScenePath =>
            OxygenCourseSamplePaths.Root + "/Scenes/OxygenLaboratory.unity";

        [MenuItem("Virtual Lab/课程/编译/氧气的实验室制取与性质")]
        public static void Build()
        {
            EnsureFolder(GeneratedDirectory);
            EnsureFolder(CourseAssetDirectory);
            var compilation = CompileCourse();
            ValidateExperimentPrefab(compilation.Domain);

            if (!new CourseAssetGenerator().TryGenerateFromBlueprint(
                    CourseAssetPath,
                    GeneratedDirectory,
                    compilation,
                    out var diagnostics))
            {
                throw new InvalidOperationException(
                    FormatDiagnostics(diagnostics));
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"已生成配置课程资产：{CourseAssetPath}");
        }

        [MenuItem("Virtual Lab/课程/资源/创建缺失的氧气实验总预制体")]
        public static void CreateMissingExampleExperimentPrefab()
        {
            EnsureFolder(Path.GetDirectoryName(ExperimentPrefabPath)
                ?.Replace('\\', '/'));
            var compilation = CompileCourse();
            CreateExperimentPrefab(compilation.Domain, false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateExperimentPrefab(compilation.Domain);
            Debug.Log("缺失的氧气实验总预制体已创建；已有美术资源未被覆盖。");
        }

        /// <summary>
        /// 重建随 Sample 提供的占位总预制体。该入口只供维护示例资源使用，
        /// 正常课程编译不会覆盖项目已经制作完成的美术预制体。
        /// </summary>
        [MenuItem("Virtual Lab/课程/资源/重建氧气实验示例总预制体")]
        public static void RebuildExampleExperimentPrefab()
        {
            EnsureFolder(Path.GetDirectoryName(ExperimentPrefabPath)
                ?.Replace('\\', '/'));
            var compilation = CompileCourse();
            CreateExperimentPrefab(compilation.Domain, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateExperimentPrefab(compilation.Domain);
            Debug.Log("氧气实验示例总预制体已按当前器材清单重建。");
        }

        [MenuItem("Virtual Lab/课程/场景/生成氧气实验生产场景")]
        public static void BuildProductionScene()
        {
            var course = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                CourseAssetPath);
            var uiScenario = AssetDatabase.LoadAssetAtPath<TextAsset>(
                UiScenarioPath);
            if (course == null || uiScenario == null)
            {
                throw new InvalidOperationException(
                    "请先编译氧气课程资产和 UI 演示步骤。");
            }

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var cameraObject = new GameObject("实验观察相机");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 3.5f, -8f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.8f, 0f));
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            var ui = new GameObject("课程演示界面");
            ui.AddComponent<CourseUiClickSimulator>().Configure(uiScenario);
            var systems = new GameObject("课程系统");
            systems.AddComponent<ChemistryConfiguredCourseSessionFactory>();
            var bootstrap = systems.AddComponent<ConfigDrivenCourseBootstrap>();
            bootstrap.ConfigureCourseId(course.CourseId);
            systems.AddComponent<CourseSemanticInputGateway>();
            systems.AddComponent<CourseDirectManipulationController>();

            if (!EditorSceneManager.SaveScene(scene, ProductionScenePath))
            {
                throw new InvalidOperationException("氧气生产场景保存失败。");
            }

            // 场景保存后不能再通过文件 API 直接改写磁盘内容，否则 Unity 会将
            // 当前已打开的场景判定为“被外部修改”，并在退出运行模式时要求重载。
            EnsureProductionSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"已生成配置驱动生产场景：{ProductionScenePath}");
        }

        private static void EnsureProductionSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var existing = scenes.FindIndex(value =>
                string.Equals(
                    value.path,
                    ProductionScenePath,
                    StringComparison.Ordinal));
            var production = new EditorBuildSettingsScene(
                ProductionScenePath,
                true);
            if (existing >= 0)
            {
                scenes[existing] = production;
            }
            else
            {
                scenes.Add(production);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static CourseBlueprintCompilationResult CompileCourse()
        {
            var source = CourseBlueprintSource.FromDirectory(
                Path.GetFullPath(AuthoringDirectory));
            var compilation = new CourseBlueprintCompiler().Compile(
                source,
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new ChemistryRecipePackageProvider()
                });
            if (!compilation.IsSuccess)
            {
                throw new InvalidOperationException(
                    FormatDiagnostics(compilation.Diagnostics));
            }

            return compilation;
        }

        private static void ValidateExperimentPrefab(
            CompiledCourseDefinition course)
        {
            var contracts = course.PrefabContracts.ToDictionary(
                value => value.EntityId,
                StringComparer.Ordinal);
            var errors = new List<string>();
            var resource = course.Resources.SingleOrDefault(value =>
                string.Equals(
                    value.ResourceId,
                    course.ExperimentPrefabResourceId,
                    StringComparison.Ordinal));
            if (resource == null)
            {
                throw new InvalidOperationException("课程没有声明实验预制体资源。");
            }

            var assetPath = VirtualLab.Unity.Authoring.Blueprints
                .CourseAssetPath.Resolve(resource.AssetPath, CourseRoot);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                errors.Add($"缺少实验预制体：{assetPath}");
            }
            else
            {
                var views = prefab
                    .GetComponentsInChildren<CourseEntityView>(true)
                    .GroupBy(value => value.EntityId, StringComparer.Ordinal)
                    .ToDictionary(
                        value => value.Key ?? string.Empty,
                        value => value.ToArray(),
                        StringComparer.Ordinal);
                var expected = new HashSet<string>(
                    course.Entities.Select(value => value.EntityId),
                    StringComparer.Ordinal);
                errors.AddRange(views
                    .Where(value => string.IsNullOrWhiteSpace(value.Key))
                    .Select(_ => "实验预制体包含未填写实体 ID 的视图。"));
                errors.AddRange(views
                    .Where(value => value.Value.Length > 1)
                    .Select(value => $"实验预制体包含重复实体视图：{value.Key}"));
                errors.AddRange(views.Keys
                    .Where(value => !string.IsNullOrWhiteSpace(value)
                                    && !expected.Contains(value))
                    .Select(value => $"实验预制体包含未知实体视图：{value}"));
                errors.AddRange(expected
                    .Where(value => !views.ContainsKey(value))
                    .Select(value => $"实验预制体缺少实体视图：{value}"));

                foreach (var contract in contracts)
                {
                    if (!views.TryGetValue(contract.Key, out var matching)
                        || matching.Length != 1)
                    {
                        continue;
                    }

                    errors.AddRange(PrefabContractValidator
                        .Validate(matching[0], contract.Value)
                        .Select(value =>
                            $"{contract.Key}：{value.Message}"));
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "实验预制体未满足课程契约：\n" + string.Join("\n", errors));
            }
        }

        private static void CreateExperimentPrefab(
            CompiledCourseDefinition course,
            bool overwriteExisting)
        {
            if (!overwriteExisting
                && AssetDatabase.LoadAssetAtPath<GameObject>(
                    ExperimentPrefabPath) != null)
            {
                return;
            }

            var contracts = course.PrefabContracts.ToDictionary(
                value => value.EntityId,
                StringComparer.Ordinal);
            var layouts = course.SceneLayouts.ToDictionary(
                value => value.EntityId,
                StringComparer.Ordinal);
            var root = CreateEnvironment(course.CourseId);
            try
            {
                foreach (var entity in course.Entities.OrderBy(
                             value => value.EntityId,
                             StringComparer.Ordinal))
                {
                    var entityObject = GameObject.CreatePrimitive(
                        PrimitiveFor(entity.EntityId));
                    entityObject.name = entity.EntityId;
                    entityObject.transform.SetParent(root.transform, false);
                    entityObject.transform.localScale = ScaleFor(
                        entity.EntityId);
                    if (layouts.TryGetValue(entity.EntityId, out var layout))
                    {
                        entityObject.transform.localPosition = new Vector3(
                            (float)layout.PositionX,
                            (float)layout.PositionY,
                            (float)layout.PositionZ);
                        entityObject.transform.localEulerAngles = new Vector3(
                            (float)layout.RotationX,
                            (float)layout.RotationY,
                            (float)layout.RotationZ);
                    }

                    var view = entityObject.AddComponent<CourseEntityView>();
                    if (contracts.TryGetValue(
                            entity.EntityId,
                            out var contract))
                    {
                        AddAnchors(
                            entityObject.transform,
                            entity.EntityId,
                            contract);
                        AddSlots(
                            entityObject.transform,
                            entity.EntityId,
                            contract);
                    }

                    view.Configure(entity.EntityId);
                }

                PrefabUtility.SaveAsPrefabAsset(root, ExperimentPrefabPath);
                RemoveTrailingWhitespace(ExperimentPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreateEnvironment(string entityId)
        {
            var root = new GameObject(entityId);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "实验台";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.55f, 0f);
            floor.transform.localScale = new Vector3(6f, 0.1f, 3f);
            return root;
        }

        private static void AddAnchors(
            Transform root,
            string entityId,
            CoursePrefabContractDefinition contract)
        {
            var capabilities = new HashSet<string>(
                contract.CapabilityIds,
                StringComparer.Ordinal);
            if (string.Equals(entityId, "学生", StringComparison.Ordinal))
            {
                AddAnchor(
                    root,
                    "抓取锚点",
                    SemanticAnchorKind.InteractionGrip);
            }

            foreach (var port in contract.PortIds)
            {
                AddAnchor(
                    root,
                    port,
                    SemanticAnchorKind.ConnectionPort);
            }

            if (capabilities.Contains(InteractionCapabilityIds.Connector)
                && contract.PortIds.Count == 0)
            {
                AddAnchor(
                    root,
                    entityId + ".连接点",
                    SemanticAnchorKind.ConnectionPort);
            }

            if (capabilities.Contains(ChemistryCapabilityIds.Pourable))
            {
                AddAnchor(
                    root,
                    entityId + ".倾倒口",
                    SemanticAnchorKind.PourOutlet);
            }

            if (capabilities.Contains(ChemistryCapabilityIds.Heatable))
            {
                AddAnchor(
                    root,
                    entityId + ".加热点",
                    SemanticAnchorKind.HeatingPoint);
            }

            if (capabilities.Contains(ChemistryCapabilityIds.Ignitable))
            {
                AddAnchor(
                    root,
                    entityId + ".点燃点",
                    SemanticAnchorKind.IgnitionPoint);
            }

            if (capabilities.Contains(InteractionCapabilityIds.Observable))
            {
                AddAnchor(
                    root,
                    entityId + ".观察点",
                    SemanticAnchorKind.ObservationFocus);
            }
        }

        private static void AddSlots(
            Transform root,
            string entityId,
            CoursePrefabContractDefinition contract)
        {
            var capabilities = new HashSet<string>(
                contract.CapabilityIds,
                StringComparer.Ordinal);
            var needsContent =
                capabilities.Contains(InteractionCapabilityIds.Container) ||
                capabilities.Contains(InteractionCapabilityIds.Observable);
            if (needsContent)
            {
                EnsureSlot(
                    root,
                    "插槽.内容",
                    PresentationSlotKind.Content);
            }

            if (string.Equals(entityId, "水槽", StringComparison.Ordinal) ||
                entityId.StartsWith("集气瓶", StringComparison.Ordinal))
            {
                EnsureSlot(
                    root,
                    "插槽.液体",
                    PresentationSlotKind.Liquid);
            }

            if (capabilities.Contains(ChemistryCapabilityIds.Ignitable) ||
                capabilities.Contains(ChemistryCapabilityIds.Combustible) ||
                entityId.StartsWith("集气瓶", StringComparison.Ordinal))
            {
                EnsureSlot(
                    root,
                    "插槽.燃烧",
                    PresentationSlotKind.Combustion);
            }

            if (entityId.StartsWith("集气瓶", StringComparison.Ordinal))
            {
                EnsureSlot(
                    root,
                    "插槽.高亮",
                    PresentationSlotKind.Highlight);
            }
        }

        private static void AddAnchor(
            Transform root,
            string anchorId,
            SemanticAnchorKind kind)
        {
            var child = new GameObject(anchorId);
            child.transform.SetParent(root, false);
            child.transform.localPosition = Vector3.up * 0.5f;
            child.AddComponent<SemanticAnchorMarker>()
                .Configure(anchorId, kind);
        }

        private static void EnsureSlot(
            Transform root,
            string slotId,
            PresentationSlotKind kind)
        {
            var matches = root
                .GetComponentsInChildren<PresentationSlotMarker>(true)
                .Where(value =>
                    value != null &&
                    string.Equals(
                        value.SlotId,
                        slotId,
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Prefab“{root.name}”存在重复表现插槽“{slotId}”。");
            }

            if (matches.Length == 1)
            {
                if (matches[0].Kind != kind)
                {
                    throw new InvalidOperationException(
                        $"Prefab“{root.name}”的表现插槽“{slotId}”种类不一致。");
                }

                return;
            }

            var child = new GameObject(slotId);
            child.transform.SetParent(root, false);
            child.AddComponent<PresentationSlotMarker>()
                .Configure(slotId, kind);
            if (kind == PresentationSlotKind.Content ||
                kind == PresentationSlotKind.Liquid ||
                kind == PresentationSlotKind.Highlight)
            {
                child.AddComponent<MeshFilter>();
                child.AddComponent<MeshRenderer>();
            }

            if (kind == PresentationSlotKind.Content ||
                kind == PresentationSlotKind.Combustion)
            {
                var particles = child.AddComponent<ParticleSystem>();
                var main = particles.main;
                main.playOnAwake = false;
                particles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static PrimitiveType PrimitiveFor(string entityId)
        {
            if (entityId.Contains("瓶")
                || entityId.Contains("试管")
                || entityId.Contains("灯"))
            {
                return PrimitiveType.Cylinder;
            }

            if (entityId.Contains("木炭")
                || entityId.Contains("棉花团"))
            {
                return PrimitiveType.Sphere;
            }

            return PrimitiveType.Cube;
        }

        private static Vector3 ScaleFor(string entityId)
        {
            if (string.Equals(entityId, "铁架台", StringComparison.Ordinal))
            {
                return new Vector3(0.18f, 2f, 0.18f);
            }

            if (entityId.Contains("试管夹"))
            {
                return new Vector3(0.9f, 0.12f, 0.12f);
            }

            if (string.Equals(entityId, "试管架", StringComparison.Ordinal))
            {
                return new Vector3(1.4f, 0.35f, 0.8f);
            }

            if (entityId.Contains("折角导气管")
                || entityId.Contains("铁丝"))
            {
                return new Vector3(0.08f, 1.2f, 0.08f);
            }

            if (entityId.Contains("镊子")
                || entityId.Contains("坩埚钳")
                || entityId.Contains("药匙"))
            {
                return new Vector3(0.12f, 1f, 0.12f);
            }

            if (entityId.Contains("玻璃片"))
            {
                return new Vector3(0.6f, 0.04f, 0.6f);
            }

            if (entityId.Contains("水槽"))
            {
                return new Vector3(2f, 0.5f, 1.2f);
            }

            if (entityId.Contains("瓶盖")
                || entityId.Contains("酒精灯帽"))
            {
                return new Vector3(0.45f, 0.12f, 0.45f);
            }

            if (entityId.Contains("废液缸"))
            {
                return new Vector3(0.8f, 0.8f, 0.8f);
            }

            return new Vector3(0.4f, 0.8f, 0.4f);
        }

        private static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        /// <summary>
        /// Unity YAML 会为部分空字段写入行尾空格；生成后统一清理，
        /// 确保批处理构建结果稳定并通过 Git 空白检查。
        /// </summary>
        private static void RemoveTrailingWhitespace(string assetPath)
        {
            var fullPath = Path.GetFullPath(assetPath);
            var content = File.ReadAllText(fullPath);
            var normalized = Regex.Replace(
                content,
                "[ \\t]+(?=\\r?$)",
                string.Empty,
                RegexOptions.Multiline);
            if (!string.Equals(
                    content,
                    normalized,
                    StringComparison.Ordinal))
            {
                File.WriteAllText(
                    fullPath,
                    normalized,
                    new UTF8Encoding(false));
            }
        }

        private static string FormatDiagnostics(
            IEnumerable<CourseCompilationDiagnostic> diagnostics)
        {
            return string.Join(
                "\n",
                diagnostics.Select(value =>
                    value.Code + ": " + value.Reason));
        }
    }
}
