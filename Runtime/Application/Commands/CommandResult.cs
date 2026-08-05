using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VirtualLab.Application.Courses;
using VirtualLab.Application.Events;

namespace VirtualLab.Application.Commands
{
    public sealed class CommandResult
    {
        private CommandResult(
            bool isAccepted,
            string rejectionCode,
            IReadOnlyList<string> rejectionCodes,
            IReadOnlyList<DomainEventEnvelope> events,
            SemanticActionExecution execution)
        {
            IsAccepted = isAccepted;
            RejectionCode = rejectionCode;
            RejectionCodes = rejectionCodes;
            Events = events;
            Execution = execution;
        }

        public bool IsAccepted { get; }

        public string RejectionCode { get; }

        public IReadOnlyList<string> RejectionCodes { get; }

        public IReadOnlyList<DomainEventEnvelope> Events { get; }

        public SemanticActionExecution Execution { get; }

        public static CommandResult Accepted(
            IReadOnlyList<DomainEventEnvelope> events,
            SemanticActionExecution execution)
        {
            if (execution == null)
            {
                throw new ArgumentNullException(nameof(execution));
            }

            return AcceptedCore(events, execution);
        }

        /// <summary>
        /// 仅供未进入课程语义操作链路的通用领域命令使用。
        /// </summary>
        public static CommandResult AcceptedWithoutExecution(
            IReadOnlyList<DomainEventEnvelope> events)
        {
            return AcceptedCore(events, null);
        }

        private static CommandResult AcceptedCore(
            IReadOnlyList<DomainEventEnvelope> events,
            SemanticActionExecution execution)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            return new CommandResult(
                true,
                null,
                EmptyRejectionCodes(),
                Copy(events),
                execution);
        }

        public static CommandResult Rejected(string code)
        {
            return Rejected(code, new[] { code });
        }

        public static CommandResult Rejected(
            string primaryCode,
            IEnumerable<string> rejectionCodes)
        {
            if (string.IsNullOrWhiteSpace(primaryCode))
            {
                throw new ArgumentException(
                    "A rejection code cannot be blank.",
                    nameof(primaryCode));
            }

            if (rejectionCodes == null)
            {
                throw new ArgumentNullException(nameof(rejectionCodes));
            }

            var primary = primaryCode.Trim();
            var copy = new List<string> { primary };
            foreach (var code in rejectionCodes)
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new ArgumentException(
                        "Rejection code lists cannot contain blank entries.",
                        nameof(rejectionCodes));
                }

                var normalized = code.Trim();
                if (!copy.Contains(normalized))
                {
                    copy.Add(normalized);
                }
            }

            return new CommandResult(
                false,
                primary,
                new ReadOnlyCollection<string>(copy),
                EmptyEvents(),
                null);
        }

        private static IReadOnlyList<DomainEventEnvelope> Copy(IReadOnlyList<DomainEventEnvelope> events)
        {
            var copy = new List<DomainEventEnvelope>(events.Count);
            for (var index = 0; index < events.Count; index++)
            {
                if (events[index] == null)
                {
                    throw new ArgumentException("Accepted event lists cannot contain null entries.", nameof(events));
                }

                copy.Add(events[index]);
            }

            return new ReadOnlyCollection<DomainEventEnvelope>(copy);
        }

        private static IReadOnlyList<DomainEventEnvelope> EmptyEvents()
        {
            return new ReadOnlyCollection<DomainEventEnvelope>(new List<DomainEventEnvelope>());
        }

        private static IReadOnlyList<string> EmptyRejectionCodes()
        {
            return new ReadOnlyCollection<string>(new List<string>());
        }
    }
}
