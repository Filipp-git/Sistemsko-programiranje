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
        private volatile bool _isRunning; // Informacija o tome da li server trenutno radi
        private readonly string _rootPath; // Putanja do root foldera sa fajlovima
        private readonly Cache _cache;
        private readonly FileConverter _fileConverter;
        private readonly int _maxConcurrentRequest; // Ogranicavamo max broj aktivnih zahteva na osnovu CountdownEvent

        //private CancellationTokenSource _cts;

        // Da bi smo implementirali ,,Graceful Shutdown":
        // Pratimo broj trenutno aktivnih zahteva
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
            _listener.Prefixes.Add($"http://localhost:{_port}/"); // Adresa na kojoj server radi

            _fileConverter = new FileConverter(_rootPath);
            _cache = new Cache();
        }

        public async Task StartAsync(CancellationToken token)
        {
            _isRunning = true;
            _listener.Start();

            //_cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            Logger.Log($"Server is listening on port: {_port}");
            Logger.Log($"Root folder: {_rootPath}");
            Logger.Log("Press Enter for server shutdown...");

            // lambda koja se poziva kada je CancellationToken cancelled
            using (token.Register(() => Stop()))
            {
                // sve dok server radi i ne stigne signal tokena da treba da se prestane
                while (_isRunning && !token.IsCancellationRequested)
                {
                    try
                    {
                        HttpListenerContext context = await _listener.GetContextAsync();

                        if (!_isRunning || token.IsCancellationRequested)
                        {
                            await SendErrorResponseAsync(context, "Server is shutting down.", HttpStatusCode.ServiceUnavailable);
                            continue;
                        }

                        // oduzima se 1 za broj aktivnih zahteva, jer brojač kreće od 1
                        int currentActive = _activeRequests.CurrentCount - 1;

                        if (currentActive < _maxConcurrentRequest)
                        {
                            _activeRequests.AddCount();
                            _ = HandleRequestAsync(context, token);
                        }
                        else
                        {
                            Logger.Log($"Request rejected: Max capacity reached ({_maxConcurrentRequest})", "WARNING");
                            await SendErrorResponseAsync(context, "Server busy.", HttpStatusCode.ServiceUnavailable);
                        }
                    }
                    // Umesto da pozivamo blokirajucu metodu za hvatanje zahteva
                    // koristimo asinhronu verziju, kod koje se nit ne blokira dok ne dodje zahtev
                    // Nit pokrene metodu, vrati u ThreadPool ili da radi neki drugi posao, 
                    // a po pristizanju zahteva, vrsi se budjenje, prihvatanje i obrada zahteva
                    //     var contextTask = _listener.GetContextAsync();

                    //     // Proverava se da li je stigao novi zahtev za obradu ili Cancel zahtev od tokena
                    //     var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, token));

                    //     // Ako je stigao zahtev za prekidanjem, izlazimo iz beskonacne petlje
                    //     if (completed != contextTask)
                    //         break;

                    //     // Odnosno stigao je zahtev, pa uzmimamo njega
                    //     HttpListenerContext context = await contextTask;

                    //     if (_activeRequests.CurrentCount <= _maxConcurrentRequest - 1 && _activeRequests.TryAddCount())
                    //     {
                    //         Logger.Log("New client request received");

                    //         _ = Task.Run(async () => await HandleRequestAsync(context, token));
                    //         // Ako bi ovde stavili await, server bi postao sekvencijalan!!
                    //         // Dodatno prosledjujemo sada i token (propagacija)
                    //     }
                    //     else
                    //     {
                    //         // Ako smo dostigli limit, odbijamo klijenta (Service Unavailable)
                    //         Logger.Log($"Request rejected: Maximum capacity of {_maxConcurrentRequest} reached", "WARNING");
                    //         SendErrorResponse(context, "Server is busy. Please try again later!", HttpStatusCode.ServiceUnavailable);
                    //     }

                    // pri pozivu Stop() dolazi se u ovaj blok
                    catch (HttpListenerException) when (!_isRunning)
                    {
                        break;
                    }
                    catch (Exception ec)
                    {
                        // da ne ispisujemo exception pri željenom gašenju servera
                        if (_isRunning)
                            Logger.Log($"Error in listener: {ec.Message}", "ERROR");
                    }
                }
            }
        }

        // Dodate su provere tokena u kriticnim tackama
        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken token)
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

                // Proverava da li je doslo do pojave signala prekida
                token.ThrowIfCancellationRequested();

                // http://localhost:5050/test.txt => ovde mi uzimamo sadrzaj posle znaka "/", sto je ime fajla koji obadjujemo
                string fileName = context.Request.Url!.AbsolutePath.TrimStart('/');

                // browser automatski traži ovaj fajl (ikonicu), ne obrađujemo ga
                if (string.Equals(fileName, "favicon.ico", StringComparison.OrdinalIgnoreCase))
                {
                    //context.Response.Close();
                    return;
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    Logger.Log($"Empty file name in request!", "WARNING");
                    await SendErrorResponseAsync(context, "Please define file name in URL request!", HttpStatusCode.BadRequest);
                    return;
                }

                Logger.Log($"Request started for file: {fileName}");

                // prvo potražimo u kešu, a tek onda konvertujemo po potrebi
                string searchkey = fileName.ToLower();

                // ova klasa više ne vodi računa o tome da li je bio pogodak u kešu
                // sva logika preneta na klasu Cache ovom metodom
                // obavezno se ime fajlova pretvara u mala slova!
                CachedResponse finalResponse = await _cache.GetOrAddSecureAsync(searchkey, async (name) =>
                {
                    // ovde dolazimo ako je promašaj u kešu
                    isCacheMiss = true;
                    Logger.Log($"Cache MISS (Processing): {name}");

                    var processingTimer = Stopwatch.StartNew();

                    Task<byte[]> processingTask = _fileConverter.ProcessFileAsync(name, token);

                    // promena: povratna vrednost ContinueWith ne može biti zanemarena!
                    Task<byte[]> analyticsTask = processingTask.ContinueWith(parentTask =>
                    {
                        if (parentTask.IsFaulted)
                            throw parentTask.Exception!.InnerException!;

                        byte[] dataToProcess = parentTask.Result;
                        string extension = Path.GetExtension(fileName).ToLower();
                        string textContent = Encoding.UTF8.GetString(dataToProcess);

                        if (extension == ".bin")
                        {
                            int characterCount = textContent.Length;
                            Logger.Log($"[ContinueWith Analytics] Number of characters in .bin file: {characterCount}");
                        }
                        else if (extension == ".txt")
                        {
                            string[] words = textContent.Split(
                                new[] { ' ', '\t', '\r', '\n' },
                                StringSplitOptions.RemoveEmptyEntries
                            );
                            Logger.Log($"[ContinueWith Analytics] Number of words in .txt file: {words.Length}");
                        }
                        return dataToProcess;
                    }, TaskContinuationOptions.ExecuteSynchronously);

                    // await na kontinuaciju
                    byte[] fileData = await analyticsTask;
                    processingTimer.Stop();

                    string extension = Path.GetExtension(name).ToLower();
                    string contentType = extension == ".bin" ? "text/plain; charset=utf-8" : "application/octet-stream";
                    string downloadName = extension == ".bin" ? Path.ChangeExtension(name, ".txt") : Path.ChangeExtension(name, ".bin");

                    return new CachedResponse(fileData, contentType, processingTimer.ElapsedMilliseconds, downloadName);
                }, token);

                Logger.Log($"File successfully processed: {fileName}!");

                // ubrzanje se računa pre slanja podataka kroz mrežu
                long logicTime = totalRequestTimer.ElapsedMilliseconds;
                if (!isCacheMiss)
                {
                    double speedup = (double)finalResponse.ProcessingTime / Math.Max(logicTime, 1); //Poredi se vreme koje je bilo potrebno za konverziju, sa vremenom citanja iz memorije (kesa)
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
                await SendErrorResponseAsync(context, "Access denied", HttpStatusCode.Forbidden);
            }
            catch (NotSupportedException ec)
            {
                Logger.Log($"Error on server side: {ec.Message}", "ERROR");
                await SendErrorResponseAsync(context, "Invalid file extension!", HttpStatusCode.NotFound);
            }
            catch (FileNotFoundException ec)
            {
                Logger.Log($"File not found: {ec.Message}", "ERROR");
                await SendErrorResponseAsync(context, ec.Message, HttpStatusCode.NotFound);
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Request cancelled due to server shutdown", "INFO");
            }
            catch (Exception ec)
            {
                Logger.Log($"Error on server side: {ec.Message}", "ERROR");
                await SendErrorResponseAsync(context, "Server error!", HttpStatusCode.InternalServerError);
            }
            finally
            {
                // uključuje i vreme za prenos kroz mrežu
                // gore merimo i poredimo samo performanse našeg servera
                totalRequestTimer.Stop();
                Logger.Log($"Total Request Trip: {totalRequestTimer.ElapsedMilliseconds} ms");

                try { context.Response.Close(); } catch { }

                try
                {
                    _activeRequests.Signal();
                }
                catch (ObjectDisposedException) { }
            }
        }

        private async Task SendErrorResponseAsync(HttpListenerContext context, string message, HttpStatusCode code)
        {
            try
            {
                byte[] errorData = Encoding.UTF8.GetBytes(message);

                context.Response.StatusCode = (int)code;

                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.ContentLength64 = errorData.Length;

                using (Stream output = context.Response.OutputStream)
                {
                    await output.WriteAsync(errorData, 0, errorData.Length);
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
            try
            {
                _activeRequests.Signal();
            }
            catch (ObjectDisposedException) { }

            // čekamo 5s da završe započete operacije
            bool gracefulShutdown = _activeRequests.Wait(5000);

            // ...ali se gasi tek nakon što obradi postojeće zahteve
            try { _listener.Close(); } catch { }
            try { _listener.Stop(); } catch { }

            if (gracefulShutdown)
                Logger.Log("Server shutdown completed gracefully!");
            else
                // može doći do terminiranja nekih zahteva
                Logger.Log("Shutdown timed out.", "WARNING");
        }
    }
}