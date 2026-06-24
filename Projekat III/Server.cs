using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Projekat3.Actors;
using Projekat3.Models;

namespace Projekat3;

// Prima HTTP zahtev sa imenom klijenta
// i vrsi dalje akcije
public class Server
{
    // Glavni kontejner i okruzenje za sve aktore
    private ActorSystem _actorSystem;

    // Glavni aktor, odnosno jedini
    // za kog mi (aplikacija) znamo, on
    // posle kreira pojedinacne
    private IActorRef _bookCoordinator; 

    // Osluskuje zahteve klijenta
    private HttpListener _listener;
    private bool _isRunning = true;

    public async Task StartAsync()
    {
        // Pokretanje Akka sistema.
        // Kreiranje aktora okruzenja i glavnog aktora!
        _actorSystem = ActorSystem.Create("BookSystem");
        // Nalazi se na vrhu hijerarhije.
        _bookCoordinator = _actorSystem.ActorOf(BookCoordinatorActor.Props(), "book-coordinator");

        // Pokretanje HTTP Listener-a
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:5000/");
        _listener.Start();
        Console.WriteLine("Server sluša na adresi: http://localhost:5000/books/{autor}");

        while (_isRunning)
        {
            try
            {
                // Da li je potrebno da skinemo Task!?
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context)); // Svaki zahtev ide na slobodnu nit
            }
            catch (HttpListenerException) when (!_isRunning)
            {
                // Izuzetak se bezbedno izostavlja !?
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        // Razdvajamo zahtev klijenta
        var request = context.Request;
        // od onoga sto cemo da mu posaljemo
        // na kraju
        var response = context.Response;

        var pathParts = request.Url?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        // Ocekivana ruta: /books/ime_autora + ono ispred
        if (pathParts?.Length == 2 && pathParts[0] == "books")
        {
            var author = pathParts[1];
            try
            {
                // Saljemo upit Koordinatoru (Timeout 5 sekundi)
                var result = await _bookCoordinator.Ask<ProcessingResult>(
                    new GetCurrentStateRequest(author), 
                    TimeSpan.FromSeconds(5)
                );

                // Formatiramo odgovor u json
                // i prikazujemo u browseru!
                var json = JsonSerializer.Serialize(result);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                // Dolazi ovde u slucaju da niko od aktora
                // ne generise rezultat u roku od 5 s
                response.StatusCode = 504; // Gateway Timeout ako ruter zakaze
            }
            catch (Exception)
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

    public async Task StopAsync()
    {
        Console.WriteLine("\n[SHUTDOWN] Pokrećem Graceful Shutdown...");

        // Prestajemo da prihvatamo zahteve!
        _isRunning = false;
        _listener?.Stop();

        if (_actorSystem != null)
        {
            // Terminate() automatski gasi hijerarhiju i okida PostStop metode kod svih aktora
            await _actorSystem.Terminate();
        }
        
        Console.WriteLine("[SHUTDOWN] Akka.NET sistem i HTTP server su uspešno ugašeni.");
    }
}