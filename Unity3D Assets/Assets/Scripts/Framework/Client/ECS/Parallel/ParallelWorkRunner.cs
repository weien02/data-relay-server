using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace DedicatedServer.Framework.Client.ECS.Parallel
{
    internal sealed class ParallelWorkThreadReport
    {
        public readonly int threadID;
        public readonly int workItemCount;
        public readonly long elapsedTicks;

        public ParallelWorkThreadReport(int threadID, int workItemCount, long elapsedTicks)
        {
            this.threadID = threadID;
            this.workItemCount = workItemCount;
            this.elapsedTicks = elapsedTicks;
        }
    }

    internal sealed class ParallelWorkReport
    {
        private readonly List<ParallelWorkThreadReport> _threadReports;
        public int WorkItemCount { get; }
        public TimeSpan TotalElapsedTime { get; }
        public int ThreadCount => this._threadReports.Count;
        public IEnumerable<ParallelWorkThreadReport> ThreadReports => this._threadReports;

        public ParallelWorkReport(int workItemCount, TimeSpan totalElapsedTime, List<ParallelWorkThreadReport> threadReports)
        {
            this.WorkItemCount = workItemCount;
            this.TotalElapsedTime = totalElapsedTime;
            this._threadReports = threadReports;
        }
    }

    internal static class ParallelWorkRunner
    {
        private const int WorkerThreadLimit_Unlimited = -1;
        private static readonly object _runnerLock = new object();
        private static DedicatedWorkerPool _workerPool;

        private sealed class ParallelWorkExecutionRecord
        {
            public readonly int threadID;
            public readonly long elapsedTicks;

            public ParallelWorkExecutionRecord(int threadID, long elapsedTicks)
            {
                this.threadID = threadID;
                this.elapsedTicks = elapsedTicks;
            }
        }

        private sealed class ParallelWorkThreadAccumulator
        {
            public readonly int threadID;
            public int workItemCount;
            public long elapsedTicks;

            public ParallelWorkThreadAccumulator(int threadID)
            {
                this.threadID = threadID;
                this.workItemCount = 0;
                this.elapsedTicks = 0;
            }

            public void AddExecution(long elapsedTicks)
            {
                this.workItemCount++;
                this.elapsedTicks += elapsedTicks;
            }
        }

        private sealed class ParallelWorkSession
        {
            private readonly List<Action>[] _assignedWorkItemsByWorker;
            private readonly ManualResetEventSlim _completionEvent;
            private readonly List<Exception> _exceptions;
            private int _remainingWorkerCount;

            public readonly ConcurrentBag<ParallelWorkExecutionRecord> executionRecords;
            public int ActiveWorkerCount { get; }

            public ParallelWorkSession(IList<Action> workItems, int workerThreadCount)
            {
                this.executionRecords = new ConcurrentBag<ParallelWorkExecutionRecord>();
                this._completionEvent = new ManualResetEventSlim(false);
                this._exceptions = new List<Exception>();
                this._assignedWorkItemsByWorker = BuildAssignedWorkItems(workItems, workerThreadCount, out int activeWorkerCount);
                this.ActiveWorkerCount = activeWorkerCount;
                this._remainingWorkerCount = activeWorkerCount;
                if (activeWorkerCount == 0)
                {
                    this._completionEvent.Set();
                }
            }

            public List<Action> GetAssignedWorkItems(int workerIndex)
            {
                return this._assignedWorkItemsByWorker[workerIndex];
            }

            public void RecordException(Exception exception)
            {
                lock (this._exceptions)
                {
                    this._exceptions.Add(exception);
                }
            }

            public void NotifyWorkerCompleted()
            {
                if (Interlocked.Decrement(ref this._remainingWorkerCount) == 0)
                {
                    this._completionEvent.Set();
                }
            }

            public void WaitForCompletion()
            {
                this._completionEvent.Wait();
            }

            public void ThrowIfFailed()
            {
                lock (this._exceptions)
                {
                    if (this._exceptions.Count == 0)
                    {
                        return;
                    }
                    if (this._exceptions.Count == 1)
                    {
                        ExceptionDispatchInfo.Capture(this._exceptions[0]).Throw();
                    }
                    throw new AggregateException(this._exceptions);
                }
            }

            private static List<Action>[] BuildAssignedWorkItems(IList<Action> workItems, int workerThreadCount, out int activeWorkerCount)
            {
                activeWorkerCount = Math.Min(workItems.Count, workerThreadCount);
                List<Action>[] assignedWorkItemsByWorker = new List<Action>[workerThreadCount];
                if (activeWorkerCount == 0)
                {
                    return assignedWorkItemsByWorker;
                }

                int nextWorkItemIndex = 0;
                for (int workerIndex = 0; workerIndex < activeWorkerCount; workerIndex++)
                {
                    List<Action> assignedWorkItems = new List<Action>();
                    assignedWorkItems.Add(workItems[nextWorkItemIndex]);
                    assignedWorkItemsByWorker[workerIndex] = assignedWorkItems;
                    nextWorkItemIndex++;
                }

                int assignedWorkerOffset = 0;
                while (nextWorkItemIndex < workItems.Count)
                {
                    int workerIndex = assignedWorkerOffset % activeWorkerCount;
                    assignedWorkItemsByWorker[workerIndex].Add(workItems[nextWorkItemIndex]);
                    assignedWorkerOffset++;
                    nextWorkItemIndex++;
                }
                return assignedWorkItemsByWorker;
            }
        }

        private sealed class DedicatedWorkerPool : IDisposable
        {
            private readonly DedicatedWorker[] _workers;
            private readonly UIntPtr[] _processorAffinities;

            public DedicatedWorkerPool(IList<UIntPtr> processorAffinities)
            {
                this._processorAffinities = new UIntPtr[processorAffinities.Count];
                this._workers = new DedicatedWorker[processorAffinities.Count];
                for (int workerIndex = 0; workerIndex < processorAffinities.Count; workerIndex++)
                {
                    UIntPtr processorAffinity = processorAffinities[workerIndex];
                    this._processorAffinities[workerIndex] = processorAffinity;
                    this._workers[workerIndex] = new DedicatedWorker(workerIndex, processorAffinity);
                }
            }

            public bool Matches(IList<UIntPtr> processorAffinities)
            {
                if (this._processorAffinities.Length != processorAffinities.Count)
                {
                    return false;
                }
                for (int i = 0; i < this._processorAffinities.Length; i++)
                {
                    if (this._processorAffinities[i] != processorAffinities[i])
                    {
                        return false;
                    }
                }
                return true;
            }

            public void Execute(IList<Action> workItems, bool generateParallelWorkReport, out ParallelWorkReport parallelWorkReport)
            {
                ParallelWorkSession session = new ParallelWorkSession(workItems, this._workers.Length);
                Stopwatch totalStopwatch = null;
                if (generateParallelWorkReport)
                {
                    totalStopwatch = Stopwatch.StartNew();
                }
                for (int workerIndex = 0; workerIndex < session.ActiveWorkerCount; workerIndex++)
                {
                    this._workers[workerIndex].Start(session, session.GetAssignedWorkItems(workerIndex));
                }
                session.WaitForCompletion();
                if (generateParallelWorkReport)
                {
                    totalStopwatch.Stop();
                }
                session.ThrowIfFailed();
                if (generateParallelWorkReport)
                {
                    parallelWorkReport = CreateReport(workItems.Count, totalStopwatch.Elapsed, session.executionRecords);
                }
                else
                {
                    parallelWorkReport = null;
                }
                return;
            }

            public void Dispose()
            {
                foreach (DedicatedWorker worker in this._workers)
                {
                    worker.Dispose();
                }
            }
        }

        private sealed class DedicatedWorker : IDisposable
        {
            private readonly object _stateLock;
            private readonly UIntPtr _processorAffinity;
            private readonly AutoResetEvent _workAvailableEvent;
            private readonly ManualResetEventSlim _initializedEvent;
            private readonly Thread _thread;
            private ParallelWorkSession _currentSession;
            private List<Action> _assignedWorkItems;
            private Exception _initializationException;
            private bool _isDisposed;
            private volatile bool _disposeRequested;

            public DedicatedWorker(int workerIndex, UIntPtr processorAffinity)
            {
                this._stateLock = new object();
                this._processorAffinity = processorAffinity;
                this._workAvailableEvent = new AutoResetEvent(false);
                this._initializedEvent = new ManualResetEventSlim(false);
                this._currentSession = null;
                this._assignedWorkItems = null;
                this._initializationException = null;
                this._isDisposed = false;
                this._disposeRequested = false;
                this._thread = new Thread(this.Run);
                this._thread.IsBackground = true;
                this._thread.Name = "ParallelWorkRunner_Worker_" + workerIndex;
                this._thread.Start();
                this._initializedEvent.Wait();
                if (!(this._initializationException is null))
                {
                    this.Dispose();
                    ExceptionDispatchInfo.Capture(this._initializationException).Throw();
                }
            }

            public void Start(ParallelWorkSession session, List<Action> assignedWorkItems)
            {
                lock (this._stateLock)
                {
                    this._currentSession = session;
                    this._assignedWorkItems = assignedWorkItems;
                }
                this._workAvailableEvent.Set();
            }

            public void Dispose()
            {
                if (this._isDisposed)
                {
                    return;
                }
                this._disposeRequested = true;
                this._workAvailableEvent.Set();
                this._thread.Join();
                this._workAvailableEvent.Dispose();
                this._initializedEvent.Dispose();
                this._isDisposed = true;
            }

            private void Run()
            {
                bool hasThreadAffinity = false;
                try
                {
                    ParallelWorkThreadAffinityPlatform.BeginWorkerThreadAffinity(this._processorAffinity);
                    hasThreadAffinity = ParallelWorkThreadAffinityPlatform.SupportsHardwareThreadAffinity;
                    this._initializedEvent.Set();
                }
                catch (Exception exception)
                {
                    this._initializationException = exception;
                    this._initializedEvent.Set();
                    return;
                }

                try
                {
                    while (true)
                    {
                        this._workAvailableEvent.WaitOne();
                        if (this._disposeRequested)
                        {
                            return;
                        }

                        ParallelWorkSession session;
                        List<Action> assignedWorkItems;
                        lock (this._stateLock)
                        {
                            session = this._currentSession;
                            assignedWorkItems = this._assignedWorkItems;
                            this._currentSession = null;
                            this._assignedWorkItems = null;
                        }
                        if (session is null || assignedWorkItems is null || assignedWorkItems.Count == 0)
                        {
                            continue;
                        }

                        try
                        {
                            foreach (Action workItem in assignedWorkItems)
                            {
                                try
                                {
                                    ExecuteWorkItem(workItem, session.executionRecords);
                                }
                                catch (Exception exception)
                                {
                                    session.RecordException(exception);
                                }
                            }
                        }
                        finally
                        {
                            session.NotifyWorkerCompleted();
                        }
                    }
                }
                finally
                {
                    if (hasThreadAffinity)
                    {
                        ParallelWorkThreadAffinityPlatform.EndWorkerThreadAffinity();
                    }
                }
            }
        }

        public static void Run(IList<Action> workItems, int workerThreadLimit, bool generateParallelWorkReport, out ParallelWorkReport parallelWorkReport)
        {
            if (workItems is null || workItems.Count == 0)
            {
                parallelWorkReport = generateParallelWorkReport ? new ParallelWorkReport(0, TimeSpan.Zero, new List<ParallelWorkThreadReport>()) : null;
                return;
            }

            // The barrier (the only lock, which locks the main thread to wait for all worker threads to finish)
            // Formally it was a while loop that keeps spin and block the main thread. Later, I changed the while loop to a low-level lock, because I think a low-level lock will consume a bit less in low-level instructions than a spinning while loop.
            lock(_runnerLock)
            {
                IList<UIntPtr> workerAffinities = ResolveWorkerAffinities(workItems.Count, workerThreadLimit);
                EnsureWorkerPool(workerAffinities);
                _workerPool.Execute(workItems, generateParallelWorkReport, out parallelWorkReport);
                return;
            }
        }

        private static IList<UIntPtr> ResolveWorkerAffinities(int workItemCount, int workerThreadLimit)
        {
            if (workerThreadLimit != WorkerThreadLimit_Unlimited && workerThreadLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workerThreadLimit), "workerThreadLimit must be -1 or a positive integer.");
            }

            List<UIntPtr> availableProcessorAffinities = ParallelWorkThreadAffinityPlatform.GetAvailableProcessorAffinities();
            if (availableProcessorAffinities.Count == 0)
            {
                throw new InvalidOperationException("No CPU hardware thread affinity is available for parallel worker threads.");
            }

            int resolvedWorkerThreadCount;
            if (workerThreadLimit == WorkerThreadLimit_Unlimited)
            {
                resolvedWorkerThreadCount = Math.Max(workItemCount, availableProcessorAffinities.Count);
            }
            else
            {
                int maximumWorkerThreadCount = Math.Min(workItemCount, availableProcessorAffinities.Count);
                resolvedWorkerThreadCount = Math.Min(workerThreadLimit, maximumWorkerThreadCount);
            }

            List<UIntPtr> resolvedProcessorAffinities = new List<UIntPtr>(resolvedWorkerThreadCount);
            for (int workerIndex = 0; workerIndex < resolvedWorkerThreadCount; workerIndex++)
            {
                resolvedProcessorAffinities.Add(availableProcessorAffinities[workerIndex % availableProcessorAffinities.Count]);
            }
            return resolvedProcessorAffinities;
        }

        private static void EnsureWorkerPool(IList<UIntPtr> processorAffinities)
        {
            if (!(_workerPool is null) && _workerPool.Matches(processorAffinities))
            {
                return;
            }

            _workerPool?.Dispose();
            _workerPool = new DedicatedWorkerPool(processorAffinities);
        }

        private static void ExecuteWorkItem(Action workItem, ConcurrentBag<ParallelWorkExecutionRecord> executionRecords)
        {
            Stopwatch workItemStopwatch = Stopwatch.StartNew();
            int threadID = Environment.CurrentManagedThreadId;
            try
            {
                workItem();
            }
            finally
            {
                workItemStopwatch.Stop();
                executionRecords.Add(new ParallelWorkExecutionRecord(threadID, workItemStopwatch.ElapsedTicks));
            }
        }

        private static ParallelWorkReport CreateReport(int workItemCount, TimeSpan totalElapsedTime, IEnumerable<ParallelWorkExecutionRecord> executionRecords)
        {
            Dictionary<int, ParallelWorkThreadAccumulator> threadAccumulators = new Dictionary<int, ParallelWorkThreadAccumulator>();
            foreach (ParallelWorkExecutionRecord executionRecord in executionRecords)
            {
                ParallelWorkThreadAccumulator threadAccumulator;
                if (!threadAccumulators.TryGetValue(executionRecord.threadID, out threadAccumulator))
                {
                    threadAccumulator = new ParallelWorkThreadAccumulator(executionRecord.threadID);
                    threadAccumulators.Add(executionRecord.threadID, threadAccumulator);
                }
                threadAccumulator.AddExecution(executionRecord.elapsedTicks);
            }

            List<ParallelWorkThreadReport> threadReports = new List<ParallelWorkThreadReport>();
            foreach (ParallelWorkThreadAccumulator threadAccumulator in threadAccumulators.Values)
            {
                threadReports.Add(new ParallelWorkThreadReport(threadAccumulator.threadID, threadAccumulator.workItemCount, threadAccumulator.elapsedTicks));
            }
            threadReports.Sort((first, second) => first.threadID.CompareTo(second.threadID));
            return new ParallelWorkReport(workItemCount, totalElapsedTime, threadReports);
        }
    }
}
