namespace CrnLib;

internal sealed class StaticHuffmanModel
{
    private const int MaxSupportedSymbols = 8192;
    private const int ModelSymbolBits = 14;
    private const int MaxExpectedCodeSize = 16;

    private const int SmallZeroRunCode = 17;
    private const int LargeZeroRunCode = 18;
    private const int SmallRepeatCode = 19;
    private const int LargeRepeatCode = 20;

    private static readonly byte[] MostProbableCodeLengthCodes =
    [
        SmallZeroRunCode, LargeZeroRunCode,
        SmallRepeatCode, LargeRepeatCode,
        0, 8,
        7, 9,
        6, 10,
        5, 11,
        4, 12,
        3, 13,
        2, 14,
        1, 15,
        16
    ];

    private readonly Dictionary<int, int>[] _decodeByLength;

    private StaticHuffmanModel(byte[] codeSizes)
    {
        CodeSizes = codeSizes;
        Codes = GenerateCodes(codeSizes);
        MaxCodeSize = 0;

        _decodeByLength = new Dictionary<int, int>[MaxExpectedCodeSize + 1];
        for (int i = 0; i < _decodeByLength.Length; i++)
            _decodeByLength[i] = [];

        for (int symbol = 0; symbol < codeSizes.Length; symbol++)
        {
            int length = codeSizes[symbol];
            if (length == 0)
                continue;

            MaxCodeSize = Math.Max(MaxCodeSize, length);
            _decodeByLength[length][Codes[symbol]] = symbol;
        }

        if (MaxCodeSize == 0)
            throw new InvalidDataException("Static Huffman model does not contain any symbols.");
    }

    public byte[] CodeSizes { get; }
    public ushort[] Codes { get; }
    public int MaxCodeSize { get; }

    public static StaticHuffmanModel CreateForSymbols(IEnumerable<int> symbols)
    {
        return CreateForSymbols(symbols, MaxExpectedCodeSize);
    }

    private static StaticHuffmanModel CreateForSymbols(IEnumerable<int> symbols, int maxCodeSize)
    {
        int maxSymbol = 0;
        bool any = false;
        int[] frequencies = new int[MaxSupportedSymbols];
        foreach (int symbol in symbols)
        {
            if (symbol < 0 || symbol >= MaxSupportedSymbols)
                throw new InvalidDataException($"CRN symbol {symbol} is outside the supported range.");

            maxSymbol = Math.Max(maxSymbol, symbol);
            frequencies[symbol]++;
            any = true;
        }

        if (!any)
            return CreateFixed(0);

        byte[]? codeSizes = TryCreateCodeSizes(frequencies, maxSymbol + 1, maxCodeSize);
        return codeSizes is null ? CreateFixed(maxSymbol) : new StaticHuffmanModel(codeSizes);
    }

    public static StaticHuffmanModel CreateFixed(int maxSymbol)
    {
        if (maxSymbol < 0 || maxSymbol >= MaxSupportedSymbols)
            throw new ArgumentOutOfRangeException(nameof(maxSymbol));

        int totalSymbols = NextPowerOfTwo(maxSymbol + 1);
        int codeSize = totalSymbols <= 1 ? 1 : Log2(totalSymbols);
        byte[] codeSizes = new byte[totalSymbols];
        Array.Fill(codeSizes, (byte)codeSize);
        return new StaticHuffmanModel(codeSizes);
    }

    public static StaticHuffmanModel Receive(BitReader reader)
    {
        int totalUsedSymbols = (int)reader.ReadBits(ModelSymbolBits);
        if (totalUsedSymbols == 0)
            throw new InvalidDataException("Empty static Huffman models are not valid in this CRN path.");

        if (totalUsedSymbols > MaxSupportedSymbols)
            throw new InvalidDataException("CRN Huffman model exceeds the maximum supported symbol count.");

        int codeLengthCodesToSend = (int)reader.ReadBits(5);
        if (codeLengthCodesToSend < 1 || codeLengthCodesToSend > MostProbableCodeLengthCodes.Length)
            throw new InvalidDataException("CRN code-length model is invalid.");

        byte[] codeLengthCodeSizes = new byte[21];
        for (int i = 0; i < codeLengthCodesToSend; i++)
            codeLengthCodeSizes[MostProbableCodeLengthCodes[i]] = (byte)reader.ReadBits(3);

        StaticHuffmanModel codeLengthModel = new(codeLengthCodeSizes);
        byte[] codeSizes = new byte[totalUsedSymbols];
        int offset = 0;

        while (offset < totalUsedSymbols)
        {
            int remaining = totalUsedSymbols - offset;
            int code = codeLengthModel.Decode(reader);
            switch (code)
            {
                case <= 16:
                    codeSizes[offset++] = (byte)code;
                    break;
                case SmallZeroRunCode:
                    offset += CheckedRun(reader, 3, 3, remaining);
                    break;
                case LargeZeroRunCode:
                    offset += CheckedRun(reader, 7, 11, remaining);
                    break;
                case SmallRepeatCode:
                case LargeRepeatCode:
                {
                    int length = code == SmallRepeatCode
                        ? CheckedRun(reader, 2, 3, remaining)
                        : CheckedRun(reader, 6, 7, remaining);

                    if (offset == 0 || codeSizes[offset - 1] == 0)
                        throw new InvalidDataException("CRN code-length repeat run has no previous non-zero length.");

                    byte previous = codeSizes[offset - 1];
                    for (int i = 0; i < length; i++)
                        codeSizes[offset++] = previous;
                    break;
                }
                default:
                    throw new InvalidDataException("CRN code-length stream contains an invalid symbol.");
            }
        }

        return new StaticHuffmanModel(codeSizes);
    }

