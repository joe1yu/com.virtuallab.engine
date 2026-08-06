using System;
using UnityEditor;

namespace VirtualLab.Unity.Authoring.Workbench.Panels
{
    /// <summary>
    /// 课程信息概览只读取工作流，不直接访问配置文件。
    /// </summary>
    public sealed class CourseInformationPanel
    {
        private readonly CourseAuthoringWorkflow _workflow;

        public CourseInformationPanel(CourseAuthoringWorkflow workflow)
        {
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        public void Draw()
        {
            var course = _workflow.Course;
            EditorGUILayout.LabelField("课程信息", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("课程标识", course.CourseId);
            EditorGUILayout.LabelField("显示名称", course.DisplayName);
            EditorGUILayout.LabelField("学科类型", course.DisciplinePackageId);
            EditorGUILayout.LabelField("操作者", course.ActorEntityId);
            EditorGUILayout.LabelField("实验总预制体路径", course.ExperimentPrefabPath);
            var status = _workflow.Section("课程信息");
            if (!status.IsComplete)
            {
                EditorGUILayout.HelpBox(
                    string.Join("；", status.MissingItems),
                    MessageType.Warning);
            }
        }
    }
}
