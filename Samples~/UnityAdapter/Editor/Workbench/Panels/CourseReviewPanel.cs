using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VirtualLab.Unity.Authoring.Workbench.Panels
{
    /// <summary>
    /// 汇总草稿或最近一次编译结果；未编译的检查明确显示为待检查。
    /// </summary>
    public sealed class CourseReviewPanel
    {
        private readonly CourseAuthoringWorkflow _workflow;
        private int _presentationSignal;
        private int _presentationSubject;
        private int _acceptanceOperation;
        private int _acceptanceSource;
        private int _acceptanceTarget;
        private int _acceptanceOrder = 1;
        private string _presentationId = string.Empty;
        private string _presentationTrigger = string.Empty;
        private string _presentationParameters = string.Empty;
        private string _scenarioId = string.Empty;
        private string _message = string.Empty;

        public CourseReviewPanel(CourseAuthoringWorkflow workflow)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
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
                        "领域事件",
                        _presentationTrigger,
                        "实体",
                        objects[_presentationSubject].EntityId,
                        signals[_presentationSignal].OptionId,
                        "对象根节点",
                        string.Empty,
                        _presentationParameters);
                    _message = "表现覆盖已保存。";
                }
                catch (Exception exception)
                {
                    _message = exception.Message;
                }
            }
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
