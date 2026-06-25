using System.Runtime.InteropServices;

namespace Nfs.Server;

internal static class MemoryHelpers
{
    public static byte[] ToArrayOrExactArray(ReadOnlyMemory<byte> memory)
    {
        if (memory.IsEmpty)
        {
            return [];
        }

        if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment)
            && segment.Array is { } array
            && segment.Offset == 0
            && segment.Count == array.Length)
        {
            return array;
        }

        return memory.ToArray();
    }
}