    public void Transmit(BitWriter writer)
    {
        int totalUsedSymbols = CodeSizes.Length;
        List<CodeLengthOperation> operations = CreateCodeLengthOperations(CodeSizes);
        StaticHuffmanModel codeLengthModel = CreateForSymbols(operations.Select(x => x.Symbol), maxCodeSize: 7);

        int orderIndex = -1;
        for (int i = 0; i < MostProbableCodeLengthCodes.Length; i++)
        {
            int symbol = MostProbableCodeLengthCodes[i];
            if (symbol < codeLengthModel.CodeSizes.Length && codeLengthModel.CodeSizes[symbol] != 0)
                orderIndex = i;
        }

        if (orderIndex < 0)
            throw new InvalidOperationException("The code-length Huffman model is empty.");

        writer.WriteBits((uint)totalUsedSymbols, ModelSymbolBits);
        writer.WriteBits((uint)(orderIndex + 1), 5);

        for (int i = 0; i <= orderIndex; i++)
        {
            int symbol = MostProbableCodeLengthCodes[i];
            uint codeSize = symbol < codeLengthModel.CodeSizes.Length ? codeLengthModel.CodeSizes[symbol] : 0U;
            writer.WriteBits(codeSize, 3);
        }

        foreach (CodeLengthOperation operation in operations)
        {
            codeLengthModel.Encode(writer, operation.Symbol);
            if (operation.ExtraBitCount > 0)
                writer.WriteBits((uint)operation.ExtraBits, operation.ExtraBitCount);
        }
    }

    public int Decode(BitReader reader)
    {
        int code = 0;
        for (int length = 1; length <= MaxCodeSize; length++)
        {
            code = (code << 1) | (int)reader.ReadBits(1);
            if (_decodeByLength[length].TryGetValue(code, out int symbol))
                return symbol;
        }

        throw new InvalidDataException("CRN Huffman stream references an unknown code.");
    }

    public void Encode(BitWriter writer, int symbol)
    {
        if ((uint)symbol >= (uint)CodeSizes.Length || CodeSizes[symbol] == 0)
            throw new InvalidDataException($"CRN symbol {symbol} is not present in the Huffman model.");

        writer.WriteBits(Codes[symbol], CodeSizes[symbol]);
    }

    private static List<CodeLengthOperation> CreateCodeLengthOperations(byte[] codeSizes)
    {
        List<CodeLengthOperation> operations = [];
        int offset = 0;
        while (offset < codeSizes.Length)
        {
            byte codeSize = codeSizes[offset];
            int runLength = 1;
            while (offset + runLength < codeSizes.Length && codeSizes[offset + runLength] == codeSize)
                runLength++;

            if (codeSize == 0)
            {
                AddZeroRun(operations, runLength);
                offset += runLength;
                continue;
            }

            operations.Add(new CodeLengthOperation(codeSize, 0, 0));
            AddRepeatRun(operations, codeSize, runLength - 1);
            offset += runLength;
        }

        return operations;
    }

    private static void AddZeroRun(List<CodeLengthOperation> operations, int runLength)
    {
        while (runLength > 0)
        {
            if (runLength >= 11)
            {
                int length = Math.Min(runLength, 138);
                operations.Add(new CodeLengthOperation(LargeZeroRunCode, length - 11, 7));
                runLength -= length;
            }
            else if (runLength >= 3)
            {
                int length = Math.Min(runLength, 10);
                operations.Add(new CodeLengthOperation(SmallZeroRunCode, length - 3, 3));
                runLength -= length;
            }
            else
            {
                operations.Add(new CodeLengthOperation(0, 0, 0));
                runLength--;
            }
        }
    }

