// Compatibility stays in the shared contract; modules do not depend on each other.
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}
namespace Golemancer.Contracts
{
    public static class FrameworkExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> source, TKey key)
            => source.TryGetValue(key, out var value) ? value : default!;
        public static TValue GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> source, TKey key, TValue fallback)
            => source.TryGetValue(key, out var value) ? value : fallback;
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        { key = pair.Key; value = pair.Value; }
    }
}
#endif
