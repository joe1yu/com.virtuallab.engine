using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VirtualLab.UnityAdapters.Input
{
    public interface ICommandIdGenerator
    {
        string Next(string operation);
    }

    public sealed class SessionCommandIdGenerator : ICommandIdGenerator
    {
        private static readonly Regex StablePart = new Regex(
            "^[A-Za-z0-9._-]+$",
            RegexOptions.CultureInvariant);

        private readonly object _gate = new object();
        private readonly string _prefix;
        private long _cursor;

        public SessionCommandIdGenerator(string sessionId)
            : this(
                sessionId,
                Guid.NewGuid().ToString("N"),
                0)
        {
        }

        public SessionCommandIdGenerator(
            string sessionId,
            string scopeId,
            long cursor)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException(
                    "A command ID session cannot be blank.",
                    nameof(sessionId));
            }

            if (string.IsNullOrWhiteSpace(scopeId) ||
                !StablePart.IsMatch(scopeId.Trim()))
            {
                throw new ArgumentException(
                    "A command ID scope must be a stable identifier.",
                    nameof(scopeId));
            }

            if (cursor < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cursor));
            }

            SessionId = sessionId.Trim();
            ScopeId = scopeId.Trim();
            _cursor = cursor;
            _prefix =
                "unity:" +
                SessionId.Length.ToString(CultureInfo.InvariantCulture) +
                ":" +
                SessionId +
                ":" +
                ScopeId +
                ":";
        }

        public string SessionId { get; }

        public string ScopeId { get; }

        public long Cursor
        {
            get
            {
                lock (_gate)
                {
                    return _cursor;
                }
            }
        }

        public string Next(string operation)
        {
            if (string.IsNullOrWhiteSpace(operation) ||
                !StablePart.IsMatch(operation.Trim()))
            {
                throw new ArgumentException(
                    "A command operation must be a stable identifier.",
                    nameof(operation));
            }

            lock (_gate)
            {
                if (_cursor == long.MaxValue)
                {
                    throw new InvalidOperationException(
                        "The command ID cursor is exhausted.");
                }

                _cursor++;
                return
                    _prefix +
                    _cursor.ToString(
                        "D12",
                        CultureInfo.InvariantCulture) +
                    ":" +
                    operation.Trim();
            }
        }

        public static SessionCommandIdGenerator FromProcessedCommandIds(
            string sessionId,
            string scopeId,
            IEnumerable<string> processedCommandIds)
        {
            if (processedCommandIds == null)
            {
                throw new ArgumentNullException(
                    nameof(processedCommandIds));
            }

            var candidate = new SessionCommandIdGenerator(
                sessionId,
                scopeId,
                0);
            var maximum = 0L;
            foreach (var commandId in processedCommandIds)
            {
                if (string.IsNullOrWhiteSpace(commandId) ||
                    !commandId.StartsWith(
                        candidate._prefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var sequenceStart = candidate._prefix.Length;
                var separator = commandId.IndexOf(
                    ':',
                    sequenceStart);
                if (separator <= sequenceStart ||
                    !long.TryParse(
                        commandId.Substring(
                            sequenceStart,
                            separator - sequenceStart),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var sequence) ||
                    sequence <= 0)
                {
                    throw new ArgumentException(
                        "A processed command has a malformed session cursor.",
                        nameof(processedCommandIds));
                }

                maximum = Math.Max(maximum, sequence);
            }

            return new SessionCommandIdGenerator(
                sessionId,
                scopeId,
                maximum);
        }
    }
}
