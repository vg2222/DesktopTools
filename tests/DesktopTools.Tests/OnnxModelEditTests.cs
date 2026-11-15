using System.IO;
using System.Text;
using DesktopTools.Core;

/// <summary>Keeping only named graph outputs of an ONNX model, without touching any other byte of the graph.</summary>
internal static class OnnxModelEditTests
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static byte[] Varint(ulong value) { var bytes = new List<byte>(); do { byte b = (byte)(value & 0x7F); value >>= 7; if (value != 0) b |= 0x80; bytes.Add(b); } while (value != 0); return bytes.ToArray(); }
        static byte[] Field(int number, byte[] payload) => [.. Varint((ulong)(number << 3 | 2)), .. Varint((ulong)payload.Length), .. payload];
        static byte[] VarintField(int number, ulong value) => [.. Varint((ulong)(number << 3)), .. Varint(value)];
        static byte[] Output(string name) => Field(12, Field(1, Encoding.UTF8.GetBytes(name)));

        byte[] node = Field(1, Encoding.UTF8.GetBytes(new string('n', 300))); // a 300-byte payload needs a two-byte length
        byte[] unknown = Field(99, [1, 2, 3]);
        byte[] graph = [.. node, .. Output("last_hidden_state"), .. Output("attn.0"), .. unknown, .. Output("attn.1")];
        byte[] model = [.. VarintField(1, 8), .. Field(7, graph), .. VarintField(8, 17)];

        byte[] edited = OnnxModelEdit.KeepOutputs(model, ["last_hidden_state"]);
        byte[] expectedGraph = [.. node, .. Output("last_hidden_state"), .. unknown];
        byte[] expected = [.. VarintField(1, 8), .. Field(7, expectedGraph), .. VarintField(8, 17)];
        Check(edited.SequenceEqual(expected), "Only the kept output remains; other bytes are unchanged");
        Check(OnnxModelEdit.OutputNames(edited).SequenceEqual(["last_hidden_state"]) && OnnxModelEdit.OutputNames(model).Count == 3, "Output names");
        Check(OnnxModelEdit.KeepOutputs(model, ["last_hidden_state", "attn.0", "attn.1"]).SequenceEqual(model), "Keeping everything is a byte copy");

        void Rejects(byte[] bytes, IReadOnlyCollection<string> keep, string what)
        {
            try { OnnxModelEdit.KeepOutputs(bytes, keep); throw new Exception(what + " accepted"); }
            catch (InvalidDataException) { }
        }
        Rejects(model, ["missing"], "No kept output");
        Rejects(model[..^5], ["last_hidden_state"], "Truncated model");
        Rejects([.. VarintField(1, 8)], ["last_hidden_state"], "Model without a graph");
        Rejects([0x0B, 0x00], ["last_hidden_state"], "Group wire type");
    }
}
