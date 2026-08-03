using System;
using System.Collections.Generic;
using VirtualLab.Kernel;

namespace VirtualLab.Domain.Processes
{
    public sealed class ProcessScheduler
    {
        private readonly IProcessEventCollector _events;
        private readonly List<IProcessHandler> _handlers = new List<IProcessHandler>();

        public ProcessScheduler(IProcessEventCollector events)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public void Register(IProcessHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _handlers.Add(handler);
        }

        public void Advance(ExperimentWorld world, SimulationTick tick)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (tick.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "A process tick cannot be negative.");
            }

            foreach (var handler in _handlers)
            {
                handler.Advance(world, tick, _events);
            }
        }
    }
}
