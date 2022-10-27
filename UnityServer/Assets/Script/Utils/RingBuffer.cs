using System;
using System.Runtime.InteropServices;

public class RingBuffer
{
    const int DEFAULT_SIZE = 1024;
    byte[] buffer;
    public byte[] Buffer { get { return buffer; } }
    int bufferSize;
    public int BufferSize { get { return bufferSize; } }
    int front;
    int rear;
    public int Front { get { return front; } }
    public int Rear { get { return rear; } }
    public int Length { get { return rear - front; } }
    public int WritableLength { get { return bufferSize - rear; } }

    public RingBuffer()
    {
        this.bufferSize = DEFAULT_SIZE;
        buffer = new byte[this.bufferSize];
        front = 0;
        rear = 0;
    }

    public RingBuffer(int bufferSize)
    {
        this.bufferSize = bufferSize;
        buffer = new byte[this.bufferSize];
        front = 0;
        rear = 0;
    }

    public void Clear()
    {
        front = 0;
        rear = 0;
    }

    public bool IsEmpty()
    {
        return (front == rear);
    }

    public void Resize(int newSize)
    {
        if (newSize <= 0)
        {
            throw new Exception("newSize �� 0���� ���� �� �����ϴ�.");
        }

        int copySize = (newSize < Length) ? newSize : Length;
        byte[] newBuffer = new byte[newSize];
        System.Buffer.BlockCopy(buffer, front, newBuffer, 0, copySize);
        buffer = newBuffer;
        bufferSize = newSize;
        front = 0;
        rear = copySize;
    }

    /// <summary>
    /// ���ۿ� �����͸� �߰��մϴ�.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns>���Ⱑ ������ ũ�⸦ ��ȯ�մϴ�.</returns>
    public int Write(byte[] value, int offset, int count)
    {
        if (WritableLength < count)
        {
            Resize(bufferSize * 2);
            return Write(value, offset, count);
        }
        else
        {
            System.Buffer.BlockCopy(value, offset, buffer, rear, count);
            rear += count;
            return count;
        }

    }

    /// <summary>
    /// ���ۿ��� �����͸� �����ϴ�. ���� �����ʹ� �����˴ϴ�.
    /// </summary>
    /// <param name="buffer">�����͸� �� ����</param>
    /// <param name="offset">������ ���� ��ġ</param>
    /// <param name="count">������ ����Ʈ ��</param>
    /// <returns>������ ����Ʈ ���� ��ȯ�մϴ�.</returns>
    public int Read(ref byte[] buffer, int offset, int count)
    {
        int readSize = count;
        if (Length < count)
        {
            readSize = Length;
        }

        System.Buffer.BlockCopy(this.buffer, front, buffer, offset, readSize);
        front += readSize;

        byte[] newBuffer = new byte[bufferSize];
        System.Buffer.BlockCopy(this.buffer, front, newBuffer, 0, Length);
        this.buffer = newBuffer;
        rear = Length;
        front = 0;
        return readSize;
    }

    /// <summary>
    /// ���ۿ��� �����͸� �����մϴ�. ���� �����ʹ� �����˴ϴ�.
    /// </summary>
    /// <param name="buffer">������ ��� ����</param>
    /// <param name="offset">������ ���� ��ġ</param>
    /// <param name="count">������ ����Ʈ ��</param>
    /// <returns>������ ����Ʈ ���� ��ȯ�մϴ�.</returns>
    public int Peek(ref byte[] buffer, int offset, int count)
    {
        int readSize = count;
        if (Length < count)
        {
            readSize = Length;
        }

        System.Buffer.BlockCopy(this.buffer, front, buffer, offset, readSize);
        return readSize;
    }

    /// <summary>
    /// ���ۿ��� �����͸� �����մϴ�. ���� �����ʹ� �����˴ϴ�.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="_struct"></param>
    /// <returns></returns>
    public int Peek<T>(ref T _struct) where T : class
    {
        int size = Marshal.SizeOf(typeof(T));
        if (Length < size)
        {
            return 0;
        }
        byte[] temp = new byte[size];
        System.Buffer.BlockCopy(this.buffer, front, temp, 0, size);

        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.Copy(buffer, 0, ptr, size);
        T obj = (T)Marshal.PtrToStructure(ptr, typeof(T));
        Marshal.FreeHGlobal(ptr);
        _struct = obj;

        return size;
    }

    public void MoveFront(int bytes)
    {
        front += bytes;
    }

    public void MoveRear(int bytes)
    {
        rear += bytes;
    }

}
