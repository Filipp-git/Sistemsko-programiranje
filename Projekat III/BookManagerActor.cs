using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Projekat3.Models;
using Projekat3.Services;

namespace Projekat3.Actors;

public class BookManagerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly BookService _bookService = new(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });

    // interno stanje aktora
    private readonly string _currentAuthor;
    private List<BookDetails> _cachedBooks = [];
    private int _totalBooksCount = 0;

    private bool _isDataInitialized = false;

    private IDisposable? _rxSubscription;

    // http zahtev na čekanju
    private IActorRef? _waitingHttpRequester;

    public BookManagerActor(string author)
    {
        _currentAuthor = author.ToLower();
        _log.Info($"Konstruktor BookManagerActor: {author}");

        // poečetak toka
        Receive<StartPeriodicFetch>(start =>
        {
            if (_rxSubscription != null) return;

            _log.Info($"[TAJMER] Rx pokrenut za: {_currentAuthor}");

            // referenca obavezno prekopirana van callback f-je
            var self = Self;

            // rx emituje podatke kao poruke aktorima
            _rxSubscription =
                _bookService.WatchBooks(_currentAuthor, start.Interval)
                   .Subscribe(books =>
                    {
                        _log.Info($"[RX NIT] {Thread.CurrentThread.ManagedThreadId}");
                        self.Tell(new BooksFetched(books));
                    });
        });

        // HTTP zahtev
        Receive<GetCurrentStateRequest>(_ =>
        {
            if (_isDataInitialized)
            {
                _log.Info($"[AKKA DISPATCHER NIT - HTTP] {Thread.CurrentThread.ManagedThreadId}");
                Sender.Tell(BuildResult());
                return;
            }
            _waitingHttpRequester = Sender;
        });

        // aktori primaju poruke
        Receive<BooksFetched>(msg =>
        {
            _log.Info($"[AKKA DISPATCHER NIT - PROCES] {Thread.CurrentThread.ManagedThreadId}");

            var books = msg.Books;
            _log.Info($"Broj učitanih knjiga uz Rx: {msg.Books.Count}.");

            var task = Task.Run(() =>
            {
                var processed = books.Select(b =>
                {
                    var text = !string.IsNullOrWhiteSpace(b.Description)
                        ? b.Description
                        : b.Title;

                    var clean = Regex.Replace(text, "<.*?>", string.Empty);

                    var words = clean.Split(new[] { ' ', '.', ',', '!', '?', ';', ':', '-', '(', ')', '"' },
                        StringSplitOptions.RemoveEmptyEntries);

                    return new BookDetails(
                        b.Title,
                        words.Count(w => w.Length > 0 && char.IsUpper(w[0])),
                        words.Select(w => w.ToLower()).Distinct().Count()
                    );
                })
                .OrderByDescending(x => x.UniqueWordsCount)
                .ToList();

                return new ProcessingResult(books.Count, processed);
            });

            // izlaz taska ide direktno u mailbox primaoca, nakon završetka taska
            task.PipeTo(
                Self,
                success: result => new ProcessedResultReady(result),
                failure: ex => new ProcessingFailed(ex)
                );
        });

        Receive<ProcessingFailed>(m =>
        {
            _log.Error(m.Exception, "Obrada neuspešna.");

            if (_waitingHttpRequester != null)
            {
                // Status.Failure je akka poruka: javlja pošiljaocu da je obrada neuspešna
                _waitingHttpRequester.Tell(new Status.Failure(m.Exception));

                _waitingHttpRequester = null;
            }
        });

        // kraj obrade unutar aktora
        Receive<ProcessedResultReady>(msg =>
        {
            // ažuriranje internog stanja aktora
            _totalBooksCount = msg.Result.TotalBooks;
            _cachedBooks = msg.Result.Books;
            _isDataInitialized = true;

            _log.Info($"Ažuriran keš autora '{_currentAuthor}': {_cachedBooks.Count} obrađenih knjiga.");

            var result = BuildResult();

            // odgovori http zahtevu koji čeka (ako postoji)
            _waitingHttpRequester?.Tell(result);
            _waitingHttpRequester = null;
        });
    }

    private ProcessingResult BuildResult() => new(_totalBooksCount, _cachedBooks);

    // messages
    public record BooksFetched(List<BookData> Books);
    public record ProcessedResultReady(ProcessingResult Result);

    // dodat akka dispatcher iz akka.conf
    public static Props Props(string author)
         => Akka.Actor.Props.Create(() => new BookManagerActor(author))
        .WithDispatcher("akka.actor.book-dispatcher");
}