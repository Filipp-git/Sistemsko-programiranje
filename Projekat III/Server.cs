using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Projekat3.Actors;
using Projekat3.Models;
using Akka.Configuration;   // za akka dispatcher!

namespace Projekat3;

// Prima HTTP zahtev sa imenom klijenta i vrši dalje akcije
public class Server
{
    // Glavni kontejner i okruzenje za sve aktore
    private ActorSystem? _actorSystem;

    // Glavni aktor, odnosno jedini za kog mi (aplikacija) znamo, 
    // on posle kreira pojedinacne
    private IActorRef? _bookCoordinator;

    // Osluskuje zahteve klijenta
    private HttpListener? _listener;
    private bool _isRunning = true;

    // pomoćna metoda za obradu izuzetaka pri obradi zahteva
    private async Task ProcessRequest(HttpListenerContext context)
    {
        try
        {
            await HandleRequest(context);   // već je asinhrona
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Greška pri obradi zahteva: {ex}");
        }
    }

    public async Task StartAsync()
    {
        // Pokretanje Akka sistema: Kreiranje aktora okruzenja i glavnog aktora!
        // dodat akka dispatcher, konfiguracija se preuzima iz akka.conf fajla
        var configText = File.ReadAllText("akka.conf");
        var config = ConfigurationFactory.ParseString(configText);

        _actorSystem = ActorSystem.Create("BookSystem", config);
        // Nalazi se na vrhu hijerarhije
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
                var context = await _listener.GetContextAsync();
                // _ = Task.Run(() => HandleRequest(context)); // Svaki zahtev ide na slobodnu nit

                // očuvan paralelizam u obradi zahteva, bez kreiranja dodatnih niti
                _ = ProcessRequest(context);
            }
            catch (HttpListenerException) when (!_isRunning)
            {
                // ovde dolazimo kad se pozove Stop() dok je GetContextAsync() u toku (gašenje servera)
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        // razdvajamo (i prikazujemo) zahtev klijenta...
        var request = context.Request;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] HTTP {request.HttpMethod} {request.RawUrl}");
        // ...od onoga što mu šaljemo na kraju
        var response = context.Response;

        // browser automatski traži, ignorišemo
        if (request.RawUrl == "/favicon.ico")
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        var pathParts = request.Url?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Ocekivana ruta: /books/ime_autora + ono ispred
        if (pathParts?.Length == 2 && pathParts[0] == "books")
        {
            // formatiranje ispisa zahteva u konzoli
            // (npr. %20 u zahtevu prikazuje kao razmak)
            var author = Uri.UnescapeDataString(pathParts[1]);
            try
            {
                // Saljemo upit Koordinatoru (Timeout 5 sekundi)
                // http zahtev postaje nova akka poruka
                var result = await _bookCoordinator.Ask<ProcessingResult>(
                    new GetCurrentStateRequest(author),
                    TimeSpan.FromSeconds(5)
                );

                // Formatiramo odgovor u json i prikazujemo u browseru!
                var json = JsonSerializer.Serialize(result);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.OutputStream.Write(buffer, 0, buffer.Length);

                // prikazujemo uspešan response
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Zahtev '{author}' uspešno obrađen.");
            }
            catch (TimeoutException)
            {
                // Dolazi ovde u slucaju da niko od aktorane generise rezultat u roku od 5s
                response.StatusCode = 504; // Gateway Timeout ako ruter zakaze
                // prikazujemo i timeout response
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Zahtev '{author}' istekao.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Zahtev '{author}' neuspešan.");
                Console.WriteLine($"[SERVER ERROR] Izuzetak: {e}");
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
        Console.WriteLine("\n[SHUTDOWN] Graceful Shutdown u toku...");

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