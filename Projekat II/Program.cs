namespace ProjekatI
{
    public class Program
    {
        public static async Task Main()
        {
            // Server smo odvojili u posebnu nit
            // da bi glavna nit ostala aktivna i bila u mogucnosti da reaguje na Enter (gasenje servera)
            HttpServer server = new HttpServer();
            
            Task.Run(() => server.Start());

            // server se gasi pritiskom na Enter
            while (Console.ReadKey().Key != ConsoleKey.Enter) { }

            // Gasenje servera
            //server.Stop();
            server.Stop();
        }
    }
}