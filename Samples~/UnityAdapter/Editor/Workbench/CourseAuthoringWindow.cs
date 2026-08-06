using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Catalogs;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Drafts;
using VirtualLab.Unity.Authoring.Workbench.Panels;
using VirtualLab.UnityAdapters.Input;

namespace VirtualLab.Unity.Authoring.Workbench
{
    /// <summary>
    /// 十表课程工作台。窗口只协调会话、模块目录和六个职责面板，
    /// 具体表结构与可选项分别由草稿协议和模块注册目录提供。
    /// </summary>
    public sealed class CourseAuthoringWindow : EditorWindow
    {
        private const string SelectedCoursePreference =
            "VirtualLab.CourseWorkbench.SelectedCourse";

        private static readonly string[] SectionNames =
            CourseAuthoringSections.Names.ToArray();

        private readonly List<CourseLocation> _courses =
            new List<CourseLocation>();
        private CourseAuthoringCatalog _catalog;
        private CourseAuthoringSession _session;
        private CourseAuthoringWorkflow _workflow;
        private IReadOnlyList<Action> _panelDrawers = Array.Empty<Action>();
        private CourseBlueprintCompilationResult _compilation;
        private CourseDiagnosticTarget _diagnosticTarget;
        private int _selectedCourseIndex = -1;
        private int _selectedSection;
        private Vector2 _scroll;
        private string _message = string.Empty;
        private MessageType _messageType = MessageType.Info;

        [MenuItem("Virtual Lab/课程/课程配置工作台")]
        public static void Open()
        {
            var window = GetWindow<CourseAuthoringWindow>();
            window.titleContent = new GUIContent("课程配置工作台");
            window.minSize = new Vector2(900f, 600f);
            window.Show();
        }

        public static bool OpenCourse(string authoringAssetPath)
        {
            var window = GetWindow<CourseAuthoringWindow>();
            window.titleContent = new GUIContent("课程配置工作台");
            window.minSize = new Vector2(900f, 600f);
            if (!window.ConfirmDiscardIfModified())
            {
                window.Show();
                return false;
            }

            window.RefreshCourses();
            var normalized = NormalizeAssetPath(authoringAssetPath);
            var index = window._courses.FindIndex(value =>
                value.AuthoringAssetPath == normalized);
            if (index < 0)
            {
                window.SetMessage(
                    $"没有找到课程配置目录“{authoringAssetPath}”。",
                    MessageType.Warning);
                window.Show();
                return false;
            }

            window.LoadCourse(index);
            window.Show();
            window.Focus();
            return true;
        }

