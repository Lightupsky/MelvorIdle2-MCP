// Decompresses Melvor Idle 2 save files (.sav) into their raw binary form.
// The game writes saves as: BrotliStream(SaveWriter binary data).
// .NET's BrotliStream only Flushes (no final close) before the bytes are captured,
// which produces a stream that non-.NET decoders reject near the tail.
// Using .NET's own BrotliStream guarantees a faithful decode.
//
// Usage:
//   SaveDecompress <input.sav> <output.bin>
//   SaveDecompress <input.sav>            (writes <input>.raw next to the save)

using System.IO.Compression;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SaveDecompress <input.sav> [output.bin]");
    return 1;
}

string input = args[0];
string output = args.Length > 1
    ? args[1]
    : Path.ChangeExtension(input, ".raw");

try
{
    using var inStream = File.OpenRead(input);
    using var brawli = new BrotliStream(inStream, CompressionMode.Decompress);
    using var outStream = File.Create(output);
    brawli.CopyTo(outStream);
    Console.WriteLine($"Decompressed '{input}' ({inStream.Length} bytes) -> '{output}' ({outStream.Length} bytes)");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Failed to decompress: {e.Message}");
    return 2;
}
