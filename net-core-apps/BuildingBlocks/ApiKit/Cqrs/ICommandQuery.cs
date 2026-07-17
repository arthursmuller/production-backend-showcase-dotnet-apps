public interface ICommandQuery<TRequest, TResponse>
{
     abstract Task<TResponse> Handle(TRequest request);
}
public interface ICommandQuery<TResponse>
{
    abstract Task<TResponse> Handle();
}
public interface ICommandQuery { }