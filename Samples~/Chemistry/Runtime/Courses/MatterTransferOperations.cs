using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Application.Courses;
using VirtualLab.Domain;
using VirtualLab.Domain.Capabilities;
using VirtualLab.Domain.Events;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Processes;
using VirtualLab.Kernel;

namespace VirtualLab.Chemistry.Courses
{
    /// <summary>
    /// 按模拟时间推进倾倒过程。流量、目标与容量策略均来自已裁决的结构化数据。
    /// </summary>
    public sealed class MatterTransferOperations
    {
        public const string BeginOperationId = "开始倾倒过程";
        public const string EndOperationId = "结束倾倒过程";
        public const string TransferOperationId = "转移物质";
        public const string PourProcessId = "倾倒过程";

        public void RegisterWith(ConfiguredStateOperationRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(new BeginOperation());
            registry.Register(new EndOperation());
            registry.Register(new TransferOperation());
        }

        public void Advance(
            ExperimentWorld world,
            double elapsedSeconds,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            if (double.IsNaN(elapsedSeconds)
                || double.IsInfinity(elapsedSeconds)
                || elapsedSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(elapsedSeconds),
                    "倾倒推进时间必须是有限正数。");
            }

            var processes = world.ActiveProcesses
                .Where(value => string.Equals(
                    value.ProcessId,
                    PourProcessId,
                    StringComparison.Ordinal))
                .OrderBy(value => value.EntityId.Value, StringComparer.Ordinal)
                .ToArray();
            foreach (var process in processes)
            {
                AdvanceOne(world, process, elapsedSeconds, tick, events);
            }
        }

        private static void AdvanceOne(
            ExperimentWorld world,
            WorldProcessState process,
            double elapsedSeconds,
            SimulationTick tick,
            IProcessEventCollector events)
        {
            var source = process.EntityId;
            var target = new EntityId(process.TextParameters[ChemistryConfigurationKeys.Common.TargetEntityId]);
            var substanceId = process.TextParameters[ChemistryConfigurationKeys.Common.SubstanceId];
            var unit = ParseStoredUnit(process.TextParameters[ChemistryConfigurationKeys.Common.MeasurementUnit]);
            var rate = (decimal)process.NumberParameters[ChemistryConfigurationKeys.Pour.FlowMillilitresPerSecond];
            var requested = rate * (decimal)elapsedSeconds;
            var capacityPolicy =
                process.TextParameters[ChemistryConfigurationKeys.Pour.CapacityPolicy];
            var available = world.Matter.Total(
                source,
                substanceId,
                unit).Value;
            var remainingCapacity = RemainingCapacity(
                world,
                target,
                unit,
                (decimal)process.NumberParameters[ChemistryConfigurationKeys.Pour.TargetCapacity]);
            var candidate = Math.Min(requested, available);
            var capacityLimited = candidate > remainingCapacity;
            var sourceLimited = available < requested;
            var amount = Math.Min(
                requested,
                Math.Min(available, remainingCapacity));
            var shouldStop = capacityLimited || sourceLimited;
            var domainEvent = capacityLimited
                && capacityPolicy == "溢出事件"
                ? (IDomainEvent)new PourCapacityReachedEvent(
                    source,
                    target,
                    substanceId,
                    new Quantity(
                        candidate - remainingCapacity,
                        unit))
                : new PourAdvancedEvent(
                    source,
                    target,
                    substanceId,
                    new Quantity(amount, unit),
                    shouldStop);

            // 只向外发布一个事件；其提交回调同时完成转移和过程停止。
            world.CommitAtomically(
                prepared =>
                {
                    events.CommitAtomically(
                        process.TextParameters[ChemistryConfigurationKeys.Common.CommandId],
                        tick,
                        domainEvent,
                        () =>
                        {
                            if (amount > 0m)
                            {
                                prepared.Matter.Transfer(
                                    source,
                                    target,
                                    substanceId,
                                    new Quantity(
                                        amount,
                                        unit),
                                    tick,
                                    SilentProcessEventCollector.Instance);
                            }

                            if (shouldStop)
                            {
                                prepared.StopProcess(
                                    PourProcessId,
                                    source);
                            }
                        });
                });
        }

