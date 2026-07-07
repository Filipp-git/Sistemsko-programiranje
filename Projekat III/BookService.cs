using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Net.Http.Json;
using Projekat3.Models;
using System.Reactive.Concurrency;

namespace Projekat3.Services;

// Klasa koja vrsi komunikaciju sa Book API!
public class BookService
{
    // Prihavatmo zahtev klijenta preko browser-a,
    // zatim pozivamo HttpClient da bi poslali zahtev i dobili podatke o knjigama
    private readonly HttpClient _httpClient;

    // powershell: $env:GOOGLE_BOOKS_API_KEY "api-key"
    private static readonly string? apiKey = Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY");

    // Konstruktor
    public BookService(HttpClient httpClient) => _httpClient = httpClient;

    // ne konvertuje Observable u Task u okviru aktora, 
    // već se aktor subscribe-uje na Rx stream
    public IObservable<List<BookData>> WatchBooks(string author, TimeSpan interval)
    {
        // interval garantuje periodično osvežavanje
        return Observable.Interval(interval, TaskPoolScheduler.Default) // rx scheduler dodat
            .StartWith(0L)
            .SelectMany(_ => Observable.FromAsync(() => Fetch(author))
             .Catch((Exception ex) =>
            {
                if (ex is HttpRequestException httpEx)
                {
                    Console.WriteLine($"[Rx Warning] HTTP greška za {author}: status={httpEx.StatusCode}, msg={httpEx.Message}");
                }
                else
                {
                    Console.WriteLine($"[Rx Warning] {ex.GetType().Name} za {author}: {ex.Message}");
                }
                // Vracamo praznu listu, a tajmer nastavlja da radi
                // pa se posle 30s ponovo salje zahtev
                return Observable.Return(new List<BookData>());
            }));
    }

    // razdvojeni konceptualno!
    private async Task<List<BookData>> Fetch(string author)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new Exception("Nepostojeći GOOGLE_BOOKS_API_KEY.");

        // default: povuče uvek 10 knjiga
        var url = $"https://www.googleapis.com/books/v1/volumes?q=inauthor:{Uri.EscapeDataString(author)}&key={apiKey}";

        var response = await _httpClient.GetFromJsonAsync<GoogleBooksResponse>(url);

        // rx radi osnovno mapiranje (naslov, opis)
        return response?.Items?
            .Select(i => new BookData(
                i.VolumeInfo.Title,
                i.VolumeInfo.Description ?? ""
            ))
            .ToList()
            ?? new List<BookData>();
    }
}