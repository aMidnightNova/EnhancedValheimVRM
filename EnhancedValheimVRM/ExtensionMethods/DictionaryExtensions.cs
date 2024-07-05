using System.Collections.Generic;

namespace EnhancedValheimVRM
{
    internal static class DictionaryExtensions
    {
        public static void Set<T1, T2>(this Dictionary<T1, T2> dict, T1 key, T2 value)
        {
            if (value == null)
            {
                dict.Remove(key);
            }
            else
            {
                dict[key] = value;
            }
        }
    }
}