using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VirtualLab.Unity.Authoring.Csv;

namespace VirtualLab.Unity.Authoring.Workbench
{
    public sealed class CourseCreationResult
    {
        internal CourseCreationResult(
            string courseDirectory,
            string authoringDirectory)
        {
            CourseDirectory = courseDirectory;
            AuthoringDirectory = authoringDirectory;
        }

        public string CourseDirectory { get; }
        public string AuthoringDirectory { get; }
    }

    /// <summary>
    /// 创建最小课程目录。先在同级临时目录完整写入，再一次移动到正式位置，
    /// 失败时不会留下半套课程。
    /// </summary>
    public sealed class CourseTemplateCreator
    {
        public CourseCreationResult Create(
            string coursesRoot,
            string courseId,
            string displayName,
            string disciplinePackageIds,
            string environmentPrefab,
            string actorEntityId = "学生")
        {
            if (string.IsNullOrWhiteSpace(coursesRoot))
            {
                throw new ArgumentException("课程根目录不能为空。", nameof(coursesRoot));
            }

            courseId = ValidateCourseId(courseId);
            displayName = string.IsNullOrWhiteSpace(displayName)
                ? courseId
                : displayName.Trim().Normalize(NormalizationForm.FormC);
            var root = Path.GetFullPath(coursesRoot);
            Directory.CreateDirectory(root);
            var target = Path.Combine(root, courseId);
            if (Directory.Exists(target) || File.Exists(target))
            {
                throw new InvalidOperationException(
                    $"课程目录“{target}”已经存在，工作台不会覆盖它。");
            }

            var staging = Path.Combine(
                root,
                ".creating-" + Guid.NewGuid().ToString("N"));
            try
            {
                var authoring = Path.Combine(staging, "Authoring");
                Directory.CreateDirectory(authoring);
                Directory.CreateDirectory(Path.Combine(staging, "Prefabs"));
                Directory.CreateDirectory(Path.Combine(staging, "Generated"));

                var course = EditableCsvDocument.Parse(
                    "课程.csv",
                    "课程标识,显示名称,学科配方包,操作者实体标识,环境预制体\r\n");
                course.AddRow(new[]
                {
                    Pair("课程ID", courseId),
                    Pair("显示名称", displayName),
                    Pair("学科配方包", disciplinePackageIds ?? string.Empty),
                    Pair("操作者实体ID", string.IsNullOrWhiteSpace(actorEntityId)
                        ? "学生"
                        : actorEntityId.Trim()),
                    Pair("环境Prefab", environmentPrefab ?? string.Empty)
                });
                WriteUtf8(Path.Combine(authoring, "课程.csv"), course.ToCsv());
                WriteUtf8(
                    Path.Combine(authoring, "实验对象.csv"),
                    "实体标识,显示名称,预制体,特征列表,初始位置,初始旋转\r\n");

                Directory.Move(staging, target);
                return new CourseCreationResult(
                    target,
                    Path.Combine(target, "Authoring"));
            }
            catch
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }

