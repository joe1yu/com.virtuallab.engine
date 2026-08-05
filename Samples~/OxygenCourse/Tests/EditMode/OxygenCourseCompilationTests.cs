using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VirtualLab.Application.Courses;
using VirtualLab.Interaction.Courses;
using VirtualLab.Spatial.Courses;
using VirtualLab.Teaching.Courses;
using VirtualLab.Chemistry.Authoring;
using VirtualLab.Chemistry.Courses;
using VirtualLab.OxygenCourse.Authoring;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Normalized;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class OxygenCourseCompilationTests
    {
        private static string CourseDirectory => Path.GetFullPath(
            OxygenCourseContentBuilder.AuthoringDirectory);
        private static string CourseAssetPath =>
            OxygenCourseContentBuilder.CourseAssetPath;

        [Test]
        public void 氧气课程由精简高层蓝图编译且使用中文实体ID()
        {
            var result = Compile();

            Assert.That(
                result.Diagnostics,
                Is.Empty,
                string.Join(
                    "\n",
                    result.Diagnostics.Select(value =>
                        value.Code + ": " + value.Reason))
                + "\n已生成动作：\n"
                + string.Join(
                    "\n",
                    result.Normalized.Actions.Select(value =>
                        value.Definition.ActionId + "|"
                        + value.Definition.SourceEntityId + "|"
                        + value.Definition.TargetEntityId)));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Domain.RequiredModuleIds,
                Is.EquivalentTo(new[]
                {
                    CourseModuleIds.Core,
                    InteractionModuleIds.Interaction,
                    ChemistryModuleIds.Chemistry,
                    SpatialModuleIds.Spatial,
                    TeachingProtocolIds.Module
                }));
            var suckBack = result.Domain.ActionAssessments.Single(value =>
                value.RiskId == "风险.冷凝水倒吸");
            Assert.That(
                suckBack.Severity,
                Is.EqualTo(CourseConsequenceSeverity.SafetyIncident));
            Assert.That(
                suckBack.Continuation,
                Is.EqualTo(CourseContinuationMode.RestartRequired));
            var thermalDamage = result.Domain.ActionAssessments.Single(value =>
                value.RiskId == "风险.集气瓶热损伤");
            Assert.That(
                thermalDamage.Severity,
                Is.EqualTo(CourseConsequenceSeverity.ExperimentRisk));
            Assert.That(
                thermalDamage.Continuation,
                Is.EqualTo(CourseContinuationMode.CannotContinue));
            Assert.That(
                thermalDamage.AffectedTargetIds,
                Is.EqualTo(new[] { "目标.观察铁丝燃烧" }));
            Assert.That(
                result.Domain.Entities.Select(value => value.EntityId),
                Is.EquivalentTo(new[]
                {
                    "学生",
                    "铁架台",
                    "铁架台试管夹",
                    "试管架",
                    "大试管",
                    "升降台",
                    "酒精灯",
                    "酒精灯帽",
                    "橡胶塞玻璃导管",
                    "折角导气管",
                    "水槽",
                    "废液缸",
                    "火柴盒",
                    "火柴",
                    "集气瓶一",
                    "集气瓶二",
                    "玻璃片一",
                    "玻璃片二",
                    "加水烧杯",
                    "澄清石灰水窄口瓶",
                    "棉花团",
                    "镊子",
                    "高锰酸钾广口瓶",
                    "高锰酸钾瓶盖",
                    "木炭广口瓶",
                    "木炭瓶盖",
                    "木炭",
                    "药匙",
                    "坩埚钳",
                    "火柴铁丝组合",
                    "组合引燃火柴"
                }));
            Assert.That(
                result.Domain.InitialRelations.Select(value =>
                    value.TypeId + "|" + value.SourceEntityId + "|"
                    + value.TargetEntityId),
                Is.EquivalentTo(new[]
                {
                    "交互.关系.覆盖|酒精灯帽|酒精灯",
                    "交互.关系.覆盖|高锰酸钾瓶盖|高锰酸钾广口瓶",
                    "交互.关系.覆盖|木炭瓶盖|木炭广口瓶",
                    "交互.关系.位于容器内|木炭|木炭广口瓶"
                }));
        }

        [Test]
        public void 氧气课程覆盖实验脚本要求的设备无关动作()
        {
            var result = Compile();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Domain.ConfiguredActions
                    .Select(value => value.ActionId)
                    .Distinct(),
                Does.Contain("抓取")
                    .And.Contain("释放")
                    .And.Contain("放置")
                    .And.Contain("拿出")
                    .And.Contain("定位")
                    .And.Contain("覆盖")
                    .And.Contain("揭开")
                    .And.Contain("连接")
                    .And.Contain("断开")
                    .And.Contain("开始倾倒")
                    .And.Contain("开始加热")
                    .And.Contain("结束加热")
                    .And.Contain("点燃")
                    .And.Contain("振荡")
                    .And.Contain("观察"));

            var policies = result.Domain.ConfiguredActions
                .Select(value =>
                    value.ActionId + "|" +
                    value.SourceEntityId + "|" +
                    (value.TargetEntityId ?? string.Empty))
                .ToArray();
            Assert.That(
                policies,
                Does.Contain("抓取|坩埚钳|")
                    .And.Contain("连接|折角导气管|橡胶塞玻璃导管")
                    .And.Contain("连接|折角导气管|集气瓶一")
                    .And.Contain(
                        "开始倾倒|加水烧杯|集气瓶一")
                    .And.Contain(
                        "开始倾倒|加水烧杯|集气瓶二")
                    .And.Contain(
                        "开始倾倒|澄清石灰水窄口瓶|集气瓶一")
                    .And.Contain(
                        "点燃|酒精灯|火柴")
                    .And.Contain(
                        "点燃|火柴|火柴盒")
                    .And.Contain(
                        "点燃|组合引燃火柴|火柴")
                    .And.Contain(
                        "开始加热|木炭|酒精灯")
                    .And.Contain(
                        "点燃|火柴铁丝组合|组合引燃火柴")
                    .And.Contain("观察|集气瓶二|"));

            var lampIgnition = result.Domain.ConfiguredActions.Single(value =>
                value.ActionId == "点燃"
                && value.SourceEntityId == "酒精灯"
                && value.TargetEntityId == "火柴");
            Assert.That(
                lampIgnition.Rules.Select(value => value.Field.Id),
                Does.Contain(InteractionStructuredFactFields.来源对象覆盖物.Id)
                    .And.Contain(ChemistryStructuredFactFields.目标对象已点燃.Id)
                    .And.Contain(InteractionStructuredFactFields.目标对象已被操作者拿起.Id));
        }

        [Test]
        public void 氧气课程只保留七张聚焦职责的高层蓝图表()
        {
            var expected = new[]
            {
                "课程.csv",
                "实验对象.csv",
                "初始关系.csv",
                "交互规则.csv",
                "学科过程.csv",
                "表现覆盖.csv",
                "实验流程.csv"
            };
            Assert.That(
                Directory.GetFiles(CourseDirectory, "*.csv")
                    .Select(Path.GetFileName),
                Is.EquivalentTo(expected));

            var result = Compile();

            Assert.That(
                result.Diagnostics,
                Is.Empty,
                string.Join(
                    "\n",
                    result.Diagnostics.Select(value =>
                        value.Code + ": " + value.Reason)));
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Presentation.States.Select(value => value.StateId),
                Does.Contain("状态.大试管被学生持有")
                    .And.Contain("状态.折角导气管已连接集气瓶一"));
            Assert.That(
                result.Presentation.Rules.Select(value =>
                    value.TriggerKind.ToString()),
                Does.Contain("CourseInitialized")
                    .And.Contain("ActionAccepted")
                    .And.Contain("ActionRejected")
                    .And.Contain("StateActive"));
            var stateIds = result.Presentation.States
                .Select(value => value.StateId)
                .ToHashSet(StringComparer.Ordinal);
            Assert.That(
                result.Presentation.Rules
                    .Where(value => value.TriggerKind
                        == CoursePresentationTriggerKind.StateActive)
                    .Select(value => value.TriggerValue),
                Has.All.Matches<string>(value => stateIds.Contains(value)),
                "状态持续表现必须引用真实的权威状态 ID，不能把实体 ID 当状态 ID。");
            Assert.That(
                result.Presentation.Rules.Any(value =>
                    value.TriggerKind
                    == CoursePresentationTriggerKind.ActionAccepted
                    && value.TriggerValue == "收集气体"
                    && value.TargetEntityId == "集气瓶一"),
                Is.True);
            Assert.That(
                result.Presentation.Rules.Any(value =>
                    value.TriggerKind
                    == CoursePresentationTriggerKind.ActionRejected
                    && value.TriggerValue
                    == "开始加热"),
                Is.True);
            Assert.That(
                result.Presentation.Effects.Select(value =>
                    value.ProtocolId),
                Does.Contain("interaction.follow-anchor")
                    .And.Contain("interaction.snap-to-anchor")
                    .And.Contain("liquid.set-level")
                    .And.Contain("renderer.show")
                    .And.Contain("vfx.play")
                    .And.Contain("transform.oscillate")
                    .And.Contain("ui.message"));
            var rejectionMessage = result.Presentation.Effects.Single(value =>
                value.EffectId == "表现.动作拒绝提示");
            Assert.That(
                rejectionMessage.ParameterBindings.Single(value =>
                    value.Name == "文案").ConstantValue.Text,
                Is.EqualTo("当前条件不允许执行该操作"));
            Assert.That(
                result.Presentation.Effects
                    .Where(value => value.Target.LocationKind
                        == CoursePresentationLocationKind.PresentationSlot)
                    .Select(value => value.Target.LocationKind),
                Has.All.EqualTo(
                    CoursePresentationLocationKind.PresentationSlot));
            Assert.That(
                result.Presentation.Effects
                    .Where(value => value.Target.LocationKind
                        == CoursePresentationLocationKind.PresentationSlot)
                    .Select(value => value.ProtocolId),
                Does.Contain("liquid.set-level")
                    .And.Contain("vfx.play"));
            Assert.That(result.GeneratedArtifacts, Is.Not.Empty);
        }

        [Test]
        public void 物质反应和初始内容物进入通用课程总览()
        {
            var result = Compile();

            Assert.That(result.IsSuccess, Is.True);
            var entries = result.GeneratedArtifacts
                .SelectMany(value => value.Definition.OverviewEntries)
                .ToArray();
            Assert.That(
                entries.Select(value => value.Category).Distinct(),
                Does.Contain("物质")
                    .And.Contain("初始内容物")
                    .And.Contain("化学反应"));
            var permanganate = entries.Single(value =>
                value.Category == "物质"
                && value.DisplayName == "高锰酸钾");
            Assert.That(permanganate.SubjectId, Is.Empty);
            var initial = entries.Single(value =>
                value.Category == "初始内容物"
                && value.SubjectId == "高锰酸钾广口瓶");
            Assert.That(initial.DisplayName, Is.EqualTo("高锰酸钾广口瓶中的高锰酸钾"));
            Assert.That(initial.Fields["数量"], Is.EqualTo("632 克"));
            Assert.That(
                entries.Where(value => value.Category == "化学反应")
                    .Select(value => value.DisplayName),
                Does.Contain("高锰酸钾分解"));
        }

        [Test]
        public void 高锰酸钾内容表现由课程初始化和语义动作配置驱动()
        {
            var result = Compile();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Presentation.Rules.Any(value =>
                    value.TriggerKind
                    == CoursePresentationTriggerKind.CourseInitialized
                    && value.TriggerValue == "course.initialized"),
                Is.True);
            Assert.That(
                result.Presentation.Rules.Any(value =>
                    value.TriggerKind
                    == CoursePresentationTriggerKind.ActionAccepted
                    && value.TriggerValue == "拿出"
                    && value.SourceEntityId == "药匙"
                    && value.TargetEntityId == "高锰酸钾广口瓶"),
                Is.True);
            Assert.That(
                result.Presentation.Effects
                    .Where(value => value.EffectId.Contains("内容"))
                    .Select(value => value.ProtocolId),
                Does.Contain("renderer.show")
                    .And.Contain("renderer.hide"));
        }

        [Test]
        public void 氧气实验总预制体内的实体提供稳定表现插槽()
        {
            AssertPrefabAnchors(
                "学生",
                ("抓取锚点", SemanticAnchorKind.InteractionGrip));
            AssertPrefabSlots(
                "大试管",
                ("插槽.内容", PresentationSlotKind.Content));
            AssertPrefabSlots(
                "折角导气管",
                ("插槽.内容", PresentationSlotKind.Content));
            AssertPrefabSlots(
                "水槽",
                ("插槽.液体", PresentationSlotKind.Liquid));
            AssertPrefabSlots(
                "酒精灯",
                ("插槽.燃烧", PresentationSlotKind.Combustion));
            AssertPrefabSlots(
                "集气瓶一",
                ("插槽.液体", PresentationSlotKind.Liquid),
                ("插槽.燃烧", PresentationSlotKind.Combustion),
                ("插槽.高亮", PresentationSlotKind.Highlight));
            AssertPrefabSlots(
                "集气瓶二",
                ("插槽.液体", PresentationSlotKind.Liquid),
                ("插槽.燃烧", PresentationSlotKind.Combustion),
                ("插槽.高亮", PresentationSlotKind.Highlight));
            AssertPrefabSlots(
                "木炭",
                ("插槽.燃烧", PresentationSlotKind.Combustion));
            AssertPrefabSlots(
                "火柴铁丝组合",
                ("插槽.燃烧", PresentationSlotKind.Combustion));
        }

        [Test]
        public void 氧气课程资产不保存Unity引用且运行时资源均可加载()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CompiledCourseAsset>(
                CourseAssetPath);

            Assert.That(asset, Is.Not.Null);
            Assert.That(
                asset.CourseId,
                Is.EqualTo("氧气的实验室制取与性质"));
            var course = CourseAssetDecoder.DecodeDomain(asset);
            var current = Compile();
            Assert.That(current.IsSuccess, Is.True);
            var assetSignature = CourseSignature(course).ToArray();
            var tableSignature =
                CourseSignature(current.Domain).ToArray();
            var differences = tableSignature
                .Except(assetSignature)
                .Select(value => "资产缺少：" + value)
                .Concat(assetSignature
                    .Except(tableSignature)
                    .Select(value => "资产多出：" + value))
                .ToArray();
            Assert.That(
                differences,
                Is.Empty,
                "生成课程资产已落后于当前 CSV：\n"
                + string.Join("\n", differences));
            Assert.That(
                File.ReadAllText(CourseAssetPath),
                Does.Not.Contain("resourceBindings:")
                    .And.Not.Contain("environmentPrefab:"));

            var resources = new CourseRuntimeResourceResolver(
                course,
                new ResourcesCourseResourceLoader());
            Assert.That(course.Resources, Has.Count.EqualTo(1));
            foreach (var resource in course.Resources)
            {
                Assert.That(
                    resources.TryResolve(
                        resource.ResourceId,
                        typeof(GameObject),
                        out var loaded),
                    Is.True,
                    resource.ResourceId + " 未能按路径加载。");
                Assert.That(loaded, Is.Not.Null);
            }

            var experimentPrefab = resources.Require<GameObject>(
                course.ExperimentPrefabResourceId);
            var views = experimentPrefab
                .GetComponentsInChildren<CourseEntityView>(true)
                .ToDictionary(value => value.EntityId, StringComparer.Ordinal);
            foreach (var contract in course.PrefabContracts)
            {
                Assert.That(
                    PrefabContractValidator.Validate(
                        views[contract.EntityId],
                        contract),
                    Is.Empty,
                    contract.EntityId);
            }

            Assert.That(
                File.ReadAllText("Packages/manifest.json"),
                Does.Not.Contain("addressable").IgnoreCase);
        }

        private static IEnumerable<string> CourseSignature(
            CompiledCourseDefinition course)
        {
            var result = new List<string>
            {
                "课程|" + course.CourseId + "|"
                + course.ExperimentPrefabResourceId
            };
            result.AddRange(course.Entities.Select(value =>
                "实体|" + value.EntityId + "|"
                + string.Join(";", value.CapabilityIds)));
            result.AddRange(course.Resources.Select(value =>
                "资源|" + value.ResourceId + "|" + value.AssetPath));
            result.AddRange(course.Ports.Select(value =>
                "端口|" + value.PortId + "|" + value.EntityId + "|"
                + value.CompatibilityGroup));
            result.AddRange(course.InitialRelations.Select(value =>
                "初始关系|" + value.RelationId + "|" + value.TypeId + "|"
                + value.SourceEntityId + "|" + value.TargetEntityId + "|"
                + (value.SourcePortId ?? string.Empty) + "|"
                + (value.TargetPortId ?? string.Empty)));
            result.AddRange(course.StateChanges.Select(value =>
                "状态变化|" + MutationSignature(value)));
            result.AddRange(course.DomainEvents.Select(value =>
                "领域事件|" + value.EventId + "|" + value.EventType + "|"
                + ParametersSignature(value.Parameters)));
            result.AddRange(course.ContinuousProcesses.Select(value =>
                "持续过程|" + value.ProcessId + "|" + value.StartMutationId
                + "|" + value.StopMutationId));
            result.AddRange(course.ActionResultGroups.Select(value =>
                "动作结果组|" + value.GroupId + "|"
                + string.Join(";", value.MutationIds) + "|"
                + string.Join(";", value.DomainEventIds)));
            result.AddRange(course.ConfiguredActions.Select(value =>
                "动作|" + value.PolicyId + "|" + value.ActionId + "|"
                + (value.SourceEntityId ?? string.Empty) + "|"
                + (value.TargetEntityId ?? string.Empty) + "|"
                + string.Join(
                    ";",
                    value.Rules.Select(RuleSignature)) + "|"
                + string.Join(
                    ";",
                    value.Mutations.Select(MutationSignature))));
            result.AddRange(course.GoalRules.SelectMany(goal =>
                goal.Conditions.Select(condition =>
                    "目标|" + goal.GoalId + "|" + condition.ConditionId + "|"
                    + condition.ActorEntityId + "|"
                    + condition.SourceEntityId + "|"
                    + (condition.TargetEntityId ?? string.Empty) + "|"
                    + string.Join(
                        ";",
                        condition.Rules.Select(RuleSignature)))));
            result.AddRange(course.ActionAssessments.Select(value =>
                "安全|" + value.AssessmentId + "|" + value.ActionId + "|"
                + value.RejectionCode + "|" + value.RiskId + "|"
                + value.ScoreDelta + "|" + value.Prompt));
            result.AddRange(course.SceneLayouts.Select(value =>
                "布局|" + value.EntityId + "|" + value.PositionX + "|"
                + value.PositionY + "|" + value.PositionZ + "|"
                + value.RotationX + "|" + value.RotationY + "|"
                + value.RotationZ));
            result.AddRange(course.PrefabContracts.Select(value =>
                "实体视图|" + value.EntityId + "|"
                + string.Join(";", value.CapabilityIds) + "|"
                + string.Join(";", value.PortIds)));
            return result;
        }

        private static CourseBlueprintCompilationResult Compile() =>
            new CourseBlueprintCompiler().Compile(
                CourseBlueprintSource.FromDirectory(CourseDirectory),
                new CoreRecipePackageProvider(),
                new IRecipePackageProvider[]
                {
                    new ChemistryRecipePackageProvider()
                });

        private static void AssertPrefabSlots(
            string entityId,
            params (string Id, PresentationSlotKind Kind)[] expected)
        {
            var path = OxygenCourseContentBuilder.ExperimentPrefabPath;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var view = prefab
                .GetComponentsInChildren<CourseEntityView>(true)
                .Single(value => value.EntityId == entityId);
            var slots = view.PresentationSlots
                .Select(value => (value.SlotId, value.Kind))
                .ToArray();
            foreach (var item in expected)
            {
                Assert.That(
                    slots,
                    Does.Contain((item.Id, item.Kind)),
                    path + " 缺少 " + item.Id);
            }

            Assert.That(
                slots.Select(value => value.SlotId),
                Is.Unique,
                path + " 存在重复插槽 ID");
        }

        private static void AssertPrefabAnchors(
            string entityId,
            params (string Id, SemanticAnchorKind Kind)[] expected)
        {
            var path = OxygenCourseContentBuilder.ExperimentPrefabPath;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var view = prefab
                .GetComponentsInChildren<CourseEntityView>(true)
                .Single(value => value.EntityId == entityId);
            var anchors = view.Anchors
                .Select(value => (value.AnchorId, value.Kind))
                .ToArray();
            foreach (var item in expected)
            {
                Assert.That(
                    anchors,
                    Does.Contain((item.Id, item.Kind)),
                    path + " 缺少 " + item.Id);
            }
        }

        [Test]
        public void 氧气场景按课程ID自动解析资产且不保存直接引用()
        {
            const string courseId = "氧气的实验室制取与性质";
            var asset = CompiledCourseAssetCatalog.Require(courseId);
            var sceneText = File.ReadAllText(
                OxygenCourseContentBuilder.ProductionScenePath);
            var assetGuid = AssetDatabase.AssetPathToGUID(CourseAssetPath);

            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.CourseId, Is.EqualTo(courseId));
            Assert.That(sceneText, Does.Contain("courseId:"));
            Assert.That(
                sceneText,
                Does.Not.Contain(assetGuid),
                "生产场景不应序列化编译课程资产引用。");
        }

        private static string RuleSignature(
            StructuredRuleDefinition rule)
        {
            return rule.RuleId + ":" + rule.Order + ":" + rule.Field + ":"
                + rule.Operator + ":" + ValueSignature(rule.ExpectedValue)
                + ":" + rule.RejectionCode;
        }

        private static string MutationSignature(
            ConfiguredMutationDefinition mutation)
        {
            return mutation.MutationId + ":" + mutation.OperationId + ":"
                + ParametersSignature(mutation.Parameters);
        }

        private static string ParametersSignature(
            IReadOnlyDictionary<string, StructuredValue> parameters)
        {
            return string.Join(
                ";",
                parameters.OrderBy(value => value.Key).Select(value =>
                    value.Key + "=" + ValueSignature(value.Value)));
        }

        private static string ValueSignature(StructuredValue value)
        {
            return value.Kind switch
            {
                StructuredValueKind.Null => "空",
                StructuredValueKind.Boolean => value.Boolean.ToString(),
                StructuredValueKind.Number =>
                    value.Number.ToString("R",
                        System.Globalization.CultureInfo.InvariantCulture),
                StructuredValueKind.Text => value.Text,
                StructuredValueKind.TextList =>
                    string.Join(",", value.TextList),
                _ => value.Kind.ToString()
            };
        }
    }
}
