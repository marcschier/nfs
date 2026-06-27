#if !NET7_0_OR_GREATER
using System.Reflection;

namespace Nfs.Xdr;

/// <summary>
/// .NET Standard fallback for the <see langword="static"/> <see langword="abstract"/>
/// <c>IXdrSerializable&lt;TSelf&gt;.ReadFrom</c> factory.
/// </summary>
/// <remarks>
/// .NET Standard 2.x has no static-virtual interface dispatch, so generic decoders cannot call
/// <c>T.ReadFrom(...)</c>. Every codec still exposes a <c>public static TSelf ReadFrom(ref XdrReader)</c>
/// method (manually written or source-generated); this helper binds that method once via reflection
/// and caches the resulting delegate per closed type, so steady-state cost is a single delegate call.
/// On <c>net7.0</c> and later this type does not exist and callers use <c>T.ReadFrom(...)</c> directly,
/// keeping the modern build reflection-free and NativeAOT-safe.
/// </remarks>
public static class XdrDecoder
{
    /// <summary>Decodes a value of type <typeparamref name="T"/> from the supplied reader.</summary>
    /// <typeparam name="T">The XDR-serializable type to decode.</typeparam>
    /// <param name="reader">The reader to decode from.</param>
    /// <returns>The decoded value.</returns>
    public static T ReadFrom<T>(ref XdrReader reader)
        where T : IXdrSerializable<T>
        => Cache<T>.Read(ref reader);

    private delegate T ReadFromDelegate<T>(ref XdrReader reader);

    private static class Cache<T>
        where T : IXdrSerializable<T>
    {
        public static readonly ReadFromDelegate<T> Read = Bind();

        private static ReadFromDelegate<T> Bind()
        {
            MethodInfo method = typeof(T).GetMethod(
                "ReadFrom",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(XdrReader).MakeByRefType() },
                modifiers: null)
                ?? throw new MissingMethodException(typeof(T).FullName, "ReadFrom");

            return (ReadFromDelegate<T>)method.CreateDelegate(typeof(ReadFromDelegate<T>));
        }
    }
}
#endif
