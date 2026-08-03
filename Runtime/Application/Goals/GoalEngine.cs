using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Application.Events;
using VirtualLab.Application.Observations;

namespace VirtualLab.Application.Goals
{
    public sealed class GoalEngine
    {
        private readonly IReadOnlyList<GoalDefinition> _definitions;
        private readonly Dictionary<string, GoalDefinition> _definitionsById;
        private readonly List<ObservationRecord> _observations = new List<ObservationRecord>();
        private readonly ReadOnlyCollection<ObservationRecord> _readOnlyObservations;
        private readonly HashSet<long> _observedEventSequences = new HashSet<long>();
        private readonly HashSet<string> _goalIdsWithEvidence = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _satisfiedGoalIds = new HashSet<string>(StringComparer.Ordinal);

        public GoalEngine(IReadOnlyList<GoalDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var copy = new List<GoalDefinition>(definitions.Count);
            _definitionsById = new Dictionary<string, GoalDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException("Goal definitions cannot contain null.", nameof(definitions));
                }

                if (_definitionsById.ContainsKey(definition.Id))
                {
                    throw new ArgumentException("Goal IDs must be unique.", nameof(definitions));
                }

                _definitionsById.Add(definition.Id, definition);
                copy.Add(definition);
            }

            foreach (var definition in copy)
            {
                foreach (var prerequisiteId in definition.PrerequisiteGoalIds)
                {
                    if (!_definitionsById.ContainsKey(prerequisiteId))
                    {
                        throw new ArgumentException("Every prerequisite goal must be defined.", nameof(definitions));
                    }
                }
            }

            EnsureAcyclic(copy);
            _definitions = new ReadOnlyCollection<GoalDefinition>(copy);
            _readOnlyObservations = new ReadOnlyCollection<ObservationRecord>(_observations);
        }

        public IReadOnlyList<GoalDefinition> Definitions => _definitions;

        public IReadOnlyList<ObservationRecord> Observations => _readOnlyObservations;

        public bool IsSatisfied(string goalId)
        {
            var normalized = GoalDefinition.RequireStableId(goalId, nameof(goalId), "goal");
            if (!_definitionsById.ContainsKey(normalized))
            {
                throw new ArgumentException("The goal ID is not defined by this engine.", nameof(goalId));
            }

            return _satisfiedGoalIds.Contains(normalized);
        }

        /// <summary>
        /// Records every valid event even when its prerequisite goals are not complete.
        /// Re-evaluation lets earlier, non-linear evidence complete later when the graph allows it.
        /// </summary>
        public void Handle(DomainEventEnvelope domainEvent)
        {
            if (domainEvent == null)
            {
                throw new ArgumentNullException(nameof(domainEvent));
            }

            if (!_observedEventSequences.Add(domainEvent.Sequence))
            {
                return;
            }

            _observations.Add(new ObservationRecord(domainEvent));
            foreach (var definition in _definitions)
            {
                if (definition.IsSatisfiedBy(domainEvent))
                {
                    _goalIdsWithEvidence.Add(definition.Id);
                }
            }

            Reevaluate();
        }

        internal GoalDefinition GetPreferredUnsatisfiedGoal()
        {
            foreach (var definition in _definitions)
            {
                if (!_satisfiedGoalIds.Contains(definition.Id) && ArePrerequisitesSatisfied(definition))
                {
                    return definition;
                }
            }

            foreach (var definition in _definitions)
            {
                if (!_satisfiedGoalIds.Contains(definition.Id))
                {
                    return definition;
                }
            }

            return null;
        }

        private void Reevaluate()
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var definition in _definitions)
                {
                    if (!_satisfiedGoalIds.Contains(definition.Id)
                        && _goalIdsWithEvidence.Contains(definition.Id)
                        && ArePrerequisitesSatisfied(definition))
                    {
                        _satisfiedGoalIds.Add(definition.Id);
                        changed = true;
                    }
                }
            }
        }

        private bool ArePrerequisitesSatisfied(GoalDefinition definition)
        {
            foreach (var prerequisiteId in definition.PrerequisiteGoalIds)
            {
                if (!_satisfiedGoalIds.Contains(prerequisiteId))
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureAcyclic(IReadOnlyList<GoalDefinition> definitions)
        {
            var definitionsById = new Dictionary<string, GoalDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                definitionsById.Add(definition.Id, definition);
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                Visit(definition, definitionsById, visiting, visited);
            }
        }

        private static void Visit(
            GoalDefinition definition,
            IReadOnlyDictionary<string, GoalDefinition> definitionsById,
            ISet<string> visiting,
            ISet<string> visited)
        {
            if (visited.Contains(definition.Id))
            {
                return;
            }

            if (!visiting.Add(definition.Id))
            {
                throw new ArgumentException("Goal prerequisites cannot contain a cycle.", "definitions");
            }

            foreach (var prerequisiteId in definition.PrerequisiteGoalIds)
            {
                Visit(definitionsById[prerequisiteId], definitionsById, visiting, visited);
            }

            visiting.Remove(definition.Id);
            visited.Add(definition.Id);
        }
    }
}
