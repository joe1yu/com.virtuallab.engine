using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VirtualLab.Application.Courses;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Presentation;

namespace VirtualLab.UnityAdapters.Courses
{
    /// <summary>
    /// 用按钮临时代替鼠标、Pointer 和 VR 输入。每个按钮只执行一个原子操作，
    /// 所有操作均可自由尝试，并通过正式语义请求和课程 Tick 驱动内核。
    /// </summary>
    public sealed class CourseUiClickSimulator :
        MonoBehaviour,
        IPresentationTextSink,
        IPresentationMessageSink
    {
        [SerializeField] private TextAsset scenario;

        private readonly List<string> _lines = new List<string>();
        private readonly Dictionary<string, Button> _buttonsByStepId =
            new Dictionary<string, Button>(StringComparer.Ordinal);
        private readonly HashSet<string> _completedStepIds =
            new HashSet<string>(StringComparer.Ordinal);
        private IReadOnlyList<UiStep> _steps;
        private ConfigDrivenCourseBootstrap _bootstrap;
        private IConfiguredCourseTickDriver _tickDriver;
        private Text _output;
        private ScrollRect _outputScroll;
        private int _commandSequence;
        private int _reportedEvidenceCount;
        private static Font _uiFont;

        public Text OutputText => _output;

        public ScrollRect OutputScrollRect => _outputScroll;

        public IReadOnlyList<string> StepIds =>
            _steps?.Select(value => value.StepId).ToArray()
            ?? Array.Empty<string>();

        public int CompletedStepCount => _completedStepIds.Count;

        /// <summary>
        /// 只提供脚本建议，不限制用户自由选择其他操作。
        /// </summary>
        public string RecommendedStepId => _steps?
            .FirstOrDefault(value => !_completedStepIds.Contains(value.StepId))
            ?.StepId;

        public void Configure(TextAsset value)
        {
            scenario = value ?? throw new ArgumentNullException(nameof(value));
        }

        private void Awake()
        {
            if (scenario == null)
            {
                throw new InvalidOperationException("未配置 UI 模拟步骤表。");
            }

            _steps = UiScenarioParser.Parse(scenario.text);
            BuildUi();
            Append("课程 UI 模拟已就绪。脚本顺序仅作建议，所有操作均可自由尝试。\n");
        }

        private void Start()
        {
            _bootstrap = FindObjectOfType<ConfigDrivenCourseBootstrap>()
                ?? throw new InvalidOperationException("场景中没有课程启动器。");
            if (!_bootstrap.IsInitialized)
            {
                _bootstrap.Initialize();
            }

            _tickDriver = FindObjectsOfType<MonoBehaviour>(true)
                .OfType<IConfiguredCourseTickDriver>()
                .SingleOrDefault()
                ?? throw new InvalidOperationException(
                    "UI 模拟课程没有注册过程推进器。");
            AppendSnapshot();
        }

        public bool ClickStep(string stepId)
        {
            if (_bootstrap == null || _tickDriver == null)
            {
                throw new InvalidOperationException("UI 模拟器尚未启动。");
            }

            var step = _steps.FirstOrDefault(value => string.Equals(
                value.StepId,
                stepId,
                StringComparison.Ordinal));
            if (step == null)
            {
                throw new ArgumentException("未知 UI 步骤。", nameof(stepId));
            }

            Append($"\n=== {step.StepId} {step.ButtonText} ===");
            if (!Execute(step))
            {
                Append("操作未完成；拒绝原因与反馈已通过课程表现链路输出，可继续尝试其他操作。");
                AppendSnapshot();
                return false;
            }

            var firstCompletion = _completedStepIds.Add(step.StepId);
            if (firstCompletion)
            {
                _buttonsByStepId[step.StepId].GetComponent<Image>().color =
                    new Color(0.18f, 0.5f, 0.34f, 1f);
            }

            Append(firstCompletion
                ? $"操作执行完成（已完成 {_completedStepIds.Count}/{_steps.Count} 项脚本操作）。"
                : "操作再次执行成功；脚本完成数不重复累计。");
            AppendSnapshot();
            return true;
        }

        public void PresentText(PresentationEffectCommand command)
        {
            var target = command.Target.EntityId
                ?? command.Target.LocationKind.ToString();
            var text = command.Parameters.TryGetValue("文案", out var message)
                       && message.Kind == PresentationValueKind.Text
                ? message.Text
                : command.EffectId;
            Append($"[表现] {text} → {target}");
        }

        public void ShowMessage(string text, double durationSeconds)
        {
            Append($"[表现] {text}（{durationSeconds:0.###} 秒）");
        }

        private bool Execute(UiStep step)
        {
            if (!string.IsNullOrWhiteSpace(step.ActionId))
            {
                var commandId = string.Format(
                    CultureInfo.InvariantCulture,
                    "UI.{0}.{1}",
                    step.StepId,
                    ++_commandSequence);
                var configuredAction = _bootstrap.Domain.ConfiguredActions
                    .Where(value => value.ActionId == step.ActionId)
                    .Where(value => value.MatchesAnyEntities
                        || (value.SourceEntityId == step.SourceEntityId
                            && value.TargetEntityId == step.TargetEntityId))
                    .OrderByDescending(value => value.Priority)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        $"UI 步骤“{step.StepId}”没有匹配的动作定义。");
                var request = new SemanticActionRequest(
                    commandId,
                    step.ActionId,
                    "UI操作." + step.StepId + "." + _commandSequence,
                    configuredAction.Phase,
                    0d,
                    _bootstrap.Domain.ActorEntityId,
                    step.SourceEntityId,
                    step.TargetEntityId,
                    step.Parameters);
                var result = _bootstrap.Dispatch(request);
                Append(result.Outcome.IsAccepted
                    ? $"[命令✓] {step.ButtonText}"
                    : $"[命令✗] {step.ButtonText}："
                      + string.Join("、", result.Outcome.RejectionCodes));
                foreach (var domainEvent in result.Outcome.Events)
                {
                    Append($"[事件] #{domainEvent.Sequence} "
                           + domainEvent.EventType);
                }

                if (!result.Outcome.IsAccepted)
                {
                    return false;
                }
            }

            if (step.ElapsedSeconds > 0d)
            {
                var tick = _tickDriver.Advance(step.ElapsedSeconds);
                Append($"[推进] {step.ElapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture)} 秒，"
                       + $"Tick={tick.Tick.Value}，事件={tick.Events.Count}");
                foreach (var domainEvent in tick.Events)
                {
                    Append($"[过程事件] #{domainEvent.Sequence} "
                           + domainEvent.EventType);
                }
            }

            if (string.IsNullOrWhiteSpace(step.ActionId)
                && step.ElapsedSeconds <= 0d)
            {
                Append($"[脚本✓] {step.ButtonText}");
            }

            return true;
        }

