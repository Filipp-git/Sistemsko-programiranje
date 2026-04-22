namespace ProjekatI
{
    public class Program
    {
        public static void Main()
        {
            //Server smo odvojili u posebnu nit
            //Razlog za to je da bi glavna nit ostala aktivna i bila u mogucnosti da reaguje na Enter (gasenje servera)
            HttpServer server = new HttpServer();
            Thread serverThread = new Thread(server.Start);
            serverThread.Start();

            Console.ReadLine(); //Ovo nam daje efekat da se server gasi pritiskom na Enter

            server.Stop();
            serverThread.Join();
        }
    }
}