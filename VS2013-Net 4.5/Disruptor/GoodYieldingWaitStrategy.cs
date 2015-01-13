using System;
using System.Diagnostics;
using System.Threading;

namespace Disruptor
{
    /// <summary>
    /// Yielding strategy that uses a Thread.Sleep(0) for <see cref="IEventProcessor"/>s waiting on a barrier
    /// after an initially spinning.
    /// 
    /// This strategy is a good compromise between performance and CPU resource without incurring significant latency spikes.
    /// </summary>
    public sealed class GoodYieldingWaitStrategy : IWaitStrategy
    {
        private const int MaxSpinTries = 2;

        /// <summary>
        /// Wait for the given sequence to be available
        /// </summary>
        /// <param name="sequence">sequence to be waited on.</param>
        /// <param name="cursor">Ring buffer cursor on which to wait.</param>
        /// <param name="dependents">dependents further back the chain that must advance first</param>
        /// <param name="barrier">barrier the <see cref="IEventProcessor"/> is waiting on.</param>
        /// <returns>the sequence that is available which may be greater than the requested sequence.</returns>
        public long WaitFor(long sequence, Sequence cursor, Sequence[] dependents, ISequenceBarrier barrier)
        {
            long availableSequence;
            var counter = 0;

            if (dependents.Length == 0)
            {
                while ((availableSequence = cursor.Value) < sequence) // volatile read
                {
                    counter = ApplyWaitMethod(barrier, counter);
                }
            }
            else
            {
                while ((availableSequence = Util.GetMinimumSequence(dependents)) < sequence)
                {
                    counter = ApplyWaitMethod(barrier, counter);
                }
            }

            return availableSequence;
        }

        /// <summary>
        /// Wait for the given sequence to be available with a timeout specified.
        /// </summary>
        /// <param name="sequence">sequence to be waited on.</param>
        /// <param name="cursor">cursor on which to wait.</param>
        /// <param name="dependents">dependents further back the chain that must advance first</param>
        /// <param name="barrier">barrier the processor is waiting on.</param>
        /// <param name="timeout">timeout value to abort after.</param>
        /// <returns>the sequence that is available which may be greater than the requested sequence.</returns>
        /// <exception cref="AlertException">AlertException if the status of the Disruptor has changed.</exception>
        public long WaitFor(long sequence, Sequence cursor, Sequence[] dependents, ISequenceBarrier barrier, TimeSpan timeout)
        {
            long availableSequence;
            var counter = 0;
            var sw = Stopwatch.StartNew();

            if (dependents.Length == 0)
            {
                while ((availableSequence = cursor.Value) < sequence) // volatile read
                {
                    counter = ApplyWaitMethod(barrier, counter);
                    if (sw.Elapsed > timeout)
                    {
                        break;
                    }
                }
            }
            else
            {
                while ((availableSequence = Util.GetMinimumSequence(dependents)) < sequence)
                {
                    counter = ApplyWaitMethod(barrier, counter);
                    if (sw.Elapsed > timeout)
                    {
                        break;
                    }
                }
            }

            return availableSequence;
        }

        /// <summary>
        /// Signal those <see cref="IEventProcessor"/> waiting that the cursor has advanced.
        /// </summary>
        public void SignalAllWhenBlocking()
        {
        }

        private static int ApplyWaitMethod(ISequenceBarrier barrier, int counter)
        {
            const int SLEEP1_INTERVAL = 128;
            const int SLEEP0_INTERVAL = 8;
            int yiled_cnt;
            barrier.CheckAlert();

            if (counter >= MaxSpinTries)
            {
                yiled_cnt = counter - MaxSpinTries;
                ++counter;
                if ((yiled_cnt % SLEEP1_INTERVAL) == (SLEEP1_INTERVAL - 1))
                {
                    Thread.Sleep(1);
                }
                else if ((yiled_cnt % SLEEP0_INTERVAL) == (SLEEP0_INTERVAL - 1))
                {
                    Thread.Sleep(0);
                }
                else
                {
                    if (!Thread.Yield())
                    {
                        Thread.SpinWait(1);
                    }
                }
                ++counter;
            }
            else
            {
                ++counter;
                Thread.SpinWait(1);
            }

            return counter;
        }
    }
}
