using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;

namespace ProjekatI
{
    public class HttpServer
    {
        private readonly HttpListener _listener;
        private readonly int _port;
        private bool _isRunning; //Da li nam je potrebna informacija o tome da li server radi?
        private readonly string _rootPath;
        private readonly Cache _cache;
        private readonly FileConverter _fileConverter;

        //Da bi smo implementirali ,,Graceful Shutdown":
        //Pratimo broj trenutno aktivnih niti (zahteva)
        //Kako se koji zahtev prihvati broj trenutno aktivnih zahteva se povecava
        //Kako se koji zahtev zavsri broj se smanjuje
        //Pozivom metode Stop(), vodicemo racuna da svi zahtevi koji su pokrenuti,
        //pre poziva metode Stop(), a nisu zavrseni, budu uspesno privedeni kraju
        private readonly CountdownEvent _activeRequests = new CountdownEvent(1);

        public HttpServer(int port = 5050)
        {
            _port = port;
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

            while (_isRunning)
            {
                try
                {
                    //Prihvatanje konekcije
                    //Blokirajuca je metoda, koja ceka da neko zahteva usluge servera
                    //context sadrzi 2 dela => Request i Response:
                    //Request predstavlja zahtev klijenta,
                    //Respone predstavlja odgovor servera.
                    HttpListenerContext context = _listener.GetContext();

                    // povećavamo broj aktivnih zahteva/niti
                    // Try za dodatnu sigurnost: 
                    // da ne dobijemo exception kad je event već 0, već samo false
                    if (_activeRequests.TryAddCount())
                    {
                        Logger.Log("New client request received");

                        // svaki request ide na zasebnu nit iz thread pool-a
                        // šta ako mnogo njih zatraži fajl proba.txt u isto vreme?
                        // -> svi će da vrše konverziju, upisuju u keš itd.
                        // zato nam treba neki mehanizam za sinhronizaciju u Cache.cs!

                        // nit izvrsava funkciju HandleRequest, ulazni parametar funkcije - context
                        ThreadPool.QueueUserWorkItem(HandleRequest, context);
                    }
                    else
                    {
                        // gašenje servera, svi novi zahtevi koji mogu da pristignu u tom trenutku se odbijaju
                        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                        context.Response.Close();
                    }
                }
                // pri pozivu Stop() dolazi se u ovaj blok
                catch (HttpListenerException) when (!_isRunning)
                {
                    break;
                }
                catch (Exception ec)
                {
                    // da ne ispisujemo exception kad želimo da se server isključi
                    if (_isRunning)
                        Logger.Log($"Error in listener: {ec.Message}", "ERROR");
                }
            }
        }
        private void HandleRequest(object request)
        {
            //Posto funkcija u QueueUserWorkItem kao ulazni parametar zahteva object,
            //moramo da vrsimo kastovanje nazad u HttpListenerContext
            var context = (HttpListenerContext)request;

            //Pokrecemo tajmer koji ce da eveidentira koliko je niti bilo potrebno vremena da obavi zahtev
            //To ce nam mozda biti zgodno da vidimo koliko je brze kada se procita iz kesa, odnosno kada imamo kes promasaj
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // testiranje graceful shutdown-a
                // ako se potraži recimo: http://localhost:5050/proba.txt
                // i odmah pritisne enter za gašenje servera
                // trebalo bi da se fajl preuzme i tek onda server ugasi
                //Logger.Log("Simulation of a large file processing (4s)");
                //Thread.Sleep(4000);

                // http://localhost:5050/test.txt => ovde mi uzimamo sadrzaj posle znaka "/", sto je ime fajla koji obadjujemo
                string fileName = context.Request.Url.AbsolutePath.TrimStart('/');

                // browser automatski traži ovaj fajl, ne obrađujemo ga
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

                // ova klasa više ne vodi računa o tome da li je bio pogodak u kešu
                // sva logika preneta na klasu Cache ovom metodom
                // obavezno se ime fajlova prevara u mala slova!
                CachedResponse finalResponse = _cache.GetOrAddSecure(fileName.ToLower(), (name) =>
                {
                    // This block is protected by the semaphore lock
                    Logger.Log($"Cache MISS (Processing): {name}");

                    // Simulate processing if needed for testing: Thread.Sleep(4000);
                    byte[] data = _fileConverter.ProcessFile(name);
                    string extension = Path.GetExtension(name).ToLower();

                    string contentType;
                    string downloadName = null;

                    if (extension == ".bin")
                    {
                        //Binarni fajl smo pretvorili u Base64 tekst, pa kazemo browseru da je to tekst
                        contentType = "text/plain; charset=utf-8";
                    }
                    else    // extension == ".txt"
                    {
                        //Tekst smo pretvorili u binarne podatke, pa saljemo kao stream
                        contentType = "application/octet-stream";
                        //Eksplicitno kazemo browser-u da se fajl preuzme sa ekstenzijom .bin
                        downloadName = Path.ChangeExtension(name, ".bin");
                    }

                    return new CachedResponse(data, contentType, downloadName);
                });


                context.Response.ContentType = finalResponse.ContentType;

                if (!string.IsNullOrEmpty(finalResponse.DownloadName))
                {
                    context.Response.AddHeader("Content-Disposition", "attachment; filename=" + finalResponse.DownloadName);
                }

                context.Response.ContentLength64 = finalResponse.Data.Length;
                context.Response.StatusCode = (int)HttpStatusCode.OK;


                using (var output = context.Response.OutputStream)
                {
                    output.Write(finalResponse.Data, 0, finalResponse.Data.Length);
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
                stopwatch.Stop();

                Logger.Log($"Request finished in {stopwatch.ElapsedMilliseconds} ms!");

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
                //Ako slanje greske ne uspe (npr. klijent je u mdjuvremenu zatvorio browser),
                //samo ispisujemo u konzolu servera da ne bi doslo do pucanja aplikacije
                Logger.Log($"Failed to send error response: {ec.Message}", "ERROR");
            }
        }

        //graceful shutdown varijanta metode
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            Logger.Log("Shutting down... waiting for active requests to finish.");

            // server više ne prihvata nove zahteve...
            _activeRequests.Signal(); //Obavestavamo da se i poslednja nit gasi

            bool gracefulShutdown = _activeRequests.Wait(5000);

            // ...ali se gasi tek nakon što obradi postojeće
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