using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VirtualLab.Unity.Authoring.Workbench
{
    /// <summary>
    /// 新建课程时直接创建完整十表，后续不再补建隐含配置文件。
    /// </summary>
    public sealed class CourseCreationWindow : EditorWindow
    {
        private string _courseId = string.Empty;
        private string _displayName = string.Empty;
        private string _disciplinePackageIds = string.Empty;
        private string _actorEntityId = "学生";
        private IReadOnlyList<string> _availableDisciplinePackageIds =
            Array.Empty<string>();
        private GameObject _experimentPrefab;
        private string _message = string.Empty;

        public static void Open()
        {
            var window = CreateInstance<CourseCreationWindow>();
            window.titleContent = new GUIContent("新建课程");
            window.minSize = new Vector2(470f, 300f);
            window.maxSize = new Vector2(760f, 460f);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            try
            {
                _availableDisciplinePackageIds =
                    CourseWorkbenchCompiler.FindAvailableDisciplinePackageIds();
                if (_availableDisciplinePackageIds.Count == 1)
                {
                    _disciplinePackageIds = _availableDisciplinePackageIds[0];
                }
            }
            catch (Exception exception)
            {
                _message = "读取可用学科类型失败：" + exception.Message;
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("创建十表课程", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "将创建完整课程职责表、Generated 目录，并把操作者作为第一个对象。实验用品随后从模块目录选择。",
                MessageType.Info);
            _courseId = EditorGUILayout.TextField("课程标识", _courseId);
            _displayName = EditorGUILayout.TextField("显示名称", _displayName);
            DrawDisciplinePackages();
            _actorEntityId = EditorGUILayout.TextField("操作者实体", _actorEntityId);
            _experimentPrefab = (GameObject)EditorGUILayout.ObjectField(
                "实验总预制体",
                _experimentPrefab,
                typeof(GameObject),
                false);

            if (_message.Length > 0)
            {
                EditorGUILayout.HelpBox(_message, MessageType.Error);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("取消", GUILayout.Width(80f))) Close();
            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_courseId));
            if (GUILayout.Button("创建课程", GUILayout.Width(100f))) CreateCourse();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDisciplinePackages()
        {
            EditorGUILayout.LabelField("学科类型", EditorStyles.boldLabel);
            if (_availableDisciplinePackageIds.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "当前项目没有可用的学科配方包。",
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
                if (next) selected.Add(packageId);
                else selected.Remove(packageId);
            }

            _disciplinePackageIds = string.Join(";",
                _availableDisciplinePackageIds.Where(selected.Contains));
        }

        private void CreateCourse()
        {
            try
            {
                var result = new CourseAuthoringDraftCreator().Create(
                    Path.GetFullPath("Assets/VirtualLab/Courses"),
                    _courseId,
                    _displayName,
                    _disciplinePackageIds,
                    AssetDatabase.GetAssetPath(_experimentPrefab),
                    _actorEntityId);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var opened = CourseAuthoringWindow.OpenCourse(
                    ToAssetPath(result.AuthoringDirectory));
                EditorUtility.DisplayDialog(
                    "课程创建完成",
                    opened
                        ? $"已创建并打开课程“{_courseId}”。"
                        : $"已创建课程“{_courseId}”，可稍后从工作台打开。",
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
            return Path.GetFullPath(absolutePath)
                .Substring(projectRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }
    }
}
