using CUE4Parse.UE4;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Exceptions;

namespace CUE4Parse.GameTypes.FN.Objects;

// Fortnite 42.40 FBitArrayWrapper's native Serialize (0x180da9770) delegates to
// TBitArray: int32 NumBits followed by ceil(NumBits / 32) uint32 words, low bit first.
// There is no reflected-property header inside this struct.
public class FBitArrayWrapper : IUStruct
{
    public readonly int NumBits;
    public readonly uint[] Words = [];

    public FBitArrayWrapper() { }

    public FBitArrayWrapper(FAssetArchive Ar)
    {
        NumBits = Ar.Read<int>();
        var wordCount = ((long) NumBits + 31) / 32;
        if (NumBits < 0 || wordCount > (Ar.Length - Ar.Position) / 4)
            throw new ParserException(Ar, $"Invalid BitArrayWrapper bit count {NumBits}");
        Words = Ar.ReadArray<uint>((int) wordCount);
        // TBitArray clears nonzero slack after loading.
        if (Words.Length > 0 && (NumBits & 31) != 0)
            Words[^1] &= (1u << (NumBits & 31)) - 1;
    }
}
