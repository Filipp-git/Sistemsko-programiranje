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

    public BookCoordinatorActor()
    {
        // Ovde dolazimo svaki pur kada se kroz browser posalje zahtev
        Receive<GetCurrentStateRequest>(request =>
        {
            // kreiramo validnu aktor putanju
            var actorName = request.Author.Replace(" ", "_").ToLower();

            var child = Context.Child(actorName);
            if (child is Nobody)
            {
                // kreira se ManagerActor za novog autora knjiga
                // dodat akka dispatcher!
                child = Context.ActorOf(BookManagerActor.Props(request.Author) .WithDispatcher("akka.actor.book-dispatcher"), actorName);

                _log.Info("Child aktor kreiran.");

                // aktor kaže rx-u da povlači podatke za autora (sa api-ja) na svakih 30 sekundi
                child.Tell(new StartPeriodicFetch(request.Author, TimeSpan.FromSeconds(30)));

                _log.Info("StartPeriodicFetch izvršen.");

                // aktor se kreira pri prvom zahtevu i zahtev se prosledi potomku
                child.Forward(request);
                _log.Info("Forward uspešan.");
            }
            else
            {
                // aktor za zadatog autora već postoji i ima keširane podatke od rx-a
                // zahtev se samo prosleđuje
                child.Forward(request);
            }
        });
    }

    // Predefinisan Props
    public static Props Props() => Akka.Actor.Props.Create<BookCoordinatorActor>();
}