        private void AppendSnapshot()
        {
            var runtime = _bootstrap.Runtime;
            if (_reportedEvidenceCount > runtime.Assessment.Evidence.Count)
            {
                _reportedEvidenceCount = 0;
            }

            foreach (var evidence in runtime.Assessment.Evidence
                         .Skip(_reportedEvidenceCount))
            {
                Append($"[错误后果] {evidence.Prompt}；"
                       + $"严重度={SeverityText(evidence.Severity)}；"
                       + $"恢复方式={RecoverabilityText(evidence.Recoverability)}");
            }

            _reportedEvidenceCount = runtime.Assessment.Evidence.Count;
            Append($"[课程状态] Tick={runtime.CurrentTick.Value}；"
                   + $"目标={runtime.Goals.CompletedGoalIds.Count}；"
                   + $"评分={runtime.Assessment.Score}；"
                   + $"进度={RunStatusText(runtime.Outcome.RunStatus)}；"
                   + $"结果={QualityText(runtime.Outcome.Quality)}；"
                   + $"事件={runtime.EventHistory.Count}");
            if (runtime.Goals.CompletedGoalIds.Count > 0)
            {
                Append("[已完成目标] "
                       + string.Join("、", runtime.Goals.CompletedGoalIds));
            }

            if (runtime.Outcome.BlockedGoalIds.Count > 0)
            {
                Append("[受阻目标] "
                       + string.Join("、", runtime.Outcome.BlockedGoalIds));
            }
        }

        private static string RunStatusText(CourseRunStatus status) =>
            status switch
            {
                CourseRunStatus.InProgress => "进行中",
                CourseRunStatus.Completed => "已完成",
                CourseRunStatus.Failed => "本轮失败",
                _ => status.ToString()
            };

        private static string QualityText(CourseResultQuality quality) =>
            quality switch
            {
                CourseResultQuality.Normal => "正常",
                CourseResultQuality.Degraded => "现象或结果偏离",
                CourseResultQuality.RecoveryRequired => "需更换器材或样品",
                _ => quality.ToString()
            };

