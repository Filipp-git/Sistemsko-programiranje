using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Net;
using System.Text;
using System.Threading;

namespace ProjekatII
{
    public class HttpServer
    {
        private readonly HttpListener _listener;
        private readonly int _port;
        private bool _isRunning; // Informacija o tome da li server trenutno radi
        private readonly string _rootPath; // Putanja do root foldera sa fajlovima
        private readonly Cache _cache;
        private readonly FileConverter _fileConverter;
        private readonly int _maxConcurrentRequest; // Ogranicavamo max broj aktivnih taskova/zahteva na osnovu CountdownEvent
                                                    // Ovo je bolja opcija jer se ThreadPool koristi na nivou cele aplikacije
                                                    // To znaci da bismo u pozadini mogli da imamo neku bibilioteku koja koristi
                                                    // niti iz pool-a i ovim bi smo mogli da je ogranicimo (ako bi smo stavili da je max = 5).
                                                    // Ne diramo sistemske niti, samo ogranicavamo broj zatheva koji obradjujemo.

        // Da bi smo implementirali ,,Graceful Shutdown":
        // Pratimo broj trenutno aktivnih niti (zahteva)
        // Kako se koji zahtev prihvati broj trenutno aktivnih zahteva se povecava
        // Kako se koji zahtev zavsri broj se smanjuje
        // Pozivom metode Stop(), vodicemo racuna da svi zahtevi koji su pokrenuti,
        // pre poziva metode Stop(), a nisu zavrseni, budu uspesno privedeni kraju
        private readonly CountdownEvent _activeRequests = new CountdownEvent(1);

        public HttpServer(int port = 5050, int maxRequests = 100)
        {
            _port = port;
            _maxConcurrentRequest = maxRequests;
            _rootPath = Path.Combine(Directory.GetCurrentDirectory(), "Files"); //Putanja do root foldera
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/"); //Adresa na kojoj server radi

            _fileConverter = new FileConverter(_rootPath);
            _cache = new Cache();
        }

