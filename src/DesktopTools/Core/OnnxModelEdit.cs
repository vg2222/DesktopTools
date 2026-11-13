using System.IO;
using System.Text;

namespace DesktopTools.Core;

/// <summary>
/// Minimal, in-memory edit of an ONNX model (protocol buffers): keeps only the named graph outputs. Every other byte is copied as is.
/// Used to load the Whisper encoder without its attention outputs, which ONNX Runtime would otherwise compute and keep (docs/ai-models.md).
/// The downloaded file is never changed.
/// </summary>
public static class OnnxModelEdit
{
    private const int ModelGraphField = 7, GraphOutputField = 12, ValueInfoNameField = 1;

    public static byte[] KeepOutputs(byte[] model, IReadOnlyCollection<string> keep)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(keep);
        // First pass: measure, so the result is written once into an array of its exact size (the Whisper encoder is up to 350 MB).
        var fields = Fields(model, 0, model.Length).ToList();
        var graphFields = fields.Where(field => field.Number == ModelGraphField && field.WireType == 2).ToList();
        if (graphFields.Count == 0) throw new InvalidDataException("The model has no graph.");
        if (graphFields.Count > 1) throw new InvalidDataException("The model has more than one graph.");
        var graphField = graphFields[0];
        var items = Fields(model, graphField.PayloadStart, graphField.End)
            .Where(item => item.Number != GraphOutputField || item.WireType != 2 || keep.Contains(Name(model, item.PayloadStart, item.End))).ToList();
        if (!items.Any(item => item.Number == GraphOutputField && item.WireType == 2)) throw new InvalidDataException("None of the requested model outputs exist.");
        long graphLength = items.Sum(item => (long)(item.End - item.Start));
        ulong tag = (ulong)(ModelGraphField << 3 | 2);
        long total = fields.Sum(field => field.Equals(graphField) ? VarintSize(tag) + VarintSize((ulong)graphLength) + graphLength : field.End - field.Start);
        var result = new byte[total];
        int position = 0;
        foreach (var field in fields)
        {
            if (!field.Equals(graphField)) { Copy(field); continue; }
            WriteVarint(result, ref position, tag);
            WriteVarint(result, ref position, (ulong)graphLength);
            foreach (var item in items) Copy(item);
        }
        return result;

        void Copy(Field part) { Buffer.BlockCopy(model, part.Start, result, position, part.End - part.Start); position += part.End - part.Start; }
    }

    /// <summary>The names of the graph outputs, in order.</summary>
    public static IReadOnlyList<string> OutputNames(byte[] model)
    {
        var names = new List<string>();
        foreach (var field in Fields(model, 0, model.Length))
            if (field.Number == ModelGraphField && field.WireType == 2)
                foreach (var item in Fields(model, field.PayloadStart, field.End))
                    if (item.Number == GraphOutputField && item.WireType == 2) names.Add(Name(model, item.PayloadStart, item.End));
        return names;
    }

    private readonly record struct Field(int Number, int WireType, int Start, int PayloadStart, int End)
    {
        public int PayloadLength => End - PayloadStart;
    }

    private static string Name(byte[] data, int start, int end)
    {
        foreach (var field in Fields(data, start, end))
            if (field.Number == ValueInfoNameField && field.WireType == 2) return Encoding.UTF8.GetString(data, field.PayloadStart, field.PayloadLength);
        return "";
    }

    private static List<Field> Fields(byte[] data, int start, int end)
    {
        var fields = new List<Field>();
        int position = start;
        while (position < end)
        {
            int fieldStart = position;
            ulong key = ReadVarint(data, ref position, end);
            int number = (int)(key >> 3), wireType = (int)(key & 7);
            if (number <= 0) throw new InvalidDataException("The model file is not valid.");
            int payloadStart = position;
            switch (wireType)
            {
                case 0: ReadVarint(data, ref position, end); break;
                case 1: position += 8; break;
                case 5: position += 4; break;
                case 2:
                    ulong length = ReadVarint(data, ref position, end);
                    if (length > (ulong)(end - position)) throw new InvalidDataException("The model file is truncated.");
                    payloadStart = position; position += (int)length; break;
                default: throw new InvalidDataException("The model file uses an unsupported encoding.");
            }
            if (position > end) throw new InvalidDataException("The model file is truncated.");
            fields.Add(new Field(number, wireType, fieldStart, payloadStart, position));
        }
        return fields;
    }

    private static ulong ReadVarint(byte[] data, ref int position, int end)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (position >= end) throw new InvalidDataException("The model file is truncated.");
            byte b = data[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new InvalidDataException("The model file is not valid.");
    }

    private static void WriteVarint(byte[] destination, ref int position, ulong value)
    {
        do { byte b = (byte)(value & 0x7F); value >>= 7; if (value != 0) b |= 0x80; destination[position++] = b; } while (value != 0);
    }

    private static int VarintSize(ulong value) { int size = 1; while ((value >>= 7) != 0) size++; return size; }
}
