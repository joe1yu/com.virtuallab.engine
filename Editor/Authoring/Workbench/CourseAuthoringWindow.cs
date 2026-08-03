using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Csv;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.Unity.Authoring.Recipes;
using VirtualLab.UnityAdapters.Courses;

namespace VirtualLab.Unity.Authoring.Workbench
{
    /// <summary>
    /// 面向课程策划和学科教师的对象中心配置入口。CSV 仍是唯一正式数据源；
    /// 窗口只把分散的配方知识投影成表单，并将修改安全回写到实验对象表。
    /// </summary>
    public sealed class CourseAuthoringWindow : EditorWindow
    {
        private const string ObjectsFile = "实验对象.csv";
        private const string CourseFile = "课程.csv";
        private const string MissingDisciplinePackageDiagnostic =
            "blueprint.discipline-package.missing";
        private const string SelectedCoursePreference =
            "VirtualLab.CourseWorkbench.SelectedCourse";
        private const string GuideExpandedPreference =
            "VirtualLab.CourseWorkbench.GuideExpanded";
        private const float MinimumObjectListWidth = 230f;
        private const float MaximumObjectListWidth = 300f;
        private const float MinimumPreviewWidth = 320f;
        private const float MaximumPreviewWidth = 520f;
        private const float SeparateOverviewMinimumWidth = 1450f;
        private static readonly string[] CoreObjectColumns =
        {
            "实体ID", "显示名称", "Prefab", "特征列表", "初始位置", "初始旋转"
        };
        private static readonly string[] ReferenceSummaryColumns =
        {
            "显示名称", "交互ID", "配置ID", "步骤ID", "覆盖ID", "动作",
            "操作名称", "表现原语", "触发值", "流程ID"
        };

        private readonly List<CourseLocation> _courses =
            new List<CourseLocation>();
        private readonly List<string> _providerMessages = new List<string>();
        private readonly List<string> _availableDisciplinePackageIds =
            new List<string>();
        private readonly CourseEntityReferenceGraph _referenceGraph =
            new CourseEntityReferenceGraph();
        private readonly Stack<CourseDocumentSnapshot> _undo =
            new Stack<CourseDocumentSnapshot>();
        private readonly Stack<CourseDocumentSnapshot> _redo =
            new Stack<CourseDocumentSnapshot>();
        private readonly HashSet<string> _expandedOverviewGroups =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _expandedReferenceGroups =
            new HashSet<string>(StringComparer.Ordinal);

        private CourseDocumentSet _documents;
        private EditableCsvDocument _objects;
        private CourseBlueprintCompilationResult _compilation;
        private CourseFeatureCatalog _featureCatalog;
        private CourseLocation _course;
        private int _selectedCourseIndex = -1;
        private int _selectedObjectIndex = -1;
        private bool _dirty;
        private bool _previewOutdated;
        private bool _hasCurrentGeneratedAsset;
        private bool _guideExpanded = true;
        private bool _courseOverviewExpanded = true;
        private Vector2 _objectScroll;
        private Vector2 _detailScroll;
        private Vector2 _featureScroll;
        private Vector2 _previewScroll;
        private Vector2 _overviewScroll;
        private Vector2 _diagnosticScroll;
        private string _objectSearch = string.Empty;
        private string _featureSearch = string.Empty;
        private string _overviewSearch = string.Empty;
        private string _referenceSearch = string.Empty;
        private string _renameSourceId = string.Empty;
        private string _pendingEntityId = string.Empty;
        private string _lastUndoGroup = string.Empty;
        private double _lastUndoTime;
        private double _previewDueTime;
        private string _operationMessage = string.Empty;
        private MessageType _operationMessageType = MessageType.Info;

        [MenuItem("Virtual Lab/课程/课程配置工作台")]
        public static void Open()
        {
            var window = GetWindow<CourseAuthoringWindow>();
            window.titleContent = new GUIContent("课程配置工作台");
            window.minSize = new Vector2(980f, 620f);
            window.Show();
        }

        /// <summary>
        /// 创建向导完成后直接切换到新课程，避免用户再执行一次“扫描课程”。
        /// 若现有工作台有未保存修改，仍由用户确认是否放弃。
        /// </summary>
        public static bool OpenCourse(string authoringAssetPath)
        {
            var window = GetWindow<CourseAuthoringWindow>();
            window.titleContent = new GUIContent("课程配置工作台");
            window.minSize = new Vector2(980f, 620f);
            if (!window.ConfirmDiscardIfDirty())
            {
                window.Show();
                window.Focus();
                return false;
            }

            window.RefreshCourseList();
            var index = window._courses.FindIndex(value => string.Equals(
                value.AuthoringAssetPath,
                authoringAssetPath,
                StringComparison.Ordinal));
            if (index < 0)
            {
                window.SetOperationMessage(
                    $"新课程已创建，但工作台没有找到配置目录：{authoringAssetPath}",
                    MessageType.Warning);
                window.Show();
                window.Focus();
                return false;
            }

            window.LoadCourse(index);
            window.Show();
            window.Focus();
            return true;
        }

