using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Assets.Objects.Unversioned;
using CUE4Parse.UE4.Assets.Readers;
using CUE4Parse.UE4.Exceptions;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace CUE4Parse.UE4.Objects.StructUtils;

[JsonConverter(typeof(EnumConverter<EPropertyBagPropertyType>))]
public enum EPropertyBagPropertyType : byte
{
    None,
    Bool,
    Byte,
    Int32,
    Int64,
    Float,
    Double,
    Name,
    String,
    Text,
    Enum,
    Struct,
    Object,
    SoftObject,
    Class,
    SoftClass,
    UInt32,	// Type not fully supported at UI, will work with restrictions to type editing
    UInt64, // Type not fully supported at UI, will work with restrictions to type editing

    Count
};

[JsonConverter(typeof(EnumConverter<EPropertyBagContainerType>))]
public enum EPropertyBagContainerType : byte
{
    None,
    Array,
    Count
};

public class FInstancedPropertyBag : IUStruct
{
    public FPropertyBagPropertyDesc[] PropertyDescs = [];
    public int SerialSize;

    public FInstancedPropertyBag(FAssetArchive Ar)
    {
        if (this is FInstancedOverridablePropertyBag
            && FOverridablePropertyBagCustomVersion.Get(Ar) < FOverridablePropertyBagCustomVersion.Type.FixSerializer)
        {
            return;
        }

        var Version = EVersion.LatestVersion;
        if (FPropertyBagCustomVersion.Get(Ar) < FPropertyBagCustomVersion.Type.ContainerTypes)
        {
            Version = Ar.Read<EVersion>();
        }

        var bHasData = Ar.ReadBoolean();
        if (!bHasData)
            return;

        PropertyDescs = Ar.ReadArray(() => new FPropertyBagPropertyDesc(Ar));

        if (Version >= EVersion.SerializeStructSize)
            SerialSize = Ar.Read<int>();

        // The values are the bag's own struct (UPropertyBag::GetOrCreateFromDescs), whose properties are
        // the descs in order. A bag whose values can't be read still skips them by their size.
        var start = Ar.Position;
        try
        {
            Properties = ReadValues(Ar, PropertyDescs);
            if (Version >= EVersion.SerializeStructSize && Ar.Position != start + SerialSize)
                throw new ParserException(Ar, $"Property bag values read {Ar.Position - start} of {SerialSize} bytes");
        }
        catch (Exception e)
        {
            Properties = null;
            Log.Debug(e, "Failed to read property bag values");
        }

        // Without a size there is nothing to skip a bag by, which is how it always was read
        if (Version >= EVersion.SerializeStructSize || Properties == null)
            Ar.Position = start + SerialSize;
    }

    /// <summary>The bag's values, one per desc, named after it. Null when they couldn't be read.</summary>
    public List<FPropertyTag>? Properties;

    private static List<FPropertyTag> ReadValues(FAssetArchive Ar, FPropertyBagPropertyDesc[] descs)
    {
        var properties = new List<FPropertyTag>(descs.Length);
        if (!Ar.HasUnversionedProperties)
        {
            Assets.Exports.UObject.DeserializePropertiesTagged(properties, Ar, true);
            return properties;
        }

        // Unversioned: the header names which properties follow and which of them are zero. The bag is
        // saved without defaults, so a property the header leaves out is zero as well.
        var values = new FPropertyTag?[descs.Length];
        var header = new FUnversionedHeader(Ar);
        if (header.HasValues)
        {
            using var iterator = new FIterator(header);
            do
            {
                var (index, isNonZero) = iterator.Current;
                if (index >= descs.Length)
                    throw new ParserException(Ar, $"Property bag value {index} is outside its {descs.Length} descs");
                values[index] = new FPropertyTag(Ar, Info(descs[index]), isNonZero ? ReadType.NORMAL : ReadType.ZERO);
            } while (iterator.MoveNext());
        }

        for (var index = 0; index < descs.Length; index++)
            properties.Add(values[index] ?? new FPropertyTag(Ar, Info(descs[index]), ReadType.ZERO));
        return properties;
    }

