using System;
using UnityEngine;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.UnityAdapters.Presentation
{
    /// <summary>
    /// 直接操纵期间的临时表现预览。输入适配器只提供目标姿态；只有语义动作
    /// 获准后才提交预览，否则恢复抓取前姿态，避免表现先于权威状态生效。
    /// </summary>
    public sealed class CourseManipulationPreview
    {
        private CourseEntityView _view;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;

        public bool IsActive => _view != null;

        public void Begin(CourseEntityView view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (IsActive)
            {
                throw new InvalidOperationException("已有直接操纵预览尚未结束。");
            }

            _view = view;
            _originalPosition = view.transform.position;
            _originalRotation = view.transform.rotation;
        }

        public void MoveTo(Vector3 position)
        {
            EnsureActive();
            var body = _view.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.MovePosition(position);
            }
            else
            {
                _view.transform.position = position;
            }

            UnityEngine.Physics.SyncTransforms();
        }

        public void RotateAround(
            Vector3 firstAxis,
            float firstDegrees,
            Vector3 secondAxis,
            float secondDegrees)
        {
            EnsureActive();
            _view.transform.Rotate(firstAxis, firstDegrees, Space.World);
            _view.transform.Rotate(secondAxis, secondDegrees, Space.World);
            UnityEngine.Physics.SyncTransforms();
        }

        public void Commit()
        {
            EnsureActive();
            _view = null;
        }

        public void Cancel()
        {
            EnsureActive();
            var body = _view.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = _originalPosition;
                body.rotation = _originalRotation;
            }

            _view.transform.SetPositionAndRotation(
                _originalPosition,
                _originalRotation);
            UnityEngine.Physics.SyncTransforms();
            _view = null;
        }

        private void EnsureActive()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("当前没有直接操纵预览。");
            }
        }
    }
}
