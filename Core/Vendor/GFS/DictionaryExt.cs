namespace ThemModdingHerds.GFS;
public static class DictionaryExt
{
    public static KeyValuePair<Key,Value>? ElementAt<Key,Value>(this Dictionary<Key,Value> dict,long index) where Key : notnull
    {
        long count = 0;
        foreach(var pair in dict)
        {
            if(index == count)
                return pair;
            count++;
        }
        return default;
    }
    public static long Length<Key,Value>(this Dictionary<Key,Value> dict) where Key : notnull
    {
        long count = 0;
        foreach(var pair in dict)
            count++;
        return count;
    }
    public static void SetIndexValue<Key,Value>(this Dictionary<Key,Value> dict,long index,Value value) where Key : notnull
    {
        var pair = ElementAt(dict,index);
        if(pair != null)
        {
            var vpair = (KeyValuePair<Key,Value>)pair;
            dict[vpair.Key] = value;
        }
    }
}