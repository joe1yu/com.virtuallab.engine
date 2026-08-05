using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VirtualLab.Unity.Authoring.Catalogs;

namespace VirtualLab.Unity.Authoring.Workbench.Panels
{
    /// <summary>
    /// 用句子式表单配置课程操作特例和科学过程，不暴露语义动作或状态操作协议。
    /// </summary>
    public sealed class CourseOperationsPanel
    {
        private static readonly string[] SelectorKinds =
            { "实体", "类型", "角色", "标签" };
        private static readonly string[] HandlingModes =
            { "收紧默认", "禁用默认", "新增特殊" };

        private readonly CourseAuthoringWorkflow _workflow;
        private readonly Dictionary<string, string> _processValues =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private int _operationIndex;
        private int _sourceKind;
        private int _targetKind;
        private int _handlingIndex;
        private int _consequenceIndex;
        private int _processIndex;
        private bool _hasTarget = true;
        private string _overrideId = string.Empty;
        private string _sourceValue = string.Empty;
        private string _targetValue = string.Empty;
        private string _fact = string.Empty;
        private string _comparison = string.Empty;
        private string _expected = string.Empty;
        private string _rejection = string.Empty;
        private string _processId = string.Empty;
        private string _processSubject = string.Empty;
        private string _message = string.Empty;

        public CourseOperationsPanel(CourseAuthoringWorkflow workflow)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("操作与科学过程", EditorStyles.boldLabel);
            DrawOperationOverride();
            EditorGUILayout.Space();
            DrawProcess();
            if (_message.Length > 0)
            {
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
        }

        private void DrawOperationOverride()
        {
            var operations = _workflow.Operations;
            if (operations.Count == 0)
            {
                EditorGUILayout.HelpBox("当前模块没有注册抽象操作。", MessageType.Info);
                return;
            }

            _operationIndex = Math.Min(_operationIndex, operations.Count - 1);
            var operation = operations[_operationIndex];
            _overrideId = EditorGUILayout.TextField("特例名称", _overrideId);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("当", GUILayout.Width(25f));
            _sourceKind = EditorGUILayout.Popup(_sourceKind, SelectorKinds,
                GUILayout.Width(65f));
            _sourceValue = EditorGUILayout.TextField(_sourceValue);
            EditorGUILayout.EndHorizontal();
            _hasTarget = EditorGUILayout.Toggle("需要目标", _hasTarget);
            if (_hasTarget)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("目标", GUILayout.Width(35f));
                _targetKind = EditorGUILayout.Popup(_targetKind, SelectorKinds,
                    GUILayout.Width(65f));
                _targetValue = EditorGUILayout.TextField(_targetValue);
                EditorGUILayout.EndHorizontal();
            }
            _operationIndex = EditorGUILayout.Popup(
                "执行操作",
                _operationIndex,
                operations.Select(value => value.DisplayName).ToArray());
            operation = operations[_operationIndex];
            EditorGUILayout.HelpBox(
                LifecycleDescription(operation.Lifecycle),
                MessageType.None);
            _fact = EditorGUILayout.TextField("并且事实", _fact);
            _comparison = EditorGUILayout.TextField("比较方式", _comparison);
            _expected = EditorGUILayout.TextField("期望值", _expected);
            _handlingIndex = EditorGUILayout.Popup(
                "处理方式",
                _handlingIndex,
                HandlingModes);
            _rejection = EditorGUILayout.TextField("拒绝或提示文案", _rejection);
            var consequences = _workflow.ConsequenceTemplates;
            if (consequences.Count > 0)
            {
                _consequenceIndex = Math.Min(
                    _consequenceIndex,
                    consequences.Count);
                _consequenceIndex = EditorGUILayout.Popup(
                    "后果模板",
                    _consequenceIndex,
                    new[] { "不附加" }
                        .Concat(consequences.Select(value => value.DisplayName))
                        .ToArray());
            }
            if (GUILayout.Button("保存操作特例"))
            {
                try
                {
                    _workflow.SetOperationOverride(new OperationOverrideFormValue(
                        _overrideId,
                        SelectorKinds[_sourceKind],
                        _sourceValue,
                        _hasTarget ? SelectorKinds[_targetKind] : string.Empty,
                        _hasTarget ? _targetValue : string.Empty,
                        operation.OperationId,
                        HandlingModes[_handlingIndex],
                        _fact,
                        _comparison,
                        _expected,
                        rejectionMessage: _rejection,
                        consequenceTemplateId: _consequenceIndex == 0
                            ? string.Empty
                            : consequences[_consequenceIndex - 1].OptionId));
                    _message = "操作特例已保存。";
                }
                catch (Exception exception)
                {
                    _message = exception.Message;
                }
            }
        }

        private void DrawProcess()
        {
            var processes = _workflow.Processes;
            if (processes.Count == 0) return;
            _processIndex = Math.Min(_processIndex, processes.Count - 1);
            _processIndex = EditorGUILayout.Popup(
                "科学过程",
                _processIndex,
                processes.Select(value => value.DisplayName).ToArray());
            var process = processes[_processIndex];
            _processId = EditorGUILayout.TextField("过程名称", _processId);
            _processSubject = EditorGUILayout.TextField("作用对象", _processSubject);
            foreach (var parameter in process.Parameters)
            {
                _processValues.TryGetValue(parameter.ParameterId, out var current);
                _processValues[parameter.ParameterId] = EditorGUILayout.TextField(
                    new GUIContent(parameter.DisplayName, parameter.Description),
                    current ?? parameter.DefaultValue);
            }

            if (GUILayout.Button("保存科学过程"))
            {
                try
                {
                    _workflow.SetProcess(
                        _processId,
                        process.ProcessId,
                        "实体",
                        _processSubject,
                        string.Join(";", process.Parameters.Select(parameter =>
                            parameter.ParameterId + "="
                            + _processValues[parameter.ParameterId])));
                    _message = "科学过程已保存。";
                }
                catch (Exception exception)
                {
                    _message = exception.Message;
                }
            }
        }

        private static string LifecycleDescription(
            AuthoringOperationLifecycle lifecycle) => lifecycle switch
            {
                AuthoringOperationLifecycle.Instant =>
                    "即时操作：操作完成时提交一次观测。",
                AuthoringOperationLifecycle.Continuous =>
                    "持续操作：表现层报告开始和完成，期间只提交观测。",
                AuthoringOperationLifecycle.Manipulation =>
                    "操纵操作：表现层报告开始和完成，移动过程只提交位置或角度观测。",
                _ => "未注册的操作生命周期。"
            };
    }
}
