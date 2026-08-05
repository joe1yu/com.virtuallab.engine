using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Chemistry.Matter;
using VirtualLab.Kernel;
using VirtualLab.Measurement;

namespace VirtualLab.Chemistry.Tests
{
    public sealed class ChemistryCourseConfigurationCodecTests
    {
        [Test]
        public void 当前课程正文可以无版本无哈希地往返()
        {
            var codec = new ChemistryConfigurationCodec();

            var payload = codec.EncodeCourseConfiguration(Configuration());
            var restored = codec.DecodeCourseConfiguration(payload);

            Assert.That(
                restored.Reactions.Single().Products.Single().SubstanceId,
                Is.EqualTo("product"));
            Assert.That(payload, Does.Not.Contain("schemaVersion"));
            Assert.That(payload, Does.Not.Contain("contentHash"));
        }

        [Test]
        public void 化学物质与反应接受稳定的自然中文标识()
        {
            var codec = new ChemistryConfigurationCodec();
            var payload = codec.EncodeCourseConfiguration(Configuration())
                .Replace("\"product\"", "\"产物\"")
                .Replace("\"source\"", "\"反应物\"")
                .Replace("\"reaction.test\"", "\"受热分解反应\"");

            var restored = codec.DecodeCourseConfiguration(payload);

            Assert.That(restored.Substances.Select(value => value.Id),
                Is.EquivalentTo(new[] { "产物", "反应物" }));
            Assert.That(restored.Reactions.Single().Id,
                Is.EqualTo("受热分解反应"));
        }

        [Test]
        public void 当前课程正文拒绝未知字段和重复字段()
        {
            var codec = new ChemistryConfigurationCodec();
            var payload = codec.EncodeCourseConfiguration(Configuration());

            Assert.That(
                codec.TryDecodeCourseConfiguration(
                    payload.Replace(
                        "\"substances\":",
                        "\"unknown\":1,\"substances\":"),
                    null,
                    out _,
                    out var unknownError),
                Is.False);
            Assert.That(
                unknownError,
                Is.EqualTo("chemistry.course.definition.invalid"));

            Assert.That(
                codec.TryDecodeCourseConfiguration(
                    payload.Replace(
                        "\"substances\":",
                        "\"substances\":[],\"substances\":"),
                    null,
                    out _,
                    out var duplicateError),
                Is.False);
            Assert.That(
                duplicateError,
                Is.EqualTo("chemistry.course.definition.invalid"));
        }

        [Test]
        public void 无指纹课程载荷仍拒绝超过平台上限的输入()
        {
            var payload = new string(' ', 4 * 1024 * 1024 + 1);

            var accepted = new ChemistryConfigurationCodec()
                .TryDecodeCourseConfiguration(
                    payload,
                    null,
                    out _,
                    out var errorCode);

            Assert.That(accepted, Is.False);
            Assert.That(
                errorCode,
                Is.EqualTo("chemistry.course.payload.too-large"));
        }

        private static ChemistryRuntimeConfiguration Configuration()
        {
            return new ChemistryRuntimeConfiguration(
                new List<ChemistrySubstanceDefinition>
                {
                    new ChemistrySubstanceDefinition(
                        "product",
                        "产物",
                        MatterPhase.Solid,
                        1m,
                        ChemistryMolarMassUnit.GramPerMole),
                    new ChemistrySubstanceDefinition(
                        "source",
                        "反应物",
                        MatterPhase.Solid,
                        1m,
                        ChemistryMolarMassUnit.GramPerMole)
                },
                new List<ChemistryReactionDefinition>
                {
                    new ChemistryReactionDefinition(
                        "reaction.test",
                        new List<ChemistryReactionTerm>
                        {
                            new ChemistryReactionTerm(
                                "source",
                                1m,
                                Unit.Gram,
                                MatterPhase.Solid,
                                1m)
                        },
                        new List<ChemistryReactionTerm>
                        {
                            new ChemistryReactionTerm(
                                "product",
                                1m,
                                Unit.Gram,
                                MatterPhase.Solid,
                                1m)
                        },
                        ChemistryReactionProcessKind.ThermalDecomposition,
                        0m,
                        1m,
                        false)
                },
                new List<ChemistryInitialSubstance>(),
                new List<ChemistryEntityCapabilityBinding>());
        }
    }
}
