using System.Threading.Tasks;
using Projekat3;

namespace Projekat3
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var server = new Server();

            // Presretanje Ctrl + C komande u konzoli za Graceful Shutdown
            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true; // Sprecava OS da naglo ubije proces
                await server.StopAsync();
                Environment.Exit(0);
            };

            await server.StartAsync();
        }
    }
}