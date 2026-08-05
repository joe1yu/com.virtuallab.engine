using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualLab.Unity.Authoring.Drafts
{
    public enum CourseSelectorKind
    {
        Entity,
        Type,
        Role,
        Tag
    }

    /// <summary>
    /// 课程选择器只描述要匹配的对象，不携带规则或 Unity 查询逻辑。
    /// </summary>
    public sealed class CourseSelector
    {
        public CourseSelector(CourseSelectorKind kind, string value)
        {
            if (!Enum.IsDefined(typeof(CourseSelectorKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            Value = string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("选择值不能为空。", nameof(value))
                : value.Trim();
        }

        public CourseSelectorKind Kind { get; }
        public string Value { get; }

        public IReadOnlyList<CourseDraftObject> Select(
            IEnumerable<CourseDraftObject> objects)
        {
            if (objects == null)
            {
                throw new ArgumentNullException(nameof(objects));
            }

            return objects
                .Where(Matches)
                .OrderBy(value => value.EntityId, StringComparer.Ordinal)
                .GroupBy(value => value.EntityId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }

        /// <summary>
        /// 匹配展开后的对象属性，使模板建议角色和标签也能参与课程选择。
        /// </summary>
        public bool Matches(
            string entityId,
            string entityType,
            IEnumerable<string> roles,
            IEnumerable<string> tags) => Kind switch
        {
            CourseSelectorKind.Entity => entityId == Value,
            CourseSelectorKind.Type => entityType == Value,
            CourseSelectorKind.Role => (roles ?? Array.Empty<string>())
                .Contains(Value, StringComparer.Ordinal),
            CourseSelectorKind.Tag => (tags ?? Array.Empty<string>())
                .Contains(Value, StringComparer.Ordinal),
            _ => false
        };

        private bool Matches(CourseDraftObject value) => Matches(
            value.EntityId,
            value.EntityType,
            List(value.Roles),
            List(value.Tags));

        public static bool TryParse(
            string configuredKind,
            string value,
            out CourseSelector selector)
        {
            var kind = configuredKind?.Trim() switch
            {
                "实体" => CourseSelectorKind.Entity,
                "类型" => CourseSelectorKind.Type,
                "角色" => CourseSelectorKind.Role,
                "标签" => CourseSelectorKind.Tag,
                _ => (CourseSelectorKind?)null
            };
            selector = kind.HasValue && !string.IsNullOrWhiteSpace(value)
                ? new CourseSelector(kind.Value, value)
                : null;
            return selector != null;
        }

        internal static IReadOnlyList<string> List(string configured) =>
            (configured ?? string.Empty)
                .Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
    }
}
