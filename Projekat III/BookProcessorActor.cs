using Akka.Actor;
using Projekat3.Models;

namespace Projekat3.Actors;

// Dobija sirove podatke o knjigama (opis)
// nad kojim se primenjuje poslovna logika
public class BookProcessorActor : UntypedActor
{
    /*protected override void OnReceive(object message)
    {
        // Aktor prima listu objekata tipa (naslov, opis)
        if (message is List<BookData> books)
        {
            // Broji broj reci koje pocinju velikim slovom
            // kao i broj jedinstvnih reci
            var processed = books.Select(b => {
                var words = b.Description?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                return new BookDetails(
                    b.Title,
                    words.Count(w => w.Length > 0 && char.IsUpper(w[0])),
                    words.Distinct().Count()
                );
            })
            .OrderByDescending(x => x.CapitalizedWordsCount)
            .ToList();

            // Vraca se niz objekata tipa (naslov, detalji o tom naslovu)
            Sender.Tell(new ProcessingResult(books.Count, processed));
        }
    }*/
    protected override void OnReceive(object message)
    {
        if (message is List<BookData> books)
        {
            // Da bi imali pregled u konzoli
            // Primetio sam da ne broji kako treba
            // reci koje pocijnu velikim slovom D:
            foreach(var b in books)
            {
                Console.WriteLine($"Knjiga: {b.Title}, Opis: {b.Description}");
            }

            var processed = books.Select(b => {
                // Uklanjanje viska karaktera
                var rawDescription = b.Description ?? "";
                var cleanDescription = System.Text.RegularExpressions.Regex.Replace(rawDescription, "<.*?>", string.Empty);
                
                // Splitovanje teksta na reci
                var words = cleanDescription.Split(new[] { ' ', '.', ',', '!', '?', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
                
                return new BookDetails(
                    b.Title,
                    words.Count(w => w.Length > 0 && char.IsUpper(w[0])), // Broj reci sa velikim slovom
                    words.Select(w => w.ToLower()).Distinct().Count()     // Broj jedinstvenih reci
                );
            })
            .OrderByDescending(x => x.CapitalizedWordsCount)
            .ToList();

            Sender.Tell(new ProcessingResult(books.Count, processed));
        }
    }

    protected override void PreStart()
    {
        Console.WriteLine("BookProcessorActor: Pokrećem se i pripremam za rad...");
        base.PreStart();
    }

    protected override void PostStop()
    {
        Console.WriteLine("BookProcessorActor: Gasim se i oslobađam resurse.");
        base.PostStop();
    }

    public static Props Props() => Akka.Actor.Props.Create<BookProcessorActor>();
}