        public void Start()
        {
            _isRunning = true;
            _listener.Start();

            Logger.Log($"Server is listening on port: {_port}");
            Logger.Log($"Root folder: {_rootPath}");
            Logger.Log("Press Enter for server shutdown...");

            // Prebacujemo izvrsenje na neku nit iz ThreadPool - a
            // Omogucava asinhrono prihvatanje zahteva, bez obzira kako je Start metoda pozvana van 
            Task.Run(async () => await ListenAsync());
        }
        // metoda je privatna jer se može pozvati samo iz Start metode, koju poziva Main
        private async Task ListenAsync()
        {
            while (_isRunning)
            {
                try
                {
                    // Umesto da pozivamo blokirajucu metodu za hvatanje zahteva
                    // koristimo asinhronu verziju, kod koje se nit ne blokira dok ne dodje zahtev
                    // Nit pokrene metodu, vrati u ThreadPool ili da radi neki drugi posao, a po pristizanju zahteva
                    // vrsi se budjenje, prihvatanje i obrada zahteva
                    HttpListenerContext context = await _listener.GetContextAsync();

                    if (_activeRequests.CurrentCount <= _maxConcurrentRequest - 1 && _activeRequests.TryAddCount())
                    {
                        Logger.Log("New client request received");

                        _ = Task.Run(async () => await HandleRequestAsync(context));
                        // Ako bi ovde stavili await, server bi postao sekvencijalan!
                    }
                    else
                    {
                        // Ako smo dostigli limit, odbijamo klijenta (Service Unavailable)
                        Logger.Log($"Request rejected: Maximum capacity of {_maxConcurrentRequest} reached", "WARNING");
                        SendErrorResponse(context, "Server is busy. Please try again later!", HttpStatusCode.ServiceUnavailable);
                    }
                }
                // pri pozivu Stop() dolazi se u ovaj blok
                catch (HttpListenerException) when (!_isRunning)
                {
                    break;
                }
                catch (Exception ec)
                {
                    // Da ne ispisujemo exception kad zelimo da se server iskljuci
                    if (_isRunning)
                        Logger.Log($"Error in listener: {ec.Message}", "ERROR");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            // Pokrecemo tajmer koji ce da eveidentira koliko je tasku bilo potrebno vremena da obavi zahtev
            // To ce nam mozda biti zgodno da vidimo koliko je brze kada se procita iz kesa, odnosno kada imamo kes promasaj
            var totalRequestTimer = Stopwatch.StartNew();

            bool isCacheMiss = false;

            try
            {
                // testiranje graceful shutdown-a
                // ako se potraži recimo: http://localhost:5050/proba.txt
                // i odmah pritisne enter za gašenje servera
                // trebalo bi da se fajl preuzme i tek onda server ugasi
                // Logger.Log("Simulation of a large file processing (4s)");
                // Thread.Sleep(4000);

                // http://localhost:5050/test.txt => ovde mi uzimamo sadrzaj posle znaka "/", sto je ime fajla koji obadjujemo
                string fileName = context.Request.Url!.AbsolutePath.TrimStart('/');

                // browser automatski traži ovaj fajl (ikonicu), ne obrađujemo ga
                if (string.Equals(fileName, "favicon.ico", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Close();
                    return;
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    Logger.Log($"Empty file name in request!", "WARNING");
                    SendErrorResponse(context, "Please define file name in URL request!", HttpStatusCode.BadRequest);
                    return;
                }

                Logger.Log($"Request started for file: {fileName}");
                // prvo potražimo u kešu, a tek onda konvertujemo po potrebi
                string searchkey = fileName.ToLower();

                // ova klasa više ne vodi računa o tome da li je bio pogodak u kešu
                // sva logika preneta na klasu Cache ovom metodom
                // obavezno se ime fajlova prevara u mala slova!
                CachedResponse finalResponse = await _cache.GetOrAddSecureAsync(searchkey, async (name) =>
                {
                    // ovde dolazimo ako je promašaj u kešu
                    isCacheMiss = true;
                    Logger.Log($"Cache MISS (Processing): {name}");

                    var processingTimer = Stopwatch.StartNew();
                    byte[] fileData = await _fileConverter.ProcessFileAsync(name);
                    // nakon obrade promašaja, čuvamo proteklo vreme
                    processingTimer.Stop();

                    // kontinuacije pomerene!
                    _ = Task.FromResult(fileData).ContinueWith(async parentTask =>
                {
                    byte[] dataToProcess = parentTask.Result;
                    string extension = Path.GetExtension(fileName).ToLower();

                    // Konverzija
                    string textContent = Encoding.UTF8.GetString(dataToProcess);

                    if (extension == ".bin")
                    {
                        // Za .bin fajl brojimo slova u Base64 tekstu i logujemo u konzolu
                        int characterCount = textContent.Length;
                        Logger.Log($"[ContinueWith Analytics] Number of characters in .bin file: {characterCount}");
                    }
                    else if (extension == ".txt")
                    {
                        // Za .txt fajl brojimo reči i logujemo u konzolu
                        string[] words = textContent.Split(
                            new[] { ' ', '\t', '\r', '\n' },
                            StringSplitOptions.RemoveEmptyEntries
                        );
                        int wordCount = words.Length;
                        Logger.Log($"[ContinueWith Analytics] Number of words in .txt file: {wordCount}");
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);

                    string extension = Path.GetExtension(name).ToLower();
                    string? contentType = null;
                    string? downloadName = null;

                    if (extension == ".bin")
                    {
                        //Binarni fajl smo pretvorili u Base64 tekst, pa kazemo browseru da je to tekst
                        contentType = "text/plain; charset=utf-8";
                        downloadName = Path.ChangeExtension(name, ".txt");
                    }
                    else if (extension == ".txt")
                    {
                        //Tekst smo pretvorili u binarne podatke, pa saljemo kao stream
                        contentType = "application/octet-stream";
                        //Eksplicitno kazemo browser-u da se fajl preuzme sa ekstenzijom .bin
                        downloadName = Path.ChangeExtension(name, ".bin");
                    }

                    return new CachedResponse(fileData, contentType!, processingTimer.ElapsedMilliseconds, downloadName!);
                });

                Logger.Log($"File successfully processed: {fileName}!");

                // ubrzanje se računa pre slanja podataka kroz mrežu
                long logicTime = totalRequestTimer.ElapsedMilliseconds;
                if (!isCacheMiss)
                {
                    double speedup = (double)finalResponse.ProcessingTime / Math.Max(logicTime, 1); //Poredi se vreme koje je bilo potrebno za konverziju, sa vremeno citanja iz memorije (kesa)
                    Logger.Log($"[PERFORMANCES] Cache HIT Speedup: {speedup:F2}x (Original: {finalResponse.ProcessingTime}ms vs Current: {logicTime}ms)");
                }
                else
                {
                    Logger.Log($"[PERFORMANCES] Cache MISS Baseline: {finalResponse.ProcessingTime}ms");
                }

                context.Response.ContentType = finalResponse.ContentType;

                if (!string.IsNullOrEmpty(finalResponse.DownloadName))
                {
                    context.Response.AddHeader("Content-Disposition", "attachment; filename=" + finalResponse.DownloadName);
                }

                context.Response.ContentLength64 = finalResponse.Data.Length;
                context.Response.StatusCode = (int)HttpStatusCode.OK;

                using (var output = context.Response.OutputStream)
                {
                    await output.WriteAsync(finalResponse.Data, 0, finalResponse.Data.Length);
                }

                Logger.Log(_cache.PrintCacheStats());
                Logger.Log($"File successfully processed: {fileName}!");
            }
            catch (UnauthorizedAccessException ec)
            {
                Logger.Log($"Attempt to violate server security: {ec.Message}", "ERROR");
                SendErrorResponse(context, "Access denied", HttpStatusCode.Forbidden);
            }
            catch (NotSupportedException ec)
            {
                Logger.Log($"Error on server side: {ec.Message}", "ERROR");
                SendErrorResponse(context, "Invalid file extension!", HttpStatusCode.NotFound);
            }
            catch (FileNotFoundException ec)
            {
                Logger.Log($"File not found: {ec.Message}", "ERROR");
                SendErrorResponse(context, ec.Message, HttpStatusCode.NotFound);
            }
            catch (Exception ec)
            {
                //Console.WriteLine(ec.Message);
                Logger.Log($"Error on server side: {ec.Message}", "ERROR");
                SendErrorResponse(context, "Server error!", HttpStatusCode.InternalServerError);
            }
            finally
            {
                // uključuje i vreme za prenos kroz mrežu
                // gore merimo i poredimo samo performanse našeg servera
                totalRequestTimer.Stop();
                Logger.Log($"Total Request Trip: {totalRequestTimer.ElapsedMilliseconds} ms");

                _activeRequests.Signal(); //Nit obavestava da se zavrsila obradu zahteva (smanjuje se broj trenutno aktivnih zahteva, čak i ako nešto pođe po zlu)
                context.Response.Close();
            }
        }

        private void SendErrorResponse(HttpListenerContext context, string message, HttpStatusCode code)
        {
            try
            {
                byte[] errorData = Encoding.UTF8.GetBytes(message);

                context.Response.StatusCode = (int)code;

                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = errorData.Length;

                using (Stream output = context.Response.OutputStream)
                {
                    output.Write(errorData, 0, errorData.Length);
                }
            }
            catch (Exception ec)
            {
                // Ako slanje greske ne uspe (npr. klijent je u mdjuvremenu zatvorio browser),
                // samo ispisujemo u konzolu servera da ne bi doslo do pucanja aplikacije
                Logger.Log($"Failed to send error response: {ec.Message}", "ERROR");
            }
            finally
            {
                context.Response.Close();
            }
        }

        // graceful shutdown varijanta metode
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            Logger.Log("Shutting down... waiting for active requests to finish.");

            // server više ne prihvata nove zahteve...
            _activeRequests.Signal(); // Obavestavamo da se i poslednja nit gasi

            bool gracefulShutdown = _activeRequests.Wait(5000);

            // ...ali se gasi tek nakon što obradi postojeće zahteve
            _listener.Stop();
            _listener.Close();

            if (gracefulShutdown)
                Logger.Log("Server shutdown completed gracefully!");
            else
                // može doći do terminiranja nekih zahteva
                Logger.Log("Shutdown timed out.", "WARNING");
        }
    }
}