    private static void AddRepeatRun(List<CodeLengthOperation> operations, byte codeSize, int runLength)
    {
        while (runLength > 0)
        {
            if (runLength >= 7)
            {
                int length = Math.Min(runLength, 70);
                operations.Add(new CodeLengthOperation(LargeRepeatCode, length - 7, 6));
                runLength -= length;
            }
            else if (runLength >= 3)
            {
                int length = Math.Min(runLength, 6);
                operations.Add(new CodeLengthOperation(SmallRepeatCode, length - 3, 2));
                runLength -= length;
            }
            else
            {
                operations.Add(new CodeLengthOperation(codeSize, 0, 0));
                runLength--;
            }
        }
    }

    private static byte[]? TryCreateCodeSizes(int[] frequencies, int symbolCount, int maxCodeSize)
    {
        List<HuffmanNode> nodes = [];
        PriorityQueue<int, long> queue = new();
        int order = 0;

        for (int symbol = 0; symbol < symbolCount; symbol++)
        {
            int frequency = frequencies[symbol];
            if (frequency == 0)
                continue;

            int nodeIndex = nodes.Count;
            nodes.Add(new HuffmanNode(symbol, frequency, -1, -1));
            queue.Enqueue(nodeIndex, CreatePriority(frequency, order++));
        }

        byte[] codeSizes = new byte[symbolCount];
        if (queue.Count == 0)
            return codeSizes;

        if (queue.Count == 1)
        {
            int onlyNode = queue.Dequeue();
            codeSizes[nodes[onlyNode].Symbol] = 1;
            return codeSizes;
        }

        while (queue.Count > 1)
        {
            int left = queue.Dequeue();
            int right = queue.Dequeue();
            int frequency = nodes[left].Frequency + nodes[right].Frequency;
            int nodeIndex = nodes.Count;
            nodes.Add(new HuffmanNode(-1, frequency, left, right));
            queue.Enqueue(nodeIndex, CreatePriority(frequency, order++));
        }

        int root = queue.Dequeue();
        Stack<(int NodeIndex, int Depth)> stack = [];
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            (int nodeIndex, int depth) = stack.Pop();
            HuffmanNode node = nodes[nodeIndex];
            if (node.Symbol >= 0)
            {
                if (depth > maxCodeSize)
                    return null;

                codeSizes[node.Symbol] = (byte)Math.Max(1, depth);
                continue;
            }

            stack.Push((node.Left, depth + 1));
            stack.Push((node.Right, depth + 1));
        }

        return codeSizes;
    }

    private static long CreatePriority(int frequency, int order)
    {
        return ((long)frequency << 32) | (uint)order;
    }

    private static int CheckedRun(BitReader reader, int extraBits, int minimum, int remaining)
    {
        int length = (int)reader.ReadBits(extraBits) + minimum;
        if (length > remaining)
            throw new InvalidDataException("CRN code-length run exceeds the model size.");

        return length;
    }

    private static ushort[] GenerateCodes(byte[] codeSizes)
    {
        int[] codeCounts = new int[MaxExpectedCodeSize + 1];
        foreach (byte codeSize in codeSizes)
        {
            if (codeSize > MaxExpectedCodeSize)
                throw new InvalidDataException("CRN Huffman code size exceeds the supported limit.");

            if (codeSize != 0)
                codeCounts[codeSize]++;
        }

        int code = 0;
        int[] nextCode = new int[MaxExpectedCodeSize + 1];
        for (int i = 1; i <= MaxExpectedCodeSize; i++)
        {
            nextCode[i] = code;
            code = (code + codeCounts[i]) << 1;
        }

        ushort[] codes = new ushort[codeSizes.Length];
        for (int symbol = 0; symbol < codeSizes.Length; symbol++)
        {
            int size = codeSizes[symbol];
            if (size == 0)
                continue;

            codes[symbol] = (ushort)nextCode[size]++;
        }

        return codes;
    }

    private static int NextPowerOfTwo(int value)
    {
        int power = 1;
        while (power < value)
            power <<= 1;

        return power;
    }

    private static int Log2(int value)
    {
        int log = 0;
        while ((1 << log) < value)
            log++;

        return log;
    }

    private readonly record struct CodeLengthOperation(int Symbol, int ExtraBits, int ExtraBitCount);

    private readonly record struct HuffmanNode(int Symbol, int Frequency, int Left, int Right);
}
