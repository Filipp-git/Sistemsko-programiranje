using System;
using System.Threading;

namespace ProjekatI
{
    public static class Logger
    {
        //sinhronizacioni objekat
        private static readonly object lockObject = new object();

        public static void Log(string message, string level = "INFO")
        {
            // Ovim se osiguravamo da ce svaki task, bez prekidanja, da ispise na konzoli
            // To nam mozda nije ni potrebno za mali broj taskova, ali za veliki da
            lock (lockObject)
            {
                // Uzimamo ID stvarne sistemske niti (Managed Thread ID)
                int threadId = Environment.CurrentManagedThreadId;

                // Opciono: Zadržavamo informaciju da li je u pitanju pozadinska nit iz ThreadPool-a
                string threadType = Thread.CurrentThread.IsThreadPoolThread ? "PoolThread" : "MainThread";

                Console.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss}] " +
                    $"[Thread {threadId} ({threadType})] " +
                    $"[{level}] {message}!"
                );
            }
        }
    }
}