        private static string SeverityText(CourseConsequenceSeverity severity) =>
            severity switch
            {
                CourseConsequenceSeverity.Advisory => "提示",
                CourseConsequenceSeverity.PhenomenonDeviation => "现象偏差",
                CourseConsequenceSeverity.ExperimentRisk => "实验风险",
                CourseConsequenceSeverity.EquipmentOrSampleDamage =>
                    "器材或样品损坏",
                CourseConsequenceSeverity.SafetyIncident => "安全事故",
                _ => severity.ToString()
            };

        private static string RecoverabilityText(
            CourseConsequenceRecoverability recoverability) =>
            recoverability switch
            {
                CourseConsequenceRecoverability.NoneRequired => "无需恢复",
                CourseConsequenceRecoverability.RecoverableByOperation =>
                    "可通过后续操作恢复",
                CourseConsequenceRecoverability.ReplacementRequired =>
                    "需更换器材或样品",
                CourseConsequenceRecoverability.RestartRequired =>
                    "需重新开始实验",
                CourseConsequenceRecoverability.GoalPermanentlyBlocked =>
                    "指定目标已永久受阻",
                _ => recoverability.ToString()
            };

        private void Append(string line)
        {
            _lines.Add(line ?? string.Empty);
            if (_lines.Count > 180)
            {
                _lines.RemoveRange(0, _lines.Count - 180);
            }

            if (_output != null)
            {
                _output.text = string.Join("\n", _lines);
                // 文本高度由 ContentSizeFitter 在布局阶段更新；强制刷新后定位到底部，
                // 确保连续实验反馈始终显示最新一条，同时仍可用滚轮或拖动向上查看。
                Canvas.ForceUpdateCanvases();
                if (_outputScroll != null)
                {
                    _outputScroll.verticalNormalizedPosition = 0f;
                }
            }
        }