        private static decimal RemainingCapacity(
            ExperimentWorld world,
            EntityId target,
            Unit unit,
            decimal configuredCapacity)
        {
            if (!world.TryGetEntity(target, out var targetEntity)
                || !targetEntity.HasCapability<ContainerCapability>())
            {
                throw new InvalidOperationException(
                    "倾倒目标必须声明容器能力。");
            }

            var used = world.Matter.Entries
                .Where(value => value.LocationId == target
                    && value.Batch.Quantity.Unit == unit)
                .Sum(value => value.Batch.Quantity.Value);
            var capacity = configuredCapacity > 0m
                ? configuredCapacity
                : targetEntity
                    .GetCapability<ContainerCapability>()
                    .CapacityMillilitres;
            return Math.Max(
                0m,
                capacity - used);
        }

        private sealed class BeginOperation : IConfiguredStateOperation
        {
            public string OperationId => BeginOperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                if (request.TargetEntityId == null)
                {
                    throw new ArgumentException("倾倒目标不能为空。");
                }

                var unit = ReadUnit(mutation);
                var rate = ReadRequestNumber(
                    request,
                    unit == Unit.Gram
                        ? ChemistryConfigurationKeys.Pour.RequestedFlowGramsPerSecond
                        : ChemistryConfigurationKeys.Pour.RequestedFlowMillilitresPerSecond);
                if (rate <= 0d)
                {
                    throw new ArgumentOutOfRangeException(
                        unit == Unit.Gram
                            ? ChemistryConfigurationKeys.Pour.RequestedFlowGramsPerSecond
                            : ChemistryConfigurationKeys.Pour.RequestedFlowMillilitresPerSecond,
                        "倾倒流量必须大于零。");
                }

                var substanceId = ReadMutationText(mutation, ChemistryConfigurationKeys.Common.SubstanceId);
                var capacityPolicy = ReadMutationText(
                    mutation,
                    ChemistryConfigurationKeys.Pour.CapacityPolicy);
                if (capacityPolicy != "停止"
                    && capacityPolicy != "溢出事件"
                    && capacityPolicy != "拒绝开始")
                {
                    throw new ArgumentException(
                        $"未知容量处理策略“{capacityPolicy}”。");
                }

                var source = new EntityId(request.SourceEntityId);
                var target = new EntityId(request.TargetEntityId);
                var configuredCapacity = mutation.Parameters.TryGetValue(
                        ChemistryConfigurationKeys.Pour.TargetCapacity,
                        out var capacityValue)
                    && capacityValue.Kind == StructuredValueKind.Number
                    ? (decimal)capacityValue.Number
                    : 0m;
                if (unit == Unit.Gram && configuredCapacity <= 0m)
                {
                    throw new ArgumentException(
                        "固体倾倒必须配置大于零的目标容量。");
                }

                var remainingCapacity = RemainingCapacity(
                    world,
                    target,
                    unit,
                    configuredCapacity);
                var available = world.Matter.Total(
                    source,
                    substanceId,
                    unit).Value;
                if (capacityPolicy == "拒绝开始"
                    && available > remainingCapacity)
                {
                    throw new ConfiguredOperationRejectedException(
                        new[]
                        {
                            ReadMutationText(
                                mutation,
                                ChemistryConfigurationKeys.Pour.CapacityInsufficientRejectionReason)
                        });
                }

