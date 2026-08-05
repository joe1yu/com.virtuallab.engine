using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualLab.Domain;
using VirtualLab.Domain.Entities;
using VirtualLab.Domain.WorldStates;
using VirtualLab.Kernel;

namespace VirtualLab.Engine.Tests.Domain
{
    public sealed class WorldStateExtensionTests
    {
        private static readonly WorldStateTypeId CounterStateId =
            new WorldStateTypeId("测试.状态.计数器");

        [Test]
        public void World_transaction_commits_extension_state_and_rolls_back_failure()
        {
            var world = new ExperimentWorld();
            world.RegisterWorldState(new CounterState(CounterStateId, world.ContainsEntity));

            world.CommitAtomically(prepared =>
                prepared.RequireWorldState<CounterState>(CounterStateId).Value = 3);

            Assert.That(
                world.RequireWorldState<CounterState>(CounterStateId).Value,
                Is.EqualTo(3));
            Assert.Throws<InvalidOperationException>(() =>
                world.CommitAtomically(prepared =>
                {
                    prepared.RequireWorldState<CounterState>(CounterStateId).Value = 9;
                    throw new InvalidOperationException("模拟后续操作失败");
                }));
            Assert.That(
                world.RequireWorldState<CounterState>(CounterStateId).Value,
                Is.EqualTo(3));
        }

        [Test]
        public void Checkpoint_restores_registered_extension_state()
        {
            var world = new ExperimentWorld();
            var state = new CounterState(CounterStateId, world.ContainsEntity)
            {
                Value = 4
            };
            world.RegisterWorldState(state);
            var checkpoint = world.CreateCheckpoint();

            state.Value = 7;
            world.RestoreCheckpoint(checkpoint);

            Assert.That(state.Value, Is.EqualTo(4));
        }

        [Test]
        public void Entity_removal_is_rolled_back_when_extension_cleanup_fails()
        {
            var world = new ExperimentWorld();
            var entity = new ExperimentEntity(new EntityId("测试实体"));
            world.AddEntity(entity);
            var state = new CounterState(CounterStateId, world.ContainsEntity);
            state.AddReference(entity.Id);
            state.ThrowOnRemove = true;
            world.RegisterWorldState(state);

            Assert.Throws<InvalidOperationException>(() =>
                world.RemoveEntity(entity.Id));
            Assert.That(world.ContainsEntity(entity.Id), Is.True);
            Assert.That(state.References, Has.Member(entity.Id));

            state.ThrowOnRemove = false;
            Assert.That(world.RemoveEntity(entity.Id), Is.True);
            Assert.That(state.References, Has.No.Member(entity.Id));
        }

        [Test]
        public void Duplicate_world_state_type_is_rejected()
        {
            var world = new ExperimentWorld();
            world.RegisterWorldState(new CounterState(CounterStateId, world.ContainsEntity));

            Assert.Throws<InvalidOperationException>(() =>
                world.RegisterWorldState(
                    new CounterState(CounterStateId, world.ContainsEntity)));
        }

        [Test]
        public void Frozen_world_rejects_new_state_types()
        {
            var world = new ExperimentWorld();
            world.FreezeWorldStateTypes();

            Assert.Throws<InvalidOperationException>(() =>
                world.RegisterWorldState(
                    new CounterState(CounterStateId, world.ContainsEntity)));
        }

        private sealed class CounterState : IWorldStateExtension
        {
            private readonly Func<EntityId, bool> _entityExists;
            private readonly HashSet<EntityId> _references = new HashSet<EntityId>();

            public CounterState(
                WorldStateTypeId typeId,
                Func<EntityId, bool> entityExists)
            {
                TypeId = typeId;
                _entityExists = entityExists;
            }

            public WorldStateTypeId TypeId { get; }

            public int Value { get; set; }

            public bool ThrowOnRemove { get; set; }

            public IReadOnlyCollection<EntityId> References => _references;

            public void AddReference(EntityId entityId)
            {
                if (!_entityExists(entityId))
                {
                    throw new InvalidOperationException("引用实体不存在。");
                }

                _references.Add(entityId);
            }

            public IWorldStateExtension CreateCopy(Func<EntityId, bool> entityExists)
            {
                var copy = new CounterState(TypeId, entityExists)
                {
                    Value = Value,
                    ThrowOnRemove = ThrowOnRemove
                };
                foreach (var entityId in _references)
                {
                    copy._references.Add(entityId);
                }

                return copy;
            }

            public void ValidateReplacement(IWorldStateExtension source)
            {
                if (!(source is CounterState replacement)
                    || replacement.TypeId != TypeId)
                {
                    throw new InvalidOperationException("计数器状态类型不匹配。");
                }
            }

            public void ReplaceStateFrom(IWorldStateExtension source)
            {
                var replacement = (CounterState)source;
                Value = replacement.Value;
                ThrowOnRemove = replacement.ThrowOnRemove;
                _references.Clear();
                foreach (var entityId in replacement._references)
                {
                    _references.Add(entityId);
                }
            }

            public void RemoveEntityReferences(EntityId entityId)
            {
                if (ThrowOnRemove)
                {
                    throw new InvalidOperationException("模拟模块状态清理失败。");
                }

                _references.Remove(entityId);
            }
        }
    }
}