        private void BuildUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            gameObject.AddComponent<GraphicRaycaster>();

            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("UI事件系统");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }

            var background = Panel("背景", transform, new Color(0.06f, 0.08f, 0.12f, 0.97f));
            Stretch(background.rectTransform, 0f, 0f, 1f, 1f, 0f);

            var buttons = Panel("步骤按钮", background.transform, new Color(0.1f, 0.14f, 0.2f, 1f));
            Stretch(buttons.rectTransform, 0.02f, 0.04f, 0.36f, 0.96f, 0f);
            AddAnchoredLabel(buttons.transform, "实验脚本操作", 26);
            var content = AddStepScrollView(buttons.transform);

            string currentGroup = null;
            for (var index = 0; index < _steps.Count; index++)
            {
                var step = _steps[index];
                if (!string.Equals(
                        currentGroup,
                        step.GroupText,
                        StringComparison.Ordinal))
                {
                    currentGroup = step.GroupText;
                    AddSectionLabel(content, currentGroup);
                }

                var capturedId = step.StepId;
                var button = AddButton(
                    content,
                    index,
                    step.StepId,
                    step.ButtonText,
                    () => ClickStep(capturedId));
                button.interactable = true;
                _buttonsByStepId.Add(step.StepId, button);
            }

            var outputPanel = Panel("文字表现", background.transform, new Color(0.02f, 0.025f, 0.04f, 1f));
            Stretch(outputPanel.rectTransform, 0.38f, 0.04f, 0.98f, 0.96f, 0f);
            _output = AddOutputScrollView(
                outputPanel.transform,
                out _outputScroll);
        }

        private static Image Panel(string name, Transform parent, Color color)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void AddAnchoredLabel(
            Transform parent,
            string value,
            int size)
        {
            var text = CreateText(parent, "标题", size);
            text.text = value;
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = new Vector2(0f, 0.91f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.offsetMin = new Vector2(14f, 0f);
            text.rectTransform.offsetMax = new Vector2(-14f, 0f);
        }

        private static Transform AddStepScrollView(Transform parent)
        {
            var root = new GameObject(
                "步骤滚动区",
                typeof(RectTransform),
                typeof(Image),
                typeof(ScrollRect));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect, 0f, 0f, 1f, 0.91f, 14f);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);

            var viewport = Panel(
                "视口",
                root.transform,
                new Color(0f, 0f, 0f, 0f));
            Stretch(viewport.rectTransform, 0f, 0f, 1f, 1f, 0f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            viewport.enabled = false;
            var content = new GameObject(
                "步骤内容",
                typeof(RectTransform),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewport.rectTransform;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;
            return content.transform;
        }

        private static Text AddOutputScrollView(
            Transform parent,
            out ScrollRect scroll)
        {
            var root = new GameObject(
                "文字滚动区",
                typeof(RectTransform),
                typeof(Image),
                typeof(ScrollRect));
            root.transform.SetParent(parent, false);
            Stretch(root.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f, 14f);
            var rootImage = root.GetComponent<Image>();
            rootImage.color = new Color(0f, 0f, 0f, 0.08f);
            rootImage.raycastTarget = true;

            var viewport = Panel(
                "视口",
                root.transform,
                new Color(0f, 0f, 0f, 0.001f));
            var viewportRect = viewport.rectTransform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = new Vector2(0.975f, 1f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var output = CreateText(viewport.transform, "输出", 18);
            output.alignment = TextAnchor.UpperLeft;
            output.horizontalOverflow = HorizontalWrapMode.Wrap;
            output.verticalOverflow = VerticalWrapMode.Overflow;
            output.rectTransform.anchorMin = new Vector2(0f, 1f);
            output.rectTransform.anchorMax = new Vector2(1f, 1f);
            output.rectTransform.pivot = new Vector2(0.5f, 1f);
            output.rectTransform.offsetMin = new Vector2(10f, 0f);
            output.rectTransform.offsetMax = new Vector2(-10f, 0f);
            output.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            var scrollbarRoot = new GameObject(
                "垂直滚动条",
                typeof(RectTransform),
                typeof(Image),
                typeof(Scrollbar));
            scrollbarRoot.transform.SetParent(root.transform, false);
            var scrollbarRect = scrollbarRoot.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(0.978f, 0f);
            scrollbarRect.anchorMax = Vector2.one;
            scrollbarRect.offsetMin = new Vector2(2f, 4f);
            scrollbarRect.offsetMax = new Vector2(-2f, -4f);
            scrollbarRoot.GetComponent<Image>().color =
                new Color(1f, 1f, 1f, 0.12f);

            var handle = new GameObject(
                "滑块",
                typeof(RectTransform),
                typeof(Image));
            handle.transform.SetParent(scrollbarRoot.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(1f, 1f);
            handleRect.offsetMax = new Vector2(-1f, -1f);
            handle.GetComponent<Image>().color =
                new Color(0.55f, 0.72f, 0.92f, 0.8f);

            var scrollbar = scrollbarRoot.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = output.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return output;
        }

        private static void AddSectionLabel(Transform parent, string value)
        {
            var text = CreateText(parent, "分组." + value, 18);
            text.text = value;
            text.color = new Color(0.64f, 0.84f, 1f, 1f);
            text.alignment = TextAnchor.MiddleLeft;
            text.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
        }

        private static Button AddButton(
            Transform parent,
            int index,
            string stepId,
            string label,
            UnityEngine.Events.UnityAction action)
        {
            var root = new GameObject(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "按钮.{0:D3}.{1}",
                    index + 1,
                    stepId),
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0.18f, 0.38f, 0.58f, 1f);
            root.GetComponent<LayoutElement>().preferredHeight = 78f;
            var button = root.GetComponent<Button>();
            button.onClick.AddListener(action);
            var text = CreateText(root.transform, "文字", 17);
            text.text = stepId + "  " + label;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Stretch(text.rectTransform, 0f, 0f, 1f, 1f, 8f);
            return button;
        }

        private static Text CreateText(Transform parent, string name, int size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Text));
            root.transform.SetParent(parent, false);
            var text = root.GetComponent<Text>();
            text.font = UiFont();
            text.fontSize = size;
            text.color = Color.white;
            text.supportRichText = false;
            return text;
        }

        private static Font UiFont()
        {
            if (_uiFont != null)
            {
                return _uiFont;
            }

            _uiFont = Font.CreateDynamicFontFromOSFont(
                new[]
                {
                    "Microsoft YaHei UI",
                    "Microsoft YaHei",
                    "SimHei",
                    "Arial"
                },
                20);
            return _uiFont != null
                ? _uiFont
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        private static void Stretch(
            RectTransform value,
            float minX,
            float minY,
            float maxX,
            float maxY,
            float inset)
        {
            value.anchorMin = new Vector2(minX, minY);
            value.anchorMax = new Vector2(maxX, maxY);
            value.offsetMin = new Vector2(inset, inset);
            value.offsetMax = new Vector2(-inset, -inset);
        }

        private sealed class UiStep
        {
            public string StepId { get; set; }
            public string GroupText { get; set; }
            public string ButtonText { get; set; }
            public string ActionId { get; set; }
            public string SourceEntityId { get; set; }
            public string TargetEntityId { get; set; }
            public IReadOnlyDictionary<string, StructuredValue> Parameters { get; set; }
            public double ElapsedSeconds { get; set; }
        }

        private static class UiScenarioParser
        {
            private const int ColumnCount = 8;

            public static IReadOnlyList<UiStep> Parse(string csv)
            {
                var rows = (csv ?? throw new ArgumentNullException(nameof(csv)))
                    .Replace("\r\n", "\n")
                    .Split('\n')
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(ParseRow)
                    .ToArray();
                if (rows.Length < 2 || rows[0].Count != ColumnCount)
                {
                    throw new InvalidOperationException("UI 模拟步骤表为空或表头无效。");
                }

                var steps = rows.Skip(1).Select((row, index) =>
                {
                    if (row.Count != ColumnCount)
                    {
                        throw new InvalidOperationException(
                            $"UI 模拟步骤第 {index + 2} 行列数不是 {ColumnCount}。");
                    }

                    return new UiStep
                    {
                        StepId = Required(row[0], "步骤ID"),
                        GroupText = Required(row[1], "实验步骤"),
                        ButtonText = Required(row[2], "按钮文字"),
                        ActionId = Optional(row[3]),
                        SourceEntityId = Optional(row[4]),
                        TargetEntityId = Optional(row[5]),
                        Parameters = ParseParameters(row[6]),
                        ElapsedSeconds = string.IsNullOrWhiteSpace(row[7])
                            ? 0d
                            : double.Parse(row[7], CultureInfo.InvariantCulture)
                    };
                }).ToArray();

                var duplicate = steps
                    .GroupBy(value => value.StepId, StringComparer.Ordinal)
                    .FirstOrDefault(group => group.Count() > 1);
                if (duplicate != null)
                {
                    throw new InvalidOperationException(
                        $"UI 模拟步骤 ID“{duplicate.Key}”重复。");
                }

                return steps;
            }

            private static IReadOnlyDictionary<string, StructuredValue>
                ParseParameters(string value)
            {
                var result = new Dictionary<string, StructuredValue>(
                    StringComparer.Ordinal);
                if (string.IsNullOrWhiteSpace(value))
                {
                    return result;
                }

                foreach (var token in value.Split('|'))
                {
                    var separator = token.IndexOf('=');
                    if (separator <= 0)
                    {
                        throw new InvalidOperationException(
                            $"UI 参数“{token}”必须使用 名称=值。 ");
                    }

                    var key = token.Substring(0, separator).Trim();
                    var raw = token.Substring(separator + 1).Trim();
                    result.Add(key, Value(raw));
                }

                return result;
            }

            private static StructuredValue Value(string raw)
            {
                if (string.Equals(raw, "是", StringComparison.Ordinal)
                    || bool.TryParse(raw, out var boolean) && boolean)
                {
                    return StructuredValue.FromBoolean(true);
                }

                if (string.Equals(raw, "否", StringComparison.Ordinal)
                    || bool.TryParse(raw, out boolean) && !boolean)
                {
                    return StructuredValue.FromBoolean(false);
                }

                return double.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var number)
                    ? StructuredValue.FromNumber(number)
                    : StructuredValue.FromText(raw);
            }

            private static List<string> ParseRow(string row)
            {
                var result = new List<string>();
                var current = new System.Text.StringBuilder();
                var quoted = false;
                for (var index = 0; index < row.Length; index++)
                {
                    var character = row[index];
                    if (character == '"')
                    {
                        if (quoted && index + 1 < row.Length && row[index + 1] == '"')
                        {
                            current.Append('"');
                            index++;
                        }
                        else
                        {
                            quoted = !quoted;
                        }
                    }
                    else if (character == ',' && !quoted)
                    {
                        result.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(character);
                    }
                }

                if (quoted)
                {
                    throw new InvalidOperationException("UI 模拟步骤包含未闭合引号。");
                }

                result.Add(current.ToString().Trim());
                return result;
            }

            private static string Required(string value, string name) =>
                string.IsNullOrWhiteSpace(value)
                    ? throw new InvalidOperationException(name + "不能为空。")
                    : value.Trim();

            private static string Optional(string value) =>
                string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
