
using System.Collections.Concurrent;
using System.Collections.Generic;

public class MemoryPool<T> where T : new()
{
    private const int DefaultSize = 10;
    private ConcurrentStack<T> stack;
    private int allocSize;

    public int Size => stack.Count;

    public MemoryPool()
    {
        this.stack = new ConcurrentStack<T>();
        Initialize(DefaultSize);
    }

    public MemoryPool(int initSize)
    {
        this.stack = new ConcurrentStack<T>();
        Initialize(initSize);
    }

    public void Initialize(int initSize)
    {
        Preallocate(initSize);
    }

    public T Allocate()
    {
        if(stack.Count == 0)
        {
            Preallocate(allocSize);
        }

        T memory;
        stack.TryPop(out memory);

        return memory;
    }

    public void Free(T memory)
    {
        stack.Push(memory);
    }

    private void Preallocate(int size)
    {
        for (int i = 0; i < size; i++)
        {
            T item = new T();
            stack.Push(item);
        }
        allocSize += size;
    }
}
