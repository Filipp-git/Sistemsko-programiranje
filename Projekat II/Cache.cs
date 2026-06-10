using System.Collections.Concurrent;
using System.Text;

namespace ProjekatII
{
    //Verzija kesa sa vremenskim isticanjem + sprecavanje kes stampeda
    public class Cache
    {
        // ConcurrentDictionary je thread safe (vise niti moze da cita/pise, bez lock-a), za implementaciju keša:
        // ključ je ime fajla sa ekstenzijom, 
        // vrednosti su klasnog tipa: 
        // - sadržaji fajlova nakon konverzije
        // - ostali parametri koji smanjuju vreme obrade serveru
        private readonly ConcurrentDictionary<string, CachedResponse> _storage = new();

        // koristimo ConcurrentDictionary i objekte za zaključavanje (ne semafore!):
        // samo niti koje imaju isti ključ (traže isti fajl) mogu da se međusobno blokiraju
        // sprečava cache stampede! implementacija u GetOrAddSecure metodi
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        // parametri samog keša
        // vremensko isticanje - stavke u kešu su validne 0.5 minuta
        private readonly TimeSpan _ttl = TimeSpan.FromMinutes(0.5);
        // kapacitet - zgodno je da se menja ovde za testiranje
        private readonly int _capacity = 3;
        // za implementaciju fifo algoritma:
        private readonly ConcurrentQueue<string> _fileOrder = new();

        // iako koristimo konkurentan queue i dictionary, oni ne obezbđuju atomičnost skupa operacija 
        // (u našem slučaju brisanje/ažuriranje stavke u kešu i queue-u)
        private readonly object _evictionLock = new();

        public bool TryGet(string fileName, out CachedResponse? response)
        {
            if (_storage.TryGetValue(fileName, out response!))
            {
                // da li je tražena stavka vremenski validna?
                if (DateTime.Now - response.CreatedAt > _ttl)
                {
                    // samo Add izbacuje stavke sa početka reda!
                    //_storage.TryRemove(fileName, out _);
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
            lock (_evictionLock)
            {
                // cache hit: ažuriramo podatke i ne dodajemo opet u queue!
                // bez obzira da li je u pitanju novi ili istekao fajl
                if (_storage.ContainsKey(fileName))
                {
                    _storage[fileName] = response;
                    return;
                }
                // nema mesta u kešu
                while (_storage.Count >= _capacity)
                {
                    // izbacivanje elementa sa početka reda i iz keša
                    if (_fileOrder.TryDequeue(out string oldestKey))
                    {
                        if (_storage.TryRemove(oldestKey, out _))
                        {
                            Logger.Log($"[FIFO] Capacity reached ({_capacity}). Evicting oldest: {oldestKey}", "INFO");
                            // prekida while, oslobođeno je jedno mesto za upis
                            break;
                        }
                    }
                    else
                    {
                        // ne bi nikada trebalo da dođemo u ovaj deo, ali za svaki slučaj
                        break;
                    }
                }
                // cache miss -> dodavanje novih fajlova u queue i keš takođe deo kritične sekcije
                _storage[fileName] = response;
                _fileOrder.Enqueue(fileName);
            }
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

        // postala asinhrona!
        public async Task<CachedResponse> GetOrAddSecureAsync(string fileName, Func<string, Task<CachedResponse>> factory, CancellationToken token)
        {
            // nema potrebe za zaključavanjem, ako je podatak u kešu i nije istekao
            if (TryGet(fileName, out var existingResponse))
            {
                Logger.Log($"Cache HIT for: {fileName}");
                return existingResponse!;
            }

            // todo: dovoljan je lock po imenu fajla, ne treba nam semafor!
            // ali da li to radi asinhrono?
            var fileLock = _locks.GetOrAdd(fileName, _ => new SemaphoreSlim(1,1));

            // početak kritične sekcije po imenu fajla
            // Sa dodatim tokenom ne moze se beskonacno 
            // cekati da se dobije lock!
            await fileLock.WaitAsync(token);

            try
            {
                // šta ako je neka druga nit završila konverziju dok se čekalo da se lock pribavi?
                // prekida se izvršenje metode!
                if (TryGet(fileName, out var delayedResponse))
                {
                    Logger.Log($"Cache HIT for: {fileName}");
                    return delayedResponse!;
                }
                // konverzija, poziv FileConverter metoda
                var newResponse = await factory(fileName);
                // dodavanje u keš
                Add(fileName, newResponse);
                return newResponse;
            }
            finally
            {
                // kraj kritične sekcije obavezno u finally bloku
                fileLock.Release();
                // da li brišemo semafore?
            }
        }
    }
}