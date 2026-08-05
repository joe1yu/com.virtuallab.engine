using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Application.Events;
using VirtualLab.Chemistry.Authoring;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Chemistry.Configuration;
using VirtualLab.Domain;
using VirtualLab.Domain.Matter;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;
using VirtualLab.OxygenCourse.Authoring;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class ConfiguredOxygenScienceTests
    {
        private static string CourseDirectory => Path.GetFullPath(
            OxygenCourseContentBuilder.AuthoringDirectory);
        private static string ChemistryConfigurationPath => Path.GetFullPath(
            OxygenCourseContentBuilder.GeneratedDirectory
            + "/化学运行配置.json");

        [Test]
        public void 化学过程由配置选择化学操作而不是课程专用代码()
        {
            var result = Compile();

            Assert.That(
                result.IsSuccess,
                Is.True,
                Diagnostics(result));
            Assert.That(
                result.Domain.ConfiguredActions
                    .SelectMany(value => value.Mutations)
                    .Select(value => value.OperationId)
                    .Distinct(),
                Does.Contain("开始倾倒过程")
                    .And.Contain("开始加热过程")
                    .And.Contain("结束加热过程")
                    .And.Contain("开始燃烧")
                    .And.Contain("记录振荡"));
        }

        [Test]
        public void 固定器材不可移动且试管夹和玻璃片保持自由配对能力()
        {
            var context = CreateRuntime();
            var runtime = context.Runtime;

            foreach (var fixedEntityId in new[]
                     {
                         "铁架台",
                         "试管架",
                         "升降台",
                         "水槽",
                         "废液缸"
                     })
            {
                var grabFixedEntity = runtime.Session.Execute(Request(
                    "命令.尝试移动." + fixedEntityId,
                    InteractionSemanticActionIds.Grab,
                    fixedEntityId,
                    null));
                Assert.That(
                    grabFixedEntity.IsAccepted,
                    Is.False,
                    fixedEntityId + "不应允许被操作者移动。");
            }

            Assert.That(
                runtime.Session.Execute(Request(
                    "命令.握住试管夹调节部位",
                    InteractionSemanticActionIds.Grab,
                    "铁架台试管夹",
                    null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(Request(
                    "命令.上下移动并旋转试管夹",
                    InteractionSemanticActionIds.Position,
                    "铁架台试管夹",
                    "大试管",
                    ("高度说明", StructuredValue.FromText("沿铁架台上下调节")),
                    ("倾斜说明", StructuredValue.FromText("试管口略向下倾斜"))))
                    .IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(Request(
                    "命令.松开试管夹调节部位",
                    InteractionSemanticActionIds.Release,
                    "铁架台试管夹",
                    null)).IsAccepted,
                Is.True);

            AssertCrossBottleCover(runtime, "玻璃片一", "集气瓶二");
            AssertCrossBottleCover(runtime, "玻璃片二", "集气瓶一");

            Assert.That(
                context.World.Relations.Count(value =>
                    value.TypeId == InteractionRelationTypeIds.Cover
                    && ((value.Source == new EntityId("玻璃片一")
                         && value.Target == new EntityId("集气瓶二"))
                        || (value.Source == new EntityId("玻璃片二")
                            && value.Target == new EntityId("集气瓶一")))),
                Is.EqualTo(2));
        }

        [Test]
        public void 酒精灯必须取帽划燃并持有火柴后才能点燃()
        {
            var runtime = CreateRuntime().Runtime;

            var directIgnition = runtime.Session.Execute(
                Request(
                    "命令.直接点燃酒精灯",
                    ChemistrySemanticActionIds.Ignite,
                    "酒精灯",
                    "火柴"));
            Assert.That(directIgnition.IsAccepted, Is.False);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.取下酒精灯帽",
                        InteractionSemanticActionIds.Grab,
                        "酒精灯帽",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放下酒精灯帽",
                        InteractionSemanticActionIds.Release,
                        "酒精灯帽",
                        null)).IsAccepted,
                Is.True);

            var unstruckMatch = runtime.Session.Execute(
                Request(
                    "命令.使用未划燃火柴点灯",
                    ChemistrySemanticActionIds.Ignite,
                    "酒精灯",
                    "火柴"));
            Assert.That(unstruckMatch.IsAccepted, Is.False);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.拿起火柴",
                        InteractionSemanticActionIds.Grab,
                        "火柴",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.划燃火柴",
                        ChemistrySemanticActionIds.Ignite,
                        "火柴",
                        "火柴盒")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放下燃烧火柴",
                        InteractionSemanticActionIds.Release,
                        "火柴",
                        null)).IsAccepted,
                Is.True);

            var releasedMatch = runtime.Session.Execute(
                Request(
                    "命令.未持有火柴点灯",
                    ChemistrySemanticActionIds.Ignite,
                    "酒精灯",
                    "火柴"));
            Assert.That(releasedMatch.IsAccepted, Is.False);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.重新拿起燃烧火柴",
                        InteractionSemanticActionIds.Grab,
                        "火柴",
                        null)).IsAccepted,
                Is.True);
            var validIgnition = runtime.Session.Execute(
                Request(
                    "命令.正确点燃酒精灯",
                    ChemistrySemanticActionIds.Ignite,
                    "酒精灯",
                    "火柴"));
            Assert.That(
                validIgnition.IsAccepted,
                Is.True,
                string.Join(",", validIgnition.RejectionCodes));
        }

        [Test]
        public void 高锰酸钾必须先由持有的药匙舀取后才能投料()
        {
            var runtime = CreateRuntime().Runtime;

            var bottlePour = runtime.Session.Execute(
                Request(
                    "命令.试剂瓶直接倾倒",
                    ChemistrySemanticActionIds.BeginPour,
                    "高锰酸钾广口瓶",
                    "大试管",
                    ("请求流量克每秒",
                        StructuredValue.FromNumber(632d))));
            Assert.That(bottlePour.IsAccepted, Is.False);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.拿起空药匙",
                        InteractionSemanticActionIds.Grab,
                        "药匙",
                        null)).IsAccepted,
                Is.True);
            var emptySpoonPour = runtime.Session.Execute(
                Request(
                    "命令.空药匙投料",
                    ChemistrySemanticActionIds.BeginPour,
                    "药匙",
                    "大试管",
                    ("请求流量克每秒",
                        StructuredValue.FromNumber(632d))));
            Assert.That(emptySpoonPour.IsAccepted, Is.False);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.取下高锰酸钾广口瓶盖",
                        InteractionSemanticActionIds.Grab,
                        "高锰酸钾瓶盖",
                        null)).IsAccepted,
                Is.True);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.药匙伸入试剂瓶",
                        InteractionSemanticActionIds.Place,
                        "药匙",
                        "高锰酸钾广口瓶")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.药匙舀取高锰酸钾",
                        InteractionSemanticActionIds.Take,
                        "药匙",
                        "高锰酸钾广口瓶")).IsAccepted,
                Is.True);
            var filledSpoonPour = runtime.Session.Execute(
                Request(
                    "命令.盛药药匙投料",
                    ChemistrySemanticActionIds.BeginPour,
                    "药匙",
                    "大试管",
                    ("请求流量克每秒",
                        StructuredValue.FromNumber(632d))));
            Assert.That(
                filledSpoonPour.IsAccepted,
                Is.True,
                string.Join(",", filledSpoonPour.RejectionCodes));
        }

        [Test]
        public void 安全风险和实验表现均来自课程配置()
        {
            var result = Compile();

            Assert.That(
                result.IsSuccess,
                Is.True,
                Diagnostics(result));
            Assert.That(
                result.Domain.ActionAssessments
                    .Select(value => value.RiskId),
                Does.Contain("风险.粉末污染")
                    .And.Contain("风险.装置漏气")
                    .And.Contain("风险.氧气不纯")
                    .And.Contain("风险.冷凝水倒吸")
                    .And.Contain("风险.集气瓶热损伤"));
            Assert.That(
                result.Presentation.Effects
                    .Select(value => value.ProtocolId),
                Does.Contain("interaction.follow-anchor")
                    .And.Contain("interaction.snap-to-anchor")
                    .And.Contain("liquid.set-level")
                    .And.Contain("transform.oscillate")
                    .And.Contain("vfx.play")
                    .And.Contain("ui.message"));
        }

        [Test]
        public void 高锰酸钾由配置倾倒并受热分解且物质守恒()
        {
            var context = CreateRuntime();
            var world = context.World;
            var runtime = context.Runtime;
            var events = new EventCollector();
            var before = world.Matter.Total(
                "高锰酸钾",
                Unit.Gram);
            var fixedActions = context.Course.ConfiguredActions
                .Where(value =>
                    value.ActionId == InteractionSemanticActionIds.Connect
                    && value.SourceEntityId == "铁架台试管夹"
                    && value.TargetEntityId == "大试管")
                .ToArray();
            Assert.That(
                fixedActions,
                Has.Length.EqualTo(1),
                string.Join(
                    "\n",
                    fixedActions.Select(value =>
                        value.PolicyId + ":"
                        + string.Join(
                            ";",
                            value.Mutations.Select(mutation =>
                                mutation.MutationId)))));
            PrepareHeatingApparatus(runtime);
            PreparePermanganateSpoon(runtime, "物质守恒");

            var pouring = runtime.Session.Execute(
                Request(
                    "命令.倾倒高锰酸钾",
                    ChemistrySemanticActionIds.BeginPour,
                    "药匙",
                    "大试管",
                    ("请求流量克每秒",
                        StructuredValue.FromNumber(632d))));
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(1),
                events);
            var heating = runtime.Session.Execute(
                Request(
                    "命令.加热高锰酸钾",
                    ChemistrySemanticActionIds.BeginHeating,
                    "大试管",
                    "酒精灯"));
            runtime.ProcessAdvancer.AdvanceProcesses(
                600d,
                new SimulationTick(2),
                events);

            Assert.That(
                pouring.IsAccepted,
                Is.True,
                string.Join(",", pouring.RejectionCodes));
            Assert.That(
                heating.IsAccepted,
                Is.True,
                string.Join(",", heating.RejectionCodes));
            Assert.That(
                world.Matter.Total(
                    new EntityId("大试管"),
                    "氧气",
                    Unit.Gram).Value,
                Is.EqualTo(32m));
            Assert.That(
                world.Matter.Total(
                        "高锰酸钾",
                        Unit.Gram).Value
                    + world.Matter.Total(
                        "二氧化锰",
                        Unit.Gram).Value
                    + world.Matter.Total(
                        "氧气",
                        Unit.Gram).Value
                    + world.Matter.Total(
                        "锰酸钾",
                        Unit.Gram).Value,
                Is.EqualTo(before.Value));
        }

        [Test]
        public void 木炭和火柴铁丝组合按配置点燃并生成对应产物()
        {
            var context = CreateRuntime();
            var world = context.World;
            var runtime = context.Runtime;
            var events = new EventCollector();
            PrepareTwoOxygenBottles(context, events);
            PrepareCarbonHolder(runtime, "产物测试");

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.预热木炭",
                        ChemistrySemanticActionIds.BeginHeating,
                        "木炭",
                        "酒精灯")).IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                200d,
                new SimulationTick(6),
                events);
            var carbonIgnited = runtime.Session.Execute(
                Request(
                    "命令.点燃木炭",
                    ChemistrySemanticActionIds.Ignite,
                    "木炭",
                    "酒精灯",
                    ("空间距离米",
                        StructuredValue.FromNumber(0.1d))));
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(7),
                events);
            PrepareIronIgniter(runtime, "产物测试");
            var ironHeating = runtime.Session.Execute(
                    Request(
                        "命令.预热火柴铁丝组合",
                        ChemistrySemanticActionIds.BeginHeating,
                        "火柴铁丝组合",
                        "组合引燃火柴"));
            Assert.That(
                ironHeating.IsAccepted,
                Is.True,
                string.Join(",", ironHeating.RejectionCodes));
            runtime.ProcessAdvancer.AdvanceProcesses(
                200d,
                new SimulationTick(8),
                events);
            var ironIgnited = runtime.Session.Execute(
                Request(
                    "命令.点燃火柴铁丝组合",
                    ChemistrySemanticActionIds.Ignite,
                    "火柴铁丝组合",
                    "组合引燃火柴",
                    ("空间距离米",
                        StructuredValue.FromNumber(0.1d))));
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(9),
                events);
            var carbonPlaced = runtime.Session.Execute(
                Request(
                    "命令.放入木炭",
                    InteractionSemanticActionIds.Place,
                    "木炭",
                    "集气瓶一"));
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.拿起火柴铁丝组合",
                        InteractionSemanticActionIds.Grab,
                        "火柴铁丝组合",
                        null)).IsAccepted,
                Is.True);
            var ironPlaced = runtime.Session.Execute(
                Request(
                    "命令.放入火柴铁丝组合",
                    InteractionSemanticActionIds.Place,
                    "火柴铁丝组合",
                    "集气瓶二"));

            Assert.That(
                carbonIgnited.IsAccepted,
                Is.True,
                string.Join(",", carbonIgnited.RejectionCodes));
            Assert.That(
                ironIgnited.IsAccepted,
                Is.True,
                string.Join(",", ironIgnited.RejectionCodes));
            Assert.That(carbonPlaced.IsAccepted, Is.True);
            Assert.That(ironPlaced.IsAccepted, Is.True);
            Assert.That(
                world.Matter.Total(
                    new EntityId("集气瓶一"),
                    "二氧化碳",
                    Unit.Gram).Value,
                Is.EqualTo(44m));
            Assert.That(
                world.Matter.Total(
                    new EntityId("集气瓶二"),
                    "四氧化三铁",
                    Unit.Gram).Value,
                Is.EqualTo(116m));
        }

        [Test]
        public void 振荡石灰水按配置生成碳酸钙浑浊物()
        {
            var context = CreateRuntime();
            var world = context.World;
            var runtime = context.Runtime;
            var events = new EventCollector();
            PrepareTwoOxygenBottles(context, events);
            PrepareCarbonDioxideInBottle(context, events);

            var limewater = runtime.Session.Execute(
                Request(
                    "命令.拿起石灰水",
                    InteractionSemanticActionIds.Grab,
                    "澄清石灰水窄口瓶",
                    null));
            Assert.That(limewater.IsAccepted, Is.True);
            limewater = runtime.Session.Execute(
                Request(
                    "命令.倾倒石灰水",
                    ChemistrySemanticActionIds.BeginPour,
                    "澄清石灰水窄口瓶",
                    "集气瓶一",
                    ("请求流量毫升每秒",
                        StructuredValue.FromNumber(10d))));
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(10),
                events);
            var held = runtime.Session.Execute(
                Request(
                    "命令.抓取集气瓶一",
                    InteractionSemanticActionIds.Grab,
                    "集气瓶一",
                    null));

            var shaken = runtime.Session.Execute(
                Request(
                    "命令.振荡石灰水",
                    ChemistrySemanticActionIds.Shake,
                    "集气瓶一",
                    null,
                    ("强度", StructuredValue.FromNumber(1d)),
                    ("持续秒数", StructuredValue.FromNumber(1d))));
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(11),
                events);

            Assert.That(limewater.IsAccepted, Is.True);
            Assert.That(
                held.IsAccepted,
                Is.True,
                string.Join(",", held.RejectionCodes));
            Assert.That(
                shaken.IsAccepted,
                Is.True,
                string.Join(",", shaken.RejectionCodes));
            Assert.That(
                world.Matter.Total(
                    new EntityId("集气瓶一"),
                    "碳酸钙",
                    Unit.Gram).Value,
                Is.EqualTo(10.00865m));
            Assert.That(
                events.Events.Select(value => value.EventType),
                Does.Contain("反应.已推进"));
        }

        [Test]
        public void 实验目标开局未完成且不规范操作会执行并形成后果证据()
        {
            var context = CreateRuntime();
            var readers = CoreCourseRegistrations.CreateFactReaders()
                .Concat(ChemistryCourseRegistrations.CreateFactReaders());
            var initialGoals = new CourseGoalEvaluator(
                    new StructuredRuleEvaluator(readers))
                .Evaluate(
                    context.World,
                    context.Course.GoalRules);

            PrepareHeatingApparatus(context.Runtime);
            context.World.RemoveRelation(new EntityRelation(
                InteractionRelationTypeIds.ContainedBy,
                new EntityId("棉花团"),
                new EntityId("大试管")));
            var stopperConnection = context.World.Relations.Single(value =>
                value.TypeId == InteractionRelationTypeIds.Connection
                && (value.Source.Value == "橡胶塞玻璃导管"
                    && value.Target.Value == "大试管"
                    || value.Source.Value == "大试管"
                    && value.Target.Value == "橡胶塞玻璃导管"));
            context.World.RemoveRelation(stopperConnection);
            var unsafeHeating = context.Runtime.Session.Execute(
                Request(
                    "命令.缺少防护直接加热",
                    ChemistrySemanticActionIds.BeginHeating,
                    "大试管",
                    "酒精灯"));
            FillBottle(
                context.Runtime,
                new EventCollector(),
                "集气瓶一",
                1);
            Assert.That(
                context.Runtime.Session.Execute(
                    Request(
                        "命令.拿起折角导气管后过早连接",
                        InteractionSemanticActionIds.Grab,
                        "折角导气管",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                context.Runtime.Session.Execute(
                    Request(
                        "命令.过早连接集气瓶",
                        InteractionSemanticActionIds.Connect,
                        "折角导气管",
                        "集气瓶一")).IsAccepted,
                Is.True);
            var earlyCollectionRequest = Request(
                "命令.过早收集",
                ChemistrySemanticActionIds.CollectGas,
                "折角导气管",
                "集气瓶一");
            var earlyCollectionDefinition = context.Course.ConfiguredActions
                .Single(value => value.Matches(earlyCollectionRequest));
            var collectionTransfers = earlyCollectionDefinition.Mutations
                .Where(value => value.OperationId
                    == MatterTransferOperations.TransferOperationId)
                .ToArray();
            Assert.That(
                collectionTransfers.Single(value =>
                        value.MutationId.StartsWith(
                            "状态变化.集气.",
                            System.StringComparison.Ordinal))
                    .Parameters.ContainsKey(
                    "数量不足风险状态键"),
                Is.True);
            var earlyCollection = context.Runtime.Session.Execute(
                earlyCollectionRequest);
            var unsafeStop = context.Runtime.Session.Execute(
                Request(
                    "命令.错误顺序停热",
                    ChemistrySemanticActionIds.EndHeating,
                    "大试管",
                    "酒精灯"));
            var assessment = context.Runtime.Session
                .ExportState()
                .Assessment;

            Assert.That(initialGoals.CompletedGoalIds, Is.Empty);
            Assert.That(unsafeHeating.IsAccepted, Is.True);
            Assert.That(
                unsafeHeating.Events.Select(value => value.EventType),
                Does.Contain("实验风险.粉末污染")
                    .And.Contain("实验风险.装置漏气"));
            Assert.That(
                earlyCollection.IsAccepted,
                Is.True,
                string.Join(",", earlyCollection.RejectionCodes));
            Assert.That(
                earlyCollection.Events.Select(value => value.EventType),
                Does.Contain("实验风险.氧气不纯"));
            Assert.That(
                unsafeStop.IsAccepted,
                Is.True,
                string.Join(",", unsafeStop.RejectionCodes));
            Assert.That(
                assessment.RiskIds,
                Does.Contain("风险.粉末污染")
                    .And.Contain("风险.装置漏气")
                    .And.Contain("风险.氧气不纯")
                    .And.Contain("风险.冷凝水倒吸"));
            Assert.That(assessment.Score, Is.LessThan(100));
        }

        [Test]
        public void 完整配置流程可以完成全部八个实验目标()
        {
            var context = CreateRuntime();
            var runtime = context.Runtime;
            var events = new EventCollector();
            PrepareTwoOxygenBottles(context, events);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.先将折角导气管移出水面",
                        InteractionSemanticActionIds.Disconnect,
                        "折角导气管",
                        "集气瓶二")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.安全停热",
                        ChemistrySemanticActionIds.EndHeating,
                        "大试管",
                        "酒精灯")).IsAccepted,
                Is.True);

            PrepareCarbonDioxideInBottle(context, events);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.完整流程拿起石灰水",
                        InteractionSemanticActionIds.Grab,
                        "澄清石灰水窄口瓶",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.加入石灰水",
                        ChemistrySemanticActionIds.BeginPour,
                        "澄清石灰水窄口瓶",
                        "集气瓶一",
                        ("请求流量毫升每秒",
                            StructuredValue.FromNumber(10d))))
                    .IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(10),
                events);
            var heldBottle = runtime.Session.Execute(
                Request(
                    "命令.握持集气瓶一",
                    InteractionSemanticActionIds.Grab,
                    "集气瓶一",
                    null));
            Assert.That(
                heldBottle.IsAccepted,
                Is.True,
                string.Join(",", heldBottle.RejectionCodes));
            PrepareIronIgniter(runtime, "完整流程");
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.振荡检验",
                        ChemistrySemanticActionIds.Shake,
                        "集气瓶一",
                        null,
                        ("强度", StructuredValue.FromNumber(1d)),
                        ("持续秒数",
                            StructuredValue.FromNumber(1d))))
                    .IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(11),
                events);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.预热铁丝",
                        ChemistrySemanticActionIds.BeginHeating,
                        "火柴铁丝组合",
                        "组合引燃火柴")).IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                200d,
                new SimulationTick(12),
                events);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.点燃铁丝",
                        ChemistrySemanticActionIds.Ignite,
                        "火柴铁丝组合",
                        "组合引燃火柴",
                        ("空间距离米",
                            StructuredValue.FromNumber(0.1d))))
                    .IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(13),
                events);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.完整流程拿起铁丝",
                        InteractionSemanticActionIds.Grab,
                        "火柴铁丝组合",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放入铁丝",
                        InteractionSemanticActionIds.Place,
                        "火柴铁丝组合",
                        "集气瓶二")).IsAccepted,
                Is.True);

            var completed = new CourseGoalEvaluator(
                    new StructuredRuleEvaluator(
                        CoreCourseRegistrations.CreateFactReaders()
                            .Concat(
                                ChemistryCourseRegistrations
                                    .CreateFactReaders())))
                .Evaluate(context.World, context.Course.GoalRules);

            Assert.That(
                completed.CompletedGoalIds,
                Is.EquivalentTo(
                    context.Course.GoalRules.Select(
                        value => value.GoalId)));
            Assert.That(completed.CompletedGoalIds, Has.Count.EqualTo(8));
        }

        private static void AssertCrossBottleCover(
            ChemistryCourseRuntime runtime,
            string glassId,
            string bottleId)
        {
            Assert.That(
                runtime.Session.Execute(Request(
                    "命令.拿起." + glassId,
                    InteractionSemanticActionIds.Grab,
                    glassId,
                    null)).IsAccepted,
                Is.True);
            var cover = runtime.Session.Execute(Request(
                "命令.交叉覆盖." + glassId + "." + bottleId,
                InteractionSemanticActionIds.Cover,
                glassId,
                bottleId));
            Assert.That(
                cover.IsAccepted,
                Is.True,
                glassId + "应当可以覆盖" + bottleId + "："
                + string.Join(",", cover.RejectionCodes));
            Assert.That(
                runtime.Session.Execute(Request(
                    "命令.放下." + glassId,
                    InteractionSemanticActionIds.Release,
                    glassId,
                    null)).IsAccepted,
                Is.True);
        }

        private static RuntimeContext CreateRuntime()
        {
            var compilation = Compile();
            Assert.That(
                compilation.IsSuccess,
                Is.True,
                Diagnostics(compilation));
            var chemistryPayload = System.Text.Encoding.UTF8.GetString(
                compilation.GeneratedArtifacts.Single(value =>
                    value.Definition.ArtifactId
                    == "化学运行配置")
                    .Definition.Content);
            var chemistry = new ChemistryConfigurationCodec()
                .DecodeCourseConfiguration(
                    chemistryPayload,
                    compilation.Domain.Entities.Select(
                        value => value.EntityId));
            var world = CourseWorldFactory.Create(compilation.Domain);
            return new RuntimeContext(
                world,
                compilation.Domain,
                ChemistryCourseRuntime.Create(
                    world,
                    compilation.Domain,
                    chemistry));
        }

        private static CourseBlueprintCompilationResult Compile() =>
            new CourseBlueprintCompiler().Compile(
                CourseBlueprintSource.FromDirectory(CourseDirectory),
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new ChemistryRecipePackageProvider()
                });

        private static string Diagnostics(
            CourseBlueprintCompilationResult result) =>
            string.Join(
                System.Environment.NewLine,
                result.Diagnostics.Select(value =>
                    $"{value.Code} {value.FileName}:{value.Line} "
                    + value.Reason));

        /// <summary>
        /// 只通过课程动作完成制氧、装水和排水集气，防止测试绕过配表注入氧气。
        /// </summary>
        private static void PrepareTwoOxygenBottles(
            RuntimeContext context,
            EventCollector events)
        {
            var runtime = context.Runtime;
            PrepareHeatingApparatus(runtime);
            PreparePermanganateSpoon(runtime, "制氧准备");
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.倒入全部高锰酸钾",
                        ChemistrySemanticActionIds.BeginPour,
                        "药匙",
                        "大试管",
                        ("请求流量克每秒",
                            StructuredValue.FromNumber(632d))))
                    .IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(1),
                events);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.开始制氧",
                        ChemistrySemanticActionIds.BeginHeating,
                        "大试管",
                        "酒精灯")).IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                600d,
                new SimulationTick(2),
                events);
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(3),
                events);

            FillBottle(runtime, events, "集气瓶一", 4);
            FillBottle(runtime, events, "集气瓶二", 5);

            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.收集前拿起折角导气管",
                        InteractionSemanticActionIds.Grab,
                        "折角导气管",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.连接第一只集气瓶",
                        InteractionSemanticActionIds.Connect,
                        "折角导气管",
                        "集气瓶一")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.收集第一瓶氧气",
                        ChemistrySemanticActionIds.CollectGas,
                        "折角导气管",
                        "集气瓶一")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.断开第一只集气瓶",
                        InteractionSemanticActionIds.Disconnect,
                        "折角导气管",
                        "集气瓶一")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.连接第二只集气瓶",
                        InteractionSemanticActionIds.Connect,
                        "折角导气管",
                        "集气瓶二")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.收集第二瓶氧气",
                        ChemistrySemanticActionIds.CollectGas,
                        "折角导气管",
                        "集气瓶二")).IsAccepted,
                Is.True);
        }

        private static void PreparePermanganateSpoon(
            ChemistryCourseRuntime runtime,
            string commandPrefix)
        {
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".取下高锰酸钾瓶盖",
                        InteractionSemanticActionIds.Grab,
                        "高锰酸钾瓶盖",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".拿起药匙",
                        InteractionSemanticActionIds.Grab,
                        "药匙",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".药匙伸入试剂瓶",
                        InteractionSemanticActionIds.Place,
                        "药匙",
                        "高锰酸钾广口瓶")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".药匙舀取药品",
                        InteractionSemanticActionIds.Take,
                        "药匙",
                        "高锰酸钾广口瓶")).IsAccepted,
                Is.True);
        }

        private static void FillBottle(
            ChemistryCourseRuntime runtime,
            EventCollector events,
            string bottleId,
            long tick)
        {
            var result = runtime.Session.Execute(
                Request(
                    "命令.拿起." + bottleId,
                    InteractionSemanticActionIds.Grab,
                    bottleId,
                    null));
            Assert.That(
                result.IsAccepted,
                Is.True,
                string.Join(",", result.RejectionCodes));
            result = runtime.Session.Execute(
                Request(
                    "命令.拿起加水烧杯." + bottleId,
                    InteractionSemanticActionIds.Grab,
                    "加水烧杯",
                    null));
            Assert.That(
                result.IsAccepted,
                Is.True,
                string.Join(",", result.RejectionCodes));
            result = runtime.Session.Execute(
                    Request(
                        "命令.装水." + bottleId,
                        ChemistrySemanticActionIds.BeginPour,
                        "加水烧杯",
                        bottleId,
                        ("请求流量毫升每秒",
                            StructuredValue.FromNumber(100d))));
            Assert.That(
                result.IsAccepted,
                Is.True,
                string.Join(",", result.RejectionCodes));
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(tick),
                events);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.停止装水." + bottleId,
                        ChemistrySemanticActionIds.EndPour,
                        "加水烧杯",
                        bottleId)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放下." + bottleId,
                        InteractionSemanticActionIds.Release,
                        bottleId,
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放下加水烧杯." + bottleId,
                        InteractionSemanticActionIds.Release,
                        "加水烧杯",
                        null)).IsAccepted,
                Is.True);
        }

        private static void PrepareCarbonDioxideInBottle(
            RuntimeContext context,
            EventCollector events)
        {
            var runtime = context.Runtime;
            PrepareCarbonHolder(runtime, "二氧化碳准备");
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.预热木炭",
                        ChemistrySemanticActionIds.BeginHeating,
                        "木炭",
                        "酒精灯")).IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                200d,
                new SimulationTick(6),
                events);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.点燃木炭",
                        ChemistrySemanticActionIds.Ignite,
                        "木炭",
                        "酒精灯",
                        ("空间距离米",
                            StructuredValue.FromNumber(0.1d))))
                    .IsAccepted,
                Is.True);
            runtime.ProcessAdvancer.AdvanceProcesses(
                1d,
                new SimulationTick(7),
                events);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放入木炭",
                        InteractionSemanticActionIds.Place,
                        "木炭",
                        "集气瓶一")).IsAccepted,
                Is.True);
        }

        private static void PrepareCarbonHolder(
            ChemistryCourseRuntime runtime,
            string commandPrefix)
        {
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".取下木炭瓶盖",
                        InteractionSemanticActionIds.Grab,
                        "木炭瓶盖",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".拿起坩埚钳",
                        InteractionSemanticActionIds.Grab,
                        "坩埚钳",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".夹住木炭",
                        InteractionSemanticActionIds.Connect,
                        "坩埚钳",
                        "木炭")).IsAccepted,
                Is.True);
        }

        private static void PrepareHeatingApparatus(
            ChemistryCourseRuntime runtime)
        {
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.拿起镊子",
                        InteractionSemanticActionIds.Grab,
                        "镊子",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.镊子夹住棉花团",
                        InteractionSemanticActionIds.Connect,
                        "镊子",
                        "棉花团")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.通过镊子拿起棉花团",
                        InteractionSemanticActionIds.Grab,
                        "棉花团",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放置棉花团",
                        InteractionSemanticActionIds.Place,
                        "棉花团",
                        "大试管")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.拿起橡皮塞",
                        InteractionSemanticActionIds.Grab,
                        "橡胶塞玻璃导管",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.连接橡皮塞",
                        InteractionSemanticActionIds.Connect,
                        "橡胶塞玻璃导管",
                        "大试管")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.拿起试管",
                        InteractionSemanticActionIds.Grab,
                        "大试管",
                        null)).IsAccepted,
                Is.True);
            var fixedTube = runtime.Session.Execute(
                Request(
                    "命令.固定试管",
                    InteractionSemanticActionIds.Connect,
                    "铁架台试管夹",
                    "大试管"));
            Assert.That(
                fixedTube.IsAccepted,
                Is.True,
                string.Join(",", fixedTube.RejectionCodes));
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.拿起折角导气管",
                        InteractionSemanticActionIds.Grab,
                        "折角导气管",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.连接两段连接件",
                        InteractionSemanticActionIds.Connect,
                        "折角导气管",
                        "橡胶塞玻璃导管")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放下折角导气管",
                        InteractionSemanticActionIds.Release,
                        "折角导气管",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.取下酒精灯帽",
                        InteractionSemanticActionIds.Grab,
                        "酒精灯帽",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放下酒精灯帽",
                        InteractionSemanticActionIds.Release,
                        "酒精灯帽",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.拿起点火火柴",
                        InteractionSemanticActionIds.Grab,
                        "火柴",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.划燃点火火柴",
                        ChemistrySemanticActionIds.Ignite,
                        "火柴",
                        "火柴盒")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.点燃酒精灯",
                        ChemistrySemanticActionIds.Ignite,
                        "酒精灯",
                        "火柴")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令.放下点火火柴",
                        InteractionSemanticActionIds.Release,
                        "火柴",
                        null)).IsAccepted,
                Is.True);
        }

        private static void PrepareIronIgniter(
            ChemistryCourseRuntime runtime,
            string commandPrefix)
        {
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".拿起火柴",
                        InteractionSemanticActionIds.Grab,
                        "火柴",
                        null)).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".划燃火柴",
                        ChemistrySemanticActionIds.Ignite,
                        "火柴",
                        "火柴盒")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".点燃铁丝底端火柴",
                        ChemistrySemanticActionIds.Ignite,
                        "组合引燃火柴",
                        "火柴")).IsAccepted,
                Is.True);
            Assert.That(
                runtime.Session.Execute(
                    Request(
                        "命令." + commandPrefix + ".放下火柴",
                        InteractionSemanticActionIds.Release,
                        "火柴",
                        null)).IsAccepted,
                Is.True);
        }

        private sealed class RuntimeContext
        {
            public RuntimeContext(
                ExperimentWorld world,
                CompiledCourseDefinition course,
                ChemistryCourseRuntime runtime)
            {
                World = world;
                Course = course;
                Runtime = runtime;
            }

            public ExperimentWorld World { get; }

            public CompiledCourseDefinition Course { get; }

            public ChemistryCourseRuntime Runtime { get; }
        }

        private static SemanticActionRequest Request(
            string commandId,
            string actionId,
            string sourceEntityId,
            string targetEntityId,
            params (string Key, StructuredValue Value)[] parameters)
        {
            var values = new Dictionary<string, StructuredValue>(
                System.StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                values.Add(parameter.Key, parameter.Value);
            }

            return new SemanticActionRequest(
                commandId,
                actionId,
                "学生",
                sourceEntityId,
                targetEntityId,
                values);
        }
    }
}
