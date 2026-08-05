using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VirtualLab.Unity.Authoring.Catalogs;

namespace VirtualLab.Unity.Authoring.Workbench.Panels
{
    /// <summary>
    /// 配置现有实验对象之间的初始关系；关系类型完全来自模块注册目录。
    /// </summary>
    public sealed class CourseSetupPanel
    {
        private readonly CourseAuthoringWorkflow _workflow;
        private int _relationIndex;
        private int _sourceIndex;
        private int _targetIndex;
        private int _sourcePortIndex;
        private int _targetPortIndex;
        private string _message = string.Empty;

        public CourseSetupPanel(CourseAuthoringWorkflow workflow)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("装置与初始状态", EditorStyles.boldLabel);
            var relations = _workflow.RelationTypes;
            var objects = _workflow.Objects.ToArray();
            if (relations.Count == 0 || objects.Length < 2)
            {
                EditorGUILayout.HelpBox(
                    relations.Count == 0
                        ? "当前模块没有注册关系类型。"
                        : "至少需要两个实验对象才能配置关系。",
                    MessageType.Info);
                return;
            }

            _relationIndex = Math.Min(_relationIndex, relations.Count - 1);
            _sourceIndex = Math.Min(_sourceIndex, objects.Length - 1);
            _targetIndex = Math.Min(_targetIndex, objects.Length - 1);
            _relationIndex = EditorGUILayout.Popup(
                "关系",
                _relationIndex,
                relations.Select(value => value.DisplayName).ToArray());
            _sourceIndex = EditorGUILayout.Popup(
                "来源用品",
                _sourceIndex,
                objects.Select(value => value.DisplayName).ToArray());
            _targetIndex = EditorGUILayout.Popup(
                "目标用品",
                _targetIndex,
                objects.Select(value => value.DisplayName).ToArray());
            var sourcePorts = _workflow.PortsOf(objects[_sourceIndex].EntityId);
            var targetPorts = _workflow.PortsOf(objects[_targetIndex].EntityId);
            _sourcePortIndex = PortPopup("来源端口", _sourcePortIndex, sourcePorts);
            _targetPortIndex = PortPopup("目标端口", _targetPortIndex, targetPorts);
            if (GUILayout.Button("建立初始关系"))
            {
                var result = _workflow.TrySetRelation(
                    relations[_relationIndex].OptionId,
                    objects[_sourceIndex].EntityId,
                    objects[_targetIndex].EntityId,
                    PortId(sourcePorts, _sourcePortIndex),
                    PortId(targetPorts, _targetPortIndex));
                _message = result.IsSuccess ? "初始关系已建立。" : result.Message;
            }

            if (_message.Length > 0)
            {
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }

            foreach (var relation in _workflow.InitialRelations)
            {
                EditorGUILayout.LabelField(
                    relation.SourceEntityId + " → " + relation.TargetEntityId,
                    relation.RelationType);
            }
        }

        private static int PortPopup(
            string label,
            int selected,
            System.Collections.Generic.IReadOnlyList<AuthoringPortDescriptor> ports)
        {
            selected = Math.Min(selected, ports.Count);
            return EditorGUILayout.Popup(
                label,
                selected,
                new[] { "不指定" }
                    .Concat(ports.Select(value => value.DisplayName))
                    .ToArray());
        }

        private static string PortId(
            System.Collections.Generic.IReadOnlyList<AuthoringPortDescriptor> ports,
            int selected) =>
            selected <= 0 || selected > ports.Count
                ? string.Empty
                : ports[selected - 1].PortId;
    }
}
