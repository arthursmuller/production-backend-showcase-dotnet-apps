public abstract record Command<TRequest, TResponse> : ICommandQuery<TRequest, TResponse>, ICommandQuery
{
    public abstract Task<TResponse> Handle(TRequest request);
}

public abstract record Query<TRequest, TResponse> : ICommandQuery<TRequest, TResponse>, ICommandQuery
{
    public abstract Task<TResponse> Handle(TRequest request);
}

public abstract record Query<TResponse> : ICommandQuery<TResponse>, ICommandQuery
{
    public abstract Task<TResponse> Handle();
}