        private void OnEnable()
        {
            RefreshCatalog();
            RefreshCourses();
            var saved = EditorPrefs.GetString(
                SelectedCoursePreference,
                string.Empty);
            var index = _courses.FindIndex(value =>
                value.AuthoringAssetPath == saved);
            if (index < 0 && _courses.Count > 0) index = 0;
            if (index >= 0) LoadCourse(index);
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (_session == null)
            {
                EditorGUILayout.HelpBox(
                    _courses.Count == 0
                        ? "Assets 中还没有十表课程。请点击“新建课程”。"
                        : "请选择一个课程。",
                    MessageType.Info);
                DrawMessage();
                return;
            }

            EditorGUILayout.Space();
            _selectedSection = GUILayout.Toolbar(
                Math.Min(_selectedSection, SectionNames.Length - 1),
                SectionNames);
            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawDiagnosticTarget();
            _panelDrawers[_selectedSection]();
            EditorGUILayout.EndScrollView();
            DrawMessage();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var names = _courses.Select(value => value.DisplayName).ToArray();
            EditorGUI.BeginDisabledGroup(names.Length == 0);
            var next = EditorGUILayout.Popup(
                Math.Max(0, _selectedCourseIndex),
                names,
                EditorStyles.toolbarPopup,
                GUILayout.Width(240f));
            if (names.Length > 0
                && next != _selectedCourseIndex
                && ConfirmDiscardIfModified())
            {
                LoadCourse(next);
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
            {
                if (ConfirmDiscardIfModified())
                {
                    RefreshCatalog();
                    RefreshCourses();
                    if (_courses.Count > 0)
                    {
                        LoadCourse(Math.Min(Math.Max(_selectedCourseIndex, 0),
                            _courses.Count - 1));
                    }
                }
            }
            if (GUILayout.Button("新建课程", EditorStyles.toolbarButton))
            {
                CourseCreationWindow.Open();
            }

            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(_session == null || !_session.CanUndo);
            if (GUILayout.Button("撤销", EditorStyles.toolbarButton))
            {
                _session.Undo();
                _compilation = null;
                _diagnosticTarget = null;
                _workflow.InvalidateCompiledReview();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_session == null || !_session.CanRedo);
            if (GUILayout.Button("重做", EditorStyles.toolbarButton))
            {
                _session.Redo();
                _compilation = null;
                _diagnosticTarget = null;
                _workflow.InvalidateCompiledReview();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_session == null);
            if (GUILayout.Button("保存", EditorStyles.toolbarButton)) Save();
            if (GUILayout.Button("保存并编译", EditorStyles.toolbarButton)) Compile();
            if (GUILayout.Button("生成课程资产", EditorStyles.toolbarButton)) Build();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void RefreshCatalog()
        {
            try
            {
                var discovery = CourseAuthoringCatalogProviderDiscovery.Discover();
                _catalog = CourseAuthoringCatalog.Create(discovery.Providers);
                var diagnostics = discovery.Diagnostics
                    .Concat(_catalog.Diagnostics)
                    .ToArray();
                if (diagnostics.Length > 0)
                {
                    SetMessage(
                        string.Join("\n", diagnostics.Select(value => value.Reason)),
                        MessageType.Error);
                }
            }
            catch (Exception exception)
            {
                _catalog = null;
                SetMessage("读取模块创作目录失败：" + exception.Message,
                    MessageType.Error);
            }
        }

        private void RefreshCourses()
        {
            _courses.Clear();
            var assets = Path.GetFullPath("Assets");
            if (!Directory.Exists(assets)) return;
            foreach (var courseFile in Directory.EnumerateFiles(
                         assets,
                         CourseAuthoringTableNames.Course,
                         SearchOption.AllDirectories))
            {
                var directory = Path.GetDirectoryName(courseFile);
                if (directory == null
                    || !string.Equals(Path.GetFileName(directory), "Authoring",
                        StringComparison.Ordinal)
                    || CourseAuthoringSchema.Tables.Any(schema =>
                        !File.Exists(Path.Combine(directory, schema.FileName))))
                {
                    continue;
                }

                var assetPath = ToAssetPath(directory);
                _courses.Add(new CourseLocation(
                    assetPath,
                    Path.GetFileName(Path.GetDirectoryName(directory))));
            }

            _courses.Sort((left, right) => string.Compare(
                left.DisplayName,
                right.DisplayName,
                StringComparison.Ordinal));
        }

        private void LoadCourse(int index)
        {
            if (_catalog == null || !_catalog.IsValid || index < 0
                || index >= _courses.Count)
            {
                return;
            }

            try
            {
                var location = _courses[index];
                _session = CourseAuthoringSession.Load(
                    Path.GetFullPath(location.AuthoringAssetPath),
                    _catalog);
                _workflow = new CourseAuthoringWorkflow(_session);
                _panelDrawers = CreatePanels(_workflow);
                _compilation = null;
                _diagnosticTarget = null;
                _selectedCourseIndex = index;
                _scroll = Vector2.zero;
                EditorPrefs.SetString(
                    SelectedCoursePreference,
                    location.AuthoringAssetPath);
                SetMessage($"已打开“{_workflow.Course.DisplayName}”。",
                    MessageType.Info);
            }
            catch (Exception exception)
            {
                _session = null;
                _workflow = null;
                _panelDrawers = Array.Empty<Action>();
                SetMessage("打开课程失败：" + exception.Message,
                    MessageType.Error);
            }
        }

        private IReadOnlyList<Action> CreatePanels(
            CourseAuthoringWorkflow workflow)
        {
            var information = new CourseInformationPanel(workflow);
            var supplies = new CourseSuppliesPanel(workflow);
            var setup = new CourseSetupPanel(workflow);
            var operations = new CourseOperationsPanel(workflow);
            var teaching = new CourseTeachingPanel(workflow);
            var review = new CourseReviewPanel(workflow, NavigateToDiagnostic);
            return new Action[]
            {
                information.Draw,
                supplies.Draw,
                setup.Draw,
                operations.Draw,
                teaching.Draw,
                review.Draw
            };
        }

        private void Save()
        {
            try
            {
                _session.Save();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                SetMessage("十张课程职责表已保存。", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetMessage("保存失败：" + exception.Message, MessageType.Error);
            }
        }

        private void Compile()
        {
            try
            {
                _session.Save();
                var result = new CourseWorkbenchCompiler().Compile(
                    CourseBlueprintSource.FromDirectory(
                        _session.AuthoringDirectory));
                _compilation = result.Compilation;
                var success = _compilation.IsSuccess;
                var executionModesRegistered = success
                    && ExecutionModesAreRegistered(_compilation);
                _workflow.SetCompiledReview(
                    CoursePairingOverview.BuildReview(
                        _compilation.Normalized,
                        _session.Draft.Processes.Count,
                        success,
                        success,
                        executionModesRegistered),
                    _compilation.Diagnostics);
                SetMessage(
                    success
                        ? "课程编译通过。"
                        : string.Join("\n", _compilation.Diagnostics.Select(value =>
                            $"{value.FileName}:{value.Line} {value.Reason}")),
                    success ? MessageType.Info : MessageType.Error);
            }
            catch (Exception exception)
            {
                _compilation = null;
                SetMessage("编译失败：" + exception.Message, MessageType.Error);
            }
        }

        private void Build()
        {
            Compile();
            if (_compilation == null || !_compilation.IsSuccess) return;
            try
            {
                var courseRoot = Path.GetDirectoryName(
                    _courses[_selectedCourseIndex].AuthoringAssetPath
                        .Replace('/', Path.DirectorySeparatorChar));
                var result = new CourseWorkbenchBuildService().Build(
                    NormalizeAssetPath(courseRoot),
                    _compilation);
                SetMessage(
                    result.IsSuccess
                        ? $"课程资产已生成：{result.AssetPath}"
                        : string.Join("\n", result.Diagnostics.Select(value =>
                            value.Reason)),
                    result.IsSuccess ? MessageType.Info : MessageType.Error);
            }
            catch (Exception exception)
            {
                SetMessage("生成失败：" + exception.Message, MessageType.Error);
            }
        }

        private static bool ExecutionModesAreRegistered(
            CourseBlueprintCompilationResult compilation)
        {
            var registered = new HashSet<string>(
                CourseOperationExecutors.CreateDefault()
                    .Select(value => value.ExecutionModeId),
                StringComparer.Ordinal);
            return compilation.Normalized.Actions.All(value =>
                registered.Contains(value.Definition.ExecutionModeId));
        }

        private bool ConfirmDiscardIfModified()
        {
            return _session == null
                   || !_session.IsModified
                   || EditorUtility.DisplayDialog(
                       "尚未保存",
                       "当前课程存在未保存修改。是否放弃这些修改？",
                       "放弃修改",
                       "返回");
        }

        private void SetMessage(string message, MessageType type)
        {
            _message = message ?? string.Empty;
            _messageType = type;
            Repaint();
        }

        private void NavigateToDiagnostic(CourseDiagnosticTarget target)
        {
            if (target == null) return;
            _diagnosticTarget = target;
            _selectedSection = CourseAuthoringSections.IndexForTable(
                target.FileName);
            _scroll = Vector2.zero;
            SetMessage(
                $"已定位到 {DescribeTarget(target)}。",
                MessageType.Info);
        }

        private void DrawDiagnosticTarget()
        {
            if (_diagnosticTarget == null) return;
            EditorGUILayout.HelpBox(
                $"当前修复位置：{DescribeTarget(_diagnosticTarget)}",
                MessageType.Info);
        }

        private static string DescribeTarget(CourseDiagnosticTarget target)
        {
            var location = string.Join(
                " / ",
                new[]
                    {
                        target.FileName,
                        target.ConfigurationId,
                        target.ColumnName
                    }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.IsNullOrWhiteSpace(target.ActionId)
                ? location
                : $"{location}（{target.ActionId}）";
        }

        private void DrawMessage()
        {
            if (_message.Length > 0)
            {
                EditorGUILayout.HelpBox(_message, _messageType);
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
            return NormalizeAssetPath(relative);
        }

        private static string NormalizeAssetPath(string path) =>
            (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');

        private sealed class CourseLocation
        {
            public CourseLocation(string authoringAssetPath, string displayName)
            {
                AuthoringAssetPath = authoringAssetPath;
                DisplayName = displayName;
            }

            public string AuthoringAssetPath { get; }
            public string DisplayName { get; }
        }
    }
}
