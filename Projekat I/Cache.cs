using System.Collections.Concurrent;
using System.Text;

namespace ProjekatI
{
    public class Cache
    {
        // todo: implementirati fifo algoritam, sprečiti stampede

        // ConcurrentDictionary je thread safe, za implementaciju keša:
        // ključ je ime fajla sa ekstenzijom, 
        // vrednosti su klasnog tipa: 
        // - sadržaji fajlova nakon konverzije
        // - ostali parametri koji smanjuju vreme obrade serveru
        private readonly ConcurrentDictionary<string, CachedResponse> _storage = new();

        // koristimo ConcurrentDictionary i za kolekciju semafora:
        // samo niti koje imaju isti ključ (traže isti fajl) mogu da se međusobno blokiraju
        // sprečava cache stampede! implementacija u GetOrAddSecure metodi
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        // parametri samog keša
        // vremensko isticanje - stavke u kešu su validne 5 minuta
        private readonly TimeSpan _ttl = TimeSpan.FromMinutes(5);
        // kapacitet - zgodno je da se menja ovde za testiranje
        private readonly int _capacity = 3;
        // za implementaciju fifo algoritma:
        private readonly ConcurrentQueue<string> _fileOrder = new();

        public bool TryGet(string fileName, out CachedResponse response)
        {
            if (_storage.TryGetValue(fileName, out response))
            {
                // da li je tražena stavka vremenski validna?
                if (DateTime.Now - response.CreatedAt > _ttl)
                {
                    // samo Add izbacuje stavke sa početka reda!
                    _storage.TryRemove(fileName, out _);
                    response = null;
                    return false;
                }
                return true;
            }
            // stavka ne postoji u kešu
            response = null;
            return false;
        }

        public void Add(string fileName, CachedResponse response)
        {
            // ako je stigao novi fajl, proveravamo da li ima mesta u kešu
            if (!_storage.ContainsKey(fileName))
            {
                while (_storage.Count >= _capacity)
                {
                    // izbacivanje elementa sa početka reda i iz keša
                    if (_fileOrder.TryDequeue(out string oldestKey))
                    {
                        if (_storage.TryRemove(oldestKey, out _))
                        {
                            Logger.Log($"[FIFO] Capacity reached ({_capacity}). Evicting oldest: {oldestKey}", "INFO");
                        }
                    }
                }

                _fileOrder.Enqueue(fileName);
            }

            // indeksiranje za ConcurrentDictionary znači AddOrUpdate
            // -> prepiše istekli fajl iz keša novim podacima
            _storage[fileName] = response;
        }

        public String PrintCacheStats()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"\n[Cache] Currently in cache: {_storage.Count} query/queries.");

            // pretvaranje u listu, da se vidi redosled dodavanja
            var currentOrder = _fileOrder.ToList();
            foreach (var key in currentOrder)
            {
                sb.AppendLine($"  -> '{key}'" + (_storage.ContainsKey(key) ? "" : " (Will be removed from cache...)"));
            }
            return sb.ToString();
        }

        public CachedResponse GetOrAddSecure(string fileName, Func<string, CachedResponse> factory)
        {
            // nema potrebe za zaključavanjem, ako je podatak u kešu i nije istekao
            if (TryGet(fileName, out var existingResponse))
            {
                Logger.Log($"Cache HIT for: {fileName}");
                return existingResponse;
            }

            // kreira se lock za određeno ime fajla
            var fileLock = _locks.GetOrAdd(fileName, _ => new SemaphoreSlim(1, 1));

            // početak kritične sekcije!
            fileLock.Wait();
            try
            {
                // šta ako je neka druga nit završila konverziju dok se čekalo da se lock pribavi?
                // prekida se izvršenje metode!
                if (TryGet(fileName, out var delayedResponse))
                {
                    Logger.Log($"Cache HIT for: {fileName}");
                    return delayedResponse;
                }

                // konverzija, poziv FileConverter metoda
                var newResponse = factory(fileName);
                // dodavanje u keš
                Add(fileName, newResponse);
                return newResponse;
            }
            finally
            {
                // kraj kritične sekcije obavezno u finally bloku
                fileLock.Release();
            }
        }
    }
}