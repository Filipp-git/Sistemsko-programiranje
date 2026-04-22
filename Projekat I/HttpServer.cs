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

        //Da bi smo implementirali ,,Gracefull Shutdown":
        //Pratimo broj trenutno aktivnih niti (zahteva)
        //Kako se koji zahtev prihvati broj trenutno aktivnih zahteva se povecava
        //Kako se koji zahtev zavsri broj se smanjuje
        //Pozivom metode Stop(), vodicemo racuna da svi zahtevi koji su pokrenuti,
        //pre poziva metode Stop(), a nisu zavreseni, budu uspesno privedi kraju
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

            //Console.WriteLine($"Server is listening on port: {_port}");
            //Console.WriteLine($"Root folder: {_rootPath}");

            Logger.Log($"Server is listening on port: {_port}");
            Logger.Log($"Root folder: {_rootPath}");

            while(_isRunning)
            {
                try
                {
                    //Prihvatanje konekcije
                    //Blokirajuca je metoda, koja ceka da neko zahteva usluge servera
                    //context sadrzi 2 dela => Request i Response:
                    //Request predstavlja zahtev klijenta,
                    //Respone predstavlja odgovor servera.
                    HttpListenerContext context = _listener.GetContext();

                    _activeRequests.AddCount(); //Povecavamo broj aktivnih zahteva/niti
                    Logger.Log("New client request received");

                    //Uzmi jednu nit iz thread pool-a
                    //Nit izvrsava funkciju HandleRequest
                    //Ulazni parametar funkcije - context
                    ThreadPool.QueueUserWorkItem(HandleRequest, context);
                }
                catch(Exception ec)
                {
                    //Console.WriteLine($"Error: {ec.Message}");
                    Logger.Log($"Error in listener: {ec.Message}", "ERROR");
                }
            }
        }
        private void HandleRequest(object request)
        {
            //Posto funkcoja u QueueUserWorkItem kao ulazni parametar zahteva object,
            //moramo da vrsimo kastovanje nazad u HttpListenerContext
            var context = (HttpListenerContext)request;

            //Pokrecemo tajmer koji ce da eveidentira koliko je niti bilo potrebno vremena da obavi zahtev
            //To ce nam mozda biti zgodno da vidimo koliko je brze kada se procita iz kesa, odnosno kada imam kes promasaj
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // http://localhost:5050/test.txt => ovde mi uzimamo sadrzaj posle znaka "/", sto je ime fajla koji obadjujemo
                string fileName = context.Request.Url.AbsolutePath.TrimStart('/');

                Logger.Log($"Request started for file: {fileName}");

                if(string.IsNullOrEmpty(fileName))
                {
                    Logger.Log($"Empty file name in request!", "WARNING");
                    SendErrorResponse(context, "Please define file name in URL request!", HttpStatusCode.BadRequest);
                    return;
                }

                byte[] responseData = _fileConverter.ProcessFile(fileName);
                string extension = Path.GetExtension(fileName).ToLower();

                if(extension == ".bin")
                {
                    //Binarni fajl smo pretvorili u Base64 tekst, pa kazemo browseru da je to tekst
                    context.Response.ContentType = "text/plain; charset=utf-8";
                }
                else if(extension == ".txt")
                {
                    //Tekst smo pretvorili u binarne podatke, pa saljemo kao stream
                    context.Response.ContentType = "application/octet-stream";
                    //Eksplicitno kazemo browser-u da se fajl preuzme sa ekstenzijom .bin
                    context.Response.AddHeader("Content-Disposition", "attachment; filename=" + Path.GetFileNameWithoutExtension(fileName) + ".bin");
                }
                else
                {
                    context.Response.ContentType = "text/plain";
                }

                //Slanje odgovora
                context.Response.ContentLength64 = responseData.Length;
                context.Response.StatusCode = (int)HttpStatusCode.OK;

                using(var output = context.Response.OutputStream)
                {
                    output.Write(responseData, 0, responseData.Length);
                }

                //Console.WriteLine($"File: {fileName}");
                Logger.Log($"File succesfully processed: {fileName}!");

            }
            catch(FileNotFoundException ec)
            {
                Logger.Log($"File not found: {ec.Message}", "ERROR");
                SendErrorResponse(context, ec.Message, HttpStatusCode.NotFound);
            }
            catch(Exception ec)
            {
                //Console.WriteLine(ec.Message);
                Logger.Log($"Error on server side: {ec.Message}", "ERROR");
                SendErrorResponse(context, "Server error!", HttpStatusCode.InternalServerError);
            }
            finally
            {
                stopwatch.Stop();

                Logger.Log($"Request finished in {stopwatch.ElapsedMilliseconds} ms!");

                _activeRequests.Signal(); //Nit obavestava da se zavrsila obradu zahteva (smanjuje se broj trenutno aktivnih zahteva)
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
            catch(Exception ec)
            {
                //Ako slanje greske ne uspe (npr. klijent je u mdjuvremenu zatvorio browser),
                //samo ispisujemo u konzolu servera da ne bi doslo do pucanja aplikacije
                //Console.WriteLine($"Failed to send error response: {ec.Message}");
                Logger.Log($"Failed to send error response: {ec.Message}", "ERROR");
            }
        }

        //gracefull shutdown varijanta metode
        public void Stop() 
        {
            _isRunning = false; 
            _listener.Stop(); //Server vise nece da prihvata nove zahteve

            _activeRequests.Signal(); //Obavestavamo da se i poslednja nit gasi

            //Console.WriteLine("Server is closing in 1 second max!");
            Logger.Log("Server is closing in 1 second max!"); //Da li je to previse malo vremena?
            _activeRequests.Wait(1000); //Definise maksimalno vreme za koje server bi trebao da se ugasi, ali moze da se ugasi i dosta ranije
            //Console.WriteLine("Server shutdown completed!");
            Logger.Log("Server shutdown completed!");
        }


    }
}