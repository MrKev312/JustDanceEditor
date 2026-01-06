using JustDanceEditor.Formats.UbiArt.Images;

using System;
using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class MontageSortingTests
{
    [Theory]
    // Rule: Number ('1') < Letter ('h'). Expected: test1, testhi
    [InlineData(new[] { "testhi", "test1" }, new[] { "test1", "testhi" })]
    // Rule: Numeric natural sort.
    [InlineData(new[] { "test1", "test2", "test10" }, new[] { "test1", "test2", "test10" })]
    // Rule: Number < Letter. 'aB' is the only Letter segment ('B'), others are Numbers.
    // Order: a1b, a2b, a10b, aB
    [InlineData(new[] { "a2b", "a10b", "aB", "a1b" }, new[] { "a1b", "a2b", "a10b", "aB" })]
    public void SortsAsExpected(string[] input, string[] expected)
    {
        List<string> items = [.. input];
        items.Sort(AlphanumericTextFirstComparer.Instance);

        if (!expected.SequenceEqual(items))
        {
            var cmp = AlphanumericTextFirstComparer.Instance;
            var details = new System.Text.StringBuilder();
            details.AppendLine($"Sorted: {string.Join(", ", items)}");
            details.AppendLine($"Expect: {string.Join(", ", expected)}");

            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    int c = cmp.Compare(items[i], items[j]);
                    // Log relations for debugging
                    details.AppendLine($"Compare({items[i]}, {items[j]}) = {c}");
                }

            Assert.Fail($"Sorting mismatch. {details}");
        }
    }

    [Fact]
    public void Compare_Returns_Positive_When_Letter_Vs_Number()
    {
        // "testhi" (Letter 'h') vs "test1" (Number '1')
        // Rule: Number < Letter, so testhi > test1
        int cmp = AlphanumericTextFirstComparer.Instance.Compare("testhi", "test1");
        Assert.True(cmp > 0, $"Expected 'testhi' > 'test1' (Letter > Number), got {cmp}");

        // "aB" (Letter 'B') vs "a1b" (Number '1')
        // Rule: Number < Letter, so aB > a1b
        cmp = AlphanumericTextFirstComparer.Instance.Compare("aB", "a1b");
        Assert.True(cmp > 0, $"Expected 'aB' > 'a1b' (Letter > Number), got {cmp}");
    }

    [Fact]
    public void Comparer_Is_AntiSymmetric_For_Sample()
    {
        string[] items = ["a2b", "a10b", "aB", "a1b"];
        var cmp = AlphanumericTextFirstComparer.Instance;
        for (int i = 0; i < items.Length; i++)
            for (int j = 0; j < items.Length; j++)
            {
                int c1 = cmp.Compare(items[i], items[j]);
                int c2 = cmp.Compare(items[j], items[i]);

                // Compare sign only
                Assert.Equal(Math.Sign(c1), -Math.Sign(c2));
            }
    }

    [Fact]
    public void SortedList_Is_Monotonic_By_Comparer()
    {
        string[] inputs = ["testhi", "test1"];
        var list = new List<string>(inputs);
        list.Sort(AlphanumericTextFirstComparer.Instance);

        var cmp = AlphanumericTextFirstComparer.Instance;
        for (int i = 0; i < list.Count - 1; i++)
        {
            int c = cmp.Compare(list[i], list[i + 1]);
            Assert.True(c <= 0, $"List is not monotonic: Compare({list[i]}, {list[i + 1]}) == {c}");
        }

        // Larger sample
        inputs = ["a2b", "a10b", "aB", "a1b"];
        list = [.. inputs];
        list.Sort(AlphanumericTextFirstComparer.Instance);
        for (int i = 0; i < list.Count - 1; i++)
        {
            int c = cmp.Compare(list[i], list[i + 1]);
            Assert.True(c <= 0, $"List is not monotonic: Compare({list[i]}, {list[i + 1]}) == {c}");
        }
    }
}