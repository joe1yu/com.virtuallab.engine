using NUnit.Framework;
using VirtualLab.Unity.Authoring.Workbench;

namespace VirtualLab.Engine.Tests.Courses
{
    public sealed class CourseWorkbenchGuideTests
    {
        [Test]
        public void EmptyWorkspaceRecommendsCreatingCourse()
        {
            var state = Evaluate(hasCourse: false);

            Assert.That(
                state.RecommendedAction,
                Is.EqualTo(CourseWorkbenchRecommendedAction.CreateCourse));
            Assert.That(state.CompletedStepCount, Is.Zero);
            Assert.That(
                state.Steps[0].Status,
                Is.EqualTo(CourseWorkbenchStepStatus.Current));
        }

        [Test]
        public void EmptyCourseRecommendsAddingFirstObject()
        {
            var state = Evaluate(hasCourse: true);

            Assert.That(
                state.RecommendedAction,
                Is.EqualTo(CourseWorkbenchRecommendedAction.AddObject));
            Assert.That(state.CompletedStepCount, Is.EqualTo(1));
        }

        [Test]
        public void IncompleteObjectTakesPriorityOverCompilerDiagnostics()
        {
            var state = Evaluate(
                hasCourse: true,
                objectCount: 3,
                incompleteObjectCount: 1,
                hasCompilation: true,
                compilationSucceeded: false,
                diagnosticCount: 6);

            Assert.That(
                state.RecommendedAction,
                Is.EqualTo(CourseWorkbenchRecommendedAction.CompleteObject));
            Assert.That(
                state.Steps[1].Status,
                Is.EqualTo(CourseWorkbenchStepStatus.NeedsAttention));
            Assert.That(
                state.Steps[2].Status,
                Is.EqualTo(CourseWorkbenchStepStatus.Pending));
        }

        [Test]
        public void ValidDirtyCourseRecommendsSingleSaveAndBuildAction()
        {
            var state = Evaluate(
                hasCourse: true,
                objectCount: 2,
                hasCompilation: true,
                compilationSucceeded: true,
                hasUnsavedChanges: true);

            Assert.That(
                state.RecommendedAction,
                Is.EqualTo(CourseWorkbenchRecommendedAction.SaveAndBuild));
            Assert.That(state.CompletedStepCount, Is.EqualTo(3));
        }

        [Test]
        public void ExternalChangeAlwaysRecommendsReloadInsteadOfSaving()
        {
            var state = Evaluate(
                hasCourse: true,
                objectCount: 2,
                hasCompilation: true,
                compilationSucceeded: true,
                hasUnsavedChanges: true,
                hasExternalChanges: true);

            Assert.That(
                state.RecommendedAction,
                Is.EqualTo(CourseWorkbenchRecommendedAction.ReloadCourse));
            Assert.That(
                state.Steps[3].Status,
                Is.EqualTo(CourseWorkbenchStepStatus.NeedsAttention));
        }

        [Test]
        public void BuiltUnchangedCourseShowsCompletedFlow()
        {
            var state = Evaluate(
                hasCourse: true,
                objectCount: 2,
                hasCompilation: true,
                compilationSucceeded: true,
                hasCurrentGeneratedAsset: true);

            Assert.That(
                state.RecommendedAction,
                Is.EqualTo(CourseWorkbenchRecommendedAction.None));
            Assert.That(state.CompletedStepCount, Is.EqualTo(4));
        }

        private static CourseWorkbenchGuideState Evaluate(
            bool hasCourse,
            int objectCount = 0,
            int incompleteObjectCount = 0,
            bool hasCompilation = false,
            bool compilationSucceeded = false,
            int diagnosticCount = 0,
            bool hasUnsavedChanges = false,
            bool hasCurrentGeneratedAsset = false,
            bool hasExternalChanges = false) =>
            CourseWorkbenchGuide.Evaluate(
                hasCourse,
                objectCount,
                incompleteObjectCount,
                hasCompilation,
                compilationSucceeded,
                diagnosticCount,
                hasUnsavedChanges,
                hasCurrentGeneratedAsset,
                hasExternalChanges);
    }
}
