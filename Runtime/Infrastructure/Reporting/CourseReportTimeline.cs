using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VirtualLab.Application.Courses;

namespace VirtualLab.Infrastructure.Reporting
{
    /// <summary>
    /// 报告侧对统一课程事件流的只读视图。它不重新采集日志，也不自行分配序号，
    /// 报告证据只能引用课程流中已经存在的事件事实。
    /// </summary>
    public sealed class CourseReportTimeline
    {
        public CourseReportTimeline(CourseRuntimeFacade runtime)
            : this((runtime ?? throw new ArgumentNullException(nameof(runtime)))
                .EventStates)
        {
        }

        public CourseReportTimeline(IEnumerable<CourseEventState> events)
        {
            var copy = (events ?? throw new ArgumentNullException(nameof(events)))
                .ToArray();
            var expected = 1L;
            foreach (var item in copy)
            {
                if (item == null || item.Sequence != expected)
                {
                    throw new ArgumentException(
                        "报告事件必须直接来自连续的课程事件流。",
                        nameof(events));
                }

                expected = checked(expected + 1);
            }

            Events = new ReadOnlyCollection<CourseEventState>(copy);
            LastSequence = expected - 1;
        }

        public IReadOnlyList<CourseEventState> Events { get; }

        public long LastSequence { get; }

        public ReportMetadata CreateMetadata(
            string courseId,
            string sessionId,
            int randomSeed) =>
            new ReportMetadata(
                courseId,
                sessionId,
                randomSeed,
                LastSequence);
    }
}
