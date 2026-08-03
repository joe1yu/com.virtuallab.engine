using System;
using System.Collections.Generic;
using System.Linq;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    /// <summary>
    /// 无模型课程把表现命令投影为文字。它仍消费正式表现命令，
    /// 只是暂时不解析模型、锚点、材质或粒子资源。
    /// </summary>
    public interface IPresentationTextSink
    {
        void PresentText(PresentationEffectCommand command);
    }

    public sealed class TextPresentationDispatcher :
        IPresentationCommandDispatcher
    {
        private readonly IPresentationTextSink _sink;

        public TextPresentationDispatcher(IPresentationTextSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public PresentationDispatchResult Dispatch(
            PresentationEffectCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            _sink.PresentText(command);
            return PresentationDispatchResult.Success(command);
        }

        public IReadOnlyList<PresentationDispatchResult>
            SynchronizeAuthoritativeState(
                IEnumerable<PresentationEffectCommand> commands)
        {
            return (commands
                    ?? throw new ArgumentNullException(nameof(commands)))
                .Select(Dispatch)
                .ToArray();
        }
    }
}
