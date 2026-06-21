using System.Threading.Tasks;
using Projekat3;

namespace Projekat3
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var server = new Server();
            await server.StartAsync();
        }
    }
}