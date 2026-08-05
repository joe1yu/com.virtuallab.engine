using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Kernel;

namespace VirtualLab.Teaching.Courses
{
    /// <summary>
    /// 教学模块状态的世界访问入口。
    /// </summary>
    public static class TeachingStateWorldExtensions
    {
        public static TeachingStateCollection RequireTeachingStates(
            this ExperimentWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            return world.RequireWorldState<TeachingStateCollection>(
                TeachingWorldStateTypeIds.NamedStates);
        }
    }

    /// <summary>
    /// 命名教学状态存档协议字段。
    /// </summary>
    public static class TeachingStateSnapshotKeys
    {
        public const string StateEntry = "命名教学状态";
        public const string EntityId = "实体标识";
        public const string StateId = "教学状态";
    }

    /// <summary>
    /// Teaching 模块负责自己的状态存档格式，Application 只传递通用条目。
    /// </summary>
    public sealed class TeachingStateCourseCodec : ICourseWorldStateCodec
    {
        public VirtualLab.Domain.WorldStates.WorldStateTypeId TypeId =>
            TeachingWorldStateTypeIds.NamedStates;

        public CourseWorldState Capture(ExperimentWorld world)
        {
            var entries = world.RequireTeachingStates().Entries.Select(value =>
                new CourseWorldStateEntry(
                    TeachingStateSnapshotKeys.StateEntry,
                    new[]
                    {
                        new KeyValuePair<string, string>(
                            TeachingStateSnapshotKeys.EntityId,
                            value.Key.Value),
                        new KeyValuePair<string, string>(
                            TeachingStateSnapshotKeys.StateId,
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
                    $"命名教学状态不能从世界状态“{state.TypeId}”恢复。");
            }

            var entries = state.Entries.Select(entry =>
            {
                if (entry.EntryType != TeachingStateSnapshotKeys.StateEntry
                    || entry.Values.Count != 2)
                {
                    throw new InvalidOperationException(
                        $"命名教学状态包含未知或字段不完整的条目“{entry.EntryType}”。");
                }

                return new KeyValuePair<EntityId, string>(
                    new EntityId(Read(entry, TeachingStateSnapshotKeys.EntityId)),
                    Read(entry, TeachingStateSnapshotKeys.StateId));
            });
            world.RequireTeachingStates().ReplaceContents(entries);
        }

        private static string Read(CourseWorldStateEntry entry, string key)
        {
            if (!entry.Values.TryGetValue(key, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"命名教学状态条目缺少字段“{key}”。");
            }

            return value;
        }
    }

    internal abstract class TeachingStateOperation : IConfiguredStateOperation
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

            var entityId = ReadOptionalText(
                    mutation,
                    TeachingConfigurationKeys.EntityId)
                ?? request.SourceEntityId;
            Apply(
                world.RequireTeachingStates(),
                entityId,
                ReadRequiredText(mutation, TeachingConfigurationKeys.StateId));
        }

        protected abstract void Apply(
            TeachingStateCollection states,
            string entityId,
            string stateId);

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
    }

    internal sealed class AddTeachingStateOperation : TeachingStateOperation
    {
        public override string OperationId =>
            TeachingConfiguredStateOperationIds.AddState;

        protected override void Apply(
            TeachingStateCollection states,
            string entityId,
            string stateId) => states.Add(entityId, stateId);
    }

    internal sealed class RemoveTeachingStateOperation : TeachingStateOperation
    {
        public override string OperationId =>
            TeachingConfiguredStateOperationIds.RemoveState;

        protected override void Apply(
            TeachingStateCollection states,
            string entityId,
            string stateId) => states.Remove(entityId, stateId);
    }
}
