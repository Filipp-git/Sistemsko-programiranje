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
            //Ovim se osiguravamo da ce svaka nit, bez prekidanja, da ispise na konzoli
            //To nam mozda nije ni potrebno za mali broj niti, ali za veliki da
            lock(lockObject)
            {
                Console.WriteLine(
                    $"[{DateTime.Now:HH:mm:ss}] " + 
                    $"[Thread] {Thread.CurrentThread.ManagedThreadId} " +
                    $"[{level}] {message}!"
                );
            }
        }
    }
}