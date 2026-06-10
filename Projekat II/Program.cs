namespace ProjekatII
{
    public class Program
    {
        public static async Task Main()
        {
            // Server smo odvojili u posebnu nit
            // da bi glavna nit ostala aktivna i bila u mogucnosti da reaguje na Enter (gasenje servera)
            HttpServer server = new HttpServer();
            
            // Kreiranje izvora tokena
            var cts = new CancellationTokenSource();

            // Jedini zadatak Main-a jeste da osluskuje Enter
            _ = server.StartAsync(cts.Token);

            // server se gasi pritiskom na Enter
            while (Console.ReadKey().Key != ConsoleKey.Enter) { }

            // Gasenje servera 
            cts.Cancel();
            
            server.Stop();
        }
    }
}