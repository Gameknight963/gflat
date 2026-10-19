using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.VisualStudio;

// This parser is independent of Concord so malformed/partial debuggee memory
// and display behavior can be tested without launching Visual Studio.
internal sealed class GflatExceptionReport
{
    internal string Type = "gflat exception";
    internal string Message = "";
    internal bool Unhandled;

    internal static GflatExceptionReport? Read(Func<ulong, int, byte[]> read, ulong address)
    {
        byte[] packet = read(address, 48);
        if (packet.Length != 48 || BitConverter.ToUInt64(packet, 0) != 1) return null;
        ulong stage = BitConverter.ToUInt64(packet, 8);
        if (stage != 1 && stage != 2) return null;
        ulong payload = BitConverter.ToUInt64(packet, 16), type = BitConverter.ToUInt64(packet, 24);
        long messageOffset = BitConverter.ToInt64(packet, 32), codeOffset = BitConverter.ToInt64(packet, 40);
        var result = new GflatExceptionReport { Unhandled = stage == 2 };
        if (payload == 0 && type == 0) result.Type = "Native exception";
        if (type != 0) result.Type = CString(read, type);
        if (payload != 0 && messageOffset >= 0 && messageOffset < 1048576 && payload <= ulong.MaxValue - (ulong)messageOffset)
        {
            byte[] pointer = read(checked(payload + (ulong)messageOffset), 8);
            if (pointer.Length == 8 && BitConverter.ToUInt64(pointer, 0) is ulong text && text != 0)
                result.Message = CString(read, text);
        }
        if (payload != 0 && codeOffset >= 0 && codeOffset < 1048576 && payload <= ulong.MaxValue - (ulong)codeOffset)
        {
            byte[] code = read(checked(payload + (ulong)codeOffset), 4);
            if (code.Length == 4) result.Message += (result.Message.Length == 0 ? "" : "\n") + "Code: " + BitConverter.ToInt32(code, 0);
        }
        return result;
    }

    private static string CString(Func<ulong, int, byte[]> read, ulong address)
    {
        var text = new List<byte>();
        while (text.Count < 4096)
        {
            if (address > ulong.MaxValue - (ulong)text.Count) break;
            byte[] chunk = read(checked(address + (ulong)text.Count), Math.Min(64, 4096 - text.Count));
            if (chunk.Length == 0) break;
            foreach (byte value in chunk)
            {
                if (value == 0) return Encoding.UTF8.GetString(text.ToArray());
                text.Add(value);
            }
        }
        return Encoding.UTF8.GetString(text.ToArray());
    }
}
