using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Projekat3.Models;

namespace Projekat3.Actors;

// Supervizor/roditelj svim ostalim aktorima
public class BookCoordinatorActor : ReceiveActor
{
    // Asinhroni logger
    private readonly ILoggingAdapter _log = Context.GetLogger();

    // Drzi podatke o tome koji aktor je odgovoran kod autora
    // (key: ime autora, value: referenca na aktora)
    // => Kreiramo po jedan aktor za autora
    private readonly Dictionary<string, IActorRef> _authorActors = new();

    public BookCoordinatorActor()
    {
        // Ovde dolazimo svaki pur kada se kroz broswer posalje zahtev
        Receive<GetCurrentStateRequest>(request =>
        {
            // kreiramo validnu aktor putanju
            var actorName = request.Author.Replace(" ", "_").ToLower();

            var child = Context.Child(actorName);
            if (child is Nobody)
            {
                // Create a dedicated manager actor for this specific author
                child = Context.ActorOf(BookManagerActor.Props(request.Author), actorName);

                _log.Info("Child created");

                // Instruct Rx.NET via the actor to start pulling periodically (e.g., every 30 seconds)
                child.Tell(new StartPeriodicFetch(request.Author, TimeSpan.FromSeconds(30)));

                _log.Info("StartPeriodicFetch sent");

                // Since it's a brand new stream, let's give Rx.NET 2-3 seconds to fetch the data
                // before replying to the client, or reply immediately with a "loading/empty" state.
                // For a reliable response on the first hit, forward the request to the child.
                child.Forward(request);
                _log.Info("Forward sent");
            }
            else
            {
                // The actor already exists and has cached data from Rx.NET! Just forward the request.
                child.Forward(request);
            }
            // Ako aktor za ovog autora ne postoji u stanju koordinatora, kreiramo ga u letu
            // if (!_authorActors.ContainsKey(author))
            // {
            //     _log.Info($"[KOORDINATOR] Prvi put detektovan autor '{author}'. Pokrećem namenski BookManagerActor...");

            //     // Kreiranje child aktora sa unikatnim imenom
            //     IActorRef childActor = Context.ActorOf(BookManagerActor.Props(), $"manager-{author}");

            //     // Automatski mu zadajemo periodicno osvežavanje na 60 sekundi (smanjiti!?)
            //     childActor.Tell(new StartPeriodicFetch(author, TimeSpan.FromSeconds(60)));

            //     _authorActors.Add(author, childActor);
            // }

            // // Prosledjujemo (Forward) poruku detetu. Izvorna HTTP nit (Sender) ostaje nepromenjena.
            // _authorActors[author].Forward(request);
        });
    }

    // Predefinisan Props! 
    public static Props Props() => Akka.Actor.Props.Create<BookCoordinatorActor>();
}