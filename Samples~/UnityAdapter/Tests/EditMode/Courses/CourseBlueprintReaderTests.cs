using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Csv;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseBlueprintReaderTests
    {
        [Test]
        public void 严格CSV支持引号内逗号空行和两种换行()
        {
            var result = new StrictCsvReader().Read(
                "实验对象.csv",
                "实体ID,显示名称\r\n"
                + "试管,\"试管,耐热\"\r\n\r\n"
                + "烧杯,烧杯\n");

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Rows.Count, Is.EqualTo(2));
            Assert.That(result.Rows[0]["显示名称"], Is.EqualTo("试管,耐热"));
            Assert.That(result.Rows[1].LineNumber, Is.EqualTo(4));
        }

        [Test]
        public void 自然中文表头在读取边界转换为稳定列协议()
        {
            var result = new StrictCsvReader().Read(
                "课程.csv",
                "课程标识,操作者实体标识,实验预制体\n"
                + "演示课程,学生,环境.prefab\n");

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Headers,
                Is.EqualTo(new[]
                {
                    "课程ID",
                    "操作者实体ID",
                    "实验Prefab"
                }));
            Assert.That(result.ConfiguredHeaders,
                Is.EqualTo(new[]
                {
                    "课程标识",
                    "操作者实体标识",
                    "实验预制体"
                }));
            Assert.That(result.Rows.Single()["课程ID"],
                Is.EqualTo("演示课程"));
            Assert.That(result.Rows.Single()["实验Prefab"],
                Is.EqualTo("环境.prefab"));

            var editable = EditableCsvDocument.Parse(
                "课程.csv",
                "课程标识,实验预制体\n演示课程,环境.prefab\n");
            StringAssert.StartsWith(
                "课程标识,实验预制体",
                editable.ToCsv());
        }

        [Test]
        public void 严格CSV拒绝非法编码表头和引号语法()
        {
            var invalidUtf8 = new StrictCsvReader().ReadBytes(
                "实验对象.csv",
                new byte[] { 0xE5, 0xAE, 0x9E, 0xC3, 0x28 });
            var invalidHeader = new StrictCsvReader().Read(
                "实验对象.csv",
                "实体ID,,显示e\u0301名称\n试管,,试\"管\n");
            var codes = invalidHeader.Diagnostics
                .Select(value => value.Code)
                .ToArray();

            Assert.That(
                invalidUtf8.Diagnostics.Select(value => value.Code),
                Does.Contain("csv.encoding.invalid"));
            Assert.That(codes, Does.Contain("csv.header.empty"));
            Assert.That(codes, Does.Contain("csv.header.not-normalized"));
            Assert.That(codes, Does.Contain("csv.quote.invalid"));
        }

        [Test]
        public void 严格CSV诊断包含文件行列原因和修复建议()
        {
            var duplicateHeader = new StrictCsvReader().Read(
                "实验对象.csv",
                "实体ID,实体ID\n试管,重复\n");
            var nonNormalized = new StrictCsvReader().Read(
                "教学评价.csv",
                "记录ID,名称\n目标.e\u0301,未规范化\n");
            var diagnostic = duplicateHeader.Diagnostics.Single();

            Assert.That(diagnostic.FileName, Is.EqualTo("实验对象.csv"));
            Assert.That(diagnostic.Line, Is.GreaterThan(0));
            Assert.That(diagnostic.Column, Is.GreaterThan(0));
            Assert.That(diagnostic.Reason, Is.Not.Empty);
            Assert.That(diagnostic.Suggestion, Is.Not.Empty);
            Assert.That(
                nonNormalized.Diagnostics.Select(value => value.Code),
                Does.Contain("csv.unicode.not-normalized"));
        }

        [Test]
        public void 最小高层蓝图夹具可从目录读取()
        {
            var directory = Path.GetFullPath(
                "Packages/com.virtuallab.engine/Tests/EditMode/Fixtures/"
                + "Courses/最小课程蓝图");

            var result = new CourseBlueprintReader().Read(
                CourseBlueprintSource.FromDirectory(directory));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Blueprint.Course.CourseId, Is.EqualTo("最小课程"));
            Assert.That(
                result.Blueprint.Objects.Select(value => value.EntityId),
                Is.EqualTo(new[] { "试管" }));
        }

        [Test]
        public void 两张必需表可读取且可选表缺失等同空集合()
        {
            var result = new CourseBlueprintReader().Read(
                MinimumBlueprint());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Blueprint.Course.CourseId, Is.EqualTo("最小课程"));
            Assert.That(
                result.Blueprint.Course.DisciplinePackageIds,
                Is.EqualTo(new[] { "化学基础" }));
            var tube = result.Blueprint.Objects.Single();
            Assert.That(tube.EntityId, Is.EqualTo("试管"));
            Assert.That(
                tube.FeatureIds,
                Is.EqualTo(new[] { "可抓取", "容器" }));
            Assert.That(tube.InitialPosition.X, Is.EqualTo(0));
            Assert.That(tube.InitialPosition.Y, Is.EqualTo(1));
            Assert.That(tube.InitialPosition.Z, Is.EqualTo(0));
            Assert.That(
                tube.ExtensionValues["参数.容量毫升"].RawValue,
                Is.EqualTo("100"));
            Assert.That(
                tube.ExtensionValues["参数.容量毫升"].Source.Line,
                Is.EqualTo(2));
            Assert.That(result.Blueprint.InteractionRules, Is.Empty);
            Assert.That(result.Blueprint.InitialRelations, Is.Empty);
            Assert.That(result.Blueprint.DisciplineProcesses, Is.Empty);
            Assert.That(result.Blueprint.TeachingEvaluations, Is.Empty);
            Assert.That(result.Blueprint.PresentationOverrides, Is.Empty);
            Assert.That(result.Blueprint.AcceptanceRecords, Is.Empty);
            Assert.That(result.Blueprint.AdvancedOverrides, Is.Empty);
        }

        [Test]
        public void 初始关系独立声明实体端点和可选端口()
        {
            var source = MinimumBlueprint()
                .Replace(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                    + "瓶盖,瓶盖,可覆盖,0|1|0,0|0|0\n"
                    + "试管,试管,可覆盖,0|0|0,0|0|0\n")
                .Append(
                    "初始关系.csv",
                    "关系标识,关系类型,来源实体,目标实体,来源端口标识,目标端口标识\n"
                    + "初始关系.盖合,交互.关系.覆盖,瓶盖,试管,,\n");

            var result = new CourseBlueprintReader().Read(source);

            Assert.That(result.IsSuccess, Is.True);
            var relation = result.Blueprint.InitialRelations.Single();
            Assert.That(relation.RelationId, Is.EqualTo("初始关系.盖合"));
            Assert.That(relation.RelationTypeId,
                Is.EqualTo("交互.关系.覆盖"));
            Assert.That(relation.SourceEntityId, Is.EqualTo("瓶盖"));
            Assert.That(relation.TargetEntityId, Is.EqualTo("试管"));
            Assert.That(relation.SourcePortId, Is.Empty);
            Assert.That(relation.TargetPortId, Is.Empty);
        }

        [Test]
        public void 初始关系拒绝未知实体和单边端口()
        {
            var result = new CourseBlueprintReader().Read(
                MinimumBlueprint().Append(
                    "初始关系.csv",
                    "关系标识,关系类型,来源实体,目标实体,来源端口标识,目标端口标识\n"
                    + "初始关系.错误,交互.关系.连接,试管,未知对象,出口,\n"));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.initial-relation.entity-missing")
                    .And.Contain(
                        "blueprint.initial-relation.port-pair-invalid"));
        }

        [Test]
        public void 课程可显式指定操作者且操作者必须是实验对象()
        {
            var valid = new CourseBlueprintReader().Read(
                MinimumBlueprint()
                    .Replace(
                        "课程.csv",
                        "课程ID,显示名称,学科配方包,操作者实体ID,实验Prefab\n"
                        + "最小课程,最小课程,化学基础,教师,环境.prefab\n")
                    .Replace(
                        "实验对象.csv",
                        "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                        + "试管,试管,可抓取,0|1|0,0|0|0\n"
                        + "教师,教师,,0|0|0,0|0|0\n"));

            Assert.That(valid.IsSuccess, Is.True);
            Assert.That(valid.Blueprint.Course.ActorEntityId, Is.EqualTo("教师"));

            var invalid = new CourseBlueprintReader().Read(
                MinimumBlueprint().Replace(
                    "课程.csv",
                    "课程ID,显示名称,学科配方包,操作者实体ID,实验Prefab\n"
                    + "最小课程,最小课程,化学基础,教师,环境.prefab\n"));
            Assert.That(
                invalid.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.course.actor-missing"));
        }

        [Test]
        public void 缺少必需列重复实体和非法向量返回中文诊断()
        {
            var source = MinimumBlueprint()
                .Replace(
                    "课程.csv",
                    "课程ID,显示名称,实验Prefab\n"
                    + "最小课程,最小课程,环境.prefab\n")
                .Replace(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转\n"
                    + "试管,试管,可抓取,不是向量,0|0|0\n"
                    + "试管,重复试管,可抓取,0|0|0,0|0|0\n");

            var result = new CourseBlueprintReader().Read(source);
            var codes = result.Diagnostics.Select(value => value.Code);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(codes, Does.Contain("blueprint.column.missing"));
            Assert.That(codes, Does.Contain("blueprint.vector.invalid"));
            Assert.That(codes, Does.Contain("blueprint.entity.duplicate"));
            Assert.That(
                result.Diagnostics.All(value =>
                    !string.IsNullOrWhiteSpace(value.Reason)),
                Is.True);
        }

        [Test]
        public void 未知文件和非法UTF8返回可定位诊断()
        {
            var source = MinimumBlueprint()
                .Append("旧动作策略.csv", "策略ID,动作\n旧策略,抓取\n")
                .Replace(
                    "实验对象.csv",
                    new byte[] { 0xE5, 0xAE, 0x9E, 0xC3, 0x28 });

            var result = new CourseBlueprintReader().Read(source);

            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.file.unsupported"));
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("csv.encoding.invalid"));
            Assert.That(
                result.Diagnostics.Single(value =>
                    value.Code == "csv.encoding.invalid").FileName,
                Is.EqualTo("实验对象.csv"));
        }

        [Test]
        public void 只接受已贯通编译链路的参数扩展列()
        {
            var result = new CourseBlueprintReader().Read(
                MinimumBlueprint().Replace(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转,"
                    + "参数.端口.出口兼容组,初始关系.位于,初始物质.水.毫升\n"
                    + "试管,试管,可抓取,0|1|0,0|0|0,"
                    + "组.导气,铁架台,10\n"));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Diagnostics
                    .Where(value => value.Code
                        == "blueprint.column.unsupported")
                    .Select(value => value.ColumnName),
                Is.EquivalentTo(new[]
                {
                    "初始关系.位于",
                    "初始物质.水.毫升"
                }));
            var values = result.Blueprint.Objects.Single().ExtensionValues;
            Assert.That(
                values.Keys,
                Is.EqualTo(new[] { "参数.端口.出口兼容组" }));
            Assert.That(
                values["参数.端口.出口兼容组"].Source.FileName,
                Is.EqualTo("实验对象.csv"));
            Assert.That(
                values["参数.端口.出口兼容组"].Source.ConfigurationId,
                Is.EqualTo("试管"));
        }

        [Test]
        public void 文件枚举顺序不同产生相同蓝图顺序()
        {
            var source = MinimumBlueprint().Append(
                "交互规则.csv",
                "交互ID,处理方式,动作,来源,目标,顺序,要求类型,要求主体,"
                + "字段,比较,值,单位,结果配方,反馈配方,拒绝文案\n"
                + "交互.乙,收紧默认,抓取,试管,,20,状态,试管,温度,小于,50,"
                + "摄氏度,,,温度过高\n"
                + "交互.甲,禁用默认,抓取,铁架台,,10,,,,,,,,,铁架台已固定\n");

            var normal = new CourseBlueprintReader().Read(source);
            var reversed = new CourseBlueprintReader().Read(source.Reverse());

            Assert.That(normal.IsSuccess, Is.True);
            Assert.That(reversed.IsSuccess, Is.True);
            Assert.That(
                normal.Blueprint.InteractionRules.Select(value =>
                    value.InteractionId),
                Is.EqualTo(reversed.Blueprint.InteractionRules.Select(value =>
                    value.InteractionId)));
        }

        [Test]
        public void Excel文件开头BOM可读取且任意额外列被拒绝()
        {
            var courseBytes = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(
                    "课程ID,显示名称,学科配方包,实验Prefab\n"
                    + "最小课程,最小课程,,环境.prefab\n"))
                .ToArray();
            var source = MinimumBlueprint()
                .Replace("课程.csv", courseBytes)
                .Replace(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转,随意备注\n"
                    + "试管,试管,可抓取,0|0|0,0|0|0,备注\n");

            var result = new CourseBlueprintReader().Read(source);

            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.column.unsupported"));
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Not.Contain("csv.encoding.invalid"));
        }

        [Test]
        public void 重复表头返回诊断而不是抛出异常()
        {
            var source = MinimumBlueprint().Replace(
                "实验对象.csv",
                "实体ID,显示名称,特征列表,初始位置,初始旋转,参数.容量,参数.容量\n"
                + "试管,试管,可抓取,0|0|0,0|0|0,100,200\n");

            CourseBlueprintReadResult result = null;
            Assert.DoesNotThrow(() =>
                result = new CourseBlueprintReader().Read(source));
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("csv.header.duplicate"));
        }

        [Test]
        public void 学科简表将一行过程参数和中文协议转换为内部蓝图()
        {
            var source = MinimumBlueprint().Append(
                "学科过程.csv",
                "配置ID,类型,配方,主体,来源,目标,操作名称,协议,参数\n"
                + "过程.倾倒,过程,化学.倾倒,开始倾倒,试管,烧杯,,,"
                + "物质标识=water;计量单位=毫升\n"
                + "过程.附加转移,附加操作,化学.倾倒,开始倾倒,试管,烧杯,"
                + "转移溶质,转移物质,物质标识=solute;数量=1\n");

            var result = new CourseBlueprintReader().Read(source);

            Assert.That(result.IsSuccess, Is.True);
            var process = result.Blueprint.DisciplineProcesses.Single(value =>
                value.DefinitionId == "过程.倾倒");
            Assert.That(process.RecordType, Is.EqualTo("过程参数"));
            Assert.That(
                process.Parameters["参数.物质标识"].RawValue,
                Is.EqualTo("water"));
            Assert.That(
                process.Parameters["参数.计量单位"].RawValue,
                Is.EqualTo("毫升"));

            var addition = result.Blueprint.DisciplineProcesses.Single(value =>
                value.DefinitionId == "过程.附加转移");
            Assert.That(addition.RecordType, Is.EqualTo("过程操作"));
            Assert.That(addition.StartInteractionId, Is.EqualTo("转移溶质"));
            Assert.That(
                addition.StopInteractionId,
                Is.EqualTo("转移物质"));
        }

        [Test]
        public void 学科简表参数格式错误返回可定位诊断()
        {
            var result = new CourseBlueprintReader().Read(
                MinimumBlueprint().Append(
                    "学科过程.csv",
                    "配置ID,类型,配方,主体,来源,目标,操作名称,协议,参数\n"
                    + "过程.错误,过程,化学.倾倒,开始倾倒,试管,烧杯,,,"
                    + "物质标识=water;缺少等号\n"));

            var diagnostic = result.Diagnostics.Single(value =>
                value.Code == "blueprint.discipline.parameter.invalid");
            Assert.That(diagnostic.FileName, Is.EqualTo("学科过程.csv"));
            Assert.That(diagnostic.Line, Is.EqualTo(2));
            Assert.That(diagnostic.Column, Is.GreaterThan(0));
            Assert.That(diagnostic.Reason, Does.Contain("名称=值"));
        }

        [Test]
        public void 实验流程统一投影教学评价和验收记录()
        {
            const string header =
                "流程ID,步骤ID,顺序,记录类型,显示名称,动作,来源,目标,"
                + "触发类型,触发值,条件类型,主体,字段,比较,值,单位,"
                + "分值变化,提示文案,参数名,参数值\n";
            var result = new CourseBlueprintReader().Read(
                MinimumBlueprint().Append(
                    "实验流程.csv",
                    header
                    + "制取,步骤.投料,10,操作,,开始倾倒,试管,烧杯,"
                    + ",,,,,,,,,,请求流量,1\n"
                    + "教学,目标.完成,20,目标,完成实验,,,,状态满足,,,"
                    + "试管,来源对象教学状态,包含,已准备,,0,,,\n"
                    + "制取,步骤.断言,30,断言,,,,,,,状态,试管,"
                    + "来源对象教学状态,包含,已准备,,,,,\n"));

            Assert.That(
                result.IsSuccess,
                Is.True,
                string.Join("\n", result.Diagnostics.Select(value => value.Reason)));
            Assert.That(
                result.Blueprint.TeachingEvaluations.Single().EvaluationId,
                Is.EqualTo("目标.完成"));
            Assert.That(result.Blueprint.AcceptanceRecords.Count, Is.EqualTo(2));
            Assert.That(
                result.Blueprint.AcceptanceRecords.Single(value =>
                    value.RecordType == "动作").ParameterName,
                Is.EqualTo("请求流量"));
            Assert.That(
                result.Blueprint.AcceptanceRecords.Single(value =>
                    value.RecordType == "断言").ObjectId,
                Is.EqualTo("试管"));
        }

        [Test]
        public void 实验流程和旧教学验收表混用时拒绝重复事实来源()
        {
            var source = MinimumBlueprint()
                .Append(
                    "实验流程.csv",
                    "流程ID,步骤ID,顺序,记录类型,显示名称,动作,来源,目标,"
                    + "触发类型,触发值,条件类型,主体,字段,比较,值,单位,"
                    + "分值变化,提示文案,参数名,参数值\n")
                .Append(
                    "教学评价.csv",
                    "评价ID,类型,显示名称,触发类型,触发值,顺序,条件类型,"
                    + "主体,字段,比较,值,单位,分值变化,提示文案\n");

            var result = new CourseBlueprintReader().Read(source);

            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("blueprint.flow.legacy-conflict"));
        }

        private static CourseBlueprintSource MinimumBlueprint()
        {
            return new CourseBlueprintSource(new[]
            {
                new CourseBlueprintFile(
                    "课程.csv",
                    "课程ID,显示名称,学科配方包,实验Prefab\n"
                    + "最小课程,最小课程,化学基础,环境.prefab\n"),
                new CourseBlueprintFile(
                    "实验对象.csv",
                    "实体ID,显示名称,特征列表,初始位置,初始旋转,"
                    + "参数.容量毫升\n"
                    + "试管,试管,可抓取;容器,"
                    + "0|1|0,0|0|0,100\n")
            });
        }
    }
}
