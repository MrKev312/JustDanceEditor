namespace JustDanceEditor.Formats.UbiArt.Images;

/// <summary>
/// Variant of AlphanumericTextFirstComparer that orders digits before symbols.
/// This ensures names like "amor1_po" come before "amor_po" when digits appear
/// at the same segment position.
/// </summary>
public sealed class AlphanumericDigitsFirstComparer : IComparer<string>
{
    public static readonly AlphanumericDigitsFirstComparer Instance = new();

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

            // Compare types explicitly: Digit (0) < Symbol (1) < Letter (2)
            if (typeX != typeY)
            {
                return typeX.CompareTo(typeY);
            }

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
                0 => CompareNumeric(segX, segY),
                // Letter
                2 => string.Compare(segX, segY, StringComparison.OrdinalIgnoreCase),
                // Symbol
                _ => string.Compare(segX, segY, StringComparison.Ordinal),
            };
            if (cmp != 0)
                return cmp;
        }

        if (ix < nx)
            return 1;
        if (iy < ny)
            return -1;
        return 0;
    }

    private static int GetCharType(char c)
    {
        // Order: Digit < Symbol < Letter
        if (char.IsDigit(c))
            return 0;
        if (char.IsLetter(c))
            return 2;
        return 1; // Symbol
    }

    private static int CompareNumeric(string a, string b)
    {
        int ia = 0;
        while (ia < a.Length && a[ia] == '0')
            ia++;
        int ib = 0;
        while (ib < b.Length && b[ib] == '0')
            ib++;

        int lenA = a.Length - ia;
        int lenB = b.Length - ib;

        if (lenA == 0 && lenB == 0)
            return a.Length.CompareTo(b.Length);
        if (lenA == 0)
            return -1;
        if (lenB == 0)
            return 1;
        if (lenA != lenB)
            return lenA.CompareTo(lenB);

        for (int i = 0; i < lenA; i++)
        {
            char ca = a[ia + i];
            char cb = b[ib + i];
            if (ca != cb)
                return ca.CompareTo(cb);
        }

        return a.Length.CompareTo(b.Length);
    }
}