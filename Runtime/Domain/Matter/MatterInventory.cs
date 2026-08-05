using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Processes;
using VirtualLab.Domain.WorldStates;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Domain.Matter
{
    /// <summary>
    /// 物质模块拥有的世界状态类型标识，迁移到化学程序集时随库存一起移动。
    /// </summary>
    public static class MatterWorldStateTypeIds
    {
        public static readonly WorldStateTypeId Inventory =
            new WorldStateTypeId("化学.状态.物质库存");
    }

    public sealed class MatterInventory : IWorldStateExtension
    {
        private readonly Func<EntityId, bool> _locationExists;
        private InventoryState _state = InventoryState.Empty();
        private bool _transactionActive;

        internal MatterInventory(Func<EntityId, bool> locationExists)
        {
            _locationExists = locationExists ?? throw new ArgumentNullException(nameof(locationExists));
        }

        public IReadOnlyList<MatterInventoryEntry> Entries
        {
            get
            {
                var entries = new List<MatterInventoryEntry>();
                foreach (var location in _state.Batches)
                {
                    foreach (var batch in location.Value)
                    {
                        entries.Add(new MatterInventoryEntry(location.Key, batch));
                    }
                }

                return new ReadOnlyCollection<MatterInventoryEntry>(entries);
            }
        }

        public IReadOnlyDictionary<string, Unit> KnownUnits =>
            new ReadOnlyDictionary<string, Unit>(
                new Dictionary<string, Unit>(
                    _state.KnownUnits,
                    StringComparer.Ordinal));

        public void RegisterUnit(string substanceId, Unit unit)
        {
            ExecuteStandalone(transaction => transaction.RegisterUnit(substanceId, unit));
        }

        public void Add(EntityId locationId, SubstanceBatch batch)
        {
            ExecuteStandalone(transaction => transaction.Add(locationId, batch));
        }

        public Quantity Total(string substanceId)
        {
            return InventoryQueries.Total(_state, substanceId);
        }

        public Quantity Total(string substanceId, Unit unit)
        {
            return InventoryQueries.Total(_state, substanceId, unit);
        }

        public Quantity Total(EntityId locationId, string substanceId)
        {
            EnsureLocationExists(locationId);
            return InventoryQueries.Total(_state, locationId, substanceId);
        }

        public Quantity Total(EntityId locationId, string substanceId, Unit unit)
        {
            EnsureLocationExists(locationId);
            return InventoryQueries.Select(
                _state,
                null,
                locationId,
                MatterBatchSelection.All(substanceId),
                unit).Quantity;
        }

        public Quantity Total(
            EntityId locationId,
            MatterBatchSelection selection,
            Unit unit)
        {
            EnsureLocationExists(locationId);
            return InventoryQueries.Select(
                _state,
                null,
                locationId,
                selection,
                unit).Quantity;
        }

        public bool TryGetTemperature(
            EntityId locationId,
            string substanceId,
            out Temperature temperature)
        {
            EnsureLocationExists(locationId);
            var normalizedId = InventoryValidation.NormalizeSubstanceId(substanceId);
            if (_state.Batches.TryGetValue(locationId, out var locationBatches))
            {
                foreach (var batch in locationBatches)
                {
                    if (string.Equals(batch.SubstanceId, normalizedId, StringComparison.Ordinal)
                        && batch.Quantity.Value > 0m)
                    {
                        temperature = batch.Temperature;
                        return true;
                    }
                }
            }

            temperature = default;
            return false;
        }

        public void Consume(
            EntityId locationId,
            MatterBatchSelection selection,
            Quantity quantity)
        {
            ExecuteStandalone(
                transaction =>
                {
                    var selected = transaction.Select(locationId, selection, quantity.Unit);
                    transaction.Consume(selected, quantity);
                });
        }

        public void Transfer(
            EntityId sourceId,
            EntityId targetId,
            string substanceId,
            Quantity quantity,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (tick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "A transfer tick cannot be negative.");
            }

            var normalizedId = InventoryValidation.NormalizeSubstanceId(substanceId);
            InventoryValidation.ValidateRequestedQuantity(quantity);
            var domainEvent = new SubstanceTransferredEvent(
                sourceId,
                targetId,
                normalizedId,
                quantity);

            CommitAtomically(
                transaction =>
                {
                    var selected = transaction.Select(
                        sourceId,
                        MatterBatchSelection.All(normalizedId),
                        quantity.Unit);
                    var moved = transaction.Consume(selected, quantity);
                    foreach (var batch in moved)
                    {
                        transaction.Add(targetId, batch);
                    }

                },
                "matter.transfer",
                tick,
                domainEvent,
                events);
        }

        public void CommitAtomically(
            Action<MatterInventoryTransaction> prepare,
            string commandId,
            SimulationTick tick,
            IDomainEvent domainEvent,
            IProcessEventCollector events)
        {
            if (prepare == null)
            {
                throw new ArgumentNullException(nameof(prepare));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            EnterTransaction();
            try
            {
                var preparedState = _state.Clone();
                var transaction = new MatterInventoryTransaction(
                    preparedState,
                    EnsureLocationExists);
                prepare(transaction);
                transaction.Seal();
                if (!transaction.HasChanges)
                {
                    return;
                }

                if (transaction.HasBatchChanges)
                {
                    events.CommitAtomically(
                        commandId,
                        tick,
                        domainEvent,
                        () => _state = preparedState);
                }
                else
                {
                    _state = preparedState;
                }
            }
            finally
            {
                _transactionActive = false;
            }
        }

        internal void EnsureCanMutate()
        {
            if (_transactionActive)
            {
                throw new InvalidOperationException("Matter inventory mutations cannot be nested.");
            }
        }

        internal void RemoveLocation(EntityId locationId)
        {
            EnterTransaction();
            try
            {
                var next = _state.Clone();
                next.Batches.Remove(locationId);
                _state = next;
            }
            finally
            {
                _transactionActive = false;
            }
        }

        internal void ReplaceStateFrom(MatterInventory source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            EnsureCanMutate();
            source.EnsureCanMutate();
            _state = source._state.Clone();
        }

        WorldStateTypeId IWorldStateExtension.TypeId =>
            MatterWorldStateTypeIds.Inventory;

        IWorldStateExtension IWorldStateExtension.CreateCopy(
            Func<EntityId, bool> entityExists)
        {
            var copy = new MatterInventory(entityExists);
            copy._state = _state.Clone();
            return copy;
        }

        void IWorldStateExtension.ValidateReplacement(
            IWorldStateExtension source)
        {
            if (!(source is MatterInventory inventory))
            {
                throw new InvalidOperationException("物质库存只能从相同类型的世界状态恢复。");
            }

            EnsureCanMutate();
            inventory.EnsureCanMutate();
        }

        void IWorldStateExtension.ReplaceStateFrom(
            IWorldStateExtension source)
        {
            ReplaceStateFrom((MatterInventory)source);
        }

        void IWorldStateExtension.RemoveEntityReferences(EntityId entityId)
        {
            RemoveLocation(entityId);
        }

        private void ExecuteStandalone(Action<MatterInventoryTransaction> prepare)
        {
            EnterTransaction();
            try
            {
                var preparedState = _state.Clone();
                var transaction = new MatterInventoryTransaction(
                    preparedState,
                    EnsureLocationExists);
                prepare(transaction);
                transaction.Seal();
                _state = preparedState;
            }
            finally
            {
                _transactionActive = false;
            }
        }

        private void EnterTransaction()
        {
            EnsureCanMutate();
            _transactionActive = true;
        }

        private void EnsureLocationExists(EntityId locationId)
        {
            if (string.IsNullOrWhiteSpace(locationId.Value))
            {
                throw new ArgumentException("A matter location ID cannot be blank.", nameof(locationId));
            }

            if (!_locationExists(locationId))
            {
                throw new InvalidOperationException("A matter location must be an entity in the experiment world.");
            }
        }
    }

    public sealed class MatterInventoryEntry
    {
        public MatterInventoryEntry(EntityId locationId, SubstanceBatch batch)
        {
            LocationId = locationId;
            Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        }

        public EntityId LocationId { get; }

        public SubstanceBatch Batch { get; }
    }

    public sealed class MatterInventoryTransaction
    {
        private readonly InventoryState _state;
        private readonly Action<EntityId> _ensureLocationExists;
        private readonly object _transactionToken = new object();
        private bool _isSealed;

        internal MatterInventoryTransaction(
            InventoryState state,
            Action<EntityId> ensureLocationExists)
        {
            _state = state;
            _ensureLocationExists = ensureLocationExists;
        }

        internal bool HasChanges { get; private set; }

        internal bool HasBatchChanges { get; private set; }

        public void RegisterUnit(string substanceId, Unit unit)
        {
            EnsureOpen();
            var normalizedId = InventoryValidation.NormalizeSubstanceId(substanceId);
            InventoryValidation.EnsureKnownUnit(unit);
            if (_state.KnownUnits.TryGetValue(normalizedId, out var existing))
            {
                if (existing != unit)
                {
                    throw new InvalidOperationException("A substance cannot be registered with multiple units.");
                }

                return;
            }

            _state.KnownUnits[normalizedId] = unit;
            HasChanges = true;
        }

        public void Add(EntityId locationId, SubstanceBatch batch)
        {
            EnsureOpen();
            _ensureLocationExists(locationId);
            if (batch == null)
            {
                throw new ArgumentNullException(nameof(batch));
            }

            RegisterUnit(batch.SubstanceId, batch.Quantity.Unit);
            if (batch.Quantity.Value == 0m)
            {
                return;
            }

            if (!_state.Batches.TryGetValue(locationId, out var locationBatches))
            {
                locationBatches = new List<SubstanceBatch>();
                _state.Batches.Add(locationId, locationBatches);
            }

            for (var index = 0; index < locationBatches.Count; index++)
            {
                var existing = locationBatches[index];
                if (string.Equals(existing.SubstanceId, batch.SubstanceId, StringComparison.Ordinal)
                    && existing.Quantity.Unit == batch.Quantity.Unit
                    && existing.Phase == batch.Phase
                    && existing.Temperature == batch.Temperature)
                {
                    locationBatches[index] = existing.WithQuantity(existing.Quantity.Add(batch.Quantity));
                    HasChanges = true;
                    HasBatchChanges = true;
                    return;
                }
            }

            locationBatches.Add(batch);
            HasChanges = true;
            HasBatchChanges = true;
        }

        public MatterBatchSelectionResult Select(
            EntityId locationId,
            MatterBatchSelection selection,
            Unit unit)
        {
            EnsureOpen();
            _ensureLocationExists(locationId);
            return InventoryQueries.Select(
                _state,
                _transactionToken,
                locationId,
                selection,
                unit);
        }

        public IReadOnlyList<SubstanceBatch> Consume(
            MatterBatchSelectionResult selected,
            Quantity quantity)
        {
            EnsureOpen();
            if (selected == null)
            {
                throw new ArgumentNullException(nameof(selected));
            }

            if (!ReferenceEquals(selected.TransactionToken, _transactionToken))
            {
                throw new InvalidOperationException("A batch selection belongs to a different matter transaction.");
            }

            InventoryValidation.ValidateRequestedQuantity(quantity);
            if (selected.Quantity.Unit != quantity.Unit)
            {
                throw new InvalidOperationException("The requested unit does not match the selected substance unit.");
            }

            if (selected.Quantity.Value < quantity.Value)
            {
                throw new InvalidOperationException("The requested quantity exceeds the selected source inventory.");
            }

            if (quantity.Value == 0m)
            {
                return Array.Empty<SubstanceBatch>();
            }

            var consumed = new List<SubstanceBatch>();
            var remaining = quantity.Value;
            var locationBatches = _state.Batches[selected.LocationId];
            foreach (var selectedBatch in selected.Batches)
            {
                var index = FindByReference(locationBatches, selectedBatch);
                if (index < 0)
                {
                    throw new InvalidOperationException("A selected matter batch is no longer available.");
                }

                var batch = locationBatches[index];
                var amount = Math.Min(batch.Quantity.Value, remaining);
                consumed.Add(batch.WithQuantity(new Quantity(amount, quantity.Unit)));
                remaining -= amount;
                var retained = batch.Quantity.Value - amount;
                if (retained == 0m)
                {
                    locationBatches.RemoveAt(index);
                }
                else
                {
                    locationBatches[index] = batch.WithQuantity(new Quantity(retained, quantity.Unit));
                }

                if (remaining == 0m)
                {
                    break;
                }
            }

            HasChanges = true;
            HasBatchChanges = true;
            return consumed;
        }

        internal void Seal()
        {
            _isSealed = true;
        }

        private static int FindByReference(
            IList<SubstanceBatch> batches,
            SubstanceBatch selected)
        {
            for (var index = 0; index < batches.Count; index++)
            {
                if (ReferenceEquals(batches[index], selected))
                {
                    return index;
                }
            }

            return -1;
        }

        private void EnsureOpen()
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("A completed matter transaction cannot be reused.");
            }
        }
    }

    internal sealed class InventoryState
    {
        private InventoryState(
            Dictionary<EntityId, List<SubstanceBatch>> batches,
            Dictionary<string, Unit> knownUnits)
        {
            Batches = batches;
            KnownUnits = knownUnits;
        }

        public Dictionary<EntityId, List<SubstanceBatch>> Batches { get; }

        public Dictionary<string, Unit> KnownUnits { get; }

        public static InventoryState Empty()
        {
            return new InventoryState(
                new Dictionary<EntityId, List<SubstanceBatch>>(),
                new Dictionary<string, Unit>(StringComparer.Ordinal));
        }

        public InventoryState Clone()
        {
            var batches = new Dictionary<EntityId, List<SubstanceBatch>>();
            foreach (var pair in Batches)
            {
                batches.Add(pair.Key, new List<SubstanceBatch>(pair.Value));
            }

            return new InventoryState(
                batches,
                new Dictionary<string, Unit>(KnownUnits, StringComparer.Ordinal));
        }
    }

    internal static class InventoryQueries
    {
        public static Quantity Total(InventoryState state, string substanceId)
        {
            var normalizedId = InventoryValidation.NormalizeSubstanceId(substanceId);
            var unit = GetKnownUnit(state, normalizedId);
            var total = Quantity.Zero(unit);
            foreach (var location in state.Batches.Values)
            {
                foreach (var batch in location)
                {
                    if (string.Equals(batch.SubstanceId, normalizedId, StringComparison.Ordinal))
                    {
                        total = total.Add(batch.Quantity);
                    }
                }
            }

            return total;
        }

        public static Quantity Total(InventoryState state, string substanceId, Unit unit)
        {
            var normalizedId = InventoryValidation.NormalizeSubstanceId(substanceId);
            InventoryValidation.EnsureKnownUnit(unit);
            EnsureCompatibleRegisteredUnit(state, normalizedId, unit);
            var total = Quantity.Zero(unit);
            foreach (var location in state.Batches.Values)
            {
                foreach (var batch in location)
                {
                    if (string.Equals(batch.SubstanceId, normalizedId, StringComparison.Ordinal)
                        && batch.Quantity.Unit == unit)
                    {
                        total = total.Add(batch.Quantity);
                    }
                }
            }

            return total;
        }

        public static Quantity Total(
            InventoryState state,
            EntityId locationId,
            string substanceId)
        {
            var normalizedId = InventoryValidation.NormalizeSubstanceId(substanceId);
            return Select(
                state,
                null,
                locationId,
                MatterBatchSelection.All(normalizedId),
                GetKnownUnit(state, normalizedId)).Quantity;
        }

        public static MatterBatchSelectionResult Select(
            InventoryState state,
            object transactionToken,
            EntityId locationId,
            MatterBatchSelection selection,
            Unit unit)
        {
            var normalizedId = InventoryValidation.NormalizeSubstanceId(selection.SubstanceId);
            InventoryValidation.EnsureKnownUnit(unit);
            EnsureCompatibleRegisteredUnit(state, normalizedId, unit);

            var total = Quantity.Zero(unit);
            var selectedBatches = new List<SubstanceBatch>();
            if (state.Batches.TryGetValue(locationId, out var locationBatches))
            {
                foreach (var batch in locationBatches)
                {
                    if (batch.Quantity.Unit == unit && selection.Matches(batch))
                    {
                        selectedBatches.Add(batch);
                        total = total.Add(batch.Quantity);
                    }
                }
            }

            return new MatterBatchSelectionResult(
                transactionToken,
                locationId,
                selection,
                total,
                selectedBatches);
        }

        public static Unit GetKnownUnit(InventoryState state, string substanceId)
        {
            if (!state.KnownUnits.TryGetValue(substanceId, out var unit))
            {
                throw new InvalidOperationException("The substance unit has not been registered.");
            }

            return unit;
        }

        private static void EnsureCompatibleRegisteredUnit(
            InventoryState state,
            string substanceId,
            Unit unit)
        {
            if (state.KnownUnits.TryGetValue(substanceId, out var registered) && registered != unit)
            {
                throw new InvalidOperationException("The requested unit does not match the substance unit.");
            }
        }
    }

    internal static class InventoryValidation
    {
        public static string NormalizeSubstanceId(string substanceId)
        {
            if (string.IsNullOrWhiteSpace(substanceId))
            {
                throw new ArgumentException("A substance ID cannot be blank.", nameof(substanceId));
            }

            return substanceId.Trim();
        }

        public static void ValidateRequestedQuantity(Quantity quantity)
        {
            EnsureKnownUnit(quantity.Unit);
            if (quantity.Value < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "A requested quantity cannot be negative.");
            }
        }

        public static void EnsureKnownUnit(Unit unit)
        {
            if (!Enum.IsDefined(typeof(Unit), unit))
            {
                throw new ArgumentOutOfRangeException(nameof(unit), "A matter quantity must use a known unit.");
            }
        }
    }

    /// <summary>
    /// 单一物质已在两个实体库存之间完成转移的事实。
    /// </summary>
    public sealed class SubstanceTransferredEvent : IDomainEvent
    {
        public SubstanceTransferredEvent(
            EntityId sourceId,
            EntityId targetId,
            string substanceId,
            Quantity quantity)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SubstanceId = substanceId;
            Quantity = quantity;
        }

        public string EventType => DomainEventTypes.SubstanceTransferred;

        public EntityId SourceId { get; }

        public EntityId TargetId { get; }

        public string SubstanceId { get; }

        public Quantity Quantity { get; }
    }
}
