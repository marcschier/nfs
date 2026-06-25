// Polyfill that lets C# 'record' / 'init' members compile when targeting netstandard2.0.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
