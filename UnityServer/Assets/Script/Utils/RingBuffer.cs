using System;
using System.Runtime.InteropServices;
using System.Text;

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
            throw new Exception("newSize 는 0보다 작을 수 없습니다.");
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
    /// 버퍼에 데이터를 추가합니다.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns>쓰기가 성공한 크기를 반환합니다.</returns>
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
    /// 버퍼에서 데이터를 꺼냅니다. 꺼낸 데이터는 삭제됩니다.
    /// </summary>
    /// <param name="buffer">데이터를 쓸 버퍼</param>
    /// <param name="offset">버퍼의 시작 위치</param>
    /// <param name="count">복사할 바이트 수</param>
    /// <returns>복사한 바이트 수를 반환합니다.</returns>
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

    public int Read<T>(ref T _struct)
    {
        int readSize = Marshal.SizeOf(typeof(T));
        if (Length < readSize)
        {
            readSize = Length;
        }
        byte[] temp = new byte[readSize];
        System.Buffer.BlockCopy(this.buffer, front, temp, 0, readSize);
        front += readSize;

        IntPtr ptr = Marshal.AllocHGlobal(readSize);
        Marshal.Copy(temp, 0, ptr, readSize);
        T obj = (T)Marshal.PtrToStructure(ptr, typeof(T));
        Marshal.FreeHGlobal(ptr);
        _struct = obj;

        return readSize;
    }

    public int Read(ref string buffer, int length)
    {
        int readSize = length;
        if (Length < length)
        {
            readSize = Length;
        }
        byte[] temp = new byte[readSize];
        System.Buffer.BlockCopy(this.buffer, front, temp, 0, readSize);
        front += readSize;
        
        buffer = Encoding.UTF8.GetString(temp, 0, length);

        byte[] newBuffer = new byte[bufferSize];
        System.Buffer.BlockCopy(this.buffer, front, newBuffer, 0, Length);
        this.buffer = newBuffer;
        rear = Length;
        front = 0;
        return readSize;
    }

    /// <summary>
    /// 버퍼에서 데이터를 복사합니다. 내부 데이터는 유지됩니다.
    /// </summary>
    /// <param name="buffer">복사할 대상 버퍼</param>
    /// <param name="offset">버퍼의 시작 위치</param>
    /// <param name="count">복사할 바이트 수</param>
    /// <returns>복사한 바이트 수를 반환합니다.</returns>
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
    /// 버퍼에서 데이터를 복사합니다. 내부 데이터는 유지됩니다.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="_struct"></param>
    /// <returns></returns>
    public int Peek<T>(ref T _struct) where T : struct
    {
        int readSize = Marshal.SizeOf(typeof(T));
        if (Length < readSize)
        {
            return 0;
        }
        byte[] temp = new byte[readSize];
        System.Buffer.BlockCopy(this.buffer, front, temp, 0, readSize);

        IntPtr ptr = Marshal.AllocHGlobal(readSize);
        Marshal.Copy(temp, 0, ptr, readSize);
        T obj = (T)Marshal.PtrToStructure(ptr, typeof(T));
        Marshal.FreeHGlobal(ptr);
        _struct = obj;

        return readSize;
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
