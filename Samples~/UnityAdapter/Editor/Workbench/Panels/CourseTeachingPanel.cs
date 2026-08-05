using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VirtualLab.Unity.Authoring.Workbench.Panels
{
    /// <summary>
    /// 错误严重度、继续方式和受影响目标分别配置，避免用一个模糊字段承载三种含义。
    /// </summary>
    public sealed class CourseTeachingPanel
    {
        private readonly CourseAuthoringWorkflow _workflow;
        private readonly HashSet<string> _affectedTargets =
            new HashSet<string>(StringComparer.Ordinal);
        private int _severityIndex;
        private int _continuationIndex;
        private string _riskId = string.Empty;
        private string _displayName = string.Empty;
        private string _prompt = string.Empty;
        private string _message = string.Empty;

        public CourseTeachingPanel(CourseAuthoringWorkflow workflow)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        public void Draw()
        {
            EditorGUILayout.LabelField(
                "教学目标与错误后果",
                EditorStyles.boldLabel);
            _riskId = EditorGUILayout.TextField("错误后果标识", _riskId);
            _displayName = EditorGUILayout.TextField("显示名称", _displayName);
            _severityIndex = EditorGUILayout.Popup(
                "错误严重度",
                _severityIndex,
                ToArray(_workflow.SeverityOptions));
            _continuationIndex = EditorGUILayout.Popup(
                "发生后如何继续",
                _continuationIndex,
                ToArray(_workflow.ContinuationOptions));
            _prompt = EditorGUILayout.TextField("提示文案", _prompt);

            EditorGUILayout.LabelField("受影响目标", EditorStyles.boldLabel);
            DrawTarget("整个实验");
            foreach (var item in _workflow.Objects)
            {
                DrawTarget(item.EntityId);
            }

            if (GUILayout.Button("保存错误后果"))
            {
                try
                {
                    _workflow.SetRisk(new RiskFormValue(
                        _riskId,
                        _workflow.SeverityOptions[_severityIndex],
                        _workflow.ContinuationOptions[_continuationIndex],
                        _affectedTargets,
                        _displayName,
                        _prompt));
                    _message = "错误后果已保存。";
                }
                catch (Exception exception)
                {
                    _message = exception.Message;
                }
            }

            if (_message.Length > 0)
            {
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
        }

        private void DrawTarget(string target)
        {
            var selected = _affectedTargets.Contains(target);
            var next = EditorGUILayout.ToggleLeft(target, selected);
            if (next) _affectedTargets.Add(target);
            else _affectedTargets.Remove(target);
        }

        private static string[] ToArray(
            System.Collections.Generic.IReadOnlyList<string> values)
        {
            var result = new string[values.Count];
            for (var index = 0; index < values.Count; index++)
            {
                result[index] = values[index];
            }

            return result;
        }
    }
}
