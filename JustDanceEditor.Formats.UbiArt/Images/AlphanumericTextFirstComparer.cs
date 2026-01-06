namespace JustDanceEditor.Formats.UbiArt.Images;

/// <summary>
/// Compares strings by segments using a Natural Sort order where:
/// Symbols (non-letter text) &lt; Numbers &lt; Letters.
/// </summary>
public sealed class AlphanumericTextFirstComparer : IComparer<string>
{
    public static readonly AlphanumericTextFirstComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x == null)
            return -1;
        if (y == null)
            return 1;

        int ix = 0, iy = 0;
        int nx = x.Length, ny = y.Length;

        while (ix < nx && iy < ny)
        {
            char cx = x[ix];
            char cy = y[iy];

            int typeX = GetCharType(cx);
            int typeY = GetCharType(cy);

            // Compare types explicitly: Symbol (0) < Digit (1) < Letter (2)
            if (typeX != typeY)
            {
                return typeX.CompareTo(typeY);
            }

            // Both characters are of the same type.
            // Extract the full segment of this type from both strings.
            int startX = ix;
            int startY = iy;

            while (ix < nx && GetCharType(x[ix]) == typeX)
                ix++;
            while (iy < ny && GetCharType(y[iy]) == typeY)
                iy++;

            string segX = x[startX..ix];
            string segY = y[startY..iy];
            int cmp = typeX switch
            {
                // Digit
                1 => CompareNumeric(segX, segY),
                // Letter
                2 => string.Compare(segX, segY, StringComparison.OrdinalIgnoreCase),
                // Symbol
                _ => string.Compare(segX, segY, StringComparison.Ordinal),// Compare symbols ordinally
            };
            if (cmp != 0)
                return cmp;
        }

        // Reached the end of one or both strings.
        // If one is shorter (a prefix of the other), it comes first.
        if (ix < nx)
            return 1; // x has more characters -> x > y
        if (iy < ny)
            return -1; // y has more characters -> x < y
        return 0;
    }

    private static int GetCharType(char c)
    {
        // Order: Symbol < Digit < Letter
        if (char.IsDigit(c))
            return 1;
        if (char.IsLetter(c))
            return 2;
        return 0; // Symbol
    }

    private static int CompareNumeric(string a, string b)
    {
        // Skip leading zeros
        int ia = 0;
        while (ia < a.Length && a[ia] == '0')
            ia++;
        int ib = 0;
        while (ib < b.Length && b[ib] == '0')
            ib++;

        // Calculate length of significant part
        int lenA = a.Length - ia;
        int lenB = b.Length - ib;

        // Both are zero (e.g. "0" vs "00")
        if (lenA == 0 && lenB == 0)
            return a.Length.CompareTo(b.Length);

        // One is zero, the other is not
        if (lenA == 0)
            return -1; // 0 < non-zero
        if (lenB == 0)
            return 1;

        // Different significant lengths -> longer is larger
        if (lenA != lenB)
            return lenA.CompareTo(lenB);

        // Same significant length -> compare digits lexicographically
        for (int i = 0; i < lenA; i++)
        {
            char ca = a[ia + i];
            char cb = b[ib + i];
            if (ca != cb)
                return ca.CompareTo(cb);
        }

        // Values are numerically equal (e.g. "1" vs "01"). 
        // Shorter string (fewer leading zeros) comes first.
        return a.Length.CompareTo(b.Length);
    }
}