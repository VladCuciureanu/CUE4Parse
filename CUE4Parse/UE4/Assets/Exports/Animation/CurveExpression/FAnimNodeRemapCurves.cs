using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Assets.Objects.Unversioned;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.UE4.Assets.Exports.Animation.CurveExpression;

// SerializeNode appends compiled expressions after the reflected properties.
public class FAnimNodeRemapCurves : FStructFallback
{
    public Dictionary<FName, FExpressionObject> CachedExpressions = [];
    public int? ExpressionMapHash;

    public FAnimNodeRemapCurves() { }

    public FAnimNodeRemapCurves(FAssetArchive Ar, string structName, UStruct? struc, ReadType? type)
    {
        Properties = type == ReadType.RAW
            ? new FStructFallback(Ar, structName, FRawHeader.FullRead, ReadType.RAW).Properties
            : (struc != null ? new FStructFallback(Ar, struc) : new FStructFallback(Ar, structName)).Properties;

        if (FCurveExpressionCustomVersion.Get(Ar) >= FCurveExpressionCustomVersion.Type.SerializedExpressions)
        {
            CachedExpressions = new FExpressionData(Ar).ExpressionMap;
            if (Ar.ReadBoolean()) ExpressionMapHash = Ar.Read<int>();
        }
    }
}
