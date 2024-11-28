using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sample_taskscheduler_limit
{
    public class LimitedConcurrencyLevelTaskScheduler
        : TaskScheduler
    {

        public LimitedConcurrencyLevelTaskScheduler(int maxDegreeOfParallelism)
        {
            if (maxDegreeOfParallelism < 1) throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "maxDegreeOfParallelism must be greater than zero.");
            _maxDegreeOfParallelism = maxDegreeOfParallelism;
        }



        public override int MaximumConcurrencyLevel => _maxDegreeOfParallelism;

        private readonly int _maxDegreeOfParallelism;
        private int _delegatesQueuedOrRunning;


        private readonly LinkedList<Task> _tasks = new();
        private readonly object _tasksLock = new();

        protected override IEnumerable<Task>? GetScheduledTasks()
        {
            //API仕様通り、ロックが取れたらタスク一覧を返し、ロックが取れないならNotSupportedExceptionを投げる。
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_tasksLock, ref lockTaken);
                if (lockTaken)
                {
                    return _tasks.ToArray();
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_tasksLock);
            }

        }

        protected override void QueueTask(Task task)
        {
            //Taskを追加した上で、同時実行スレッド数を超えていなければNotifyThreadPoolOfPendingWorkへ進む。
            //NotifyThreadPoolOfPendingWorkは、Taskが空になるまで実行を続けるある種のループ構造となるので、これが何個同時に走るかをコントロールすることで、実質的にスレッドプールの同時処理上限をコントロールできる。
            lock (_tasksLock)
            {
                _tasks.AddLast(task);
                if (_delegatesQueuedOrRunning < _maxDegreeOfParallelism)
                {
                    _delegatesQueuedOrRunning++;
                    NotifyThreadPoolOfPendingWork();
                }
            }

        }

        private void NotifyThreadPoolOfPendingWork()
        {
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                Task? item;
                lock (_tasksLock)
                {
                    //タスクが空になったら_tasks.Firstはnull
                    item = _tasks.First?.Value;
                    if (item is null)
                    {
                        //空になったらこの一連のNotifyThreadPoolOfPendingWorkのループを終了し、ループの数をデクリメントする。
                        _delegatesQueuedOrRunning--;
                        return;
                    }
                    _tasks.RemoveFirst();
                }

                base.TryExecuteTask(item);
                NotifyThreadPoolOfPendingWork();
            }, null);
        }


        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            //インライン実行については、現在回しているループの数が上限以下であれば、空きがあるということで実行する。そうではない場合はfalseで断る。
            if (_delegatesQueuedOrRunning < _maxDegreeOfParallelism)
            {
                if (taskWasPreviouslyQueued)
                {
                    if (TryDequeue(task))
                    {
                        return base.TryExecuteTask(task);
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return base.TryExecuteTask(task);
                }
            }
            else
            {
                return false;
            }
        }

        protected override bool TryDequeue(Task task)
        {
            lock (_tasksLock)
            {
                return _tasks.Remove(task);
            }
        }

    }
}
