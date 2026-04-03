using System;
using System.Threading;

namespace FrozenOCR.Core;

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool HasHandle { get; }

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name: name, createdNew: out var createdNew);
        HasHandle = createdNew;
    }

    public void Dispose()
    {
        try
        {
            if (HasHandle)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}

