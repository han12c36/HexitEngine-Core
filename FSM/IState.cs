public interface IState<TContext, TState> where TState : struct, System.Enum
{
    TState State { get; }

    void Enter(TContext ctx);
    void Update(TContext ctx, float dt);
    void LateUpdate(TContext ctx) { }
    void Exit(TContext ctx);
}
