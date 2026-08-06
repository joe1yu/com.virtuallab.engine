using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VirtualLab.Unity.Authoring.Diagnostics;
using VirtualLab.Unity.Authoring.Drafts;

namespace VirtualLab.Unity.Authoring.Workbench.Panels
{
    /// <summary>
    /// 汇总草稿或最近一次编译结果；未编译的检查明确显示为待检查。
    /// </summary>
    public sealed class CourseReviewPanel
    {
        private readonly CourseAuthoringWorkflow _workflow;
        private readonly Action<CourseDiagnosticTarget> _navigate;
        private int _presentationSignal;
        private int _presentationSubject;
        private int _presentationTriggerType;
        private int _presentationTriggerSource;
        private int _presentationTriggerTarget;
        private int _acceptanceOperation;
        private int _acceptanceSource;
        private int _acceptanceTarget;
        private int _acceptanceOrder = 1;
        private string _presentationId = string.Empty;
        private string _presentationTrigger = string.Empty;
        private string _presentationParameters = string.Empty;
        private string _scenarioId = string.Empty;
        private string _message = string.Empty;

        public CourseReviewPanel(
            CourseAuthoringWorkflow workflow,
            Action<CourseDiagnosticTarget> navigate = null)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _navigate = navigate;
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("检查与生成", EditorStyles.boldLabel);
            var review = _workflow.Review;
            EditorGUILayout.LabelField("展开后对象数", review.ObjectCount.ToString());
            EditorGUILayout.LabelField("展开后动作数", review.ActionCount.ToString());
            EditorGUILayout.LabelField("科学过程数", review.ProcessCount.ToString());
            DrawCheck("教学目标可达性", review.GoalsReachable);
            DrawCheck("实验总预制体绑定", review.PrefabBindingsValid);
            DrawCheck("执行方式注册", review.ExecutionModesRegistered);
            EditorGUILayout.LabelField(
                "表现配置数",
                _workflow.Presentations.Count.ToString());
            EditorGUILayout.LabelField(
                "验收记录数",
                _workflow.AcceptanceRecords.Count.ToString());

            DrawDiagnostics();

            DrawPresentationForm();
            DrawAcceptanceForm();

            if (GUILayout.Button("查看原始表"))
            {
                EditorUtility.RevealInFinder(_workflow.AuthoringDirectory);
            }

            if (_message.Length > 0)
            {
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
        }

        private void DrawDiagnostics()
        {
            var diagnostics = _workflow.CompilationDiagnostics;
            if (diagnostics.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"编译问题（{diagnostics.Count}）",
                EditorStyles.boldLabel);
            foreach (var diagnostic in diagnostics)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    diagnostic.Severity == CourseDiagnosticSeverity.Error
                        ? "错误"
                        : "警告",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "问题",
                    diagnostic.Reason,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "建议",
                    diagnostic.Suggestion,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "位置",
                    DescribeTarget(diagnostic.Target),
                    EditorStyles.wordWrappedLabel);

                EditorGUI.BeginDisabledGroup(
                    _navigate == null
                    || string.IsNullOrWhiteSpace(diagnostic.Target.FileName));
                if (GUILayout.Button(
                        string.IsNullOrWhiteSpace(diagnostic.Target.ActionId)
                            ? CourseDiagnosticActionIds.LocateConfiguration
                            : diagnostic.Target.ActionId))
                {
                    _navigate?.Invoke(diagnostic.Target);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndVertical();
            }
        }

