namespace Projekat3.Models;

// record-i su immutable strukture podataka!

// Google API Models:
public record GoogleBooksResponse(List<Item> Items);
public record Item(string Id, VolumeInfo VolumeInfo);
public record VolumeInfo(string Title, string Description);

// Aktor Models i Messagess
public record BookData(string Title, string Description);
public record BookDetails(string Title, int CapitalizedWordsCount, int UniqueWordsCount);
public record ProcessingResult(int TotalBooks, List<BookDetails> Books);
public record GoogleBookDetailResponse(VolumeInfo VolumeInfo);

// Poruke:
public record StartPeriodicFetch(string Author, TimeSpan Interval);
public record FetchTick;
public record GetCurrentStateRequest(string Author);

// kontrolne poruke: kada je serija podataka počela?
// kada se završila?
// govori aktoru kada da sortira knjige
public record StreamStartedMessage(string Author);
public record StreamFinishedMessage(string Author, int TotalBooks);