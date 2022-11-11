using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

public static class MemoryHelper
{
    public const int MEMORY_CAPACITY = 1024;

    static BinaryFormatter FORMATTER = new BinaryFormatter();

    static MemoryStream STREAM = new (MEMORY_CAPACITY);

    public static byte[] BUFFER_I = new byte[MemoryHelper.MEMORY_CAPACITY];

    public static byte[] BUFFER_S = new byte[MemoryHelper.MEMORY_CAPACITY];

    /// <summary>
    /// Generic aren't supported so this deserialized the data to get our struct back
    /// </summary>
    public static T ReadArray<T>(byte[] data)
    {
        STREAM.Position = 0;
        STREAM.Write(data.AsSpan());
        STREAM.Position = 0;
        return (T)FORMATTER.Deserialize(STREAM);
    }

    /// <summary>
    /// Generic aren't supported so this serialized the data to bytes
    /// </summary>
    public static void WriteArray<I>(I data, byte[] target)
    {
        STREAM.Position = 0;
        FORMATTER.Serialize(STREAM, data);

        STREAM.Position = 0;
        STREAM.Read(target.AsSpan());
    }
}