    private static PropertyInfo Info(FPropertyBagPropertyDesc desc)
    {
        var typeName = desc.ValueTypeObject.ResolvedObject?.Name.Text;
        var type = desc.ValueType switch
        {
            EPropertyBagPropertyType.Bool => new PropertyType("BoolProperty"),
            EPropertyBagPropertyType.Byte => new PropertyType("ByteProperty"),
            EPropertyBagPropertyType.Int32 => new PropertyType("IntProperty"),
            EPropertyBagPropertyType.Int64 => new PropertyType("Int64Property"),
            EPropertyBagPropertyType.UInt32 => new PropertyType("UInt32Property"),
            EPropertyBagPropertyType.UInt64 => new PropertyType("UInt64Property"),
            EPropertyBagPropertyType.Float => new PropertyType("FloatProperty"),
            EPropertyBagPropertyType.Double => new PropertyType("DoubleProperty"),
            EPropertyBagPropertyType.Name => new PropertyType("NameProperty"),
            EPropertyBagPropertyType.String => new PropertyType("StrProperty"),
            EPropertyBagPropertyType.Text => new PropertyType("TextProperty"),
            // A bag's enum is an FEnumProperty over a uint8
            EPropertyBagPropertyType.Enum => new PropertyType("EnumProperty", innerType: new PropertyType("ByteProperty"), enumName: typeName, isEnumAsByte: true),
            EPropertyBagPropertyType.Struct => new PropertyType("StructProperty", structType: typeName),
            EPropertyBagPropertyType.Object => new PropertyType("ObjectProperty"),
            EPropertyBagPropertyType.SoftObject => new PropertyType("SoftObjectProperty"),
            EPropertyBagPropertyType.Class => new PropertyType("ClassProperty"),
            EPropertyBagPropertyType.SoftClass => new PropertyType("SoftClassProperty"),
            _ => throw new NotSupportedException($"Property bag type {desc.ValueType}")
        };

        // A user defined struct has no mapping of its own, its asset is the schema
        if (desc.ValueType == EPropertyBagPropertyType.Struct && desc.ValueTypeObject.TryLoad<UStruct>(out var schema))
            type.Struct = schema;

        // Containers are listed outermost first
        foreach (var container in (desc.ContainerTypes.Types ?? []).Reverse())
        {
            if (container != EPropertyBagContainerType.Array)
                throw new NotSupportedException($"Property bag container {container}");
            type = new PropertyType("ArrayProperty", innerType: type);
        }

        return new PropertyInfo(0, desc.Name.Text, type);
    }

    public enum EVersion : byte
    {
        InitialVersion = 0,
        SerializeStructSize,
        // -----<new versions can be added above this line>-----
        VersionPlusOne,
        LatestVersion = VersionPlusOne - 1
    };
}

public struct FPropertyBagPropertyDesc
{
    public FPackageIndex ValueTypeObject;
    public FGuid ID;
    public FName Name;
    public EPropertyBagPropertyType ValueType;
    public EPropertyBagContainerType ContainerType;
    public EPropertyFlags PropertyFlags;
    public FPropertyBagContainerTypes ContainerTypes;
    public FPropertyBagPropertyDescMetaData[]? MetaData;
    public FPackageIndex? MetaClass;
    public EPropertyBagPropertyType KeyType;
    public FPackageIndex? KeyTypeObject;

    public FPropertyBagPropertyDesc(FAssetArchive Ar)
    {
        ValueTypeObject = new FPackageIndex(Ar);
        ID = Ar.Read<FGuid>();
        Name = Ar.ReadFName();
        ValueType = Ar.Read<EPropertyBagPropertyType>();

        if (FPropertyBagCustomVersion.Get(Ar) < FPropertyBagCustomVersion.Type.ContainerTypes)
        {
            ContainerType = Ar.Read<EPropertyBagContainerType>();
            if (ContainerType != EPropertyBagContainerType.None)
                ContainerTypes = new FPropertyBagContainerTypes([ContainerType]);
        }
        else
        {
            ContainerTypes = new FPropertyBagContainerTypes(Ar);
        }

        var bHasMetaData = Ar.ReadBoolean();
        if (bHasMetaData)
        {
            MetaData = Ar.ReadArray(() => new FPropertyBagPropertyDescMetaData(Ar));
            if (FPropertyBagCustomVersion.Get(Ar) >= FPropertyBagCustomVersion.Type.MetaClass)
            {
                MetaClass = new FPackageIndex(Ar);
            }
        }

        if (FPropertyBagCustomVersion.Get(Ar) >= FPropertyBagCustomVersion.Type.PropertyFlags)
        {
            PropertyFlags = Ar.Read<EPropertyFlags>();
        }

        if (FPropertyBagCustomVersion.Get(Ar) >= FPropertyBagCustomVersion.Type.KeyTypes)
        {
            KeyType = Ar.Read<EPropertyBagPropertyType>();
            KeyTypeObject = new FPackageIndex(Ar);
        }
    }
}

public struct FPropertyBagPropertyDescMetaData(FAssetArchive Ar)
{
    public FName Key = Ar.ReadFName();
    public string Value = Ar.ReadFString();
}

public struct FPropertyBagContainerTypes
{
    public EPropertyBagContainerType[] Types;

    public FPropertyBagContainerTypes(EPropertyBagContainerType[] types)
    {
        Types = types;
    }

    public FPropertyBagContainerTypes(FAssetArchive Ar)
    {
        Types = Ar.ReadArray(Ar.Read<byte>(), Ar.Read<EPropertyBagContainerType>);
    }
}
