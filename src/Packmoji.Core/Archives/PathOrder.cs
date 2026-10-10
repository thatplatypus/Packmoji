namespace Packmoji.Core.Archives
{
    /// <summary>
    /// The order an archive's files are in: by code point, which is also the order of their UTF-8
    /// bytes. It is not the order .NET gives two strings, which goes by UTF-16 unit and so puts every
    /// emoji before the characters from U+E000 up. An archive's order must not be an accident of the
    /// language pmj happens to be written in.
    /// </summary>
    public sealed class PathOrder : IComparer<string>
    {
        public static PathOrder Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            var left = x.EnumerateRunes().GetEnumerator();
            var right = y.EnumerateRunes().GetEnumerator();
            while (true)
            {
                var more = left.MoveNext();
                if (more != right.MoveNext())
                {
                    return more ? 1 : -1;
                }

                if (!more)
                {
                    return 0;
                }

                var difference = left.Current.Value.CompareTo(right.Current.Value);
                if (difference != 0)
                {
                    return difference;
                }
            }
        }
    }
}
