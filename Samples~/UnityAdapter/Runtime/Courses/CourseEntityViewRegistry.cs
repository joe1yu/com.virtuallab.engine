using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.UnityAdapters.Authoring;

namespace VirtualLab.UnityAdapters.Courses
{
    public sealed class CourseEntityViewRegistry
    {
        private readonly Dictionary<string, CourseEntityView> _views =
            new Dictionary<string, CourseEntityView>(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, CourseEntityView>
            _readOnlyViews;

        public CourseEntityViewRegistry()
        {
            _readOnlyViews =
                new ReadOnlyDictionary<string, CourseEntityView>(_views);
        }

        public IReadOnlyDictionary<string, CourseEntityView> Views =>
            _readOnlyViews;

        public int Count => _views.Count;

        public void Register(CourseEntityView view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (string.IsNullOrWhiteSpace(view.EntityId))
            {
                throw new ArgumentException(
                    "课程实体视图 ID 不能为空。",
                    nameof(view));
            }

            if (_views.TryGetValue(view.EntityId, out var existing) &&
                existing != view)
            {
                throw new InvalidOperationException(
                    $"课程实体视图 ID“{view.EntityId}”重复。");
            }

            _views[view.EntityId] = view;
        }

        public bool Unregister(CourseEntityView view)
        {
            if (ReferenceEquals(view, null))
            {
                return false;
            }

            var entry = _views.FirstOrDefault(value =>
                ReferenceEquals(value.Value, view));
            return entry.Key != null && _views.Remove(entry.Key);
        }

        public bool TryGet(string entityId, out CourseEntityView view)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                view = null;
                return false;
            }

            var normalized = entityId.Trim();
            if (!_views.TryGetValue(normalized, out view) || view == null)
            {
                _views.Remove(normalized);
                view = null;
                return false;
            }

            if (!string.Equals(
                    view.EntityId,
                    normalized,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"实体视图“{normalized}”注册后被改名，请重新装配场景。" );
            }

            return true;
        }

        public void Clear()
        {
            _views.Clear();
        }
    }
}
