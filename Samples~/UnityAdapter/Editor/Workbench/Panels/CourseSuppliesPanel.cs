using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VirtualLab.Unity.Authoring.Workbench.Panels
{
    /// <summary>
    /// 从模块提供的类别和模板构建用品选择，不展示内部组件或语义动作标识。
    /// </summary>
    public sealed class CourseSuppliesPanel
    {
        private readonly CourseAuthoringWorkflow _workflow;
        private int _categoryIndex;
        private int _templateIndex;
        private int _quantity = 1;
        private string _message = string.Empty;

        public CourseSuppliesPanel(CourseAuthoringWorkflow workflow)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        public void Draw()
        {
            EditorGUILayout.LabelField("实验用品", EditorStyles.boldLabel);
            var categories = _workflow.Categories;
            if (categories.Count == 0)
            {
                EditorGUILayout.HelpBox("当前模块没有注册用品类别。", MessageType.Warning);
                return;
            }

            _categoryIndex = Math.Min(_categoryIndex, categories.Count - 1);
            var nextCategory = EditorGUILayout.Popup(
                "用品类别",
                _categoryIndex,
                categories.Select(value => value.DisplayName).ToArray());
            if (nextCategory != _categoryIndex)
            {
                _categoryIndex = nextCategory;
                _templateIndex = 0;
            }

            var category = categories[_categoryIndex];
            var templates = _workflow.TemplatesIn(category.CategoryId);
            if (templates.Count == 0)
            {
                EditorGUILayout.HelpBox("该类别没有可用用品模板。", MessageType.Info);
                return;
            }

            _templateIndex = Math.Min(_templateIndex, templates.Count - 1);
            _templateIndex = EditorGUILayout.Popup(
                "用品",
                _templateIndex,
                templates.Select(value => value.DisplayName).ToArray());
            _quantity = EditorGUILayout.IntSlider("数量", _quantity, 1, 20);
            if (GUILayout.Button("加入实验用品"))
            {
                try
                {
                    var added = _workflow.AddSupplies(
                        category.CategoryId,
                        templates[_templateIndex].TemplateId,
                        _quantity);
                    _message = "已加入：" + string.Join(
                        "、",
                        added.Select(value => value.DisplayName));
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

            foreach (var item in _workflow.Objects.Where(value =>
                         value.EntityId != _workflow.Course.ActorEntityId))
            {
                EditorGUILayout.LabelField(item.DisplayName, item.EntityType);
            }
        }
    }
}