        private static string DescribeTarget(CourseDiagnosticTarget target)
        {
            var parts = new[]
                {
                    target.FileName,
                    target.ConfigurationId,
                    target.ColumnName
                }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" / ", parts);
        }

        private void DrawPresentationForm()
        {
            var signals = _workflow.PresentationSignals;
            var objects = _workflow.Objects;
            if (signals.Count == 0 || objects.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("表现覆盖", EditorStyles.boldLabel);
            _presentationId = EditorGUILayout.TextField(
                "表现名称",
                _presentationId);
            _presentationTrigger = EditorGUILayout.TextField(
                "触发值",
                _presentationTrigger);
            _presentationTriggerType = EditorGUILayout.Popup(
                "触发类型",
                Math.Min(
                    _presentationTriggerType,
                    CoursePresentationTriggerNames.All.Count - 1),
                CoursePresentationTriggerNames.All.ToArray());
            var triggerType = CoursePresentationTriggerNames.All[
                _presentationTriggerType];
            var triggerObjectNames = new[] { "任意对象" }
                .Concat(objects.Select(value => value.DisplayName))
                .ToArray();
            EditorGUI.BeginDisabledGroup(
                !CoursePresentationTriggerNames.SupportsActionEntities(
                    triggerType));
            _presentationTriggerSource = EditorGUILayout.Popup(
                "触发来源",
                Math.Min(
                    _presentationTriggerSource,
                    triggerObjectNames.Length - 1),
                triggerObjectNames);
            _presentationTriggerTarget = EditorGUILayout.Popup(
                "触发目标",
                Math.Min(
                    _presentationTriggerTarget,
                    triggerObjectNames.Length - 1),
                triggerObjectNames);
            EditorGUI.EndDisabledGroup();
            _presentationSignal = EditorGUILayout.Popup(
                "表现信号",
                Math.Min(_presentationSignal, signals.Count - 1),
                signals.Select(value => value.DisplayName).ToArray());
            _presentationSubject = EditorGUILayout.Popup(
                "作用对象",
                Math.Min(_presentationSubject, objects.Count - 1),
                objects.Select(value => value.DisplayName).ToArray());
            _presentationParameters = EditorGUILayout.TextField(
                "表现参数",
                _presentationParameters);
            if (GUILayout.Button("保存表现覆盖"))
            {
                try
                {
                    _workflow.SetPresentation(
                        _presentationId,
                        triggerType,
                        _presentationTrigger,
                        "实体",
                        objects[_presentationSubject].EntityId,
                        signals[_presentationSignal].OptionId,
                        "对象根节点",
                        string.Empty,
                        _presentationParameters,
                        TriggerEntityId(
                            objects,
                            _presentationTriggerSource,
                            triggerType),
                        TriggerEntityId(
                            objects,
                            _presentationTriggerTarget,
                            triggerType));
                    _message = "表现覆盖已保存。";
                }
                catch (Exception exception)
                {
                    _message = exception.Message;
                }
            }
        }

        private static string TriggerEntityId(
            System.Collections.Generic.IReadOnlyList<
                CourseDraftObject> objects,
            int selectedIndex,
            string triggerType)
        {
            if (!CoursePresentationTriggerNames.SupportsActionEntities(
                    triggerType)
                || selectedIndex <= 0)
            {
                return string.Empty;
            }

            return objects[Math.Min(selectedIndex - 1, objects.Count - 1)]
                .EntityId;
        }

        private void DrawAcceptanceForm()
        {
            var operations = _workflow.Operations;
            var objects = _workflow.Objects;
            if (operations.Count == 0 || objects.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("验收场景", EditorStyles.boldLabel);
            _scenarioId = EditorGUILayout.TextField("场景名称", _scenarioId);
            _acceptanceOrder = EditorGUILayout.IntField("顺序", _acceptanceOrder);
            _acceptanceOperation = EditorGUILayout.Popup(
                "操作",
                Math.Min(_acceptanceOperation, operations.Count - 1),
                operations.Select(value => value.DisplayName).ToArray());
            _acceptanceSource = EditorGUILayout.Popup(
                "来源",
                Math.Min(_acceptanceSource, objects.Count - 1),
                objects.Select(value => value.DisplayName).ToArray());
            _acceptanceTarget = EditorGUILayout.Popup(
                "目标",
                Math.Min(_acceptanceTarget, objects.Count),
                new[] { "无" }
                    .Concat(objects.Select(value => value.DisplayName))
                    .ToArray());
            if (GUILayout.Button("加入验收动作"))
            {
                try
                {
                    _workflow.SetAcceptanceAction(
                        _scenarioId,
                        _acceptanceOrder,
                        operations[_acceptanceOperation].OperationId,
                        objects[_acceptanceSource].EntityId,
                        _acceptanceTarget == 0
                            ? string.Empty
                            : objects[_acceptanceTarget - 1].EntityId);
                    _message = "验收动作已保存。";
                }
                catch (Exception exception)
                {
                    _message = exception.Message;
                }
            }
        }

        private static void DrawCheck(string label, bool? result)
        {
            EditorGUILayout.LabelField(
                label,
                result.HasValue
                    ? result.Value ? "通过" : "未通过"
                    : "待编译检查");
        }
    }
}
