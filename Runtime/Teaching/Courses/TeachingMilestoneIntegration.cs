using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Kernel;

namespace VirtualLab.Teaching.Courses
{
    /// <summary>
    /// 教学模块课程里程碑的世界访问入口。
    /// </summary>
    public static class TeachingMilestoneWorldExtensions
    {
        public static TeachingMilestoneCollection RequireCourseMilestones(
            this ExperimentWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            return world.RequireWorldState<TeachingMilestoneCollection>(
                TeachingWorldStateTypeIds.CourseMilestones);
        }
    }

    /// <summary>
    /// 课程里程碑存档协议字段。
    /// </summary>
    public static class TeachingMilestoneSnapshotKeys
    {
        public const string MilestoneEntry = "课程里程碑";
        public const string EntityId = "实体标识";
        public const string MilestoneId = "里程碑名称";
    }

    /// <summary>
    /// Teaching 模块负责自己的里程碑存档格式，Application 只传递通用条目。
    /// </summary>
    public sealed class TeachingMilestoneCourseCodec : ICourseWorldStateCodec
    {
        public VirtualLab.Domain.WorldStates.WorldStateTypeId TypeId =>
            TeachingWorldStateTypeIds.CourseMilestones;

        public CourseWorldState Capture(ExperimentWorld world)
        {
            var entries = world.RequireCourseMilestones().Entries.Select(value =>
                new CourseWorldStateEntry(
                    TeachingMilestoneSnapshotKeys.MilestoneEntry,
                    new[]
                    {
                        new KeyValuePair<string, string>(
                            TeachingMilestoneSnapshotKeys.EntityId,
                            value.Key.Value),
                        new KeyValuePair<string, string>(
                            TeachingMilestoneSnapshotKeys.MilestoneId,
                            value.Value)
                    }));
            return new CourseWorldState(TypeId, entries);
        }

        public void Restore(ExperimentWorld world, CourseWorldState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.TypeId != TypeId)
            {
                throw new InvalidOperationException(
                    $"课程里程碑不能从世界状态“{state.TypeId}”恢复。");
            }

            var entries = state.Entries.Select(entry =>
            {
                if (entry.EntryType != TeachingMilestoneSnapshotKeys.MilestoneEntry
                    || entry.Values.Count != 2)
                {
                    throw new InvalidOperationException(
                        $"课程里程碑包含未知或字段不完整的条目“{entry.EntryType}”。");
                }

                return new KeyValuePair<EntityId, string>(
                    new EntityId(Read(
                        entry,
                        TeachingMilestoneSnapshotKeys.EntityId)),
                    Read(entry, TeachingMilestoneSnapshotKeys.MilestoneId));
            });
            world.RequireCourseMilestones().ReplaceContents(entries);
        }

        private static string Read(CourseWorldStateEntry entry, string key)
        {
            if (!entry.Values.TryGetValue(key, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"课程里程碑条目缺少字段“{key}”。");
            }

            return value;
        }
    }

    internal abstract class TeachingMilestoneOperation : IConfiguredStateOperation
    {
        public abstract string OperationId { get; }

        public void Apply(
            SemanticActionRequest request,
            ExperimentWorld world,
            ConfiguredMutationDefinition mutation)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!ShouldApply(world, mutation))
            {
                return;
            }

            var entityId = ReadOptionalText(
                    mutation,
                    TeachingConfigurationKeys.EntityId)
                ?? request.SourceEntityId;
            Record(
                world.RequireCourseMilestones(),
                entityId,
                ReadRequiredText(
                    mutation,
                    TeachingConfigurationKeys.MilestoneId));
        }

        protected virtual bool ShouldApply(
            ExperimentWorld world,
            ConfiguredMutationDefinition mutation) => true;

        protected abstract void Record(
            TeachingMilestoneCollection milestones,
            string entityId,
            string milestoneId);

        private static string ReadRequiredText(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            return ReadOptionalText(mutation, key)
                ?? throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”缺少参数“{key}”。");
        }

        private static string ReadOptionalText(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value))
            {
                return null;
            }

            if (value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”的参数“{key}”必须是非空文本。");
            }

            return value.Text.Trim();
        }

        protected static string RequiredText(
            ConfiguredMutationDefinition mutation,
            string key) => ReadRequiredText(mutation, key);

        protected static double RequiredNumber(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Number)
            {
                throw new ArgumentException(
                    $"状态变更“{mutation.MutationId}”的参数“{key}”必须是数值。");
            }

            return value.Number;
        }
    }

    internal sealed class RecordCourseMilestoneOperation :
        TeachingMilestoneOperation
    {
        public override string OperationId =>
            TeachingConfiguredMilestoneOperationIds.RecordMilestone;

        protected override void Record(
            TeachingMilestoneCollection milestones,
            string entityId,
            string milestoneId) => milestones.Record(entityId, milestoneId);
    }

    /// <summary>
    /// 仅当权威世界标量等于期望值时记录教学结果，避免错误后果与成功目标并存。
    /// </summary>
    internal sealed class ConditionalRecordCourseMilestoneOperation :
        TeachingMilestoneOperation
    {
        public override string OperationId =>
            TeachingConfiguredMilestoneOperationIds
                .RecordMilestoneWhenScalarEquals;

        protected override bool ShouldApply(
            ExperimentWorld world,
            ConfiguredMutationDefinition mutation)
        {
            var scalarKey = RequiredText(
                mutation,
                TeachingConfigurationKeys.ScalarKey);
            var expected = RequiredNumber(
                mutation,
                TeachingConfigurationKeys.ExpectedValue);
            return world.TryGetScalar(scalarKey, out var actual)
                   && Math.Abs(actual.Value - expected) <= 0.000001d;
        }

        protected override void Record(
            TeachingMilestoneCollection milestones,
            string entityId,
            string milestoneId) => milestones.Record(entityId, milestoneId);
    }
}
