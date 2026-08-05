using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualLab.Application.Courses;
using VirtualLab.Unity.Authoring.Blueprints;
using VirtualLab.Unity.Authoring.Recipes;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CompatiblePairRecipeExpansionTests
    {
        [Test]
        public void 只为同兼容组端口生成连接断开条件状态和表现()
        {
            var blueprint = ReadConnectionBlueprint();
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            var result = new RecipeExpander().Expand(blueprint, catalog);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Model.Actions.Select(value =>
                    value.Definition.PolicyId),
                Is.EquivalentTo(new[]
                {
                    "策略.通用连接.导气管.集气瓶",
                    "策略.通用断开.导气管.集气瓶"
                }));
            Assert.That(
                result.Model.Actions.All(value =>
                    value.Definition.SourceEntityId == "导气管"
                    && value.Definition.TargetEntityId == "集气瓶"),
                Is.True);
            Assert.That(
                result.Model.Ports.Select(value =>
                    value.Definition.PortId),
                Is.EquivalentTo(new[]
                {
                    "导气管.出口",
                    "集气瓶.入口"
                }));
            Assert.That(
                result.Model.Rules.Select(value => value.Definition.FieldId),
                Does.Contain("来源连接点占用状态"));
            Assert.That(
                result.Model.Rules.Select(value => value.Definition.FieldId),
                Does.Contain("目标连接点占用状态"));
            Assert.That(
                result.Model.Rules.Select(value => value.Definition.FieldId),
                Does.Contain("连接标签相匹配"));
            Assert.That(
                result.Model.Rules.Select(value => value.Definition.FieldId),
                Does.Contain("来源对象已被操作者拿起"),
                "兼容端口连接的持有前置条件应由平台共享配方生成。课程不应重复配表。");
            Assert.That(
                result.Model.StateChanges.Select(value =>
                    value.Definition.OperationId),
                Is.SupersetOf(new[]
                {
                    "设置关系",
                    "移除关系"
                }));
            Assert.That(
                result.Model.PresentationStates.Select(value =>
                    value.Definition.StateId),
                Does.Contain("状态.导气管已连接集气瓶"));
            Assert.That(
                result.Model.PresentationEffects.Select(value =>
                    value.Definition.ProtocolId),
                Is.SupersetOf(new[]
                {
                    "interaction.snap-to-anchor",
                    "transform.detach"
                }));
            var snap = result.Model.PresentationEffects.Single(value =>
                    value.Definition.ProtocolId
                    == "interaction.snap-to-anchor")
                .Definition;
            Assert.That(
                snap.Lifecycle,
                Is.EqualTo(CoursePresentationLifecycle.UntilReplaced));
            Assert.That(
                snap.SubjectId,
                Is.EqualTo(CoursePresentationSemanticSubjects.ActionTarget));
            Assert.That(
                snap.LocationKind,
                Is.EqualTo(CoursePresentationLocationKind.SemanticAnchor));
            Assert.That(snap.LocationId, Is.EqualTo("集气瓶.入口"));
            Assert.That(
                snap.TriggerKind,
                Is.EqualTo(CoursePresentationTriggerKind.StateActive));
            Assert.That(
                snap.TriggerValue,
                Is.EqualTo("状态.导气管已连接集气瓶"));
            Assert.That(
                result.Model.Actions.Any(value =>
                    value.Definition.TargetEntityId == "铁架台"),
                Is.False);
        }

        [Test]
        public void 缺少匹配端口参数时不实例化双实体配方()
        {
            var blueprint = ReadConnectionBlueprint(false);
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            var result = new RecipeExpander().Expand(blueprint, catalog);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Model.Actions, Is.Empty);
            Assert.That(result.Model.Ports, Is.Empty);
        }

        [Test]
        public void 没有反馈配方的双实体动作不生成空表现组()
        {
            var blueprint = CourseBlueprintTestFactory.Blueprint(
                Object("集气瓶", "可放置来源", "参数.作用组.放置", "水槽"),
                Object("水槽", "可放置目标", "参数.作用组.放置", "水槽"));
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            var result = new RecipeExpander().Expand(
                blueprint,
                catalog);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Model.Actions.Count(),
                Is.EqualTo(2));
            Assert.That(
                result.Model.Actions.All(value =>
                    string.IsNullOrEmpty(
                        value.Definition.PresentationGroupId)),
                Is.True);
            Assert.That(result.Model.PresentationGroups, Is.Empty);
        }

        [Test]
        public void 固定连接自动要求操作者持有目标对象()
        {
            var blueprint = CourseBlueprintTestFactory.Blueprint(
                Object("铁架台", "可夹持", "参数.端口.出口.兼容组", "组.夹持"),
                Object("试管", "可连接", "参数.端口.入口.兼容组", "组.夹持"));
            var catalog = RecipeCatalog.Create(
                new CoreRecipePackageProvider(),
                Array.Empty<IRecipePackageProvider>());

            var result = new RecipeExpander().Expand(blueprint, catalog);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Model.Rules.Select(value => value.Definition.FieldId),
                Does.Contain("目标对象已被操作者拿起"));
        }

        private static CourseBlueprint ReadConnectionBlueprint(
            bool includeTargetGroup = true)
            => CourseBlueprintTestFactory.Blueprint(
                Object("导气管", "可连接", "参数.端口.出口.兼容组", "组.导气"),
                includeTargetGroup
                    ? Object("集气瓶", "可连接", "参数.端口.入口.兼容组", "组.导气")
                    : Object("集气瓶", "可连接"),
                Object("铁架台", "可连接", "参数.端口.入口.兼容组", "组.夹持"));

        private static CourseObjectBlueprint Object(
            string id,
            string feature,
            string parameter = null,
            string value = null) =>
            CourseBlueprintTestFactory.Object(
                id,
                new[] { feature },
                string.IsNullOrEmpty(parameter)
                    ? Array.Empty<KeyValuePair<string, string>>()
                    : new[] { new KeyValuePair<string, string>(parameter, value) });
    }
}
