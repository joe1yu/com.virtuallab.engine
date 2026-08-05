using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VirtualLab.Application.Courses;
using VirtualLab.Chemistry.Courses;
using VirtualLab.Chemistry.UnityAdapters;
using VirtualLab.Domain.Relations;
using VirtualLab.Kernel;
using VirtualLab.Presentation;
using VirtualLab.UnityAdapters.Courses;
using VirtualLab.UnityAdapters.Authoring;
using VirtualLab.UnityAdapters.Input;
using VirtualLab.UnityAdapters.Presentation;
using Object = UnityEngine.Object;

namespace VirtualLab.Engine.PlayModeTests
{
    public sealed class ConfiguredOxygenEndToEndTests
    {
        [UnityTearDown]
        public IEnumerator 清理生产场景避免污染其他运行时测试()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name == "OxygenLaboratory")
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    Object.Destroy(root);
                }

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator 生产氧气场景只使用通用配置链路()
        {
            yield return SceneManager.LoadSceneAsync("OxygenLaboratory");
            yield return null;

            var bootstrap =
                Object.FindObjectOfType<ConfigDrivenCourseBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.IsInitialized, Is.True);
            Assert.That(bootstrap.Course.CourseId,
                Is.EqualTo("氧气的实验室制取与性质"));
            Assert.That(bootstrap.SceneAssembly, Is.Not.Null);
            Assert.That(bootstrap.SceneAssembly.CourseViews.Views.Count,
                Is.GreaterThan(0));
            Assert.That(
                Object.FindObjectOfType<CourseSemanticInputGateway>(),
                Is.Not.Null);
            Assert.That(
                Object.FindObjectOfType<CourseDirectManipulationController>(),
                Is.Not.Null,
                "生产场景必须允许输入设备通过语义网关进行直接操作。");
            Assert.That(Camera.main, Is.Not.Null);
            var ui = Object.FindObjectOfType<CourseUiClickSimulator>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.OutputScrollRect, Is.Not.Null);
            Assert.That(ui.OutputScrollRect.vertical, Is.True);
            Assert.That(ui.OutputScrollRect.horizontal, Is.False);
            Assert.That(ui.OutputScrollRect.viewport, Is.Not.Null);
            Assert.That(
                ui.OutputScrollRect.content,
                Is.EqualTo(ui.OutputText.rectTransform));
            Assert.That(ui.StepIds.Count, Is.EqualTo(164));
            Assert.That(ui.StepIds.First(), Is.EqualTo("1.1"));
            Assert.That(ui.StepIds.Last(), Is.EqualTo("8.13"));
            Assert.That(
                Object.FindObjectsOfType<Button>(true).Length,
                Is.EqualTo(164));
            Assert.That(
                Object.FindObjectsOfType<CourseEntityView>(true),
                Is.Not.Empty,
                "生产场景必须装配真实实验实体，UI 只作为可选演示输入。");

            var componentNames = Object
                .FindObjectsOfType<MonoBehaviour>(true)
                .Select(value => value.GetType().Name)
                .ToArray();
            Assert.That(componentNames,
                Does.Not.Contain("OxygenExperimentBootstrap"));
            Assert.That(componentNames,
                Does.Not.Contain("OxygenSemanticActionController"));
            Assert.That(componentNames,
                Does.Not.Contain("OxygenPresentationBindings"));
        }

        [UnityTest]
        public IEnumerator 直接操纵从课程策略恢复语义来源和目标而不依赖拖动方向()
        {
            yield return SceneManager.LoadSceneAsync("OxygenLaboratory");
            yield return null;

            var gateway = Object.FindObjectOfType<CourseSemanticInputGateway>();
            Assert.That(gateway, Is.Not.Null);
            var candidates = gateway.FindCandidates("大试管", "铁架台试管夹");

            Assert.That(
                candidates.Any(value =>
                    value.ActionId == InteractionSemanticActionIds.Connect
                    && value.SourceEntityId == "铁架台试管夹"
                    && value.TargetEntityId == "大试管"),
                Is.True,
                "拖动大试管接触试管夹时，命令仍应保持课程定义的固定端/被固定端角色。");
        }

        [UnityTest]
        public IEnumerator 非法落点不会把表现对象留在未获批准的位置()
        {
            yield return SceneManager.LoadSceneAsync("OxygenLaboratory");
            yield return null;

            var bootstrap =
                Object.FindObjectOfType<ConfigDrivenCourseBootstrap>();
            var controller =
                Object.FindObjectOfType<CourseDirectManipulationController>();
            var camera = Camera.main;
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);

            var originalPoses = bootstrap.SceneAssembly.CourseViews.Views
                .ToDictionary(
                    value => value.Key,
                    value => new
                    {
                        Position = value.Value.transform.position,
                        Rotation = value.Value.transform.rotation
                    });
            CourseEntityView selected = null;
            foreach (var view in bootstrap.SceneAssembly.CourseViews.Views
                         .Values.OrderBy(value => value.EntityId))
            {
                var screenPoint = camera.WorldToScreenPoint(
                    view.transform.position);
                if (screenPoint.z <= 0f
                    || !controller.TryBeginManipulation(screenPoint))
                {
                    continue;
                }

                selected = controller.Selected;
                break;
            }

            Assert.That(
                selected,
                Is.Not.Null,
                "生产场景中至少应有一个能通过指针抓取的对象。");
            var original = originalPoses[selected.EntityId];
            var invalidPointer = new Vector2(-10000f, -10000f);
            controller.UpdateManipulation(invalidPointer);
            yield return new WaitForFixedUpdate();

            Assert.That(
                controller.CompleteManipulation(invalidPointer),
                Is.False,
                "实验视口外的落点不能被提交。");
            yield return new WaitForFixedUpdate();

            Assert.That(controller.Selected, Is.Null);
            Assert.That(controller.CurrentDropAvailability, Is.Null);
            Assert.That(
                Vector3.Distance(
                    selected.transform.position,
                    original.Position),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(
                    selected.transform.rotation,
                    original.Rotation),
                Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator 实验视口内的空白位置允许自由放置()
        {
            yield return SceneManager.LoadSceneAsync("OxygenLaboratory");
            yield return null;

            var bootstrap =
                Object.FindObjectOfType<ConfigDrivenCourseBootstrap>();
            var controller =
                Object.FindObjectOfType<CourseDirectManipulationController>();
            var camera = Camera.main;
            CourseEntityView selected = null;
            foreach (var view in bootstrap.SceneAssembly.CourseViews.Views
                         .Values.OrderBy(value => value.EntityId))
            {
                var screenPoint = camera.WorldToScreenPoint(
                    view.transform.position);
                if (screenPoint.z > 0f
                    && controller.TryBeginManipulation(screenPoint))
                {
                    selected = controller.Selected;
                    break;
                }
            }

            Assert.That(selected, Is.Not.Null);
            var originalPosition = selected.transform.position;
            Vector2? emptyPointer = null;
            for (var x = 1; x < 10 && emptyPointer == null; x++)
            {
                for (var y = 1; y < 10; y++)
                {
                    var pointer = new Vector2(
                        camera.pixelRect.xMin + camera.pixelRect.width * x / 10f,
                        camera.pixelRect.yMin + camera.pixelRect.height * y / 10f);
                    var hitsEntity = Physics.RaycastAll(
                            camera.ScreenPointToRay(pointer),
                            100f,
                            ~0,
                            QueryTriggerInteraction.Collide)
                        .Select(value => value.collider.GetComponentInParent<
                            CourseEntityView>())
                        .Any(value => value != null && value != selected);
                    if (!hitsEntity)
                    {
                        emptyPointer = pointer;
                        break;
                    }
                }
            }

            Assert.That(emptyPointer.HasValue, Is.True, "场景中应存在可自由放置的空白位置。");
            controller.UpdateManipulation(emptyPointer.Value);
            Assert.That(controller.CompleteManipulation(emptyPointer.Value), Is.True);
            Assert.That(controller.Selected, Is.Null);
            Assert.That(
                Vector3.Distance(selected.transform.position, originalPosition),
                Is.GreaterThan(0.01f));
        }

        [UnityTest]
        public IEnumerator 生产场景拒绝动作通过全局接收器显示中文提示()
        {
            yield return SceneManager.LoadSceneAsync("OxygenLaboratory");
            yield return null;

            var bootstrap =
                Object.FindObjectOfType<ConfigDrivenCourseBootstrap>();
            var ui = Object.FindObjectOfType<CourseUiClickSimulator>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(ui, Is.Not.Null);

            var result = bootstrap.Dispatch(Request(
                "命令.拒绝加热",
                ChemistrySemanticActionIds.BeginHeating,
                "大试管",
                "酒精灯"));

            Assert.That(result.Outcome.IsAccepted, Is.False);
            Assert.That(
                result.PresentationCommands.Select(value => value.EffectId),
                Does.Contain("ui.message"));
            Assert.That(
                ui.OutputText.text,
                Does.Contain("当前条件不允许执行该操作"));
        }

        [UnityTest]
        public IEnumerator 生产场景通过语义命令完成制取收集和性质实验()
        {
            yield return SceneManager.LoadSceneAsync("OxygenLaboratory");
            yield return null;

            var bootstrap =
                Object.FindObjectOfType<ConfigDrivenCourseBootstrap>();
            var chemistry = Object.FindObjectOfType<
                ChemistryConfiguredCourseSessionFactory>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(chemistry, Is.Not.Null);
            Assert.That(chemistry.Runtime, Is.Not.Null);
            var processSignals = new List<PresentationSignal>();
            chemistry.PresentationSignalProduced += processSignals.Add;
            var reagentContent = ContentRenderer(
                bootstrap,
                "高锰酸钾广口瓶");
            var spoonContent = ContentRenderer(bootstrap, "药匙");
            var tubeContent = ContentRenderer(bootstrap, "大试管");
            Assert.That(reagentContent.enabled, Is.True);
            Assert.That(spoonContent.enabled, Is.False);
            Assert.That(tubeContent.enabled, Is.False);

            Execute(bootstrap, "命令.拿起镊子",
                InteractionSemanticActionIds.Grab, "镊子", null);
            Execute(bootstrap, "命令.镊子夹住棉花团",
                InteractionSemanticActionIds.Connect, "镊子", "棉花团");
            Execute(bootstrap, "命令.通过镊子拿起棉花团",
                InteractionSemanticActionIds.Grab, "棉花团", null);
            Execute(bootstrap, "命令.放置棉花团",
                InteractionSemanticActionIds.Place, "棉花团", "大试管");
            Execute(bootstrap, "命令.拿起橡皮塞",
                InteractionSemanticActionIds.Grab, "橡胶塞玻璃导管", null);
            Execute(bootstrap, "命令.连接橡皮塞",
                InteractionSemanticActionIds.Connect, "橡胶塞玻璃导管", "大试管");
            Execute(bootstrap, "命令.拿起试管",
                InteractionSemanticActionIds.Grab, "大试管", null);
            Execute(bootstrap, "命令.固定试管",
                InteractionSemanticActionIds.Connect, "铁架台试管夹", "大试管");
            Execute(bootstrap, "命令.拿起折角导气管并组装气路",
                InteractionSemanticActionIds.Grab, "折角导气管", null);
            Execute(bootstrap, "命令.折角导气管连接橡皮塞",
                InteractionSemanticActionIds.Connect, "折角导气管", "橡胶塞玻璃导管");
            Execute(bootstrap, "命令.放下已组装折角导气管",
                InteractionSemanticActionIds.Release, "折角导气管", null);
            Execute(bootstrap, "命令.取下酒精灯帽",
                InteractionSemanticActionIds.Grab, "酒精灯帽", null);
            Execute(bootstrap, "命令.放下酒精灯帽",
                InteractionSemanticActionIds.Release, "酒精灯帽", null);
            Execute(bootstrap, "命令.拿起火柴",
                InteractionSemanticActionIds.Grab, "火柴", null);
            Execute(bootstrap, "命令.划燃火柴",
                ChemistrySemanticActionIds.Ignite, "火柴", "火柴盒");
            var igniteLamp = Execute(bootstrap, "命令.点燃酒精灯",
                ChemistrySemanticActionIds.Ignite, "酒精灯", "火柴");
            Execute(bootstrap, "命令.放下火柴",
                InteractionSemanticActionIds.Release, "火柴", null);
            Assert.That(
                igniteLamp.PresentationCommands.Select(value =>
                    value.EffectId),
                Does.Contain("vfx.play"));
            Execute(bootstrap, "命令.取下高锰酸钾广口瓶盖",
                InteractionSemanticActionIds.Grab, "高锰酸钾瓶盖", null);
            Execute(bootstrap, "命令.放下高锰酸钾广口瓶盖",
                InteractionSemanticActionIds.Release, "高锰酸钾瓶盖", null);
            Execute(bootstrap, "命令.拿起药匙",
                InteractionSemanticActionIds.Grab, "药匙", null);
            Execute(bootstrap, "命令.药匙伸入试剂瓶",
                InteractionSemanticActionIds.Place, "药匙", "高锰酸钾广口瓶");
            Execute(bootstrap, "命令.药匙舀取高锰酸钾",
                InteractionSemanticActionIds.Take, "药匙", "高锰酸钾广口瓶");
            Assert.That(reagentContent.enabled, Is.False);
            Assert.That(spoonContent.enabled, Is.True);
            var pourPermanganate = Execute(
                bootstrap,
                "命令.倒入高锰酸钾",
                ChemistrySemanticActionIds.BeginPour,
                "药匙",
                "大试管",
                ("请求流量克每秒", StructuredValue.FromNumber(632d)));
            Assert.That(
                pourPermanganate.PresentationCommands.Select(value =>
                    value.EffectId),
                Does.Contain("transform.oscillate"));
            Assert.That(spoonContent.enabled, Is.False);
            Assert.That(tubeContent.enabled, Is.True);
            chemistry.Advance(1d);
            Execute(bootstrap, "命令.开始制氧",
                ChemistrySemanticActionIds.BeginHeating, "大试管", "酒精灯");
            chemistry.Advance(600d);
            chemistry.Advance(1d);

            FillBottle(bootstrap, chemistry, "集气瓶一");
            FillBottle(bootstrap, chemistry, "集气瓶二");
            Execute(bootstrap, "命令.拿起折角导气管一",
                InteractionSemanticActionIds.Grab, "折角导气管", null);
            var connectFirst = Execute(bootstrap, "命令.连接第一只集气瓶",
                InteractionSemanticActionIds.Connect, "折角导气管", "集气瓶一");
            Execute(bootstrap, "命令.放下折角导气管一",
                InteractionSemanticActionIds.Release, "折角导气管", null);
            Assert.That(
                connectFirst.PresentationCommands.Select(value =>
                    value.EffectId),
                Does.Contain("interaction.snap-to-anchor"));
            Assert.That(
                bootstrap.Runtime.ExportState().Relations.Count(value =>
                    value.TypeId == InteractionRelationTypeIds.Connection
                    && (value.SourceEntityId == "折角导气管"
                        || value.TargetEntityId == "折角导气管")),
                Is.EqualTo(2),
                "折角导气管两端应分别连接橡皮塞和集气瓶，形成完整气路。");
            var collectFirst = Execute(bootstrap, "命令.收集第一瓶氧气",
                ChemistrySemanticActionIds.CollectGas,
                "折角导气管",
                "集气瓶一");
            Assert.That(
                collectFirst.PresentationCommands.Select(value =>
                    value.EffectId),
                Does.Contain("liquid.set-level"));
            Execute(bootstrap, "命令.再次拿起折角导气管",
                InteractionSemanticActionIds.Grab, "折角导气管", null);
            Execute(bootstrap, "命令.断开第一只集气瓶",
                InteractionSemanticActionIds.Disconnect, "折角导气管", "集气瓶一");
            Execute(bootstrap, "命令.连接第二只集气瓶",
                InteractionSemanticActionIds.Connect, "折角导气管", "集气瓶二");
            Execute(bootstrap, "命令.放下折角导气管二",
                InteractionSemanticActionIds.Release, "折角导气管", null);
            Execute(bootstrap, "命令.收集第二瓶氧气",
                ChemistrySemanticActionIds.CollectGas,
                "折角导气管",
                "集气瓶二");
            Execute(bootstrap, "命令.第三次拿起折角导气管",
                InteractionSemanticActionIds.Grab, "折角导气管", null);
            Execute(bootstrap, "命令.将折角导气管移出水面",
                InteractionSemanticActionIds.Disconnect, "折角导气管", "集气瓶二");
            Execute(bootstrap, "命令.停止加热",
                ChemistrySemanticActionIds.EndHeating, "大试管", "酒精灯");

            Execute(bootstrap, "命令.打开木炭瓶",
                InteractionSemanticActionIds.Grab, "木炭瓶盖", null);
            Execute(bootstrap, "命令.放下木炭瓶盖",
                InteractionSemanticActionIds.Release, "木炭瓶盖", null);
            Execute(bootstrap, "命令.拿起坩埚钳",
                InteractionSemanticActionIds.Grab, "坩埚钳", null);
            Execute(bootstrap, "命令.夹住木炭",
                InteractionSemanticActionIds.Connect, "坩埚钳", "木炭");
            Execute(bootstrap, "命令.预热木炭",
                ChemistrySemanticActionIds.BeginHeating, "木炭", "酒精灯");
            chemistry.Advance(200d);
            Execute(bootstrap, "命令.拿起石灰水",
                InteractionSemanticActionIds.Grab, "澄清石灰水窄口瓶", null);
            Execute(
                bootstrap,
                "命令.点燃木炭",
                ChemistrySemanticActionIds.Ignite,
                "木炭",
                "酒精灯",
                ("空间距离米", StructuredValue.FromNumber(0.1d)));
            chemistry.Advance(1d);
            Execute(bootstrap, "命令.放入木炭",
                InteractionSemanticActionIds.Place, "木炭", "集气瓶一");
            Execute(
                bootstrap,
                "命令.加入石灰水",
                ChemistrySemanticActionIds.BeginPour,
                "澄清石灰水窄口瓶",
                "集气瓶一",
                ("请求流量毫升每秒", StructuredValue.FromNumber(10d)));
            chemistry.Advance(1d);
            Execute(bootstrap, "命令.握持集气瓶一",
                InteractionSemanticActionIds.Grab, "集气瓶一", null);
            var shake = Execute(
                bootstrap,
                "命令.振荡石灰水",
                ChemistrySemanticActionIds.Shake,
                "集气瓶一",
                null,
                ("强度", StructuredValue.FromNumber(1d)),
                ("持续秒数", StructuredValue.FromNumber(1d)));
            Assert.That(
                shake.PresentationCommands.Select(value => value.EffectId),
                Does.Contain("transform.oscillate"));
            chemistry.Advance(1d);

            Execute(bootstrap, "命令.再次拿起火柴",
                InteractionSemanticActionIds.Grab, "火柴", null);
            Execute(bootstrap, "命令.再次划燃火柴",
                ChemistrySemanticActionIds.Ignite, "火柴", "火柴盒");
            Execute(bootstrap, "命令.点燃铁丝底端火柴",
                ChemistrySemanticActionIds.Ignite, "组合引燃火柴", "火柴");
            Execute(bootstrap, "命令.再次放下火柴",
                InteractionSemanticActionIds.Release, "火柴", null);
            Execute(bootstrap, "命令.预热铁丝",
                ChemistrySemanticActionIds.BeginHeating, "火柴铁丝组合", "组合引燃火柴");
            chemistry.Advance(200d);
            Execute(
                bootstrap,
                "命令.点燃铁丝",
                ChemistrySemanticActionIds.Ignite,
                "火柴铁丝组合",
                "组合引燃火柴",
                ("空间距离米", StructuredValue.FromNumber(0.1d)));
            chemistry.Advance(1d);
            Execute(bootstrap, "命令.拿起铁丝",
                InteractionSemanticActionIds.Grab, "火柴铁丝组合", null);
            Execute(bootstrap, "命令.放入铁丝",
                InteractionSemanticActionIds.Place, "火柴铁丝组合", "集气瓶二");
            Execute(bootstrap, "命令.观察铁丝燃烧",
                InteractionSemanticActionIds.Observe, "集气瓶二", null);

            var state = chemistry.Runtime.Session.ExportState();
            Assert.That(
                state.Matter.Any(value =>
                    value.LocationId == "集气瓶一"
                    && value.SubstanceId == "碳酸钙"
                    && value.Value == 10.00865m),
                Is.True);
            Assert.That(
                state.Matter.Any(value =>
                    value.LocationId == "集气瓶二"
                    && value.SubstanceId == "四氧化三铁"
                    && value.Value == 116m),
                Is.True);
            Assert.That(
                state.Commands.Select(value => value.Request.ActionId),
                Does.Contain(InteractionSemanticActionIds.Connect)
                    .And.Contain(InteractionSemanticActionIds.Disconnect)
                    .And.Contain(InteractionSemanticActionIds.Place)
                    .And.Contain(ChemistrySemanticActionIds.BeginPour)
                    .And.Contain(ChemistrySemanticActionIds.BeginHeating)
                    .And.Contain(ChemistrySemanticActionIds.EndHeating)
                    .And.Contain(ChemistrySemanticActionIds.Ignite)
                    .And.Contain(ChemistrySemanticActionIds.Shake));
            Assert.That(
                processSignals.Select(value => value.SignalId),
                Does.Contain("气体.已生成")
                    .And.Contain("反应.已推进"));
            Assert.That(
                bootstrap.Runtime,
                Is.SameAs(chemistry.Runtime.Facade));
            Assert.That(
                bootstrap.Runtime.Goals.CompletedGoalIds,
                Is.EquivalentTo(state.Goals.CompletedGoalIds));
            Assert.That(
                bootstrap.Runtime.Goals.CompletedGoalIds,
                Has.Count.EqualTo(8));
            Assert.That(
                bootstrap.Runtime.Observations.Select(value =>
                    value.ObservedEntityId),
                Does.Contain("集气瓶二"));
            Assert.That(
                bootstrap.Runtime.EventHistory.Select(value =>
                    value.EventType),
                Does.Contain("气体.已生成")
                    .And.Contain("反应.已推进"));
            Assert.That(
                bootstrap.Runtime.EventHistory.Select(value =>
                    value.Sequence),
                Is.EqualTo(Enumerable.Range(
                    1,
                    bootstrap.Runtime.EventHistory.Count)
                    .Select(value => (long)value)));
            var ui = Object.FindObjectOfType<CourseUiClickSimulator>();
            Assert.That(
                ui.OutputText.text,
                Does.Contain("已记录实验现象"),
                "真实表现模式下，UI 只接收显式的全局消息表现。");
        }

        [UnityTest]
        public IEnumerator UI允许乱序操作且错误操作产生文字表现()
        {
            yield return SceneManager.LoadSceneAsync("OxygenLaboratory");
            yield return null;

            var ui = Object.FindObjectOfType<CourseUiClickSimulator>();
            Assert.That(ui, Is.Not.Null);

            var buttons = Object.FindObjectsOfType<Button>(true);
            Assert.That(buttons.Length, Is.EqualTo(164));
            Assert.That(buttons.All(value => value.interactable), Is.True);
            Assert.That(ui.RecommendedStepId, Is.EqualTo("1.1"));

            // 尚未完成安全准备就开始加热：命令应进入内核并得到可见拒绝，
            // 而不是被 UI 的脚本顺序提前拦截。
            Assert.That(ui.ClickStep("4.5"), Is.False);
            Assert.That(ui.CompletedStepCount, Is.Zero);
            Assert.That(ui.RecommendedStepId, Is.EqualTo("1.1"));
            Assert.That(buttons.All(value => value.interactable), Is.True);
            Assert.That(
                ui.OutputText.text,
                Does.Contain("[命令✗]")
                    .And.Contain("[表现]")
                    .And.Contain("当前条件不允许执行该操作")
                    .And.Contain("操作未完成"));

            // 拿起折角导气管不依赖前序装配，可以在建议步骤 1.1 之前合法执行。
            Assert.That(ui.ClickStep("1.4"), Is.True, ui.OutputText.text);
            Assert.That(ui.CompletedStepCount, Is.EqualTo(1));
            Assert.That(ui.RecommendedStepId, Is.EqualTo("1.1"));
            Assert.That(buttons.All(value => value.interactable), Is.True);
        }

        [UnityTest]
        public IEnumerator UI按钮能够完成制氧收集和性质实验()
        {
            yield return SceneManager.LoadSceneAsync("OxygenLaboratory");
            yield return null;

            var ui = Object.FindObjectOfType<CourseUiClickSimulator>();
            var bootstrap = Object.FindObjectOfType<ConfigDrivenCourseBootstrap>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(bootstrap, Is.Not.Null);

            var buttons = Object.FindObjectsOfType<Button>(true)
                .OrderBy(value => value.name, System.StringComparer.Ordinal)
                .ToArray();
            Assert.That(buttons.Length, Is.EqualTo(164));
            Assert.That(buttons.All(value => value.interactable), Is.True);
            for (var index = 0; index < buttons.Length; index++)
            {
                buttons[index].onClick.Invoke();
                Assert.That(
                    ui.CompletedStepCount,
                    Is.EqualTo(index + 1),
                    ui.OutputText.text);
            }

            Assert.That(ui.CompletedStepCount, Is.EqualTo(164));
            Assert.That(ui.RecommendedStepId, Is.Null);
            Canvas.ForceUpdateCanvases();
            Assert.That(
                ui.OutputText.preferredHeight,
                Is.GreaterThan(ui.OutputScrollRect.viewport.rect.height));
            Assert.That(
                ui.OutputScrollRect.verticalNormalizedPosition,
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                bootstrap.Runtime.Goals.CompletedGoalIds,
                Has.Count.EqualTo(8));
            Assert.That(
                ui.OutputText.text,
                Does.Contain("[命令✓]")
                    .And.Contain("[过程事件]")
                    .And.Contain("[课程状态]")
                    .And.Contain("观察火柴铁丝组合在氧气中的燃烧现象")
                    .And.Not.Contain("操作未完成")
                    .And.Not.Contain("[脚本✓]"));
        }

        private static void FillBottle(
            ConfigDrivenCourseBootstrap bootstrap,
            ChemistryConfiguredCourseSessionFactory chemistry,
            string bottleId)
        {
            Execute(
                bootstrap,
                "命令.拿起." + bottleId,
                InteractionSemanticActionIds.Grab,
                bottleId,
                null);
            Execute(
                bootstrap,
                "命令.拿起加水烧杯." + bottleId,
                InteractionSemanticActionIds.Grab,
                "加水烧杯",
                null);
            Execute(
                bootstrap,
                "命令.装水." + bottleId,
                ChemistrySemanticActionIds.BeginPour,
                "加水烧杯",
                bottleId,
                ("请求流量毫升每秒", StructuredValue.FromNumber(100d)));
            chemistry.Advance(1d);
            Execute(
                bootstrap,
                "命令.停止装水." + bottleId,
                ChemistrySemanticActionIds.EndPour,
                "加水烧杯",
                bottleId);
            Execute(
                bootstrap,
                "命令.放下加水烧杯." + bottleId,
                InteractionSemanticActionIds.Release,
                "加水烧杯",
                null);
            Execute(
                bootstrap,
                "命令.放下." + bottleId,
                InteractionSemanticActionIds.Release,
                bottleId,
                null);
        }

        private static CourseDispatchResult Execute(
            ConfigDrivenCourseBootstrap bootstrap,
            string commandId,
            string actionId,
            string sourceEntityId,
            string targetEntityId,
            params (string Key, StructuredValue Value)[] parameters)
        {
            var result = bootstrap.Dispatch(Request(
                commandId,
                actionId,
                sourceEntityId,
                targetEntityId,
                parameters));
            Assert.That(
                result.Outcome.IsAccepted,
                Is.True,
                commandId + ": "
                    + string.Join(",", result.Outcome.RejectionCodes));
            return result;
        }

        private static Renderer ContentRenderer(
            ConfigDrivenCourseBootstrap bootstrap,
            string entityId)
        {
            Assert.That(
                bootstrap.SceneAssembly.CourseViews.TryGet(
                    entityId,
                    out var view),
                Is.True,
                "场景中缺少实验对象：" + entityId);
            var slot = view.PresentationSlots.Single(value =>
                value.SlotId == "插槽.内容");
            var renderer = slot.GetComponent<Renderer>();
            Assert.That(renderer, Is.Not.Null, entityId + "的内容插槽缺少渲染器");
            return renderer;
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
