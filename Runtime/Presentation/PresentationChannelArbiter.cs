using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VirtualLab.Presentation
{
    public sealed class PresentationChannelTransition
    {
        public PresentationChannelTransition(
            PresentationEffectCommand previous,
            PresentationEffectCommand current)
        {
            Previous = previous;
            Current = current;
        }

        public PresentationEffectCommand Previous { get; }

        public PresentationEffectCommand Current { get; }
    }

    public sealed class PresentationChannelSnapshot
    {
        public PresentationChannelSnapshot(
            PresentationTargetReference target,
            string channel,
            PresentationEffectCommand previous)
        {
            Target = target ??
                throw new ArgumentNullException(nameof(target));
            Channel = PresentationContractGuard.Required(
                channel,
                "表现通道");
            Previous = previous;
        }

        public PresentationTargetReference Target { get; }

        public string Channel { get; }

        public PresentationEffectCommand Previous { get; }
    }

    /// <summary>
    /// 按“目标实体 + 通道”仲裁表现。权威状态与临时效果分开保存，
    /// 临时效果结束后会直接恢复最新权威状态，不依赖事件回放。
    /// </summary>
    public sealed class PresentationChannelArbiter
    {
        private readonly Dictionary<ChannelKey, List<Candidate>>
            _authoritative = new Dictionary<ChannelKey, List<Candidate>>();
        private readonly Dictionary<ChannelKey, List<Candidate>>
            _temporary = new Dictionary<ChannelKey, List<Candidate>>();
        private long _sequence;

        public IReadOnlyList<PresentationEffectCommand> CurrentCommands =>
            new ReadOnlyCollection<PresentationEffectCommand>(
                SnapshotCurrent()
                    .Values
                    .Where(value => value != null)
                    .OrderBy(value => value.CommandId, StringComparer.Ordinal)
                    .ToArray());

        public IReadOnlyList<PresentationChannelTransition>
            SynchronizeAuthoritativeState(
                IEnumerable<PresentationEffectCommand> commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            var before = SnapshotCurrent();
            _authoritative.Clear();
            foreach (var command in commands)
            {
                AddCandidate(_authoritative, command);
            }

            return BuildTransitions(before, SnapshotCurrent());
        }

        public PresentationChannelTransition BeginTemporary(
            PresentationEffectCommand command)
        {
            ValidateChannelCommand(command);
            var key = ChannelKey.From(command);
            var previous = SelectCurrent(key);
            AddCandidate(_temporary, command);
            return new PresentationChannelTransition(
                previous,
                SelectCurrent(key));
        }

        public PresentationChannelTransition ReplaceTemporary(
            PresentationEffectCommand command)
        {
            ValidateChannelCommand(command);
            var key = ChannelKey.From(command);
            var previous = SelectCurrent(key);
            _temporary.Remove(key);
            AddCandidate(_temporary, command);
            return new PresentationChannelTransition(
                previous,
                SelectCurrent(key));
        }

        public PresentationChannelTransition EndTemporary(
            string commandId,
            PresentationTargetReference target,
            string channel)
        {
            var normalizedCommandId = PresentationContractGuard.Required(
                commandId,
                "临时表现命令 ID");
            var key = new ChannelKey(target, channel);
            var previous = SelectCurrent(key);
            if (_temporary.TryGetValue(key, out var candidates))
            {
                candidates.RemoveAll(candidate => string.Equals(
                    candidate.Command.CommandId,
                    normalizedCommandId,
                    StringComparison.Ordinal));
                if (candidates.Count == 0)
                {
                    _temporary.Remove(key);
                }
            }

            return new PresentationChannelTransition(
                previous,
                SelectCurrent(key));
        }

        public PresentationEffectCommand GetCurrent(
            PresentationTargetReference target,
            string channel)
        {
            return SelectCurrent(new ChannelKey(target, channel));
        }

        public PresentationChannelSnapshot Capture(
            PresentationTargetReference target,
            string channel)
        {
            return new PresentationChannelSnapshot(
                target,
                channel,
                GetCurrent(target, channel));
        }

        public void Remove(string commandId)
        {
            var normalized = PresentationContractGuard.Required(
                commandId,
                "表现命令 ID");
            RemoveFrom(_authoritative, normalized);
            RemoveFrom(_temporary, normalized);
        }

        public void Restore(PresentationChannelSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.Previous == null)
            {
                return;
            }

            var key = new ChannelKey(snapshot.Target, snapshot.Channel);
            _temporary.Remove(key);
            AddCandidate(_temporary, snapshot.Previous);
        }

        private void AddCandidate(
            IDictionary<ChannelKey, List<Candidate>> destination,
            PresentationEffectCommand command)
        {
            ValidateChannelCommand(command);
            var key = ChannelKey.From(command);
            if (!destination.TryGetValue(key, out var candidates))
            {
                candidates = new List<Candidate>();
                destination.Add(key, candidates);
            }

            candidates.RemoveAll(candidate => string.Equals(
                candidate.Command.CommandId,
                command.CommandId,
                StringComparison.Ordinal));
            candidates.Add(new Candidate(command, ++_sequence));
        }

        private PresentationEffectCommand SelectCurrent(ChannelKey key)
        {
            var candidates = Enumerable.Empty<Candidate>();
            if (_authoritative.TryGetValue(key, out var authoritative))
            {
                candidates = candidates.Concat(authoritative);
            }

            if (_temporary.TryGetValue(key, out var temporary))
            {
                candidates = candidates.Concat(temporary);
            }

            var selected = candidates
                .OrderByDescending(value => value.Command.Priority)
                .ThenByDescending(value => value.Sequence)
                .FirstOrDefault();
            return selected?.Command;
        }

        private static void RemoveFrom(
            IDictionary<ChannelKey, List<Candidate>> source,
            string commandId)
        {
            var empty = new List<ChannelKey>();
            foreach (var pair in source)
            {
                pair.Value.RemoveAll(candidate => string.Equals(
                    candidate.Command.CommandId,
                    commandId,
                    StringComparison.Ordinal));
                if (pair.Value.Count == 0)
                {
                    empty.Add(pair.Key);
                }
            }

            foreach (var key in empty)
            {
                source.Remove(key);
            }
        }

        private Dictionary<ChannelKey, PresentationEffectCommand>
            SnapshotCurrent()
        {
            var keys = new HashSet<ChannelKey>(_authoritative.Keys);
            keys.UnionWith(_temporary.Keys);
            return keys.ToDictionary(key => key, SelectCurrent);
        }

        private static IReadOnlyList<PresentationChannelTransition>
            BuildTransitions(
                IReadOnlyDictionary<
                    ChannelKey,
                    PresentationEffectCommand> before,
                IReadOnlyDictionary<
                    ChannelKey,
                    PresentationEffectCommand> after)
        {
            var keys = new HashSet<ChannelKey>(before.Keys);
            keys.UnionWith(after.Keys);
            var transitions = new List<PresentationChannelTransition>();
            foreach (var key in keys)
            {
                before.TryGetValue(key, out var previous);
                after.TryGetValue(key, out var current);
                if (!ReferenceEquals(previous, current))
                {
                    transitions.Add(new PresentationChannelTransition(
                        previous,
                        current));
                }
            }

            return new ReadOnlyCollection<PresentationChannelTransition>(
                transitions);
        }

        private static void ValidateChannelCommand(
            PresentationEffectCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (command.Lifecycle == PresentationEffectLifecycle.OneShot)
            {
                throw new ArgumentException(
                    "一次性效果不应进入持续通道仲裁。",
                    nameof(command));
            }
        }

        private sealed class Candidate
        {
            public Candidate(
                PresentationEffectCommand command,
                long sequence)
            {
                Command = command;
                Sequence = sequence;
            }

            public PresentationEffectCommand Command { get; }

            public long Sequence { get; }
        }

        private readonly struct ChannelKey : IEquatable<ChannelKey>
        {
            public ChannelKey(
                PresentationTargetReference target,
                string channel)
            {
                Target = target ??
                    throw new ArgumentNullException(nameof(target));
                Channel = PresentationContractGuard.Required(
                    channel,
                    "表现通道");
            }

            public PresentationTargetReference Target { get; }

            public string Channel { get; }

            public static ChannelKey From(PresentationEffectCommand command)
            {
                return new ChannelKey(
                    command.Target,
                    command.Channel);
            }

            public bool Equals(ChannelKey other)
            {
                return Target.Equals(other.Target) &&
                       string.Equals(
                           Channel,
                           other.Channel,
                           StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ChannelKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return
                        (Target.GetHashCode() * 397) ^
                        StringComparer.Ordinal.GetHashCode(Channel);
                }
            }
        }
    }
}
