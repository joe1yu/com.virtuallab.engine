using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VirtualLab.Infrastructure.Reporting
{
    public sealed class ExperimentReportJsonReadResult
    {
        private ExperimentReportJsonReadResult(
            ExperimentReport report,
            string errorCode)
        {
            Report = report;
            ErrorCode = errorCode;
        }

        public bool IsSuccess => Report != null;
        public ExperimentReport Report { get; }
        public string ErrorCode { get; }

        internal static ExperimentReportJsonReadResult Success(
            ExperimentReport report)
        {
            return new ExperimentReportJsonReadResult(
                report ?? throw new ArgumentNullException(nameof(report)),
                null);
        }

        internal static ExperimentReportJsonReadResult Failure(string code)
        {
            return new ExperimentReportJsonReadResult(
                null,
                string.IsNullOrWhiteSpace(code)
                    ? "report.json.invalid"
                    : code);
        }
    }

    public sealed class ExperimentReportJsonReader
    {
        public const int MaximumCharacters = 4 * 1024 * 1024;
        public const int MaximumDepth = 64;
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public ExperimentReportJsonReadResult TryRead(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ExperimentReportJsonReadResult.Failure(
                    "report.json.path.invalid");
            }

            try
            {
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (var reader = new StreamReader(
                    stream,
                    StrictUtf8,
                    true))
                {
                    if (!TryReadLimited(reader, out var json))
                    {
                        return ExperimentReportJsonReadResult.Failure(
                            "report.json.size.invalid");
                    }

                    return TryParse(json);
                }
            }
            catch (DecoderFallbackException)
            {
                return ExperimentReportJsonReadResult.Failure(
                    "report.json.invalid");
            }
            catch (Exception error) when (
                error is ArgumentException ||
                error is NotSupportedException ||
                error is PathTooLongException ||
                error is IOException ||
                error is UnauthorizedAccessException ||
                error is System.Security.SecurityException)
            {
                return ExperimentReportJsonReadResult.Failure(
                    "report.json.read.failed");
            }
        }

        private static bool TryReadLimited(
            TextReader reader,
            out string json)
        {
            var buffer = new char[4096];
            var builder = new StringBuilder();
            while (true)
            {
                var count = reader.Read(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    json = builder.ToString();
                    return true;
                }

                if (builder.Length > MaximumCharacters - count)
                {
                    json = null;
                    return false;
                }

                builder.Append(buffer, 0, count);
            }
        }

        public ExperimentReportJsonReadResult TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) ||
                json.Length > MaximumCharacters)
            {
                return ExperimentReportJsonReadResult.Failure(
                    "report.json.size.invalid");
            }

            try
            {
                JObject root;
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = MaximumDepth,
                    SupportMultipleContent = true
                })
                {
                    root = JObject.Load(
                        reader,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling =
                                DuplicatePropertyNameHandling.Error
                        });
                    if (reader.Read())
                    {
                        return ExperimentReportJsonReadResult.Failure(
                            "report.json.trailing-content");
                    }
                }

                return Decode(root);
            }
            catch (Exception error) when (
                error is JsonException ||
                error is ArgumentException ||
                error is InvalidOperationException ||
                error is FormatException ||
                error is OverflowException)
            {
                return ExperimentReportJsonReadResult.Failure(
                    "report.json.invalid");
            }
        }

        private static ExperimentReportJsonReadResult Decode(JObject root)
        {
            if (!Exact(
                    root,
                    "metadata",
                    "score",
                    "goals",
                    "observations",
                    "risks",
                    "deductions",
                    "hints") ||
                !(root["metadata"] is JObject metadata) ||
                !(root["score"] is JObject score) ||
                !(root["goals"] is JArray goals) ||
                !(root["observations"] is JArray observations) ||
                !(root["risks"] is JArray risks) ||
                !(root["deductions"] is JArray deductions) ||
                !(root["hints"] is JArray hints))
            {
                return ExperimentReportJsonReadResult.Failure(
                    "report.json.schema.invalid");
            }

            if (!TryMetadata(metadata, out var decodedMetadata) ||
                !TryScore(score, out var decodedScore) ||
                !TryGoals(goals, out var decodedGoals) ||
                !TryObservations(
                    observations,
                    out var decodedObservations) ||
                !TryRisks(risks, out var decodedRisks) ||
                !TryDeductions(
                    deductions,
                    out var decodedDeductions) ||
                !TryHints(hints, out var decodedHints))
            {
                return ExperimentReportJsonReadResult.Failure(
                    "report.json.field.invalid");
            }

            if (!Ordered(decodedGoals) ||
                !Ordered(decodedObservations) ||
                !Ordered(decodedRisks) ||
                !Ordered(decodedDeductions) ||
                !Ordered(decodedHints) ||
                decodedHints
                    .Select(value => value.Id)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != decodedHints.Count ||
                decodedGoals.Any(value =>
                    value.Sequence > decodedMetadata.LastSequence) ||
                decodedObservations.Any(value =>
                    value.Sequence > decodedMetadata.LastSequence) ||
                decodedRisks.Any(value =>
                    value.Sequence > decodedMetadata.LastSequence) ||
                decodedDeductions.Any(value =>
                    value.Sequence > decodedMetadata.LastSequence) ||
                decodedHints.Any(value =>
                    value.Sequence > decodedMetadata.LastSequence))
            {
                return ExperimentReportJsonReadResult.Failure(
                    "report.json.order.invalid");
            }

            return ExperimentReportJsonReadResult.Success(
                new ExperimentReport(
                    decodedMetadata,
                    decodedScore,
                    decodedGoals,
                    decodedObservations,
                    decodedRisks,
                    decodedDeductions,
                    decodedHints));
        }

        private static bool TryMetadata(
            JObject value,
            out ReportMetadata result)
        {
            result = null;
            if (!Exact(
                    value,
                    "courseId",
                    "sessionId",
                    "randomSeed",
                    "lastSequence") ||
                !Text(value["courseId"], out var courseId) ||
                !Text(value["sessionId"], out var sessionId) ||
                !Integer(value["randomSeed"], out var randomSeed) ||
                !Long(value["lastSequence"], out var lastSequence) ||
                lastSequence < 0)
            {
                return false;
            }

            result = new ReportMetadata(
                courseId,
                sessionId,
                randomSeed,
                lastSequence);
            return true;
        }

        private static bool TryScore(
            JObject value,
            out ReportScore result)
        {
            result = null;
            if (!Exact(
                    value,
                    "scientificResult",
                    "operationQuality",
                    "safety",
                    "efficiency") ||
                !Score(value["scientificResult"], out var scientific) ||
                !Score(value["operationQuality"], out var operation) ||
                !Score(value["safety"], out var safety) ||
                !Score(value["efficiency"], out var efficiency))
            {
                return false;
            }

            result = new ReportScore(
                scientific,
                operation,
                safety,
                efficiency);
            return true;
        }

        private static bool TryGoals(
            JArray values,
            out List<ReportGoal> result)
        {
            result = new List<ReportGoal>();
            foreach (var token in values)
            {
                if (!(token is JObject item) ||
                    !Exact(item, "id", "sequence", "isSatisfied") ||
                    !Text(item["id"], out var id) ||
                    !Sequence(item["sequence"], out var sequence) ||
                    !Boolean(item["isSatisfied"], out var satisfied))
                {
                    return false;
                }

                result.Add(new ReportGoal(id, sequence, satisfied));
            }

            return true;
        }

        private static bool TryObservations(
            JArray values,
            out List<ReportObservation> result)
        {
            result = new List<ReportObservation>();
            foreach (var token in values)
            {
                if (!(token is JObject item) ||
                    !Exact(item, "id", "sequence", "eventType") ||
                    !Text(item["id"], out var id) ||
                    !Sequence(item["sequence"], out var sequence) ||
                    !Text(item["eventType"], out var eventType))
                {
                    return false;
                }

                result.Add(new ReportObservation(
                    id,
                    sequence,
                    eventType));
            }

            return true;
        }

        private static bool TryRisks(
            JArray values,
            out List<ReportRisk> result)
        {
            result = new List<ReportRisk>();
            foreach (var token in values)
            {
                if (!(token is JObject item) ||
                    !Exact(item, "id", "sequence", "eventType") ||
                    !Text(item["id"], out var id) ||
                    !Sequence(item["sequence"], out var sequence) ||
                    !Text(item["eventType"], out var eventType))
                {
                    return false;
                }

                result.Add(new ReportRisk(id, sequence, eventType));
            }

            return true;
        }

        private static bool TryDeductions(
            JArray values,
            out List<ReportDeduction> result)
        {
            result = new List<ReportDeduction>();
            foreach (var token in values)
            {
                if (!(token is JObject item) ||
                    !Exact(
                        item,
                        "id",
                        "sequence",
                        "reason",
                        "scientificResultDelta",
                        "operationQualityDelta",
                        "safetyDelta",
                        "efficiencyDelta") ||
                    !Text(item["id"], out var id) ||
                    !Sequence(item["sequence"], out var sequence) ||
                    !Text(item["reason"], out var reason) ||
                    !Integer(
                        item["scientificResultDelta"],
                        out var scientific) ||
                    !Integer(
                        item["operationQualityDelta"],
                        out var operation) ||
                    !Integer(item["safetyDelta"], out var safety) ||
                    !Integer(
                        item["efficiencyDelta"],
                        out var efficiency))
                {
                    return false;
                }

                result.Add(new ReportDeduction(
                    id,
                    sequence,
                    reason,
                    scientific,
                    operation,
                    safety,
                    efficiency));
            }

            return true;
        }

        private static bool TryHints(
            JArray values,
            out List<ReportHint> result)
        {
            result = new List<ReportHint>();
            foreach (var token in values)
            {
                if (!(token is JObject item) ||
                    !Exact(
                        item,
                        "id",
                        "sequence",
                        "ruleId",
                        "goalId",
                        "level",
                        "message",
                        "tick") ||
                    !Text(item["id"], out var id) ||
                    !Sequence(item["sequence"], out var sequence) ||
                    !Text(item["ruleId"], out var ruleId) ||
                    !Text(item["goalId"], out var goalId) ||
                    !Integer(item["level"], out var level) ||
                    level < 1 ||
                    level > 3 ||
                    !Text(item["message"], out var message) ||
                    !Long(item["tick"], out var tick) ||
                    tick < 0)
                {
                    return false;
                }

                result.Add(new ReportHint(
                    id,
                    sequence,
                    ruleId,
                    goalId,
                    level,
                    message,
                    tick));
            }

            return true;
        }

        private static bool Ordered<T>(IReadOnlyList<T> values)
            where T : ReportEvidence
        {
            for (var index = 1; index < values.Count; index++)
            {
                var previous = values[index - 1];
                var current = values[index];
                if (current.Sequence < previous.Sequence ||
                    (current.Sequence == previous.Sequence &&
                        string.CompareOrdinal(
                            current.Id,
                            previous.Id) < 0))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Exact(
            JObject value,
            params string[] expected)
        {
            if (value == null || value.Count != expected.Length)
            {
                return false;
            }

            var names = new HashSet<string>(
                expected,
                StringComparer.Ordinal);
            return value.Properties().All(property =>
                names.Contains(property.Name));
        }

        private static bool Text(JToken token, out string value)
        {
            value = null;
            if (token == null ||
                token.Type != JTokenType.String ||
                string.IsNullOrWhiteSpace(token.Value<string>()))
            {
                return false;
            }

            value = token.Value<string>().Trim();
            return true;
        }

        private static bool Boolean(JToken token, out bool value)
        {
            value = false;
            if (token == null || token.Type != JTokenType.Boolean)
            {
                return false;
            }

            value = token.Value<bool>();
            return true;
        }

        private static bool Score(JToken token, out int value)
        {
            return Integer(token, out value) &&
                value >= 0 &&
                value <= 100;
        }

        private static bool Sequence(JToken token, out long value)
        {
            return Long(token, out value) && value >= 0;
        }

        private static bool Integer(JToken token, out int value)
        {
            value = 0;
            if (!Long(token, out var parsed) ||
                parsed < int.MinValue ||
                parsed > int.MaxValue)
            {
                return false;
            }

            value = (int)parsed;
            return true;
        }

        private static bool Long(JToken token, out long value)
        {
            value = 0L;
            if (token == null || token.Type != JTokenType.Integer)
            {
                return false;
            }

            return long.TryParse(
                token.ToString(Formatting.None),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
