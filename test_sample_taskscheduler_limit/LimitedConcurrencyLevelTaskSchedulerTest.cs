using System.Collections.Concurrent;
using System.Diagnostics;
using NUnit.Framework;
using sample_taskscheduler_limit;

namespace test_sample_taskscheduler_limit
{
    [TestFixture]
    public class LimitedConcurrencyLevelTaskSchedulerTest
    {
        private void TraceOut(string message)
        {
            Console.WriteLine(message);
        }

        /// <summary>
        /// 同時実行数の制限が正しく働いていることをテストする。
        /// </summary>
        /// <param name="maxDegreeOfParallelism">同時に実行可能なスレッド数。実行環境で動かせるスレッド数より小さい値にしないと、テストが成立しない点に注意。</param>
        /// <param name="threadCount">このテストで実行するスレッド数。この数のうち、maxDegreeOfParallelismの数が同時実行される。maxDegreeOfParallelismより大きい値にしないと、テストが成立しない点に注意。</param>
        [TestCase(1, 10)]
        [TestCase(2, 10)]
        [TestCase(1, 4)]
        [TestCase(4, 4)]
        [TestCase(4, 9)]
        public async Task 同時実行数別の動作テスト(int maxDegreeOfParallelism, int threadCount)
        {
            var target = new LimitedConcurrencyLevelTaskScheduler(maxDegreeOfParallelism);
            var taskFactory = new TaskFactory(target);

            var sw = new Stopwatch();
            sw.Start();

            //スレッドを立てて処理開始する程度であれば1秒のウエイトと比べて十分に処理時間が短い、と考えられることを利用したテスト。
            //立てたスレッドの中で1秒を待機することで、数百ミリ秒以上の待ちが発生するときには同時実行が制限されていると判断できる。
            ConcurrentBag<TimeSpan> resultsBuf = new();
            List<Task> tasks = new();
            for (int i = 0; i < threadCount; i++)
            {
                var task = taskFactory.StartNew(() =>
                {
                    var swElapsed = sw.Elapsed;
                    resultsBuf.Add(swElapsed);
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                });
                tasks.Add(task);

            }

            await Task.WhenAll(tasks);

            var results = resultsBuf.OrderBy(d => d).ToArray();

            for (int i = 1; i < threadCount; i++)
            {
                //実行スレッド総数がmaxDegreeOfParallelismの倍数の時に限り、すでに立っているスレッドの1秒ウエイトにより同時実行数制限に引っかかって1秒近くの待ちが発生するはずなので、それを判定する
                if (i % maxDegreeOfParallelism == 0)
                {
                    Assert.That(Math.Abs((results[i] - results[i - 1]).TotalMilliseconds), Is.GreaterThanOrEqualTo(500));
                }
                else
                {
                    Assert.That(Math.Abs((results[i] - results[i - 1]).TotalMilliseconds), Is.LessThan(500));
                }

            }

        }

    }
}
