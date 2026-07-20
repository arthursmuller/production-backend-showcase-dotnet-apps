namespace Interaction.Application.Providers.Privacy;

public interface IObserverKeyFactory
{
    string Create(long actorId, DateTimeOffset bucketStartUtc);
}