        private void OnEnable()
        {
            EditorApplication.update -= UpdateDeferredPreview;
            EditorApplication.update += UpdateDeferredPreview;
            _guideExpanded = EditorPrefs.GetBool(
                GuideExpandedPreference,
                true);
            RefreshCourseList();
            var savedPath = EditorPrefs.GetString(
                SelectedCoursePreference,
                string.Empty);
            var index = _courses.FindIndex(value => string.Equals(
                value.AuthoringAssetPath,
                savedPath,
                StringComparison.Ordinal));
            if (index < 0 && _courses.Count > 0)
            {
                index = 0;
            }

            if (index >= 0)
            {
                LoadCourse(index);
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateDeferredPreview;
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawGettingStarted();
            if (_course == null || _objects == null)
            {
                DrawNoCourseState();
                DrawOperationMessage();
                return;
            }

            if (_documents.HasExternalChanges())
            {
                EditorGUILayout.HelpBox(
                    _dirty
                        ? "至少一张课程 CSV 已在外部发生变化。为防止覆盖 Excel 内容，当前修改不能直接保存；请先自行记录修改，再点击重新载入。"
                        : "检测到 Excel 或其他程序更新了课程 CSV，请点击重新载入。",
                    MessageType.Warning);
            }

            DrawOperationMessage();
            EditorGUILayout.Space(3f);
            if (_objects.Rows.Count == 0)
            {
                DrawEmptyCourseState();
                DrawDiagnostics();
                return;
            }

            var availableWidth = Mathf.Max(960f, position.width - 8f);
            var objectListWidth = Mathf.Clamp(
                availableWidth * 0.18f,
                MinimumObjectListWidth,
                MaximumObjectListWidth);
            var separateOverview = availableWidth >= SeparateOverviewMinimumWidth;
            var previewWidth = separateOverview
                ? Mathf.Clamp(availableWidth * 0.22f, 320f, 430f)
                : Mathf.Clamp(
                    availableWidth * 0.28f,
                    MinimumPreviewWidth,
                    MaximumPreviewWidth);
            var detailsWidth = separateOverview
                ? Mathf.Clamp(availableWidth * 0.28f, 430f, 560f)
                : Mathf.Max(
                    390f,
                    availableWidth - objectListWidth - previewWidth - 12f);
            EditorGUILayout.BeginHorizontal(
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            DrawObjectList(objectListWidth);
            DrawObjectDetails(detailsWidth);
            DrawPreview(previewWidth);
            if (separateOverview)
            {
                DrawCourseOverviewPanel(true);
            }

            EditorGUILayout.EndHorizontal();
            if (!separateOverview)
            {
                DrawCourseOverviewPanel(false);
            }

            DrawDiagnostics();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("课程", GUILayout.Width(34f));
            var labels = _courses.Select(value => value.DisplayName).ToArray();
            EditorGUI.BeginDisabledGroup(labels.Length == 0);
            var nextIndex = EditorGUILayout.Popup(
                Math.Max(0, _selectedCourseIndex),
                labels,
                EditorStyles.toolbarPopup,
                GUILayout.Width(Mathf.Clamp(position.width * 0.22f, 260f, 420f)));
            EditorGUI.EndDisabledGroup();
            if (labels.Length > 0
                && nextIndex != _selectedCourseIndex
                && ConfirmDiscardIfDirty())
            {
                LoadCourse(nextIndex);
            }

            if (GUILayout.Button("扫描课程", EditorStyles.toolbarButton))
            {
                var currentPath = _course?.AuthoringAssetPath;
                RefreshCourseList();
                var currentIndex = _courses.FindIndex(value => string.Equals(
                    value.AuthoringAssetPath,
                    currentPath,
                    StringComparison.Ordinal));
                if (currentIndex >= 0)
                {
                    _selectedCourseIndex = currentIndex;
                }
            }

            if (GUILayout.Button("新建课程", EditorStyles.toolbarButton))
            {
                CourseCreationWindow.Open();
            }

            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(_objects == null);
            EditorGUI.BeginDisabledGroup(_undo.Count == 0);
            if (GUILayout.Button("撤销", EditorStyles.toolbarButton))
            {
                UndoEdit();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_redo.Count == 0);
            if (GUILayout.Button("重做", EditorStyles.toolbarButton))
            {
                RedoEdit();
            }

            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("重新载入", EditorStyles.toolbarButton)
                && ConfirmDiscardIfDirty())
            {
                LoadCourse(_selectedCourseIndex);
            }

            if (GUILayout.Button(
                    _previewOutdated ? "刷新检查 *" : "刷新检查",
                    EditorStyles.toolbarButton))
            {
                CompilePreview();
            }

            if (GUILayout.Button(
                    _dirty ? "仅保存 CSV *" : "仅保存 CSV",
                    EditorStyles.toolbarButton))
            {
                SaveCourse();
            }

            if (GUILayout.Button(
                    _dirty ? "保存并生成课程 *" : "生成课程",
                    EditorStyles.toolbarButton))
            {
                BuildCourse();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGettingStarted()
        {
            var guide = EvaluateGuide();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(
                $"课程制作向导　已完成 {guide.CompletedStepCount}/{guide.Steps.Count}",
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    _guideExpanded ? "收起说明" : "展开说明",
                    EditorStyles.miniButton,
                    GUILayout.Width(72f)))
            {
                _guideExpanded = !_guideExpanded;
                EditorPrefs.SetBool(GuideExpandedPreference, _guideExpanded);
            }

            EditorGUILayout.EndHorizontal();

            if (_guideExpanded)
            {
                var stepWidth = Mathf.Max(
                    145f,
                    (position.width - 28f - (guide.Steps.Count - 1) * 3f)
                    / guide.Steps.Count);
                EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
                foreach (var step in guide.Steps)
                {
                    DrawGuideStep(step, stepWidth);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "怎么使用：左侧添加或选择对象 → 中间指定模型预制体、勾选能力并填写参数 → 右侧确认生成结果 → 修正底部问题 → 生成课程。",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "普通课程不需要直接编辑自动生成的动作、规则和表现；只有默认行为不满足要求时，才在关联课程表中填写例外。",
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("下一步：" + guide.Recommendation, EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();
            DrawRecommendedAction(guide.RecommendedAction);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void DrawGuideStep(
            CourseWorkbenchGuideStep step,
            float width)
        {
            var prefix = step.Status == CourseWorkbenchStepStatus.Complete
                ? "✓ "
                : step.Status == CourseWorkbenchStepStatus.NeedsAttention
                    ? "! "
                    : step.Status == CourseWorkbenchStepStatus.Current
                        ? "→ "
                        : "· ";
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Width(width));
            GUILayout.Label(prefix + step.Title, EditorStyles.miniBoldLabel);
            GUILayout.Label(
                step.Description,
                EditorStyles.wordWrappedMiniLabel,
                GUILayout.MinHeight(28f));
            EditorGUILayout.EndVertical();
        }

        private void DrawRecommendedAction(
            CourseWorkbenchRecommendedAction action)
        {
            switch (action)
            {
                case CourseWorkbenchRecommendedAction.CreateCourse:
                    if (GUILayout.Button("新建课程", GUILayout.Width(120f)))
                    {
                        CourseCreationWindow.Open();
                    }
                    break;

                case CourseWorkbenchRecommendedAction.AddObject:
                    if (GUILayout.Button("添加第一个对象", GUILayout.Width(120f)))
                    {
                        AddObject();
                    }
                    break;

                case CourseWorkbenchRecommendedAction.CompleteObject:
                    if (GUILayout.Button("定位待补全对象", GUILayout.Width(120f)))
                    {
                        SelectFirstIncompleteObject();
                    }
                    break;

                case CourseWorkbenchRecommendedAction.ReviewDiagnostics:
                    if (GUILayout.Button("处理第一个问题", GUILayout.Width(120f)))
                    {
                        FocusFirstDiagnostic();
                    }
                    break;

                case CourseWorkbenchRecommendedAction.ReloadCourse:
                    if (GUILayout.Button("重新载入课程", GUILayout.Width(120f))
                        && ConfirmDiscardIfDirty())
                    {
                        LoadCourse(_selectedCourseIndex);
                    }
                    break;

                case CourseWorkbenchRecommendedAction.SaveAndBuild:
                    if (GUILayout.Button(
                            _dirty ? "保存并生成课程" : "生成课程",
                            GUILayout.Width(120f)))
                    {
                        BuildCourse();
                    }
                    break;
            }
        }

        private void DrawNoCourseState()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(12f);
            GUILayout.Label(
                _courses.Count == 0 ? "还没有可配置的课程" : "当前课程无法载入",
                EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                _courses.Count == 0
                    ? "点击“新建课程”，填写课程名称和学科配方包。工作台会自动创建所需目录与两张基础 CSV。"
                    : "请查看窗口中的错误提示，修正课程 CSV 后点击上方“扫描课程”或重新选择课程。",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8f);
            if (_courses.Count == 0 && GUILayout.Button(
                    "新建第一门课程",
                    GUILayout.Height(30f),
                    GUILayout.Width(180f)))
            {
                CourseCreationWindow.Open();
            }

            GUILayout.Space(12f);
            EditorGUILayout.EndVertical();
        }

        private void DrawEmptyCourseState()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(14f);
            GUILayout.Label("这门课程还没有实验对象", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "每个可操作器材、试剂、容器或实验参与者都是一个对象。先添加对象，再选择已经制作好的模型预制体；勾选“可抓取”“可加热”等能力后，工作台会自动生成标准操作。",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8f);
            if (GUILayout.Button(
                    "添加第一个实验对象",
                    GUILayout.Height(32f),
                    GUILayout.Width(190f)))
            {
                AddObject();
            }

            GUILayout.Space(14f);
            EditorGUILayout.EndVertical();
        }

        private void DrawObjectList(float width)
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Width(width),
                GUILayout.ExpandHeight(true));
            GUILayout.Label($"① 实验对象（{_objects.Rows.Count}）", EditorStyles.boldLabel);
            _objectSearch = EditorGUILayout.TextField(
                new GUIContent("搜索"),
                _objectSearch);
            _objectScroll = EditorGUILayout.BeginScrollView(_objectScroll);
            for (var index = 0; index < _objects.Rows.Count; index++)
            {
                var row = _objects.Rows[index];
                var entityId = row["实体ID"];
                var displayName = row["显示名称"];
                if (!MatchesSearch(_objectSearch, entityId, displayName))
                {
                    continue;
                }

                var selected = index == _selectedObjectIndex;
                var label = string.IsNullOrWhiteSpace(displayName)
                    ? entityId
                    : displayName + (displayName == entityId ? string.Empty : $"  [{entityId}]");
                if (GUILayout.Toggle(selected, label, "Button") && !selected)
                {
                    _selectedObjectIndex = index;
                    _detailScroll = Vector2.zero;
                    _referenceSearch = string.Empty;
                    GUI.FocusControl(null);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加对象"))
            {
                AddObject();
            }

            EditorGUI.BeginDisabledGroup(!HasSelectedObject());
            if (GUILayout.Button("删除对象"))
            {
                DeleteSelectedObject();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawObjectDetails(float width)
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Width(width),
                GUILayout.ExpandHeight(true));
            GUILayout.Label("② 编辑选中对象", EditorStyles.boldLabel);
            if (!HasSelectedObject())
            {
                EditorGUILayout.HelpBox("从左侧选择一个实验对象。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var row = _objects.Rows[_selectedObjectIndex];
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            DrawEntityId(row);
            DrawActorSelection(row);
            DrawTextCell(row, "显示名称", "面向教师和学生显示的自然中文名称。", true);
            DrawPrefabCell(row);
            DrawVectorCell(row, "初始位置");
            DrawVectorCell(row, "初始旋转");
            EditorGUILayout.Space(6f);
            DrawFeatures(row);
            EditorGUILayout.Space(6f);
            DrawParameters(row);
            EditorGUILayout.Space(6f);
            DrawPairingOverview(row);
            EditorGUILayout.Space(6f);
            DrawRelatedCourseConfiguration(row);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawActorSelection(EditableCsvRow objectRow)
        {
            if (!_documents.TryGetDocument(CourseFile, out var courseDocument)
                || courseDocument.Rows.Count != 1
                || !courseDocument.Headers.Contains("操作者实体ID"))
            {
                return;
            }

            var courseRow = courseDocument.Rows[0];
            var entityId = objectRow["实体ID"];
            var actorId = courseRow["操作者实体ID"];
            if (string.Equals(entityId, actorId, StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox(
                    "当前对象是课程操作者，鼠标、触控和 VR 输入都将以它的身份提交命令。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(actorId)
                    ? "当前未指定课程操作者"
                    : "当前课程操作者：" + actorId,
                EditorStyles.miniLabel);
            if (GUILayout.Button("设为课程操作者", GUILayout.Width(120f)))
            {
                ApplyDocumentEdit(
                    "设置课程操作者",
                    false,
                    () => courseRow["操作者实体ID"] = entityId);
                SetOperationMessage(
                    $"已把“{entityId}”设为课程操作者。请点击“保存并生成课程”。",
                    MessageType.Info);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEntityId(EditableCsvRow row)
        {
            var currentId = row["实体ID"];
            if (!string.Equals(
                    _renameSourceId,
                    currentId,
                    StringComparison.Ordinal))
            {
                _renameSourceId = currentId;
                _pendingEntityId = currentId;
            }

            EditorGUILayout.BeginHorizontal();
            _pendingEntityId = EditorGUILayout.TextField(
                new GUIContent(
                    "对象 ID",
                    "对象的稳定编号。点击应用后，工作台会同步更新整套课程表中的对象引用。"),
                _pendingEntityId);
            EditorGUI.BeginDisabledGroup(
                string.Equals(currentId, _pendingEntityId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(_pendingEntityId));
            if (GUILayout.Button("应用重命名", GUILayout.Width(90f)))
            {
                RenameSelectedEntity(currentId, _pendingEntityId);
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPrefabCell(EditableCsvRow row)
        {
            var currentPath = row["Prefab"];
            var current = AssetDatabase.LoadAssetAtPath<GameObject>(currentPath);
            EditorGUI.BeginChangeCheck();
            var selected = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("模型预制体", "该实验对象使用的 Unity 预制体资源。"),
                current,
                typeof(GameObject),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                SetCell(row, "Prefab", AssetDatabase.GetAssetPath(selected));
            }

            if (current == null && !string.IsNullOrWhiteSpace(currentPath))
            {
                EditorGUILayout.HelpBox(
                    $"当前路径无法载入模型预制体：{currentPath}",
                    MessageType.Error);
                DrawTextCell(row, "Prefab", "可直接修正资产路径。", false);
            }
        }

        private void DrawVectorCell(EditableCsvRow row, string column)
        {
            var raw = row[column];
            if (!TryParseVector(raw, out var vector))
            {
                EditorGUILayout.HelpBox(
                    $"{column} 必须使用 X|Y|Z，例如 0|1|0。",
                    MessageType.Error);
                DrawTextCell(row, column, "X|Y|Z", false);
                return;
            }

            EditorGUI.BeginChangeCheck();
            var edited = EditorGUILayout.Vector3Field(
                CourseWorkbenchDisplayNames.Field(column),
                vector);
            if (EditorGUI.EndChangeCheck())
            {
                SetCell(row, column, FormatVector(edited));
            }
        }

        private void DrawFeatures(EditableCsvRow row)
        {
            GUILayout.Label("对象能力与交互", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "交互能力会自动生成可执行操作、执行条件、操作结果、画面提示和预制体要求；基础能力只供实验规则识别，不会单独生成操作。",
                MessageType.None);
            _featureSearch = EditorGUILayout.TextField("搜索能力", _featureSearch);
            var selected = ParseList(row["特征列表"]);
            var known = _featureCatalog?.Features
                        ?? Array.Empty<CourseFeatureDescriptor>();
            _featureScroll = EditorGUILayout.BeginScrollView(
                _featureScroll,
                GUILayout.MinHeight(130f),
                GUILayout.MaxHeight(220f));
            var visibleFeatures = known.Where(feature => MatchesSearch(
                    _featureSearch,
                    feature.FeatureId,
                    string.Join(";", feature.RecipeIds)))
                .ToArray();
            var columnCount = position.width >= 1650f
                ? 3
                : position.width >= 1250f
                    ? 2
                    : 1;
            for (var index = 0; index < visibleFeatures.Length; index += columnCount)
            {
                EditorGUILayout.BeginHorizontal();
                for (var column = 0; column < columnCount; column++)
                {
                    var itemIndex = index + column;
                    if (itemIndex >= visibleFeatures.Length)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    var feature = visibleFeatures[itemIndex];
                    var enabled = selected.Contains(feature.FeatureId);
                    var displayName = CourseWorkbenchDisplayNames.Feature(
                        feature.FeatureId);
                    var tooltip = feature.IsCapabilityOnly
                        ? "基础能力；供实验规则和学科过程识别，不会单独生成操作。"
                        : "自动生成来源：" + string.Join("、", feature.RecipeIds);
                    if (!string.Equals(
                            displayName,
                            feature.FeatureId,
                            StringComparison.Ordinal))
                    {
                        tooltip = $"配置标签：{feature.FeatureId}。" + tooltip;
                    }

                    var next = EditorGUILayout.ToggleLeft(
                        new GUIContent(displayName, tooltip),
                        enabled,
                        GUILayout.MinWidth(120f),
                        GUILayout.ExpandWidth(true));
                    if (next != enabled)
                    {
                        SetFeature(row, feature, next);
                        selected = ParseList(row["特征列表"]);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            var unknown = selected
                .Where(value => _featureCatalog == null
                                || !_featureCatalog.TryGet(value, out _))
                .ToArray();
            if (unknown.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    "工作台暂不认识这些能力标签，将原样保留："
                    + string.Join("、", unknown),
                    MessageType.Warning);
            }
        }

        private void DrawParameters(EditableCsvRow row)
        {
            GUILayout.Label("能力参数", EditorStyles.boldLabel);
            var selectedFeatures = ParseList(row["特征列表"]);
            var descriptors = selectedFeatures
                .Select(feature =>
                    _featureCatalog != null
                    && _featureCatalog.TryGet(feature, out var descriptor)
                        ? descriptor
                        : null)
                .Where(value => value != null)
                .SelectMany(value => value.Parameters)
                .GroupBy(value => value.ColumnName, StringComparer.Ordinal)
                .ToDictionary(
                    value => value.Key,
                    value => value.First(),
                    StringComparer.Ordinal);
            var columns = _objects.Headers
                .Where(value => value.StartsWith("参数.", StringComparison.Ordinal))
                .Where(value => descriptors.ContainsKey(value)
                                || !string.IsNullOrWhiteSpace(row[value]))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (columns.Length == 0)
            {
                EditorGUILayout.LabelField("当前特征不需要额外参数。", EditorStyles.miniLabel);
                return;
            }

            if (columns.Any(column =>
                    column.IndexOf("兼容组", StringComparison.Ordinal) >= 0
                    || column.IndexOf("作用组", StringComparison.Ordinal) >= 0))
            {
                EditorGUILayout.HelpBox(
                    "配对标签用于决定两个对象能否自动配合：两边至少有一个相同标签时，系统才会生成连接、放置、倾倒等操作。多个标签请用半角分号（;）分隔。",
                    MessageType.None);
            }

            foreach (var column in columns)
            {
                descriptors.TryGetValue(column, out var descriptor);
                var label = CourseWorkbenchDisplayNames.Parameter(
                    column.Substring("参数.".Length));
                var tooltip = descriptor == null
                    ? "该参数来自现有 CSV，工作台会继续保留。"
                    : descriptor.Purpose
                      + (string.IsNullOrWhiteSpace(descriptor.Unit)
                          ? string.Empty
                          : $"；单位：{descriptor.Unit}")
                      + (descriptor.IsRequired ? "；必填" : string.Empty);
                EditorGUI.BeginChangeCheck();
                var value = DrawParameterField(
                    new GUIContent(label, tooltip),
                    row[column],
                    descriptor);
                if (EditorGUI.EndChangeCheck())
                {
                    SetCell(row, column, value);
                }

                DrawParameterValidation(row[column], descriptor);
            }
        }

        private void DrawPairingOverview(EditableCsvRow row)
        {
            GUILayout.Label("可配对对象", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这里展示当前能力和配对标签实际生成的双对象操作。结果直接来自编译后的配方，不需要另外维护配对清单。",
                MessageType.None);

            if (_previewOutdated)
            {
                EditorGUILayout.LabelField(
                    "正在根据最新配置重新计算配对关系……",
                    EditorStyles.miniLabel);
                return;
            }

            if (_compilation == null || !_compilation.IsSuccess)
            {
                EditorGUILayout.LabelField(
                    "配置通过检查后，这里会列出能够配合的对象。",
                    EditorStyles.miniLabel);
                return;
            }

            var entityId = row["实体ID"];
            var pairings = CoursePairingOverview.Build(
                _compilation.Normalized.Actions,
                entityId);
            if (pairings.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "当前没有生成可配对对象。如果原本应该存在，请检查双方是否分别选择了来源能力和目标能力，并确认配对标签至少有一个相同值。",
                    MessageType.Info);
                return;
            }

            foreach (var pairing in pairings)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    ObjectDisplayName(pairing.OtherEntityId),
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "配合方向："
                    + (pairing.SelectedIsSource
                        ? $"{ObjectDisplayName(entityId)} → "
                          + ObjectDisplayName(pairing.OtherEntityId)
                        : $"{ObjectDisplayName(pairing.OtherEntityId)} → "
                          + ObjectDisplayName(entityId)),
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "相关操作："
                    + string.Join(
                        "、",
                        pairing.Operations.Select(PairingOperationText)),
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawRelatedCourseConfiguration(EditableCsvRow row)
        {
            GUILayout.Label("相关课程步骤与规则", EditorStyles.boldLabel);
            var references = _referenceGraph.FindExternalReferences(
                _documents,
                row["实体ID"]);
            if (references.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "当前对象没有交互特例、学科过程、实验流程或表现引用。",
                    EditorStyles.miniLabel);
                return;
            }

            var entityId = row["实体ID"];
            var groups = references
                .GroupBy(value => value.FileName, StringComparer.Ordinal)
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .ToArray();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"共 {references.Count} 处引用，分布在 {groups.Length} 类配置中。重命名对象时会同步更新。",
                EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("筛选", GUILayout.Width(30f));
            _referenceSearch = EditorGUILayout.TextField(
                _referenceSearch,
                EditorStyles.toolbarSearchField);
            if (GUILayout.Button("全部展开", EditorStyles.miniButton, GUILayout.Width(66f)))
            {
                foreach (var group in groups)
                {
                    _expandedReferenceGroups.Add(
                        ReferenceGroupKey(entityId, group.Key));
                }
            }

            if (GUILayout.Button("全部收起", EditorStyles.miniButton, GUILayout.Width(66f)))
            {
                foreach (var group in groups)
                {
                    _expandedReferenceGroups.Remove(
                        ReferenceGroupKey(entityId, group.Key));
                }
            }

            EditorGUILayout.EndHorizontal();

            var visibleGroupCount = 0;
            foreach (var group in groups)
            {
                var displayName = CourseWorkbenchDisplayNames.ReferenceFile(group.Key);
                var groupMatches = MatchesSearch(
                    _referenceSearch,
                    group.Key,
                    displayName);
                var visibleReferences = (groupMatches
                        ? group
                        : group.Where(reference => ReferenceMatchesSearch(
                            reference,
                            _referenceSearch)))
                    .ToArray();
                if (visibleReferences.Length == 0)
                {
                    continue;
                }

                visibleGroupCount++;
                DrawReferenceGroup(
                    entityId,
                    group.Key,
                    displayName,
                    group.Count(),
                    visibleReferences);
            }

            if (visibleGroupCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有找到匹配的课程步骤或规则。",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawReferenceGroup(
            string entityId,
            string fileName,
            string displayName,
            int totalCount,
            IReadOnlyList<CourseEntityReference> visibleReferences)
        {
            var key = ReferenceGroupKey(entityId, fileName);
            var searching = !string.IsNullOrWhiteSpace(_referenceSearch);
            var expanded = searching || _expandedReferenceGroups.Contains(key);
            var countText = visibleReferences.Count == totalCount
                ? totalCount.ToString(CultureInfo.InvariantCulture)
                : $"{visibleReferences.Count}/{totalCount}";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            var requested = EditorGUILayout.Foldout(
                expanded,
                $"{displayName}（{countText}）",
                true);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                fileName,
                EditorStyles.miniLabel,
                GUILayout.Width(90f));
            if (GUILayout.Button(
                    "打开表格",
                    EditorStyles.miniButton,
                    GUILayout.Width(58f)))
            {
                OpenCourseFile(fileName, 1);
            }

            EditorGUILayout.EndHorizontal();
            if (!searching)
            {
                if (requested)
                {
                    _expandedReferenceGroups.Add(key);
                }
                else
                {
                    _expandedReferenceGroups.Remove(key);
                }

                expanded = requested;
            }

            if (expanded)
            {
                foreach (var reference in visibleReferences)
                {
                    DrawReferenceRow(reference);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawReferenceRow(CourseEntityReference reference)
        {
            var role = CourseWorkbenchDisplayNames.ReferenceRole(
                reference.ColumnName);
            var summary = ReferenceSummary(reference);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"第 {reference.CsvLine} 行",
                EditorStyles.miniLabel,
                GUILayout.Width(52f));
            EditorGUILayout.LabelField(
                new GUIContent(role, $"CSV 字段：{reference.ColumnName}"),
                EditorStyles.miniLabel,
                GUILayout.Width(88f));
            EditorGUILayout.LabelField(
                new GUIContent(summary, summary),
                EditorStyles.miniLabel,
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button(
                    "定位",
                    EditorStyles.miniButton,
                    GUILayout.Width(42f)))
            {
                OpenCourseFile(reference.FileName, reference.CsvLine);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string ReferenceSummary(CourseEntityReference reference)
        {
            foreach (var column in ReferenceSummaryColumns)
            {
                var value = reference.Row[column].Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                return CourseWorkbenchDisplayNames.ConfiguredItem(value);
            }

            return "未填写说明";
        }

        private static bool ReferenceMatchesSearch(
            CourseEntityReference reference,
            string search) =>
            MatchesSearch(
                search,
                reference.FileName,
                reference.ColumnName,
                CourseWorkbenchDisplayNames.ReferenceFile(reference.FileName),
                CourseWorkbenchDisplayNames.ReferenceRole(reference.ColumnName),
                ReferenceSummary(reference),
                reference.CsvLine.ToString(CultureInfo.InvariantCulture))
            || reference.Row.Values.Values.Any(value => MatchesSearch(search, value));

        private static string ReferenceGroupKey(string entityId, string fileName) =>
            (entityId ?? string.Empty) + "|" + (fileName ?? string.Empty);

        private string DrawParameterField(
            GUIContent label,
            string rawValue,
            CourseFeatureParameterDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return EditorGUILayout.TextField(label, rawValue);
            }

            switch (descriptor.Type)
            {
                case RecipeParameterType.Boolean:
                    if (string.IsNullOrWhiteSpace(rawValue)
                        || rawValue == "是"
                        || rawValue == "否")
                    {
                        var values = new[] { "", "是", "否" };
                        var labels = new[] { "（未设置）", "是", "否" };
                        var index = Math.Max(0, Array.IndexOf(values, rawValue));
                        return values[EditorGUILayout.Popup(label, index, labels)];
                    }

                    return EditorGUILayout.TextField(label, rawValue);

                case RecipeParameterType.Integer:
                    if (int.TryParse(
                            rawValue,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var integer))
                    {
                        return EditorGUILayout.IntField(label, integer)
                            .ToString(CultureInfo.InvariantCulture);
                    }

                    return EditorGUILayout.TextField(label, rawValue);

                case RecipeParameterType.Number:
                    if (double.TryParse(
                            rawValue,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var number))
                    {
                        return EditorGUILayout.DoubleField(label, number)
                            .ToString("0.############", CultureInfo.InvariantCulture);
                    }

                    return EditorGUILayout.TextField(label, rawValue);

                case RecipeParameterType.EntityId:
                    return DrawChoiceField(
                        label,
                        rawValue,
                        _objects.Rows.Select(value => value["实体ID"]));

                case RecipeParameterType.PortId:
                    return DrawChoiceField(
                        label,
                        rawValue,
                        _compilation?.Normalized?.Ports
                            .Select(value => value.Definition.PortId)
                        ?? Array.Empty<string>());

                default:
                    return EditorGUILayout.TextField(label, rawValue);
            }
        }

        private static string DrawChoiceField(
            GUIContent label,
            string rawValue,
            IEnumerable<string> available)
        {
            var options = new[] { string.Empty }
                .Concat(available ?? Array.Empty<string>())
                .Where(value => value != null)
                .Append(rawValue ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => string.IsNullOrEmpty(value) ? 0 : 1)
                .ThenBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var index = Array.IndexOf(options, rawValue ?? string.Empty);
            return options[EditorGUILayout.Popup(label, Math.Max(0, index), options)];
        }

        private static void DrawParameterValidation(
            string rawValue,
            CourseFeatureParameterDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return;
            }

            if (descriptor.IsRequired && string.IsNullOrWhiteSpace(rawValue))
            {
                EditorGUILayout.HelpBox("该参数必填。", MessageType.Error);
                return;
            }

            if (descriptor.Type == RecipeParameterType.Boolean
                && !string.IsNullOrWhiteSpace(rawValue)
                && rawValue != "是"
                && rawValue != "否")
            {
                EditorGUILayout.HelpBox("布尔参数必须填写“是”或“否”。", MessageType.Error);
                return;
            }

            if (descriptor.Type != RecipeParameterType.Integer
                && descriptor.Type != RecipeParameterType.Number)
            {
                return;
            }

            if (!double.TryParse(
                    rawValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number))
            {
                if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    EditorGUILayout.HelpBox("参数必须是有效数字。", MessageType.Error);
                }

                return;
            }

            if (descriptor.Minimum.HasValue && number < descriptor.Minimum.Value)
            {
                EditorGUILayout.HelpBox(
                    $"参数不能小于 {descriptor.Minimum.Value.ToString(CultureInfo.InvariantCulture)}。",
                    MessageType.Error);
            }
            else if (descriptor.Maximum.HasValue && number > descriptor.Maximum.Value)
            {
                EditorGUILayout.HelpBox(
                    $"参数不能大于 {descriptor.Maximum.Value.ToString(CultureInfo.InvariantCulture)}。",
                    MessageType.Error);
            }
        }

        private void DrawPreview(float width)
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Width(width),
                GUILayout.ExpandHeight(true));
            GUILayout.Label(
                _previewOutdated ? "③ 生成结果（待检查）" : "③ 生成结果",
                EditorStyles.boldLabel);
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll);
            if (_compilation == null)
            {
                EditorGUILayout.HelpBox("尚未得到编译预览。", MessageType.Info);
                DrawProviderMessages();
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            if (_compilation.Summary != null)
            {
                var summary = _compilation.Summary;
                EditorGUILayout.LabelField("实验对象", summary.ObjectCount.ToString());
                EditorGUILayout.LabelField(
                    "自动生成操作",
                    summary.AutomaticInteractionCount.ToString());
                EditorGUILayout.LabelField(
                    "特殊规则",
                    summary.SpecialInteractionCount.ToString());
                EditorGUILayout.LabelField("课程自定义", summary.OverrideCount.ToString());
            }

            if (!_compilation.IsSuccess)
            {
                EditorGUILayout.HelpBox(
                    "课程配置还有问题，请按照下方“配置问题”中的解决方法处理。",
                    MessageType.Error);
                DrawProviderMessages();
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var model = _compilation.Normalized;
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("生成内容统计", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("可执行操作", model.Actions.Count.ToString());
            EditorGUILayout.LabelField("执行条件", model.Rules.Count.ToString());
            EditorGUILayout.LabelField("操作结果", model.StateChanges.Count.ToString());
            EditorGUILayout.LabelField("画面与提示", model.PresentationEffects.Count.ToString());

            if (HasSelectedObject())
            {
                var entityId = _objects.Rows[_selectedObjectIndex]["实体ID"];
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField($"当前对象：{entityId}", EditorStyles.boldLabel);
                var ports = model.Ports
                    .Where(value => value.Definition.EntityId == entityId)
                    .Select(value => value.Definition);
                DrawPreviewGroup(
                    "连接点",
                    ports.Select(value =>
                        $"{value.PortId}  [连接标签：{value.CompatibilityGroup}]"));

                var disciplineContents = model.GeneratedArtifacts
                    .SelectMany(value => value.Definition.OverviewEntries)
                    .Where(value => string.Equals(
                        value.SubjectId,
                        entityId,
                        StringComparison.Ordinal));
                DrawPreviewGroup(
                    "对象内容",
                    disciplineContents.Select(value =>
                        value.DisplayName
                        + (value.Fields.Count == 0
                            ? string.Empty
                            : "｜" + string.Join(
                                "｜",
                                value.Fields.Select(field =>
                                    field.Key + "：" + field.Value)))));

                var pairings = CoursePairingOverview.Build(
                    model.Actions,
                    entityId);
                DrawPreviewGroup(
                    "可配对对象",
                    pairings.Select(FormatPairingSummary));

                var ownActions = model.Actions.Where(value =>
                    value.Definition.SourceEntityId == entityId
                    && string.IsNullOrWhiteSpace(
                        value.Definition.TargetEntityId));
                DrawPreviewGroup(
                    "对象自身操作",
                    ownActions.Select(value =>
                        CourseWorkbenchDisplayNames.ConfiguredItem(
                            value.Identity.LocalKey)
                        + (string.Equals(
                            value.Definition.PolicyEffect,
                            "允许",
                            StringComparison.Ordinal)
                            ? string.Empty
                            : $"（{value.Definition.PolicyEffect}）")));

                var effects = model.PresentationEffects
                    .Where(value => value.Provenance.Sources.Any(source =>
                        source.ConfigurationId == entityId));
                DrawPreviewGroup(
                    "画面与提示",
                    effects.Select(value =>
                        CourseWorkbenchDisplayNames.ConfiguredItem(
                            value.Identity.LocalKey)
                        + "："
                        + DisplayEndpoint(value.Definition.SubjectId)));
            }

            DrawProviderMessages();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCourseOverviewPanel(bool useRemainingWidth)
        {
            var options = useRemainingWidth
                ? new[]
                {
                    GUILayout.MinWidth(380f),
                    GUILayout.ExpandWidth(true),
                    GUILayout.ExpandHeight(true)
                }
                : new[]
                {
                    GUILayout.Height(_courseOverviewExpanded ? 230f : 34f),
                    GUILayout.ExpandWidth(true)
                };
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, options);
            _courseOverviewExpanded = EditorGUILayout.Foldout(
                _courseOverviewExpanded,
                "④ 全课程数据总览",
                true);
            if (!_courseOverviewExpanded)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            if (_compilation == null || !_compilation.IsSuccess)
            {
                EditorGUILayout.HelpBox(
                    "配置通过检查后，这里会按类别展示全课程生成数据。",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _overviewSearch = EditorGUILayout.TextField(
                new GUIContent("搜索", "同时搜索所有类别中的名称、对象和参数。"),
                _overviewSearch);
            _overviewScroll = EditorGUILayout.BeginScrollView(_overviewScroll);
            DrawCourseOverview(_compilation.Normalized);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCourseOverview(NormalizedCourseModel model)
        {
            DrawOverviewGroup(
                "对象",
                model.Entities.Select(value =>
                {
                    var definition = value.Definition;
                    var features = definition.FeatureIds.Count == 0
                        ? "无已声明能力"
                        : string.Join(
                            "、",
                            definition.FeatureIds.Select(
                                CourseWorkbenchDisplayNames.Feature));
                    return $"{definition.EntityId}｜能力：{features}";
                }));
            DrawOverviewGroup(
                "连接点",
                model.Ports.Select(value =>
                    $"{value.Definition.PortId}｜对象：{value.Definition.EntityId}"
                    + $"｜连接标签：{value.Definition.CompatibilityGroup}"));
            DrawOverviewGroup(
                "可执行操作",
                model.Actions.Select(value =>
                {
                    var definition = value.Definition;
                    return $"{CourseWorkbenchDisplayNames.ConfiguredItem(value.Identity.LocalKey)}｜"
                           + $"{DisplayEndpoint(definition.SourceEntityId)} → "
                           + $"{DisplayEndpoint(definition.TargetEntityId)}"
                           + $"｜{definition.PolicyEffect}｜优先级 {definition.Priority}";
                }));
            DrawOverviewGroup(
                "执行条件",
                model.Rules.Select(value =>
                {
                    var definition = value.Definition;
                    return $"{definition.RuleId}｜"
                           + $"{CourseWorkbenchDisplayNames.FactField(definition.FieldId)} "
                           + $"{CourseWorkbenchDisplayNames.Operator(definition.OperatorId)} "
                           + $"{definition.ExpectedValue}"
                           + (string.IsNullOrWhiteSpace(definition.UnitId)
                               ? string.Empty
                               : " " + definition.UnitId);
                }));
            DrawOverviewGroup(
                "操作结果",
                model.StateChanges.Select(value =>
                {
                    var definition = value.Definition;
                    var parameters = definition.Parameters.Count == 0
                        ? string.Empty
                        : "｜参数：" + string.Join(
                            "；",
                            definition.Parameters.Select(parameter =>
                                parameter.Key + "=" + parameter.Value));
                    return $"{CourseWorkbenchDisplayNames.ConfiguredItem(value.Identity.LocalKey)}"
                           + parameters;
                }));
            DrawOverviewGroup(
                "实验事件",
                model.DomainEvents.Select(value =>
                    $"{value.Definition.EventId}｜类型：{value.Definition.EventType}"));
            DrawOverviewGroup(
                "持续画面状态",
                model.PresentationStates.Select(value =>
                    $"{value.Definition.StateId}｜对象："
                    + DisplayEndpoint(value.Definition.SubjectEntityId)));
            DrawOverviewGroup(
                "画面与提示",
                model.PresentationEffects.Select(value =>
                {
                    var definition = value.Definition;
                    return CourseWorkbenchDisplayNames.ConfiguredItem(
                               value.Identity.LocalKey)
                           + $"｜对象：{DisplayEndpoint(definition.SubjectId)}"
                           + (definition.TriggerKind.HasValue
                               ? "｜触发："
                                 + CourseWorkbenchDisplayNames.Trigger(
                                     definition.TriggerKind.Value.ToString())
                                 + " " + definition.TriggerValue
                               : string.Empty);
                }));
            DrawOverviewGroup(
                "教学目标与评分",
                model.Evaluations.Select(value =>
                {
                    var definition = value.Definition;
                    return $"{definition.EvaluationId}｜{definition.EvaluationType}"
                           + $"｜{definition.DisplayName}｜顺序 {definition.Order}"
                           + (definition.ScoreDelta == 0
                               ? string.Empty
                               : $"｜分值 {definition.ScoreDelta:+#;-#;0}");
                }));
            DrawOverviewGroup(
                "自动验收",
                model.AcceptanceScenarios.Select(value =>
                    $"{value.Definition.ScenarioId}｜步骤 {value.Definition.Steps.Count}"));
            DrawOverviewGroup(
                "自动检查的预制体要求",
                model.PrefabContracts.Select(value =>
                    $"{value.Definition.EntityId}｜{value.Definition.ContractKind}"
                    + $"｜{value.Definition.Identifier}"));
            foreach (var group in model.GeneratedArtifacts
                         .SelectMany(value =>
                             value.Definition.OverviewEntries)
                         .GroupBy(value => value.Category, StringComparer.Ordinal)
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                DrawOverviewGroup(
                    group.Key,
                    group.Select(value =>
                        value.DisplayName
                        + (value.Fields.Count == 0
                            ? string.Empty
                            : "｜" + string.Join(
                                "｜",
                                value.Fields.Select(field =>
                                    field.Key + "：" + field.Value)))));
            }

            DrawOverviewGroup(
                "学科配置文件",
                model.GeneratedArtifacts.Select(value =>
                    $"{value.Definition.ArtifactId}｜{value.Definition.SuggestedFileName}"));
        }

        private void DrawOverviewGroup(
            string title,
            IEnumerable<string> values)
        {
            var all = (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            var filtered = all
                .Where(value => MatchesSearch(_overviewSearch, value))
                .ToArray();
            var expanded = _expandedOverviewGroups.Contains(title);
            var countLabel = string.IsNullOrWhiteSpace(_overviewSearch)
                ? all.Length.ToString()
                : $"{filtered.Length}/{all.Length}";
            var next = EditorGUILayout.Foldout(
                expanded,
                $"{title}（{countLabel}）",
                true);
            if (next != expanded)
            {
                if (next)
                {
                    _expandedOverviewGroups.Add(title);
                }
                else
                {
                    _expandedOverviewGroups.Remove(title);
                }
            }

            if (!next && string.IsNullOrWhiteSpace(_overviewSearch))
            {
                return;
            }

            EditorGUI.indentLevel++;
            if (filtered.Length == 0)
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(_overviewSearch)
                        ? "暂无数据"
                        : "没有匹配项",
                    EditorStyles.miniLabel);
            }
            else
            {
                foreach (var item in filtered)
                {
                    EditorGUILayout.LabelField(
                        "• " + item,
                        EditorStyles.wordWrappedMiniLabel);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawDiagnostics()
        {
            var diagnostics = _compilation?.Diagnostics
                              ?? Array.Empty<CourseCompilationDiagnostic>();
            var panelHeight = diagnostics.Count == 0
                ? 34f
                : Mathf.Clamp(58f + diagnostics.Count * 76f, 130f, 300f);
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox,
                GUILayout.Height(panelHeight));
            GUILayout.Label(
                diagnostics.Count == 0
                    ? "⑤ 配置检查：没有问题"
                    : $"⑤ 配置问题（{diagnostics.Count}）",
                EditorStyles.boldLabel);
            if (diagnostics.Count == 0)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            _diagnosticScroll = EditorGUILayout.BeginScrollView(_diagnosticScroll);
            foreach (var diagnostic in diagnostics)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var icon = diagnostic.Severity == CourseDiagnosticSeverity.Error
                    ? "console.erroricon.sml"
                    : "console.warnicon.sml";
                var iconContent = EditorGUIUtility.IconContent(icon);
                iconContent.tooltip = "技术编号：" + diagnostic.Code;
                GUILayout.Label(iconContent, GUILayout.Width(20f));
                GUILayout.Label(
                    "问题：" + diagnostic.Reason,
                    EditorStyles.wordWrappedLabel);
                if (GUILayout.Button(
                        $"打开 {diagnostic.FileName} 第 {diagnostic.Line} 行",
                        EditorStyles.miniButton,
                        GUILayout.Width(150f)))
                {
                    OpenDiagnostic(diagnostic);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(
                    "解决方法：" + diagnostic.Suggestion,
                    EditorStyles.wordWrappedMiniLabel);
                DrawDiagnosticQuickFix(diagnostic);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiagnosticQuickFix(CourseCompilationDiagnostic diagnostic)
        {
            if (diagnostic.Code != MissingDisciplinePackageDiagnostic
                || _availableDisciplinePackageIds.Count == 0)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "一键修正为：",
                EditorStyles.miniLabel,
                GUILayout.Width(70f));
            foreach (var packageId in _availableDisciplinePackageIds)
            {
                if (GUILayout.Button(
                        packageId,
                        EditorStyles.miniButton,
                        GUILayout.MaxWidth(120f)))
                {
                    ReplaceMissingDisciplinePackage(
                        diagnostic.ConfigurationId,
                        packageId);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void ReplaceMissingDisciplinePackage(
            string missingPackageId,
            string replacementPackageId)
        {
            if (!_documents.TryGetDocument(CourseFile, out var courseDocument)
                || courseDocument.Rows.Count != 1)
            {
                SetOperationMessage(
                    "无法自动修正：课程.csv 应当只有一行课程信息。",
                    MessageType.Error);
                return;
            }

            var row = courseDocument.Rows[0];
            ApplyDocumentEdit(
                "修正学科类型",
                false,
                () =>
                {
                    var packageIds = ParseOrderedList(row["学科配方包"]);
                    var index = packageIds.FindIndex(value => string.Equals(
                        value,
                        missingPackageId,
                        StringComparison.Ordinal));
                    if (index >= 0)
                    {
                        packageIds[index] = replacementPackageId;
                    }
                    else
                    {
                        packageIds.Add(replacementPackageId);
                    }

                    row["学科配方包"] = string.Join(
                        ";",
                        packageIds.Distinct(StringComparer.Ordinal));
                });
            SetOperationMessage(
                $"已把未知学科类型“{missingPackageId}”改为“{replacementPackageId}”。请点击“保存并生成课程”。",
                MessageType.Info);
        }

        private void DrawTextCell(
            EditableCsvRow row,
            string column,
            string tooltip,
            bool alwaysVisible)
        {
            if (!alwaysVisible && !_objects.Headers.Contains(column))
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            var value = EditorGUILayout.TextField(
                new GUIContent(
                    CourseWorkbenchDisplayNames.Field(column),
                    tooltip),
                row[column]);
            if (EditorGUI.EndChangeCheck())
            {
                SetCell(row, column, value);
            }
        }

        private void DrawPreviewGroup(string title, IEnumerable<string> values)
        {
            var items = values.ToArray();
            EditorGUILayout.LabelField($"{title}（{items.Length}）", EditorStyles.miniBoldLabel);
            foreach (var item in items.Take(30))
            {
                EditorGUILayout.LabelField("• " + item, EditorStyles.wordWrappedMiniLabel);
            }

            if (items.Length > 30)
            {
                EditorGUILayout.LabelField($"另有 {items.Length - 30} 项……", EditorStyles.miniLabel);
            }
        }

        private void DrawProviderMessages()
        {
            foreach (var message in _providerMessages)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
        }

        private void DrawOperationMessage()
        {
            if (!string.IsNullOrWhiteSpace(_operationMessage))
            {
                EditorGUILayout.HelpBox(_operationMessage, _operationMessageType);
            }
        }

        private CourseWorkbenchGuideState EvaluateGuide()
        {
            var diagnostics = _compilation?.Diagnostics?.Count ?? 0;
            return CourseWorkbenchGuide.Evaluate(
                _course != null && _objects != null,
                _objects?.Rows.Count ?? 0,
                CountIncompleteObjects(),
                _compilation != null,
                _compilation != null && _compilation.IsSuccess,
                diagnostics,
                _dirty,
                _hasCurrentGeneratedAsset,
                _documents != null && _documents.HasExternalChanges());
        }

        private int CountIncompleteObjects()
        {
            if (_objects == null)
            {
                return 0;
            }

            return _objects.Rows.Count(IsObjectIncomplete);
        }

        private static bool IsObjectIncomplete(EditableCsvRow row) =>
            string.IsNullOrWhiteSpace(row["实体ID"])
            || string.IsNullOrWhiteSpace(row["显示名称"])
            || string.IsNullOrWhiteSpace(row["Prefab"]);

        private void SelectFirstIncompleteObject()
        {
            if (_objects == null)
            {
                return;
            }

            var index = -1;
            for (var candidate = 0; candidate < _objects.Rows.Count; candidate++)
            {
                if (IsObjectIncomplete(_objects.Rows[candidate]))
                {
                    index = candidate;
                    break;
                }
            }
            if (index < 0)
            {
                return;
            }

            _selectedObjectIndex = index;
            _detailScroll = Vector2.zero;
            _referenceSearch = string.Empty;
            GUI.FocusControl(null);
            var row = _objects.Rows[index];
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(row["实体ID"])) missing.Add("对象 ID");
            if (string.IsNullOrWhiteSpace(row["显示名称"])) missing.Add("显示名称");
            if (string.IsNullOrWhiteSpace(row["Prefab"])) missing.Add("模型预制体");
            SetOperationMessage(
                $"已定位“{row["显示名称"]}”。请补全：{string.Join("、", missing)}。",
                MessageType.Warning);
        }

        private void FocusFirstDiagnostic()
        {
            var diagnostic = _compilation?.Diagnostics?.FirstOrDefault();
            if (diagnostic == null)
            {
                CompilePreview();
                return;
            }

            OpenDiagnostic(diagnostic);
            _diagnosticScroll = Vector2.zero;
            SetOperationMessage(
                $"请先处理这个问题：{diagnostic.Reason}",
                MessageType.Warning);
        }

        private void RefreshCourseList()
        {
            _courses.Clear();
            // Sample 导入目录由包名和版本决定，因此从 Assets 发现课程，
            // 不把工作台绑定到某个固定业务目录。
            var root = Path.GetFullPath("Assets");
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var authoring in Directory.EnumerateDirectories(
                         root,
                         "Authoring",
                         SearchOption.AllDirectories))
            {
                if (!File.Exists(Path.Combine(authoring, "课程.csv"))
                    || !File.Exists(Path.Combine(authoring, ObjectsFile)))
                {
                    continue;
                }

                var courseRoot = Directory.GetParent(authoring)?.FullName;
                if (courseRoot == null)
                {
                    continue;
                }

                _courses.Add(new CourseLocation(
                    Directory.GetParent(authoring)?.Name ?? "未命名课程",
                    ToAssetPath(courseRoot),
                    ToAssetPath(authoring)));
            }

            _courses.Sort((left, right) => string.Compare(
                left.DisplayName,
                right.DisplayName,
                StringComparison.Ordinal));
        }

        private void LoadCourse(int index)
        {
            if (index < 0 || index >= _courses.Count)
            {
                return;
            }

            _course = _courses[index];
            _selectedCourseIndex = index;
            _selectedObjectIndex = -1;
            _dirty = false;
            _previewOutdated = false;
            _hasCurrentGeneratedAsset = false;
            _undo.Clear();
            _redo.Clear();
            _renameSourceId = string.Empty;
            _pendingEntityId = string.Empty;
            _referenceSearch = string.Empty;
            _expandedReferenceGroups.Clear();
            _operationMessage = string.Empty;
            EditorPrefs.SetString(
                SelectedCoursePreference,
                _course.AuthoringAssetPath);
            try
            {
                _documents = CourseDocumentSet.Load(
                    _course.AuthoringAbsolutePath);
                _objects = _documents.GetRequiredDocument(ObjectsFile);
                foreach (var column in CoreObjectColumns)
                {
                    _objects.EnsureColumn(column);
                }
                _dirty = _documents.IsModified;

                if (_objects.Rows.Count > 0)
                {
                    _selectedObjectIndex = 0;
                }

                CompilePreview();
                if (_documents.LoadMessages.Count > 0)
                {
                    SetOperationMessage(
                        string.Join(Environment.NewLine, _documents.LoadMessages),
                        MessageType.Warning);
                }
            }
            catch (Exception exception)
            {
                _documents = null;
                _objects = null;
                _compilation = null;
                _featureCatalog = null;
                SetOperationMessage(
                    "课程载入失败：" + exception.Message,
                    MessageType.Error);
            }
        }

        private void CompilePreview()
        {
            if (_course == null || _objects == null)
            {
                return;
            }

            _providerMessages.Clear();
            _availableDisciplinePackageIds.Clear();
            try
            {
                var result = new CourseWorkbenchCompiler().Compile(
                    CurrentSource());
                _compilation = result.Compilation;
                _featureCatalog = result.FeatureCatalog;
                _providerMessages.AddRange(result.Messages);
                _availableDisciplinePackageIds.AddRange(
                    result.AvailableDisciplinePackageIds);
                _previewOutdated = false;
                _hasCurrentGeneratedAsset = _compilation.IsSuccess
                                            && !_dirty
                                            && HasCurrentGeneratedAsset();
                if (_compilation.IsSuccess)
                {
                    SetOperationMessage("内存配置已通过编译。", MessageType.Info);
                }
                else
                {
                    SetOperationMessage(
                        "课程配置还不能生成。请按照下方“配置问题”中的解决方法处理；当前修改尚未保存到 CSV。",
                        MessageType.Warning);
                }
            }
            catch (Exception exception)
            {
                _compilation = null;
                _featureCatalog = null;
                _availableDisciplinePackageIds.Clear();
                _previewOutdated = false;
                SetOperationMessage(
                    "编译预览失败：" + exception.Message,
                    MessageType.Error);
            }

            Repaint();
        }

        private bool HasCurrentGeneratedAsset()
        {
            if (_course == null
                || _documents == null
                || _compilation?.Domain == null)
            {
                return false;
            }

            var generatedPath = _course.CourseRootAssetPath + "/Generated";
            var matchingAssets = AssetDatabase.FindAssets(
                    "t:CompiledCourseAsset",
                    new[] { generatedPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(
                    AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(path)
                        ?.CourseId,
                    _compilation.Domain.CourseId,
                    StringComparison.Ordinal))
                .ToArray();
            if (matchingAssets.Length != 1)
            {
                return false;
            }

            var assetWriteTime = File.GetLastWriteTimeUtc(
                Path.GetFullPath(matchingAssets[0]));
            var latestSourceWriteTime = Directory.EnumerateFiles(
                    _documents.AuthoringDirectory,
                    "*.csv",
                    SearchOption.TopDirectoryOnly)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MaxValue)
                .Max();
            return assetWriteTime >= latestSourceWriteTime;
        }

        private CourseBlueprintSource CurrentSource() =>
            _documents.CreateBlueprintSource();

        private bool SaveCourse()
        {
            if (_objects == null)
            {
                return false;
            }

            if (!_dirty)
            {
                if (_documents.HasExternalChanges())
                {
                    LoadCourse(_selectedCourseIndex);
                }

                return _objects != null;
            }

            try
            {
                // 先用严格读取器验证序列化结果，避免保存编辑器自身无法再次读取的文件。
                var parsed = new StrictCsvReader().Read(ObjectsFile, _objects.ToCsv());
                if (parsed.Diagnostics.Count > 0)
                {
                    throw new InvalidDataException(string.Join(
                        Environment.NewLine,
                        parsed.Diagnostics.Select(value => value.Reason)));
                }

                _documents.SaveModified();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                _dirty = _documents.IsModified;
                CompilePreview();
                SetOperationMessage(
                    _compilation != null && _compilation.IsSuccess
                        ? "课程 CSV 已安全保存，配置已通过编译。"
                        : "课程 CSV 已安全保存，但配置仍有问题，请按照下方解决方法继续处理。",
                    _compilation != null && _compilation.IsSuccess
                        ? MessageType.Info
                        : MessageType.Warning);
                return true;
            }
            catch (Exception exception)
            {
                SetOperationMessage("保存失败：" + exception.Message, MessageType.Error);
                return false;
            }
        }

        private void BuildCourse()
        {
            if (!SaveCourse())
            {
                return;
            }

            CompilePreview();
            if (_compilation == null || !_compilation.IsSuccess)
            {
                SetOperationMessage(
                    "暂时不能生成课程：请先解决下方列出的配置问题。",
                    MessageType.Error);
                return;
            }

            try
            {
                var result = new CourseWorkbenchBuildService().Build(
                    _course.CourseRootAssetPath,
                    _compilation);
                if (!result.IsSuccess)
                {
                    SetOperationMessage(
                        "课程资产生成失败：" + string.Join(
                            Environment.NewLine,
                            result.Diagnostics.Select(value => value.Reason)),
                        MessageType.Error);
                    return;
                }

                SetOperationMessage(
                    $"课程已构建：{result.AssetPath}",
                    MessageType.Info);
                _hasCurrentGeneratedAsset = true;
                EditorGUIUtility.PingObject(result.Asset);
            }
            catch (Exception exception)
            {
                SetOperationMessage("构建失败：" + exception.Message, MessageType.Error);
            }
        }

        private void AddObject()
        {
            var usedIds = new HashSet<string>(
                _objects.Rows.Select(value => value["实体ID"]),
                StringComparer.Ordinal);
            var suffix = 1;
            var entityId = "新对象";
            while (usedIds.Contains(entityId))
            {
                suffix++;
                entityId = "新对象" + suffix;
            }

            ApplyDocumentEdit("添加对象", false, () => _objects.AddRow(new[]
            {
                Pair("实体ID", entityId),
                Pair("显示名称", entityId),
                Pair("初始位置", "0|0|0"),
                Pair("初始旋转", "0|0|0")
            }));
            _selectedObjectIndex = _objects.Rows.Count - 1;
            _detailScroll = Vector2.zero;
            _referenceSearch = string.Empty;
        }

        private void DeleteSelectedObject()
        {
            var row = _objects.Rows[_selectedObjectIndex];
            var externalReferences = _referenceGraph.FindExternalReferences(
                _documents,
                row["实体ID"]);
            if (externalReferences.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "对象仍被课程引用",
                    $"不能直接删除“{row["显示名称"]}”。还有 {externalReferences.Count} 个跨表引用：\n\n"
                    + FormatReferences(externalReferences)
                    + "\n\n请先修改相关配置，或把对象重命名为新的稳定 ID。",
                    "知道了");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "删除实验对象",
                    $"确定删除“{row["显示名称"]}”（{row["实体ID"]}）吗？该对象当前没有跨表引用。",
                    "删除",
                    "取消"))
            {
                return;
            }

            ApplyDocumentEdit(
                "删除对象:" + row["实体ID"],
                false,
                () => _objects.RemoveRow(row));
            _selectedObjectIndex = Math.Min(
                _selectedObjectIndex,
                _objects.Rows.Count - 1);
            _referenceSearch = string.Empty;
        }

        private void SetFeature(
            EditableCsvRow row,
            CourseFeatureDescriptor feature,
            bool enabled)
        {
            ApplyDocumentEdit(
                "切换特征:" + row["实体ID"] + ":" + feature.FeatureId,
                false,
                () =>
                {
                    var values = ParseOrderedList(row["特征列表"]);
                    if (enabled)
                    {
                        if (!values.Contains(feature.FeatureId, StringComparer.Ordinal))
                        {
                            values.Add(feature.FeatureId);
                        }

                        foreach (var parameter in feature.Parameters)
                        {
                            _objects.EnsureColumn(
                                parameter.ColumnName,
                                string.Empty);
                            if (string.IsNullOrWhiteSpace(row[parameter.ColumnName])
                                && !string.IsNullOrWhiteSpace(parameter.DefaultValue))
                            {
                                row[parameter.ColumnName] = parameter.DefaultValue;
                            }
                        }
                    }
                    else
                    {
                        values.RemoveAll(value => value == feature.FeatureId);
                    }

                    row["特征列表"] = string.Join(";", values);
                });
        }

        private void SetCell(
            EditableCsvRow row,
            string column,
            string value)
        {
            value ??= string.Empty;
            if (row[column] == value)
            {
                return;
            }

            ApplyDocumentEdit(
                "字段:" + _selectedObjectIndex + ":" + column,
                true,
                () => row[column] = value);
        }

        private void RenameSelectedEntity(string oldEntityId, string newEntityId)
        {
            try
            {
                var references = _referenceGraph.Find(_documents, oldEntityId);
                var external = references.Where(value =>
                        value.FileName != ObjectsFile
                        || value.ColumnName != "实体ID")
                    .ToArray();
                var detail = external.Length == 0
                    ? "没有发现其他表引用。"
                    : $"将同步更新 {external.Length} 个跨表引用：\n\n"
                      + FormatReferences(external);
                if (!EditorUtility.DisplayDialog(
                        "应用实体重命名",
                        $"将实体 ID 从“{oldEntityId}”改为“{newEntityId}”。\n\n{detail}",
                        "重命名",
                        "取消"))
                {
                    return;
                }

                ApplyDocumentEdit(
                    "重命名实体:" + oldEntityId,
                    false,
                    () => _referenceGraph.Rename(
                        _documents,
                        oldEntityId,
                        newEntityId));
                _renameSourceId = newEntityId.Trim();
                _pendingEntityId = _renameSourceId;
                SetOperationMessage(
                    $"实体“{oldEntityId}”及其 {external.Length} 个跨表引用已在内存中重命名；保存前仍可撤销。",
                    MessageType.Info);
            }
            catch (Exception exception)
            {
                SetOperationMessage(
                    "实体重命名失败：" + exception.Message,
                    MessageType.Error);
            }
        }

        private static string FormatReferences(
            IEnumerable<CourseEntityReference> references)
        {
            var values = references.Take(10)
                .Select(value =>
                    $"• {value.FileName} 第 {value.CsvLine} 行 / {value.ColumnName}")
                .ToList();
            var count = references.Count();
            if (count > values.Count)
            {
                values.Add($"• 另有 {count - values.Count} 项……");
            }

            return string.Join("\n", values);
        }

        private void ApplyDocumentEdit(
            string undoGroup,
            bool allowCoalesce,
            Action edit)
        {
            var before = _documents.CaptureSnapshot();
            try
            {
                edit();
            }
            catch
            {
                _documents.Restore(before);
                _objects = _documents.GetRequiredDocument(ObjectsFile);
                throw;
            }

            var now = EditorApplication.timeSinceStartup;
            var coalesced = allowCoalesce
                            && string.Equals(
                                _lastUndoGroup,
                                undoGroup,
                                StringComparison.Ordinal)
                            && now - _lastUndoTime < 0.8d;
            if (!coalesced)
            {
                _undo.Push(before);
            }

            _redo.Clear();
            _lastUndoGroup = undoGroup;
            _lastUndoTime = now;
            MarkDirty();
        }

        private void UndoEdit()
        {
            if (_undo.Count == 0)
            {
                return;
            }

            _redo.Push(_documents.CaptureSnapshot());
            _documents.Restore(_undo.Pop());
            AfterHistoryNavigation("已撤销上一步课程配置修改。");
        }

        private void RedoEdit()
        {
            if (_redo.Count == 0)
            {
                return;
            }

            _undo.Push(_documents.CaptureSnapshot());
            _documents.Restore(_redo.Pop());
            AfterHistoryNavigation("已重做课程配置修改。");
        }

        private void AfterHistoryNavigation(string message)
        {
            _objects = _documents.GetRequiredDocument(ObjectsFile);
            _selectedObjectIndex = Math.Min(
                _selectedObjectIndex,
                _objects.Rows.Count - 1);
            _renameSourceId = string.Empty;
            _lastUndoGroup = string.Empty;
            MarkDirty();
            SetOperationMessage(message, MessageType.Info);
        }

        private void MarkDirty()
        {
            _dirty = _documents != null && _documents.IsModified;
            _hasCurrentGeneratedAsset = false;
            _previewOutdated = true;
            _previewDueTime = EditorApplication.timeSinceStartup + 0.45d;
            _operationMessage = string.Empty;
            Repaint();
        }

        private void UpdateDeferredPreview()
        {
            if (!_previewOutdated
                || _objects == null
                || EditorApplication.timeSinceStartup < _previewDueTime)
            {
                return;
            }

            CompilePreview();
        }

        private bool ConfirmDiscardIfDirty()
        {
            return !_dirty || EditorUtility.DisplayDialog(
                "放弃未保存修改",
                "当前实验对象配置尚未保存。继续会放弃这些修改。",
                "放弃并继续",
                "取消");
        }

        private bool HasSelectedObject() =>
            _objects != null
            && _selectedObjectIndex >= 0
            && _selectedObjectIndex < _objects.Rows.Count;

        private void SetOperationMessage(string message, MessageType type)
        {
            _operationMessage = message;
            _operationMessageType = type;
            Repaint();
        }

        private void OpenDiagnostic(CourseCompilationDiagnostic diagnostic)
        {
            var courseFile = _course.AuthoringAssetPath + "/" + diagnostic.FileName;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(courseFile);
            if (asset == null)
            {
                var name = Path.GetFileNameWithoutExtension(diagnostic.FileName);
                var path = AssetDatabase.FindAssets(name + " t:TextAsset")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(value => string.Equals(
                        Path.GetFileName(value),
                        diagnostic.FileName,
                        StringComparison.Ordinal));
                asset = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            }

            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset, Math.Max(1, diagnostic.Line));
            }
        }

        private void OpenCourseFile(string fileName, int line)
        {
            if (_course == null)
            {
                return;
            }

            var path = _course.AuthoringAssetPath + "/" + fileName;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset, Math.Max(1, line));
            }
        }

        private static HashSet<string> ParseList(string value) =>
            new HashSet<string>(
                ParseOrderedList(value),
                StringComparer.Ordinal);

        private static List<string> ParseOrderedList(string value) =>
            (value ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        private static bool MatchesSearch(string search, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            return values.Any(value =>
                (value ?? string.Empty).IndexOf(
                    search,
                    StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TryParseVector(string value, out Vector3 vector)
        {
            vector = Vector3.zero;
            var parts = (value ?? string.Empty).Split('|');
            if (parts.Length != 3
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                return false;
            }

            vector = new Vector3(x, y, z);
            return true;
        }

        private static string FormatVector(Vector3 vector) =>
            string.Join(
                "|",
                vector.x.ToString("0.###", CultureInfo.InvariantCulture),
                vector.y.ToString("0.###", CultureInfo.InvariantCulture),
                vector.z.ToString("0.###", CultureInfo.InvariantCulture));

        private static string DisplayEndpoint(string entityId) =>
            string.IsNullOrWhiteSpace(entityId) ? "（无）" : entityId;

        private string FormatPairingSummary(CoursePairingSummary pairing)
        {
            var direction = pairing.SelectedIsSource
                ? "当前对象 → " + ObjectDisplayName(pairing.OtherEntityId)
                : ObjectDisplayName(pairing.OtherEntityId) + " → 当前对象";
            return direction
                   + "｜"
                   + string.Join(
                       "、",
                       pairing.Operations.Select(PairingOperationText));
        }

        private string ObjectDisplayName(string entityId)
        {
            var objectRow = _objects?.Rows.FirstOrDefault(value =>
                string.Equals(
                    value["实体ID"],
                    entityId,
                    StringComparison.Ordinal));
            var displayName = objectRow?["显示名称"]?.Trim();
            return string.IsNullOrWhiteSpace(displayName)
                   || string.Equals(
                       displayName,
                       entityId,
                       StringComparison.Ordinal)
                ? DisplayEndpoint(entityId)
                : $"{displayName}（{entityId}）";
        }

        private static string PairingOperationText(
            CoursePairingOperationSummary operation)
        {
            return string.IsNullOrWhiteSpace(operation.PolicyEffect)
                   || string.Equals(
                       operation.PolicyEffect,
                       "允许",
                       StringComparison.Ordinal)
                ? operation.OperationName
                : $"{operation.OperationName}（{operation.PolicyEffect}）";
        }

        private static KeyValuePair<string, string> Pair(
            string key,
            string value) =>
            new KeyValuePair<string, string>(key, value);

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

        private sealed class CourseLocation
        {
            public CourseLocation(
                string displayName,
                string courseRootAssetPath,
                string authoringAssetPath)
            {
                DisplayName = displayName;
                CourseRootAssetPath = courseRootAssetPath;
                AuthoringAssetPath = authoringAssetPath;
            }

            public string DisplayName { get; }
            public string CourseRootAssetPath { get; }
            public string AuthoringAssetPath { get; }
            public string AuthoringAbsolutePath =>
                Path.GetFullPath(AuthoringAssetPath);
        }
    }
}
