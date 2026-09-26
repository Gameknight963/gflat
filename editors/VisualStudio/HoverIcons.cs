using System.Collections.Generic;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace gflat.VisualStudio;

internal static class HoverIcons
{
    public static readonly IReadOnlyDictionary<string, object> Catalog = Create();
    private static Dictionary<string, object> Create()
    {
        var icons = new Dictionary<string, object>();
        void Add(string key, ImageMoniker icon) => icons.Add(key, new { guid = icon.Guid, id = icon.Id });
        Add("class.public", KnownMonikers.ClassPublic);
        Add("class.private", KnownMonikers.ClassPrivate);
        Add("class.protected", KnownMonikers.ClassProtected);
        Add("class.internal", KnownMonikers.ClassInternal);
        Add("struct.public", KnownMonikers.StructurePublic);
        Add("struct.private", KnownMonikers.StructurePrivate);
        Add("struct.protected", KnownMonikers.StructureProtected);
        Add("struct.internal", KnownMonikers.StructureInternal);
        Add("interface.public", KnownMonikers.InterfacePublic);
        Add("interface.private", KnownMonikers.InterfacePrivate);
        Add("interface.protected", KnownMonikers.InterfaceProtected);
        Add("interface.internal", KnownMonikers.InterfaceInternal);
        Add("enum.public", KnownMonikers.EnumerationPublic);
        Add("enum.private", KnownMonikers.EnumerationPrivate);
        Add("enum.protected", KnownMonikers.EnumerationProtected);
        Add("enum.internal", KnownMonikers.EnumerationInternal);
        Add("enumMember.public", KnownMonikers.EnumerationItemPublic);
        Add("enumMember.private", KnownMonikers.EnumerationItemPrivate);
        Add("enumMember.protected", KnownMonikers.EnumerationItemProtected);
        Add("enumMember.internal", KnownMonikers.EnumerationItemInternal);
        Add("method.public", KnownMonikers.MethodPublic);
        Add("method.private", KnownMonikers.MethodPrivate);
        Add("method.protected", KnownMonikers.MethodProtected);
        Add("method.internal", KnownMonikers.MethodInternal);
        Add("function.public", KnownMonikers.MethodPublic);
        Add("function.private", KnownMonikers.MethodPrivate);
        Add("function.protected", KnownMonikers.MethodProtected);
        Add("function.internal", KnownMonikers.MethodInternal);
        Add("property.public", KnownMonikers.PropertyPublic);
        Add("property.private", KnownMonikers.PropertyPrivate);
        Add("property.protected", KnownMonikers.PropertyProtected);
        Add("property.internal", KnownMonikers.PropertyInternal);
        Add("field.public", KnownMonikers.FieldPublic);
        Add("field.private", KnownMonikers.FieldPrivate);
        Add("field.protected", KnownMonikers.FieldProtected);
        Add("field.internal", KnownMonikers.FieldInternal);
        Add("constant.public", KnownMonikers.ConstantPublic);
        Add("constant.private", KnownMonikers.ConstantPrivate);
        Add("constant.protected", KnownMonikers.ConstantProtected);
        Add("constant.internal", KnownMonikers.ConstantInternal);
        Add("type.public", KnownMonikers.ClassPublic);
        Add("type.private", KnownMonikers.ClassPrivate);
        Add("type.protected", KnownMonikers.ClassProtected);
        Add("type.internal", KnownMonikers.ClassInternal);
        Add("namespace.public", KnownMonikers.Namespace);
        Add("variable.public", KnownMonikers.LocalVariable);
        Add("parameter.public", KnownMonikers.Parameter);
        return icons;
    }
}