                throw;
            }
        }

        private static string ValidateCourseId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("课程 ID 不能为空。", nameof(value));
            }

            value = value.Trim().Normalize(NormalizationForm.FormC);
            if (value == "."
                || value == ".."
                || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || value.IndexOf(Path.DirectorySeparatorChar) >= 0
                || value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                throw new ArgumentException(
                    "课程 ID 必须同时是有效的单级目录名称。",
                    nameof(value));
            }

            return value;
        }

        private static void WriteUtf8(string path, string content) =>
            File.WriteAllText(path, content, new UTF8Encoding(false));

        private static KeyValuePair<string, string> Pair(
            string key,
            string value) =>
            new KeyValuePair<string, string>(key, value);
    }

    public sealed class CourseCreationWindow : EditorWindow
    {
        private string _courseId = string.Empty;
        private string _displayName = string.Empty;
        private string _disciplinePackageIds = string.Empty;
        private string _actorEntityId = "学生";
        private IReadOnlyList<string> _availableDisciplinePackageIds =
            Array.Empty<string>();
        private GameObject _environmentPrefab;
        private string _message = string.Empty;

        public static void Open()
        {
            var window = CreateInstance<CourseCreationWindow>();
            window.titleContent = new GUIContent("新建课程");
            window.minSize = new Vector2(470f, 285f);
            window.maxSize = new Vector2(760f, 440f);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            try
            {
                _availableDisciplinePackageIds =
                    CourseWorkbenchCompiler.FindAvailableDisciplinePackageIds();
                if (_availableDisciplinePackageIds.Count == 1
                    && string.IsNullOrWhiteSpace(_disciplinePackageIds))
                {
                    _disciplinePackageIds = _availableDisciplinePackageIds[0];
                }
            }
            catch (Exception exception)
            {
                _availableDisciplinePackageIds = Array.Empty<string>();
                _message = "读取可用学科类型失败：" + exception.Message;
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("创建最小课程", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只创建课程基础表、实验对象表、预制体目录（Prefabs）和生成资源目录（Generated）；其他配置表按实际需要再添加。",
                MessageType.Info);
            _courseId = EditorGUILayout.TextField(
                new GUIContent("课程 ID", "稳定中文 ID，同时作为课程目录名。"),
                _courseId);
            _displayName = EditorGUILayout.TextField("显示名称", _displayName);
            DrawDisciplinePackageSelection();
            _actorEntityId = EditorGUILayout.TextField(
                new GUIContent(
                    "操作者对象 ID",
                    "接收输入命令的课程对象 ID。创建后请在实验对象中添加同名对象。"),
                _actorEntityId);
            _environmentPrefab = (GameObject)EditorGUILayout.ObjectField(
                "实验室场景预制体",
                _environmentPrefab,
                typeof(GameObject),
                false);

            if (!string.IsNullOrWhiteSpace(_message))
            {
                EditorGUILayout.HelpBox(_message, MessageType.Error);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("取消", GUILayout.Width(80f)))
            {
                Close();
            }

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_courseId));
            if (GUILayout.Button("创建课程", GUILayout.Width(100f)))
            {
                CreateCourse();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDisciplinePackageSelection()
        {
            EditorGUILayout.LabelField(
                new GUIContent(
                    "学科类型",
                    "决定课程可以使用哪些学科操作、规则和物质数据。通常只需选择一个。"),
                EditorStyles.boldLabel);
            if (_availableDisciplinePackageIds.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "当前项目没有检测到可用的学科类型。请先确认化学、物理或生物扩展包已经安装并且能够编译。",
                    MessageType.Warning);
                return;
            }

            var selected = new HashSet<string>(
                (_disciplinePackageIds ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()),
                StringComparer.Ordinal);
            foreach (var packageId in _availableDisciplinePackageIds)
            {
                var enabled = selected.Contains(packageId);
                var next = EditorGUILayout.ToggleLeft(packageId, enabled);
                if (next == enabled)
                {
                    continue;
                }

                if (next)
                {
                    selected.Add(packageId);
                }
                else
                {
                    selected.Remove(packageId);
                }
            }

            _disciplinePackageIds = string.Join(
                ";",
                _availableDisciplinePackageIds.Where(selected.Contains));
            EditorGUILayout.LabelField(
                "学科类型来自当前项目已安装的扩展包，不需要手工填写内部编号。",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void CreateCourse()
        {
            try
            {
                var result = new CourseTemplateCreator().Create(
                    Path.GetFullPath("Assets/VirtualLab/Courses"),
                    _courseId,
                    _displayName,
                    _disciplinePackageIds,
                    AssetDatabase.GetAssetPath(_environmentPrefab),
                    _actorEntityId);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var assetPath = ToAssetPath(
                    Path.Combine(result.AuthoringDirectory, "课程.csv"));
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                }

                var opened = CourseAuthoringWindow.OpenCourse(
                    ToAssetPath(result.AuthoringDirectory));
                EditorUtility.DisplayDialog(
                    "课程创建完成",
                    opened
                        ? $"已创建并打开课程“{_courseId}”。接下来点击“添加第一个实验对象”。"
                        : $"已创建课程“{_courseId}”。当前工作台保留了未保存修改，稍后可从课程下拉框切换到新课程。",
                    "好的");
                Close();
            }
            catch (Exception exception)
            {
                _message = exception.Message;
            }
        }

        private static string ToAssetPath(string absolutePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(
                UnityEngine.Application.dataPath,
                ".."));
            var relative = Path.GetFullPath(absolutePath)
                .Substring(projectRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace('\\', '/');
        }
    }
}
