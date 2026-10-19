using Cysharp.Threading.Tasks;
using System.Threading;

namespace HexitEngine 
{
    public interface ISingleton
    {
        bool IsInitialized { get; }

        UniTask InitializeAsync(CancellationToken ct);
    }
}