                // 在开始阶段验证物质及单位，避免把错误留到后续 Tick。
                world.StartProcess(
                    PourProcessId,
                    source,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [ChemistryConfigurationKeys.Common.TargetEntityId] = target.Value,
                        [ChemistryConfigurationKeys.Common.SubstanceId] = substanceId,
                        [ChemistryConfigurationKeys.Pour.CapacityPolicy] = capacityPolicy,
                        [ChemistryConfigurationKeys.Common.MeasurementUnit] = unit.ToString(),
                        [ChemistryConfigurationKeys.Common.CommandId] = request.CommandId
                    },
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        [ChemistryConfigurationKeys.Pour.FlowMillilitresPerSecond] = rate,
                        [ChemistryConfigurationKeys.Pour.TargetCapacity] = (double)configuredCapacity
                    });
            }
        }

        private sealed class EndOperation : IConfiguredStateOperation
        {
            public string OperationId => EndOperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                world.StopProcess(
                    PourProcessId,
                    new EntityId(request.SourceEntityId));
            }
        }

        /// <summary>
        /// 在一个配置效果组的原子事务内转移定量物质。
        /// 显式实体 ID 可用于“动作对象”和“物质所在容器”不一致的场景。
        /// </summary>
        private sealed class TransferOperation : IConfiguredStateOperation
        {
            public string OperationId => TransferOperationId;

            public void Apply(
                SemanticActionRequest request,
                ExperimentWorld world,
                ConfiguredMutationDefinition mutation)
            {
                var source = new EntityId(
                    ReadOptionalMutationText(
                        mutation,
                        ChemistryConfigurationKeys.Common.SourceEntityId,
                        request.SourceEntityId));
                var target = new EntityId(
                    ReadOptionalMutationText(
                        mutation,
                        ChemistryConfigurationKeys.Common.TargetEntityId,
                        request.TargetEntityId));
                var substanceId = ReadMutationText(mutation, ChemistryConfigurationKeys.Common.SubstanceId);
                var amount = ReadMutationNumber(mutation, ChemistryConfigurationKeys.Common.Quantity);
                if (amount <= 0m)
                {
                    throw new ArgumentOutOfRangeException(
                        ChemistryConfigurationKeys.Common.Quantity,
                        "物质转移数量必须大于零。");
                }

                var unit = ReadUnit(mutation);
                var available = world.Matter.Total(
                    source,
                    substanceId,
                    unit).Value;
                var riskStateKey = mutation.Parameters.ContainsKey(
                        ChemistryConfigurationKeys.Pour.QuantityInsufficientRiskStateKey)
                    ? ReadMutationText(
                        mutation,
                        ChemistryConfigurationKeys.Pour.QuantityInsufficientRiskStateKey)
                    : null;
                if (riskStateKey != null)
                {
                    world.SetScalar(
                        riskStateKey,
                        available < amount ? 1d : 0d,
                        new WorldScalarUnit("布尔标记"),
                        0d,
                        1d);
                }

                // 定量收集不足时不凭空补足，也不尝试搬运一个不完整批次。
                // 动作仍然成立，风险标量会由后续配置转换为后果事件。
                if (available < amount && riskStateKey != null)
                {
                    return;
                }

                if (available < amount
                    && riskStateKey == null
                    && mutation.Parameters.ContainsKey(
                        ChemistryConfigurationKeys.Pour.QuantityInsufficientRejectionReason))
                {
                    throw new ConfiguredOperationRejectedException(
                        new[]
                        {
                            ReadMutationText(
                                mutation,
                                ChemistryConfigurationKeys.Pour.QuantityInsufficientRejectionReason)
                        });
                }

                world.Matter.Transfer(
                    source,
                    target,
                    substanceId,
                    new Quantity(amount, unit),
                    new SimulationTick(0),
                    SilentProcessEventCollector.Instance);
            }
        }

        private static double ReadRequestNumber(
            SemanticActionRequest request,
            string key)
        {
            if (!request.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Number)
            {
                throw new ArgumentException(
                    $"倾倒请求参数“{key}”必须是数值。");
            }

            return value.Number;
        }

        private static string ReadMutationText(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ArgumentException(
                    $"倾倒配置参数“{key}”必须是文本。");
            }

            return value.Text.Trim();
        }

        private static string ReadOptionalMutationText(
            ConfiguredMutationDefinition mutation,
            string key,
            string fallback)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value))
            {
                if (string.IsNullOrWhiteSpace(fallback))
                {
                    throw new ArgumentException(
                        $"物质转移缺少实体参数“{key}”。");
                }

                return fallback.Trim();
            }

            if (value.Kind != StructuredValueKind.Text
                || string.IsNullOrWhiteSpace(value.Text))
            {
                throw new ArgumentException(
                    $"物质转移配置参数“{key}”必须是文本。");
            }

            return value.Text.Trim();
        }

        private static decimal ReadMutationNumber(
            ConfiguredMutationDefinition mutation,
            string key)
        {
            if (!mutation.Parameters.TryGetValue(key, out var value)
                || value.Kind != StructuredValueKind.Number
                || double.IsNaN(value.Number)
                || double.IsInfinity(value.Number))
            {
                throw new ArgumentException(
                    $"物质转移配置参数“{key}”必须是有限数值。");
            }

            return (decimal)value.Number;
        }

        private static Unit ReadUnit(
            ConfiguredMutationDefinition mutation)
        {
            if (!mutation.Parameters.TryGetValue(ChemistryConfigurationKeys.Common.MeasurementUnit, out var value))
            {
                return Unit.Millilitre;
            }

            if (value.Kind != StructuredValueKind.Text)
            {
                throw new ArgumentException(
                    "倾倒配置参数“计量单位”必须是文本。");
            }

            return value.Text switch
            {
                "克" => Unit.Gram,
                "毫升" => Unit.Millilitre,
                _ => throw new ArgumentException(
                    $"未知倾倒计量单位“{value.Text}”。")
            };
        }

        private static Unit ParseStoredUnit(string value)
        {
            return Enum.TryParse(value, out Unit unit)
                ? unit
                : throw new InvalidOperationException(
                    $"倾倒过程计量单位“{value}”无效。");
        }
    }

    /// <summary>
    /// 目标容器容量不足，记录本次未能转移的物质量。
    /// </summary>
    public sealed class PourCapacityReachedEvent : IDomainEvent
    {
        public PourCapacityReachedEvent(
            EntityId sourceId,
            EntityId targetId,
            string substanceId,
            Quantity preventedQuantity)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SubstanceId = substanceId;
            PreventedQuantity = preventedQuantity;
        }

        public string EventType => ChemistryEventTypes.PourCapacityReached;

        public EntityId SourceId { get; }

        public EntityId TargetId { get; }

        public string SubstanceId { get; }

        public Quantity PreventedQuantity { get; }
    }

    /// <summary>
    /// 倾倒过程已推进一次，并记录实际转移量及过程是否停止。
    /// </summary>
    public sealed class PourAdvancedEvent : IDomainEvent
    {
        public PourAdvancedEvent(
            EntityId sourceId,
            EntityId targetId,
            string substanceId,
            Quantity transferredQuantity,
            bool processStopped)
        {
            SourceId = sourceId;
            TargetId = targetId;
            SubstanceId = substanceId;
            TransferredQuantity = transferredQuantity;
            ProcessStopped = processStopped;
        }

        public string EventType => ChemistryEventTypes.PourAdvanced;

        public EntityId SourceId { get; }

        public EntityId TargetId { get; }

        public string SubstanceId { get; }

        public Quantity TransferredQuantity { get; }

        public bool ProcessStopped { get; }
    }

    internal sealed class SilentProcessEventCollector :
        IProcessEventCollector
    {
        public static readonly SilentProcessEventCollector Instance =
            new SilentProcessEventCollector();

        private SilentProcessEventCollector()
        {
        }

        public void CommitAtomically(
            string commandId,
            SimulationTick tick,
            IDomainEvent domainEvent,
            Action commitState)
        {
            commitState();
        }
    }
}
