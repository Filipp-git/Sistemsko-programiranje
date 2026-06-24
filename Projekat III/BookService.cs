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
    // zatim pozivamo HttpClient da bi poslali zahtev
    // i dobili podatke o knjigama
    private readonly HttpClient _httpClient;

    // Verovantno mozemo da ga prebacimo u env fajl
    string apiKey = "AIzaSyDFsEV-cCPLRfBMxTthBdurPr-7da-B8Jw";

    // Konstruktor
    public BookService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<List<BookData>> FetchAndProcessBooks(string author)
    {
        var url = $"https://www.googleapis.com/books/v1/volumes?q=inauthor:{author}&key={apiKey}";

        // IObservable nam omogucava da nad odgovorom
        // primenimo razne transformacije!
        // Select bira knjige koje poseduju informacije.
        // ObsrrveOn - omogucava da se obrada prebaci na pozadinske niti,
        // a da glavna nit ostane slobodna da prihaati nove zahteve.
        return await Observable.FromAsync(() => _httpClient.GetFromJsonAsync<GoogleBooksResponse>(url))
            .Select(res => res?.Items?.Where(i => i.VolumeInfo != null)
                                      .Select(i => new BookData(i.VolumeInfo.Title, i.VolumeInfo.Description ?? ""))
                                      .ToList() ?? new List<BookData>())
            .ObserveOn(TaskPoolScheduler.Default) // Sve obrade/transformacije prebaci
                                                  // na neku drugu nit, ali nemoj da 
                                                  // kreiras drugu, vec iskoristi 
                                                  // postojecu
            .ToTask(); // Pretvaramo nazad u Task, da
                       // aktor moze da uradi await
    }
}