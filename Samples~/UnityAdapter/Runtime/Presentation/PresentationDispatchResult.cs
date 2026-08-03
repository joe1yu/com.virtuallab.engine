using System;
using System.Collections.Generic;
using VirtualLab.Presentation;

namespace VirtualLab.UnityAdapters.Presentation
{
    public sealed class PresentationDispatchResult
    {
        private PresentationDispatchResult(
            bool succeeded,
            PresentationEffectCommand command,
            string errorCode,
            string errorMessage)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            Succeeded = succeeded;
            CommandId = command.CommandId;
            ProtocolId = command.EffectId;
            Target = command.Target;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public bool Succeeded { get; }

        public string CommandId { get; }

        public string ProtocolId { get; }

        public PresentationTargetReference Target { get; }

        public string ErrorCode { get; }

        public string ErrorMessage { get; }

        public static PresentationDispatchResult Success(
            PresentationEffectCommand command)
        {
            return new PresentationDispatchResult(
                true,
                command,
                null,
                null);
        }

        public static PresentationDispatchResult Failure(
            PresentationEffectCommand command,
            string errorCode,
            string errorMessage)
        {
            return new PresentationDispatchResult(
                false,
                command,
                errorCode,
                errorMessage);
        }
    }

    public enum PresentationFailurePolicy
    {
        RecordAndContinue,
        ThrowAfterRecording
    }

    public interface IPresentationCommandDispatcher
    {
        PresentationDispatchResult Dispatch(
            PresentationEffectCommand command);

        IReadOnlyList<PresentationDispatchResult>
            SynchronizeAuthoritativeState(
                IEnumerable<PresentationEffectCommand> commands);
    }
}
