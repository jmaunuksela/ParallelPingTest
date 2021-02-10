using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ParallelPingTest01
{
    class Program
    {
        const int VK_RETURN = 0x0D;
        const int WM_KEYDOWN = 0x100;
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, int wParam, int lParam);
        static void Main(string[] args)
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CancellationTokenSource cts;
            uint maxConcurrency = default(uint);
            uint numRequests = default(uint);
            List<string> hostNameList = new List<string>();
            Func<Action<string>, string, bool> readUserInput = (inputOperation, prompt) => {
                ConsoleColor curColor = Console.ForegroundColor;
                bool exitLoop;

                do
                {
                    Console.Write(prompt + ": ");

                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;
                        string userInput = Console.ReadLine();
                        if (string.IsNullOrEmpty(userInput))
                        {
                            return false;
                        }
                        else
                        {
                            inputOperation(userInput);
                        }

                        exitLoop = true;
                    }
                    catch (FormatException)
                    {
                        exitLoop = false;
                    }
                    catch (OverflowException ex)
                    {
                        exitLoop = false;
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                    finally
                    {
                        Console.ForegroundColor = curColor;
                    }
                } while (!exitLoop);

                return true;
            };

            Func<Func<Action<string>, string, bool>, bool> readList = (userInput) => {
                for (int i = 0; i < numRequests; i++)
                {
                    if (!userInput((input) => hostNameList.Add(input), $"Enter host name or address {i + 1}/{numRequests}"))
                    {
                        return false;
                    }
                }

                return true;
            };

            Console.WriteLine("Enter empty line to exit the program at any time...");
            Console.WriteLine();
            while (
                readUserInput((inputString) => {
                    maxConcurrency = uint.Parse(inputString);
                    if (maxConcurrency > int.MaxValue)
                    {
                        throw new OverflowException();
                    }
                }, $"Enter the maximum number of concurrently executing ping requests (max {int.MaxValue})")
                &&
                readUserInput((inputString) => numRequests = uint.Parse(inputString), "Enter the number of ping requests to be made")
                &&
                readList(readUserInput))
            {
                object pingStatusLock = new object();
                object userInputLock = new object();
                cts = new CancellationTokenSource();

                Console.WriteLine("Pinging... press any key to cancel.");
                Task.Run(() => {
                    try
                    {
                        Console.ReadKey(true);
                    }
                    catch (InvalidOperationException)
                    {

                    }

                    lock (userInputLock)
                    {
                        if (cts != null)
                        {
                            cts.Cancel();
                            Console.WriteLine("Ping canceled.");
                        }
                    }
                });

                try
                {
                    Parallel.ForEach(hostNameList, new ParallelOptions { MaxDegreeOfParallelism = (int)maxConcurrency, CancellationToken = cts.Token }, (hostName, parallelLoopState) =>
                    {
                        Ping ping = new Ping();
                        int cursorLeft;
                        int cursorTop;
                        lock (pingStatusLock)
                        {
                            Console.Write($"Task #{Task.CurrentId} pinging host {hostName}... ");
                            cursorLeft = Console.CursorLeft;
                            cursorTop = Console.CursorTop;
                            Console.WriteLine();
                        }

                        if (cts.Token.IsCancellationRequested)
                        {
                            parallelLoopState.Stop();
                        }

                        PingReply reply = null;
                        bool exceptionThrown = false;

                        try
                        {
                            var pingTask = ping.SendPingAsync(hostName);
                            reply = pingTask.Result;

                            if (pingTask.IsCanceled)
                            {
                                parallelLoopState.Stop();
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (pingStatusLock)
                            {
                                int tempCursorLeft = Console.CursorLeft;
                                int tempCursorTop = Console.CursorTop;
                                ConsoleColor tempColor = Console.ForegroundColor;
                                Console.CursorLeft = cursorLeft;
                                Console.CursorTop = cursorTop;
                                exceptionThrown = true;

                                Console.ForegroundColor = ConsoleColor.Red;

                                Console.Write($"{ex.GetType().Name} - {ex.Message}{(ex.InnerException.InnerException == null ? "" : ": " + ex.InnerException.InnerException.Message)}...");
                                cursorLeft = Console.CursorLeft;
                                cursorTop = Console.CursorTop;

                                Console.CursorLeft = tempCursorLeft;
                                Console.CursorTop = tempCursorTop;
                                Console.ForegroundColor = tempColor;
                            }
                        }

                        lock (pingStatusLock)
                        {
                            int tempCursorLeft = Console.CursorLeft;
                            int tempCursorTop = Console.CursorTop;
                            ConsoleColor tempColor = Console.ForegroundColor;
                            Console.CursorLeft = cursorLeft;
                            Console.CursorTop = cursorTop;


                            if (reply != null && reply.Status == IPStatus.Success)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                            }

                            if (exceptionThrown)
                            {
                                Console.Write("(");
                            }

                            Console.Write($"{(reply != null ? reply.Status.ToString() : "no reply status")}");

                            if (exceptionThrown)
                            {
                                Console.Write(")");
                            }

                            Console.ForegroundColor = tempColor;
                            Console.CursorLeft = tempCursorLeft;
                            Console.CursorTop = tempCursorTop;
                        }
                    });
                }
                catch (Exception ex) when (ex is AggregateException || ex is OperationCanceledException)
                {

                }

                bool lockTaken = false;
                Monitor.TryEnter(userInputLock, ref lockTaken);


                PostMessage(GetForegroundWindow(), WM_KEYDOWN, VK_RETURN, 0);
                cts.Dispose();
                cts = null;

                hostNameList.Clear();
                Console.WriteLine();
            }
        }
    }
}
