using System.Collections.Concurrent;

namespace ProjekatI
{
    public class Cache
    {
        // ConcurrentDictionary je thread safe, ključ je ime fajla sa ekstenzijom, 
        // vrednosti su struktura: 
        // - sadržaji fajlova nakon konverzije
        // - ostali parametri koji smanjuju vreme obrade serveru

        // todo: implementirati vremensko isticanje

        private readonly ConcurrentDictionary<string, CachedResponse> _storage = new();

        public bool TryGet(string fileName, out CachedResponse response)
        {
            return _storage.TryGetValue(fileName, out response);
        }

        public void Add(string fileName, CachedResponse response)
        {
            _storage.TryAdd(fileName, response);
        }

        public void PrintCacheStats()
        {
            Console.WriteLine($"\n[Cache] Currently in cache: {_storage.Count} query/queries.");
            foreach (var key in _storage.Keys)
            {
                Console.WriteLine($"  -> '{key}'");
            }
        }

    }
}