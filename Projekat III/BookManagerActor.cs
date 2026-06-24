using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Event;
using Projekat3.Models;
using Projekat3.Services;

namespace Projekat3.Actors;

// Aktor po autoru!
// ReceiveActor => Ponsanje se definise u okviru konstruktora
public class BookManagerActor : ReceiveActor
{
    // Asinhroni logger
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly BookService _bookService = new(new HttpClient());
    
    // Stanje Aktora => Ne moramo da vodimo racuna
    // o konkurentnosti, jer nikada vise od 1 niti ne
    // pristupa internom stanju aktora!
    private List<BookDetails> _cachedBooks = new();
    private int _totalBooksCount = 0;
    
    // Referenca na tajmer (definise vreme reseta/ponovnog povlacenja podataka),
    // da bi smo mogli da ga uklonimo prilikom brisanja
    private ICancelable _scheduleCancelable;

    // Ime trenutnog aktora, odnosno autora!
    private string _currentAuthor;

    // Ako pukne mreža/API, primenjujemo Resume kako ne bismo uništili instancu i obrisali keš
    protected override SupervisorStrategy SupervisorStrategy()
    {
        // Strategija u slucaju greske!
        // Ukoliko aktor napravi aktore, i desi se
        // greska, strategija se primenjuje samo na 
        // aktoru kod koga je doslo do greske
        return new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                // U slucaju mrezne greske ne radimo nista,
                // da ne bi smo izgubili stanje aktora!                          
                if (ex is HttpRequestException)
                {
                    _log.Error(ex, $"[GREŠKA] Problem sa API-jem za autora {_currentAuthor}. Nastavljam rad sa starim kešom.");
                    return Directive.Resume; 
                }
                return Directive.Restart;
            });
    }

    public BookManagerActor()
    {
        // Konfigurisanje Akka Schedulera
        // Kada BookCoordinatorActor kreira aktora, odmah poziva
        // ovu metodu, da bi aktoru dodelio ime i interval
        Receive<StartPeriodicFetch>(start =>
        {
            if (_scheduleCancelable != null) return; 

            _currentAuthor = start.Author;
            _log.Info($"[TAJMER] Inicijalizovan periodični pulling za '{_currentAuthor}' na svakih {start.Interval.TotalSeconds}s.");

            _scheduleCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                initialDelay: TimeSpan.FromSeconds(0), // Krece odmah pri prvom zahtevu
                interval: start.Interval, // Posle ovoliko vremena, ponovo povuci podatke!
                receiver: Self,
                message: new FetchTick(),
                sender: Self
            );
        });

        // Periodicno povlacenje i analiza teksta
        // Async => zbog mreznog poziva
        ReceiveAsync<FetchTick>(async _ =>
        {
            if (string.IsNullOrEmpty(_currentAuthor)) return;

            _log.Info($"[PULL] Povlačim sveže podatke sa Google API-ja za autora: {_currentAuthor}...");
            
            try
            {
                // Poziv API-a
                var rawBooks = await _bookService.FetchAndProcessBooks(_currentAuthor);
                
                // Kompleksna obrada i filtriranje unutar aktora
                // Kao i cuvanje stanja
                _cachedBooks = rawBooks.Select(b =>
                {
                    // Ako nema opisa, uzmi naslov kao tekst za analizu
                    string tekstZaAnalizu = !string.IsNullOrWhiteSpace(b.Description) ? b.Description : b.Title;

                    // Ciscenje HTML tagova
                    var cleanText = Regex.Replace(tekstZaAnalizu, "<.*?>", string.Empty);
                    
                    // Razbijanje na reci
                    var words = cleanText.Split(new[] { ' ', '.', ',', '!', '?', ';', ':', '-', '(', ')', '"' }, StringSplitOptions.RemoveEmptyEntries);

                    return new BookDetails(
                        b.Title,
                        words.Count(w => w.Length > 0 && char.IsUpper(w[0])),
                        words.Select(w => w.ToLower()).Distinct().Count()
                    );
                })
                .OrderByDescending(x => x.CapitalizedWordsCount) // Sortiranje
                .ToList();

                _totalBooksCount = rawBooks.Count;
                _log.Info($"[USPEH] Keš uspešno ažuriran za autora '{_currentAuthor}'. Ukupno knjiga: {_totalBooksCount}");
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Neuspešno osvežavanje podataka za {_currentAuthor}.");
                throw; // Propagacija ka nadzornoj strategiji
            }
        });

        // Odgovor na HTTP server upit (Ekspresno vracanje stanja bez blokiranja)
        Receive<GetCurrentStateRequest>(_ =>
        {
            _log.Info($"[HTTP ZAHTEV] Serviram keširane podatke za autora: {_currentAuthor}");
            Sender.Tell(new ProcessingResult(_totalBooksCount, _cachedBooks));
        });
    }

    protected override void PreStart() => _log.Info($"Aktor {Self.Path.Name} uspešno podignut.");

    protected override void PostStop()
    {
        _scheduleCancelable?.Cancel(); // Gasimo tajmer da ne curi memorija
        _log.Warning($"Aktor {Self.Path.Name} je ugašen. Tajmer zaustavljen.");
    }

    // Predefinisan Props!
    public static Props Props() => Akka.Actor.Props.Create<BookManagerActor>();
}