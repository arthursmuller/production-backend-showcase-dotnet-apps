public interface ICommandQuery<TRequest, TResponse>
{
    abstract Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
public interface ICommandQuery<TResponse>
{
    abstract Task<TResponse> Handle(CancellationToken cancellationToken);
}
public interface ICommandQuery { }
