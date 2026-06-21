using System.Net;
using System.Text;
using System.Text.Json;
using Akka.Actor;
using Akka.Routing;
using Projekat3.Actors;
using Projekat3.Services;

namespace Projekat3;

public class Server
{
    private IActorRef _processorRouter;
    private readonly BookService _bookService = new BookService(new HttpClient());

    public async Task StartAsync()
    {
        // Glavni supervizor!
        var actorSystem = ActorSystem.Create("BookSystem");
        // Router - upravljanje sa 5 aktora
        _processorRouter = actorSystem.ActorOf(BookProcessorActor.Props().WithRouter(new RoundRobinPool(5)), "processor-router");

        // Kako sam ja shvatio: Mi prihvatamo ime autora
        // zatim kreiramo klijenta koji kontaktira api i dobija
        // podatke, koje mi onda dalje obradjujemo i saljemo
        // nazad, preko response, odgovor (ispisuje se u browseru)
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5000/");
        listener.Start();
        Console.WriteLine("Server sluša na http://localhost:5000/...");

        while (true)
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(() => HandleRequest(context)); // Svaki zahtev u novom tasku
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // Ekstrakcija autora iz rute (npr. /books/tolkien)
        var pathParts = request.Url?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        if (pathParts?.Length == 2 && pathParts[0] == "books")
        {
            var author = pathParts[1];
            try
            {
                // Preuzimamo podatke i filtriramo
                var books = await _bookService.FetchAndProcessBooks(author);
                // Saljemo aktoru da obradi i cekamo asinhrono
                var result = await _processorRouter.Ask<object>(books, TimeSpan.FromSeconds(10));

                // Odogovor aktora prebacujemo u json format
                // i saljemo nazad klijentu
                var json = JsonSerializer.Serialize(result);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
            }
        }
        else
        {
            response.StatusCode = 404;
        }
        response.Close();
    }
}