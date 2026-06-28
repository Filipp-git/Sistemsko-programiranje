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
    private readonly BookService _bookService =
        new(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });

    private readonly string _currentAuthor;

    private List<BookDetails> _cachedBooks = new();
    private int _totalBooksCount = 0;
    private bool _isDataInitialized = false;

    private IDisposable? _rxSubscription;

    // Pending HTTP request (only ONE logical state, no list)
    private IActorRef? _waitingHttpRequester;

    public BookManagerActor(string author)
    {
        _currentAuthor = author.ToLower();
        Console.WriteLine($"CTOR BookManagerActor: {author}");

        // 1) Start stream
        Receive<StartPeriodicFetch>(start =>
        {
            if (_rxSubscription != null) return;

            _log.Info($"[TAJMER] Starting Rx for {_currentAuthor}");

            var self = Self;

            _rxSubscription =
                _bookService.WatchBooks(_currentAuthor, start.Interval)
                    .Subscribe(
                        books => self.Tell(new BooksFetched(books)),
                        ex => _log.Error(ex, "Rx error")
                    );
        });

        // 2) HTTP request
        Receive<GetCurrentStateRequest>(_ =>
        {
            if (_isDataInitialized)
            {
                Sender.Tell(BuildResult());
                return;
            }

            // store only ONE waiter (latest request wins)
            _waitingHttpRequester = Sender;
        });

        // 3) async pipeline via PipeTo
        Receive<BooksFetched>(msg =>
        {
            var books = msg.Books;

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

            // ⭐ THE KEY FIX
            task.PipeTo(Self, success: result => new ProcessedResultReady(result));
        });

        // 4) finalize processing inside actor thread
        Receive<ProcessedResultReady>(msg =>
        {
            _totalBooksCount = msg.Result.TotalBooks;
            _cachedBooks = msg.Result.Books;
            _isDataInitialized = true;

            var result = BuildResult();

            // reply to waiting HTTP request (if any)
            _waitingHttpRequester?.Tell(result);
            _waitingHttpRequester = null;
        });
    }

    private ProcessingResult BuildResult()
        => new(_totalBooksCount, _cachedBooks);

    // messages
    public record BooksFetched(List<BookData> Books);
    public record ProcessedResultReady(ProcessingResult Result);

    public static Props Props(string author)
        => Akka.Actor.Props.Create(() => new BookManagerActor(author));
}