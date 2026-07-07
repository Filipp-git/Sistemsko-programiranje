using System.Threading.Tasks;
using Projekat3;

namespace Projekat3
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var server = new Server();

            // Mehanizam koji će signalizirati Main-u kada je gašenje STVARNO gotovo
            var shutdownTcs = new TaskCompletionSource<bool>();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Spresava OS da naglo ubije proces odmah

                // Pokrećemo gasenje u pozadini, a
                // li NE pozivamo Environment.Exit ovde!
                Task.Run(async () =>
                {
                    try
                    {
                        await server.StopAsync();
                    }
                    finally
                    {
                        // Signaliziramo glavnoj niti 
                        // da je sve bezbedno ugaseno
                        shutdownTcs.SetResult(true);
                    }
                });
            };

            //await server.StartAsync();

            var serverTask = server.StartAsync();

            // Cekamo da se Http beskonacna petlja
            // uspesno privede kraju!
            await serverTask;

            // Cekamo da se aktori terminiraju i
            // pozovu svoje postStop metode!
            await shutdownTcs.Task;

            Console.WriteLine("[MAIN] Aplikacija se uspešno završava.");
        }
    }
}
