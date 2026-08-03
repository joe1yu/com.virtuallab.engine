using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    /// <summary>
    /// 在效果进入通道仲裁前完成契约、参数、资源和目标的原子准备。
    /// 准备失败只生成表现失败结果，不改变科学状态或当前表现通道。
    /// </summary>
    public sealed class PresentationEffectPreparer
    {
        private readonly PresentationExecutionContext _context;
        private readonly PresentationEffectCatalog _catalog;
        private readonly PresentationTargetResolver _targetResolver;
        private readonly IReadOnlyDictionary<
            string,
            IPresentationEffectExecutor> _executors;

        public PresentationEffectPreparer(
            PresentationExecutionContext context,
            PresentationEffectCatalog catalog,
            IEnumerable<IPresentationEffectExecutor> executors)
        {
            _context = context
                ?? throw new ArgumentNullException(nameof(context));
            _catalog = catalog
                ?? throw new ArgumentNullException(nameof(catalog));
            _targetResolver = new PresentationTargetResolver(
                context.CourseViews,
                context.ResolveGlobalReceiver);
            _executors = CopyExecutors(executors);
            RegisteredEffectIds = new ReadOnlyCollection<string>(
                _executors.Keys
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray());
        }

        public IReadOnlyList<string> RegisteredEffectIds { get; }

        public PresentationPreparationResult Prepare(
            PresentationEffectCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            try
            {
                var descriptor = _catalog.RequireByProtocolId(
                    command.EffectId);
                ValidateCommandContract(command, descriptor);
                if (!_executors.TryGetValue(
                        descriptor.ProtocolId,
                        out var executor))
                {
                    throw new InvalidOperationException(
                        $"标准表现效果“{descriptor.ProtocolId}”没有注册 Unity 执行器。");
                }

                var parameters = NormalizeParameters(command, descriptor);
                return PresentationPreparationResult.Success(
                    new PreparedPresentationEffect(
                        command,
                        descriptor,
                        executor,
                        _targetResolver.ResolveTarget(command, descriptor),
                        _targetResolver.ResolveSignalContext(
                            command,
                            descriptor),
                        parameters,
                        ResolveResources(command, descriptor, parameters)));
            }
            catch (Exception exception)
            {
                return PresentationPreparationResult.Failed(
                    PresentationDispatchResult.Failure(
                        command,
                        "表现准备.配置或场景无效",
                        exception.Message));
            }
        }

        private static void ValidateCommandContract(
            PresentationEffectCommand command,
            PresentationEffectDescriptor descriptor)
        {
            if (!string.Equals(
                    command.Channel,
                    descriptor.Channel,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"表现命令“{command.CommandId}”的通道“{command.Channel}”" +
                    $"与原语通道“{descriptor.Channel}”不一致。");
            }

            if (!descriptor.AllowedLifecycles.Contains(command.Lifecycle))
            {
                throw new InvalidOperationException(
                    $"表现命令“{command.CommandId}”的生命周期“" +
                    $"{command.Lifecycle}”不被原语“{descriptor.ChineseName}”允许。");
            }
        }

        private static IReadOnlyDictionary<string, PresentationValue>
            NormalizeParameters(
                PresentationEffectCommand command,
                PresentationEffectDescriptor descriptor)
        {
            var declared = descriptor.Parameters.ToDictionary(
                value => value.Name,
                StringComparer.Ordinal);
            var unknown = command.Parameters.Keys.FirstOrDefault(
                key => !declared.ContainsKey(key));
            if (unknown != null)
            {
                throw new InvalidOperationException(
                    $"表现命令“{command.CommandId}”包含未声明参数“{unknown}”。");
            }

            var values = new Dictionary<string, PresentationValue>(
                StringComparer.Ordinal);
            foreach (var parameter in descriptor.Parameters)
            {
                if (!command.Parameters.TryGetValue(
                        parameter.Name,
                        out var value))
                {
                    value = parameter.DefaultValue;
                }

                if (value == null)
                {
                    if (parameter.Required)
                    {
                        throw new InvalidOperationException(
                            $"表现命令“{command.CommandId}”缺少必填参数“" +
                            $"{parameter.Name}”。");
                    }

                    continue;
                }

                if (!parameter.Matches(value, out var reason))
                {
                    throw new InvalidOperationException(
                        $"表现命令“{command.CommandId}”的参数“" +
                        $"{parameter.Name}”无效：{reason}");
                }

                values.Add(parameter.Name, value);
            }

            return new ReadOnlyDictionary<string, PresentationValue>(
                values);
        }

        private IReadOnlyDictionary<string, UnityEngine.Object>
            ResolveResources(
                PresentationEffectCommand command,
                PresentationEffectDescriptor descriptor,
                IReadOnlyDictionary<string, PresentationValue> parameters)
        {
            var resources = new Dictionary<string, UnityEngine.Object>(
                StringComparer.Ordinal);
            foreach (var parameter in descriptor.Parameters.Where(value =>
                         value.Kind == PresentationParameterKind.Resource))
            {
                var resourceId = parameters[parameter.Name].Text;
                if (_context.Resources == null
                    || !_context.Resources.TryResolve(
                        resourceId,
                        out var resource)
                    || resource == null)
                {
                    throw new InvalidOperationException(
                        $"表现命令“{command.CommandId}”无法解析资源“" +
                        $"{resourceId}”。");
                }

                resources.Add(parameter.Name, resource);
            }

            return new ReadOnlyDictionary<string, UnityEngine.Object>(
                resources);
        }

        private static IReadOnlyDictionary<
            string,
            IPresentationEffectExecutor> CopyExecutors(
                IEnumerable<IPresentationEffectExecutor> executors)
        {
            if (executors == null)
            {
                throw new ArgumentNullException(nameof(executors));
            }

            var copy = new Dictionary<string, IPresentationEffectExecutor>(
                StringComparer.Ordinal);
            foreach (var executor in executors)
            {
                if (executor == null
                    || string.IsNullOrWhiteSpace(executor.EffectId)
                    || !copy.TryAdd(executor.EffectId.Trim(), executor))
                {
                    throw new ArgumentException(
                        "表现执行器不能为 null、效果 ID 不能为空且不能重复。",
                        nameof(executors));
                }
            }

            return new ReadOnlyDictionary<
                string,
                IPresentationEffectExecutor>(copy);
        }
    }
}
