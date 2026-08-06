using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Application.Goals;

namespace VirtualLab.Application.Hints
{
    public enum HintLevel
    {
        CurrentGoal = 1,
        MissingConditionCategory = 2,
        SuggestedAction = 3
    }

    /// <summary>Immutable presentation data. It never changes goal state.</summary>
    public sealed class Hint
    {
        public Hint(HintLevel level, string goalId, string message)
        {
            if (!Enum.IsDefined(typeof(HintLevel), level))
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            if (string.IsNullOrWhiteSpace(goalId))
            {
                throw new ArgumentException("A hint goal ID cannot be blank.", nameof(goalId));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A hint message cannot be blank.", nameof(message));
            }

            Level = level;
            GoalId = goalId;
            Message = message;
        }

        public HintLevel Level { get; }

        public string GoalId { get; }

        public string Message { get; }
    }

    public sealed class HintEngine
    {
        private readonly GoalEngine _goals;

        public HintEngine(GoalEngine goals)
        {
            _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        }

        public IReadOnlyList<Hint> GetHints()
        {
            var goal = _goals.GetPreferredUnsatisfiedGoal();
            if (goal == null)
            {
                return new ReadOnlyCollection<Hint>(new List<Hint>());
            }

            return new ReadOnlyCollection<Hint>(new List<Hint>
            {
                new Hint(HintLevel.CurrentGoal, goal.Id, goal.Id),
                new Hint(HintLevel.MissingConditionCategory, goal.Id, goal.MissingConditionCategory),
                new Hint(HintLevel.SuggestedAction, goal.Id, goal.SuggestedAction)
            });
        }

        public Hint GetHint(HintLevel level)
        {
            if (!Enum.IsDefined(typeof(HintLevel), level))
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            var goal = _goals.GetPreferredUnsatisfiedGoal();
            if (goal == null)
            {
                return null;
            }

            switch (level)
            {
                case HintLevel.CurrentGoal:
                    return new Hint(level, goal.Id, goal.Id);
                case HintLevel.MissingConditionCategory:
                    return new Hint(
                        level,
                        goal.Id,
                        goal.MissingConditionCategory);
                case HintLevel.SuggestedAction:
                    return new Hint(
                        level,
                        goal.Id,
                        goal.SuggestedAction);
                default:
                    throw new ArgumentOutOfRangeException(nameof(level));
            }
        }
    }
}
