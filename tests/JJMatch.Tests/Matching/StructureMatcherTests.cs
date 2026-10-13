using System.Text.Json;

namespace JJMatch.Tests.Matching;

// Every input is a section of TestData/ComplexJson, or the root of a TestData/Stringified file for the stringified test.
// A test may compare two different sections with each other. Expected differences and counts are written out by hand.
public class StructureMatcherTests
{
    private static readonly JsonElement Left = Load("ComplexJson", "Left.json");
    private static readonly JsonElement Right = Load("ComplexJson", "Right.json");

    private static JsonElement Load(string folder, string fileName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", folder, fileName)),
            new JsonDocumentOptions { MaxDepth = 128 });
        return (folder == "ComplexJson" ? document.RootElement.GetProperty("core") : document.RootElement).Clone();
    }

    private static JsonStructureNode Tree(JsonElement element) =>
        global::JJMatch.JsonStructureTraverser.Traverse(element);

    private static StructureMatchResult Match(JsonStructureNode left, JsonStructureNode right) =>
        global::JJMatch.StructureMatcher.Match(left, right);

    private static StructureMatchResult Match(JsonElement left, JsonElement right) =>
        Match(Tree(left), Tree(right));

    private static StructureMatchResult MatchSection(string name) =>
        Match(Left.GetProperty(name), Right.GetProperty(name));

    // The mismatch type and path of each difference, in the order the result holds them.
    private static IEnumerable<(StructureMismatchType Type, string Path)> Differences(StructureMatchResult result) =>
        result.Differences.Select(difference => (difference.MismatchType, difference.Path));

    private static JsonStructureNode Child(JsonStructureNode node, string name) =>
        node.Children.Single(child => child.PropertyName == name);

    private static void AssertFullMatch(int expectedMatchedCount, StructureMatchResult result)
    {
        Assert.True(result.IsMatch);
        Assert.Empty(result.Differences);
        Assert.Equal(expectedMatchedCount, result.MatchedCount);
    }

    [Fact]
    public void Assignment_agrees_with_exhaustive_search_including_all_tie_breaks()
    {
        // Test the combinatorial solver independently of JSON traversal and structural costs. No JSON is generated.
        // Reflection keeps this implementation detail private; the oracle enumerates partial injections and
        // compares tuples directly, without the solver's scalar cost encoding, potentials or augmenting paths.
        var solve = typeof(global::JJMatch.StructureMatcher).GetMethod("BestAssignment",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        for (var rows = 1; rows <= 3; rows++)
        {
            for (var columns = 1; columns <= 3; columns++)
            {
                var possibilities = (int)Math.Pow(3, rows * columns);
                for (var sample = 0; sample < possibilities; sample++)
                {
                    var states = sample;
                    var counts = new int[rows, columns];
                    var differences = new List<StructureDifference>?[rows, columns];
                    for (var row = 0; row < rows; row++)
                    {
                        for (var column = 0; column < columns; column++)
                        {
                            var state = states % 3;
                            states /= 3;
                            counts[row, column] = state == 0 ? -1 : 1;
                            differences[row, column] = state == 0 ? null : state == 1 ? [null!] : [];
                        }
                    }
                    Check(counts, differences);
                }
            }
        }

        // Rectangular, weighted cases include zero-benefit pairs, which still win the pair-count tie.
        var random = new Random(20261012);
        for (var sample = 0; sample < 2000; sample++)
        {
            var rows = random.Next(1, 6);
            var columns = random.Next(1, 6);
            var counts = new int[rows, columns];
            var differences = new List<StructureDifference>?[rows, columns];
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var count = counts[row, column] = random.Next(-1, 5);
                    differences[row, column] = count < 0 ? null : count > 0 && random.Next(2) == 0 ? [] : [null!];
                }
            }
            Check(counts, differences);
        }

        void Check(int[,] counts, List<StructureDifference>?[,] differences)
        {
            var rows = counts.GetLength(0);
            var columns = counts.GetLength(1);
            var used = new bool[columns];
            var candidate = new int[rows];
            var expected = new int[rows];
            var best = (Nodes: -1, Identical: -1, Pairs: -1);
            Search(0, 0, 0, 0);
            var actual = (int[])solve.Invoke(null, [counts, differences])!;
            Assert.Equal(expected, actual);

            void Search(int row, int nodes, int identical, int pairs)
            {
                if (row == rows)
                {
                    var score = (nodes, identical, pairs);
                    // Enumeration is already lexicographic: right partners first, unmatched last.
                    if (score.CompareTo(best) > 0)
                    {
                        best = score;
                        candidate.CopyTo(expected, 0);
                    }
                    return;
                }
                for (var column = 0; column < columns; column++)
                {
                    if (!used[column] && counts[row, column] >= 0)
                    {
                        used[column] = true;
                        candidate[row] = column;
                        Search(row + 1, nodes + counts[row, column],
                            identical + (differences[row, column]!.Count == 0 ? 1 : 0), pairs + 1);
                        used[column] = false;
                    }
                }
                candidate[row] = -1;
                Search(row + 1, nodes, identical, pairs);
            }
        }
    }

    [Fact]
    public void Structural_equivalence_agrees_with_an_independent_recursive_relation()
    {
        // This oracle uses neither canonical signatures nor weighted assignment. Object occurrences form a
        // multiset; array shapes form a set. Greedy removal is valid here only because equality is an equivalence.
        var sections = AllSections();
        foreach (var left in sections)
        {
            foreach (var right in sections)
            {
                Assert.Equal(Equivalent(left, right), Match(left, right).IsMatch);
            }
        }

        static bool Equivalent(JsonStructureNode left, JsonStructureNode right)
        {
            static JsonValueKind Kind(JsonStructureNode node) =>
                node.ValueKind == JsonValueKind.False ? JsonValueKind.True : node.ValueKind;

            if (Kind(left) != Kind(right)) return false;
            if (left.ValueKind == JsonValueKind.Array)
            {
                return left.Children.All(l => right.Children.Any(r => Equivalent(l, r)))
                    && right.Children.All(r => left.Children.Any(l => Equivalent(l, r)));
            }
            if (left.ValueKind != JsonValueKind.Object) return true;
            if (left.Children.Count != right.Children.Count) return false;
            var remaining = right.Children.ToList();
            foreach (var child in left.Children)
            {
                var index = remaining.FindIndex(other => child.PropertyName == other.PropertyName && Equivalent(child, other));
                if (index < 0) return false;
                remaining.RemoveAt(index);
            }
            return remaining.Count == 0;
        }
    }

    [Fact]
    public void Document_matches_itself_with_no_differences()
    {
        var left = Match(Left, Left);
        var right = Match(Right, Right);

        Assert.True(left.IsMatch);
        Assert.Empty(left.Differences);
        Assert.True(right.IsMatch);
        Assert.Empty(right.Differences);
    }

    [Fact]
    public void Primitive_values_do_not_affect_the_match()
    {
        // Different strings, true against false, and numbers written differently.
        AssertFullMatch(1, MatchSection("id"));
        AssertFullMatch(1, MatchSection("active"));
        AssertFullMatch(3, MatchSection("primitiveValues"));
    }

    [Fact]
    public void Object_property_order_does_not_affect_the_match()
    {
        // head, head.alpha, head.tail, tail.alpha, tail.beta and foot, plus the root.
        AssertFullMatch(7, MatchSection("propertyOrder"));

        var inArrays = MatchSection("propertyOrderInArrays");
        Assert.True(inArrays.IsMatch);
        Assert.Empty(inArrays.Differences);
    }

    [Fact]
    public void Array_element_order_does_not_affect_the_match()
    {
        // The root, two arrays, and element shapes of 3, 4, 3 and 3 nodes.
        AssertFullMatch(16, MatchSection("arrayElementOrder"));

        // The root, { item, group } and { item, group: { item } }.
        AssertFullMatch(8, MatchSection("idListOrder"));

        var nested = MatchSection("nestedArrayElementOrder");
        Assert.True(nested.IsMatch);
        Assert.Empty(nested.Differences);
    }

    [Fact]
    public void Repeated_element_shapes_do_not_affect_the_match()
    {
        // Three strings against two: one String shape on each side.
        AssertFullMatch(
            2,
            Match(
                Left.GetProperty("items")[0].GetProperty("attributes").GetProperty("options"),
                Right.GetProperty("items")[0].GetProperty("attributes").GetProperty("options")));

        // Six elements against five, both holding String, Number, Boolean and Null.
        AssertFullMatch(5, MatchSection("primitives"));
    }

    [Fact]
    public void Different_types_are_a_type_mismatch_and_nothing_below_is_reported()
    {
        var left = Tree(Left.GetProperty("payment"));
        var right = Tree(Right.GetProperty("payment"));

        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.TypeMismatch, "$.card.expiry"),
                (StructureMismatchType.ExtraArrayElementShape, "$.refunds[*]"),
            ],
            Differences(result));

        // An object against a string: month and year are not reported on their own.
        Assert.Same(Child(Child(left, "card"), "expiry"), result.Differences[0].Left);
        Assert.Same(Child(Child(right, "card"), "expiry"), result.Differences[0].Right);

        // An empty array against an array holding one object.
        Assert.Null(result.Differences[1].Left);
        Assert.Same(Child(right, "refunds").Children[0], result.Differences[1].Right);

        // The root, method, amount, currency, captured, card, brand, last4 and refunds.
        Assert.Equal(9, result.MatchedCount);
    }

    [Fact]
    public void Null_against_another_type_is_a_type_mismatch()
    {
        var result = MatchSection("notes");

        Assert.False(result.IsMatch);
        Assert.Equal([(StructureMismatchType.TypeMismatch, "$")], Differences(result));
        Assert.Equal(JsonValueKind.Null, result.Differences[0].Left!.ValueKind);
        Assert.Equal(JsonValueKind.String, result.Differences[0].Right!.ValueKind);
        Assert.Equal(0, result.MatchedCount);
    }

    [Fact]
    public void Object_against_an_array_holding_objects_is_a_type_mismatch()
    {
        var result = Match(
            Left.GetProperty("items")[1].GetProperty("attributes"),
            Right.GetProperty("items")[1].GetProperty("attributes"));

        Assert.Equal([(StructureMismatchType.TypeMismatch, "$")], Differences(result));
        Assert.Equal(JsonValueKind.Object, result.Differences[0].Left!.ValueKind);
        Assert.Equal(JsonValueKind.Array, result.Differences[0].Right!.ValueKind);
        Assert.Equal(0, result.MatchedCount);
    }

    [Fact]
    public void Missing_and_extra_properties_are_reported_at_every_depth()
    {
        var left = Tree(Left.GetProperty("meta"));
        var right = Tree(Right.GetProperty("meta"));

        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$.flags.priority"),
                (StructureMismatchType.ExtraProperty, "$.flags.données.schlüssel"),
                (StructureMismatchType.MissingProperty, "$.meta.meta.name"),
            ],
            Differences(result));

        Assert.Same(Child(Child(left, "flags"), "priority"), result.Differences[0].Left);
        Assert.Null(result.Differences[0].Right);
        Assert.Null(result.Differences[1].Left);
        Assert.Same(Child(Child(Child(right, "flags"), "données"), "schlüssel"), result.Differences[1].Right);

        // The root, id, source, flags, gift, données, clé, meta, meta.meta, depth and the empty name.
        Assert.Equal(11, result.MatchedCount);
    }

    [Fact]
    public void Unmatched_subtree_is_one_difference_at_its_top()
    {
        var right = Tree(Right);

        var result = Match(Tree(Left), right);

        Assert.False(result.IsMatch);

        var differences = Differences(result).ToList();
        Assert.Contains((StructureMismatchType.TypeMismatch, "$.version"), differences);
        Assert.Contains((StructureMismatchType.MissingProperty, "$.deletedAt"), differences);
        Assert.Contains((StructureMismatchType.TypeMismatch, "$.notes"), differences);
        Assert.Contains((StructureMismatchType.MissingProperty, "$.caseVariants"), differences);
        Assert.Contains((StructureMismatchType.MissingProperty, "$.comment"), differences);
        Assert.Contains((StructureMismatchType.ExtraProperty, "$.shipping"), differences);

        // caseVariants and shipping hold properties of their own, which are carried by the node only.
        Assert.DoesNotContain(differences, difference => difference.Path.StartsWith("$.caseVariants.", StringComparison.Ordinal));
        Assert.DoesNotContain(differences, difference => difference.Path.StartsWith("$.shipping.", StringComparison.Ordinal));
        Assert.Same(Child(right, "shipping"), result.Differences.Single(difference => difference.Path == "$.shipping").Right);
    }

    [Fact]
    public void Path_writes_names_that_are_not_plain_identifiers_in_brackets()
    {
        // A dot and a bracket inside names are not path syntax.
        Assert.Contains((StructureMismatchType.TypeMismatch, "$['a.b']['e[0]']"), Differences(Match(Left, Right)));

        // Two unrelated sections of one fixture: a quote, a backslash, and precomposed against decomposed "café".
        var result = Match(Left.GetProperty("escapedNames"), Left.GetProperty("unicodeNormalization"));

        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$['quote\"d']"),
                (StructureMismatchType.MissingProperty, "$['back\\\\slash']"),
                (StructureMismatchType.ExtraProperty, "$.café"),
                (StructureMismatchType.ExtraProperty, "$['café']"),
            ],
            Differences(result));
        Assert.Equal(1, result.MatchedCount);
    }

    [Fact]
    public void Property_names_are_case_sensitive()
    {
        // { name, Name, NAME } against { name }: only the exact name pairs up, and its types differ.
        var result = Match(Left.GetProperty("caseVariants"), Left.GetProperty(""));

        Assert.Equal(
            [
                (StructureMismatchType.TypeMismatch, "$.name"),
                (StructureMismatchType.MissingProperty, "$.Name"),
                (StructureMismatchType.MissingProperty, "$.NAME"),
            ],
            Differences(result));
        Assert.Equal(1, result.MatchedCount);
    }

    [Fact]
    public void Element_shape_on_one_side_only_is_reported_even_when_a_similar_shape_matched()
    {
        var left = Tree(Left.GetProperty("items")[0].GetProperty("bundle").GetProperty("items"));
        var right = Tree(Right.GetProperty("items")[0].GetProperty("bundle").GetProperty("items"));

        // { id, name, quantity } is on both sides; { id, name, quantity, length/cm } only on the left. Pairing the
        // longer left element with the right one would match 4 nodes too; the identical pair is preferred.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal([(StructureMismatchType.MissingArrayElementShape, "$[*]")], Differences(result));
        Assert.Same(left.Children[1], result.Differences[0].Left);
        Assert.Null(result.Differences[0].Right);
        Assert.Equal(5, result.MatchedCount);
    }

    [Fact]
    public void Nested_arrays_are_element_shapes_of_their_own()
    {
        var right = Tree(Right.GetProperty("matrix"));

        // Arrays of numbers and an empty array on both sides; [7, [8, 9]] only on the right. Pairing [4, 5, 6] with it
        // and [] with [1, 2, 3] would match as many nodes; the identical pairs are preferred.
        var result = Match(Tree(Left.GetProperty("matrix")), right);

        Assert.False(result.IsMatch);
        Assert.Equal([(StructureMismatchType.ExtraArrayElementShape, "$[*]")], Differences(result));
        Assert.Null(result.Differences[0].Left);
        Assert.Same(right.Children[3], result.Differences[0].Right);

        // The root, the array of numbers with its Number shape, and the empty array.
        Assert.Equal(4, result.MatchedCount);
    }

    [Fact]
    public void Array_boundaries_are_preserved()
    {
        var left = Tree(Left.GetProperty("arrayBoundaries"));
        var right = Tree(Right.GetProperty("arrayBoundaries"));

        // [[A], [B]] against [[A, B], []]: [B] is closest to [A, B], which leaves [A] with [].
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.MissingArrayElementShape, "$[*][*]"),
                (StructureMismatchType.ExtraArrayElementShape, "$[*][*]"),
            ],
            Differences(result));
        Assert.Same(left.Children[0].Children[0], result.Differences[0].Left);
        Assert.Same(right.Children[0].Children[0], result.Differences[1].Right);

        // The root, [A] with [], and [B] with [A, B] sharing B and its two properties.
        Assert.Equal(6, result.MatchedCount);
    }

    [Fact]
    public void Similar_element_shapes_are_paired_to_report_the_exact_differences()
    {
        var left = Tree(Left.GetProperty("customer"));
        var right = Tree(Right.GetProperty("customer"));
        var leftAddresses = Child(left, "addresses").Children;
        var rightAddresses = Child(right, "addresses").Children;

        var result = Match(left, right);

        // No address shape is on both sides. Each of the two left addresses matches 6 nodes of the first right one
        // and 9 of the second, so both ways of pairing them match 15 and document order decides: first with first,
        // second with second. The short third right address has no partner.
        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.TypeMismatch, "$.größe"),
                (StructureMismatchType.ExtraProperty, "$.preferences.newsletter"),
                (StructureMismatchType.ExtraArrayElementShape, "$.tags[*]"),
                (StructureMismatchType.TypeMismatch, "$.addresses[*].geo"),
                (StructureMismatchType.ExtraProperty, "$.addresses[*].geo.alt"),
                (StructureMismatchType.MissingProperty, "$.addresses[*].instructions"),
                (StructureMismatchType.ExtraArrayElementShape, "$.addresses[*]"),
            ],
            Differences(result));

        Assert.Same(Child(leftAddresses[0], "geo"), result.Differences[3].Left);
        Assert.Same(Child(rightAddresses[0], "geo"), result.Differences[3].Right);
        Assert.Same(Child(Child(rightAddresses[1], "geo"), "alt"), result.Differences[4].Right);
        Assert.Same(Child(leftAddresses[1], "instructions"), result.Differences[5].Left);
        Assert.Same(rightAddresses[2], result.Differences[6].Right);

        // The root, five primitives, preferences, tags, and addresses with pairs of 6 and 9 matched nodes.
        Assert.Equal(24, result.MatchedCount);
    }

    [Fact]
    public void Mixed_array_pairs_only_containers_of_the_same_type()
    {
        var left = Tree(Left.GetProperty("history"));

        var result = Match(left, Tree(Right.GetProperty("history")));

        // String, Boolean and Null are on both sides. The number and the empty array have no partner; the two
        // objects are paired with each other, and so are the two arrays holding an object.
        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.MissingArrayElementShape, "$[*]"),
                (StructureMismatchType.MissingProperty, "$[*].meta.attempt"),
                (StructureMismatchType.ExtraProperty, "$[*][*].meta.body"),
                (StructureMismatchType.MissingArrayElementShape, "$[*]"),
            ],
            Differences(result));
        Assert.Same(left.Children[1], result.Differences[0].Left);
        Assert.Same(left.Children[6], result.Differences[3].Left);

        // The root, three primitive shapes, the object pair (5) and the array pair (7).
        Assert.Equal(16, result.MatchedCount);
    }

    [Fact]
    public void Objects_without_a_common_property_name_are_not_paired()
    {
        // [{ alpha, beta }, { beta, gamma }] against [{ item, group }, { item, group: { item } }].
        var result = Match(
            Left.GetProperty("arrayElementOrder").GetProperty("differentIds"),
            Left.GetProperty("idListOrder"));

        Assert.Equal(
            [
                (StructureMismatchType.MissingArrayElementShape, "$[*]"),
                (StructureMismatchType.MissingArrayElementShape, "$[*]"),
                (StructureMismatchType.ExtraArrayElementShape, "$[*]"),
                (StructureMismatchType.ExtraArrayElementShape, "$[*]"),
            ],
            Differences(result));
        Assert.Equal(1, result.MatchedCount);
    }

    [Fact]
    public void Duplicate_property_names_are_paired_by_occurrence_and_their_count_is_preserved()
    {
        // { name, name, age } on both sides.
        AssertFullMatch(4, MatchSection("duplicateKeys"));

        var duplicates = Tree(Left.GetProperty("duplicateKeys"));
        var single = Tree(Left.GetProperty(""));

        // Against { name }: the first name pairs up, the second is surplus, and age is missing.
        var surplusOnLeft = Match(duplicates, single);

        Assert.False(surplusOnLeft.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.name"),
                (StructureMismatchType.MissingProperty, "$.age"),
            ],
            Differences(surplusOnLeft));
        Assert.Same(duplicates.Children[1], surplusOnLeft.Differences[0].Left);
        Assert.Null(surplusOnLeft.Differences[0].Right);
        Assert.Equal(2, surplusOnLeft.MatchedCount);

        var surplusOnRight = Match(single, duplicates);

        Assert.Equal(
            [
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.name"),
                (StructureMismatchType.ExtraProperty, "$.age"),
            ],
            Differences(surplusOnRight));
        Assert.Null(surplusOnRight.Differences[0].Left);
        Assert.Same(duplicates.Children[1], surplusOnRight.Differences[0].Right);
        Assert.Equal(2, surplusOnRight.MatchedCount);
    }

    [Fact]
    public void Duplicate_property_names_of_different_types_match_in_any_order()
    {
        var left = Tree(Left.GetProperty("duplicateTypeOrder"));
        var right = Tree(Right.GetProperty("duplicateTypeOrder"));

        // { value: Number, value: String, retyped: { value: Boolean, value: String } } against the same object with
        // both pairs of values swapped: the root, two values, retyped and its two values.
        AssertFullMatch(6, Match(left, right));
        AssertFullMatch(6, Match(right, left));

        // Every occurrence is still a node of its own, in document order.
        Assert.Equal(
            [("value", JsonValueKind.Number), ("value", JsonValueKind.String), ("retyped", JsonValueKind.Object)],
            left.Children.Select(child => (child.PropertyName!, child.ValueKind)));
        Assert.Equal(
            [("value", JsonValueKind.String), ("value", JsonValueKind.Number), ("retyped", JsonValueKind.Object)],
            right.Children.Select(child => (child.PropertyName!, child.ValueKind)));
    }

    [Fact]
    public void Duplicate_property_name_of_a_different_type_is_a_type_mismatch_between_the_two_unpaired_occurrences()
    {
        var left = Tree(Left.GetProperty("duplicateTypeOrder"));
        var retyped = Tree(Right.GetProperty("duplicateTypeOrder").GetProperty("retyped"));

        // { value: Number, value: String, retyped } against { value: String, value: Boolean }: the strings pair up,
        // which leaves the number with the boolean.
        var result = Match(left, retyped);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.TypeMismatch, "$.value"),
                (StructureMismatchType.MissingProperty, "$.retyped"),
            ],
            Differences(result));
        Assert.Same(left.Children[0], result.Differences[0].Left);
        Assert.Same(retyped.Children[1], result.Differences[0].Right);

        // The root and the string.
        Assert.Equal(2, result.MatchedCount);
    }

    [Fact]
    public void Removed_duplicate_occurrence_is_reported_as_the_occurrence_whose_type_has_no_partner()
    {
        var booleanFirst = Tree(Left.GetProperty("duplicateTypeOrder").GetProperty("retyped"));
        var stringFirst = Tree(Right.GetProperty("duplicateTypeOrder").GetProperty("retyped"));
        var single = Tree(Right.GetProperty("items")[1].GetProperty("attributes")[0]);

        // { value: Boolean, value: String } against { name, value: String }: the second occurrence pairs up and the
        // first one is the surplus.
        var surplusFirst = Match(booleanFirst, single);

        Assert.False(surplusFirst.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.value"),
                (StructureMismatchType.ExtraProperty, "$.name"),
            ],
            Differences(surplusFirst));
        Assert.Same(booleanFirst.Children[0], surplusFirst.Differences[0].Left);
        Assert.Null(surplusFirst.Differences[0].Right);
        Assert.Equal(2, surplusFirst.MatchedCount);

        // { value: String, value: Boolean } against the same object: now the second occurrence is the surplus.
        var surplusSecond = Match(stringFirst, single);

        Assert.Equal(
            [
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.value"),
                (StructureMismatchType.ExtraProperty, "$.name"),
            ],
            Differences(surplusSecond));
        Assert.Same(stringFirst.Children[1], surplusSecond.Differences[0].Left);
        Assert.Equal(2, surplusSecond.MatchedCount);

        // With the arguments swapped the surplus is on the right.
        var surplusOnRight = Match(single, booleanFirst);

        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$.name"),
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.value"),
            ],
            Differences(surplusOnRight));
        Assert.Null(surplusOnRight.Differences[1].Left);
        Assert.Same(booleanFirst.Children[0], surplusOnRight.Differences[1].Right);
        Assert.Equal(2, surplusOnRight.MatchedCount);
    }

    [Fact]
    public void Duplicate_objects_of_the_same_shape_are_paired_whatever_their_order()
    {
        var left = Tree(Left.GetProperty("duplicateStructures").GetProperty("objects"));
        var right = Tree(Right.GetProperty("duplicateStructures").GetProperty("objects"));

        // { entry: { north }, entry: { north, south } } against { entry: { south, north }, entry: { north } }:
        // the root, { north } with its property and { north, south } with its two.
        AssertFullMatch(6, Match(left, right));
        AssertFullMatch(6, Match(right, left));

        // Both occurrences are still there on each side, in document order.
        Assert.Equal([("entry", 1), ("entry", 2)], left.Children.Select(child => (child.PropertyName!, child.Children.Count)));
        Assert.Equal([("entry", 2), ("entry", 1)], right.Children.Select(child => (child.PropertyName!, child.Children.Count)));
    }

    [Fact]
    public void Duplicate_object_of_the_same_shape_is_paired_before_the_remaining_occurrences()
    {
        var left = Tree(Left.GetProperty("duplicateStructures").GetProperty("objects"));
        var right = Tree(Left.GetProperty("duplicateStructures").GetProperty("partialObjects"));

        // [{ north }, { north, south }] against [{ north, south }, { south, east }]: the second left occurrence has
        // the shape of the first right one, which leaves { north } with { south, east }. Pairing { north } with
        // { north, south } and { north, south } with { south, east } would match 4 nodes too; the identical pair is
        // preferred.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$.entry.north"),
                (StructureMismatchType.ExtraProperty, "$.entry.south"),
                (StructureMismatchType.ExtraProperty, "$.entry.east"),
            ],
            Differences(result));
        Assert.Same(left.Children[0].Children[0], result.Differences[0].Left);
        Assert.Same(right.Children[1].Children[0], result.Differences[1].Right);
        Assert.Same(right.Children[1].Children[1], result.Differences[2].Right);

        // The root, { north } with { south, east } as objects, and { north, south } with its two properties.
        Assert.Equal(5, result.MatchedCount);
    }

    [Fact]
    public void Partially_similar_duplicate_objects_are_paired_to_match_the_most_nodes()
    {
        var left = Tree(Left.GetProperty("duplicateStructures").GetProperty("partialObjects"));
        var right = Tree(Right.GetProperty("duplicateStructures").GetProperty("partialObjects"));

        // [{ north, south }, { south, east }] against [{ south, east, west }, { north, south, west }]: no shape is
        // on both sides. First with first and second with second would share only south in each pair; crosswise,
        // each pair shares two properties and only the two west properties are left over.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.ExtraProperty, "$.entry.west"),
                (StructureMismatchType.ExtraProperty, "$.entry.west"),
            ],
            Differences(result));

        // Each occurrence is paired exactly once: the first left one with the second right one, and the reverse.
        Assert.Null(result.Differences[0].Left);
        Assert.Same(Child(right.Children[1], "west"), result.Differences[0].Right);
        Assert.Same(Child(right.Children[0], "west"), result.Differences[1].Right);

        // The root, and each pair as an object with two properties.
        Assert.Equal(7, result.MatchedCount);
    }

    [Fact]
    public void Duplicate_objects_are_paired_by_the_best_assignment_of_the_whole_group_not_pair_by_pair()
    {
        var left = Tree(Left.GetProperty("duplicateStructures").GetProperty("assignedObjects"));
        var right = Tree(Right.GetProperty("duplicateStructures").GetProperty("assignedObjects"));

        // [{ north, south, east }, { east, west }] against [{ north, south, east, west }, { north, south }]: the
        // best single pair is the first with the first (4 nodes), but it leaves { east, west } with
        // { north, south }, which share nothing: 5 nodes. Crosswise the pairs match 3 nodes each: 6 nodes.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$.entry.east"),
                (StructureMismatchType.ExtraProperty, "$.entry.north"),
                (StructureMismatchType.ExtraProperty, "$.entry.south"),
            ],
            Differences(result));
        Assert.Same(Child(left.Children[0], "east"), result.Differences[0].Left);
        Assert.Same(Child(right.Children[0], "north"), result.Differences[1].Right);
        Assert.Same(Child(right.Children[0], "south"), result.Differences[2].Right);

        // The root and two pairs of 3 nodes.
        Assert.Equal(7, result.MatchedCount);
    }

    [Fact]
    public void Array_element_shapes_are_paired_by_the_best_assignment_of_the_whole_group_not_pair_by_pair()
    {
        var left = Tree(Left.GetProperty("duplicateStructures").GetProperty("assignedElements"));
        var right = Tree(Right.GetProperty("duplicateStructures").GetProperty("assignedElements"));

        // [{ north, south }, { south }] against [{ north, south, east }, { north }]: the best single pair is the
        // first with the first (3 nodes), but it leaves { south } with { north }, which cannot be paired.
        // Crosswise both pairs can be made and match 2 nodes each.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$[*].south"),
                (StructureMismatchType.ExtraProperty, "$[*].north"),
                (StructureMismatchType.ExtraProperty, "$[*].east"),
            ],
            Differences(result));
        Assert.Same(Child(left.Children[0], "south"), result.Differences[0].Left);
        Assert.Same(Child(right.Children[0], "north"), result.Differences[1].Right);
        Assert.Same(Child(right.Children[0], "east"), result.Differences[2].Right);

        // The root and two pairs of 2 nodes.
        Assert.Equal(5, result.MatchedCount);
    }

    [Fact]
    public void Identical_shapes_are_not_paired_first_when_another_pairing_matches_more_nodes()
    {
        var left = Tree(Left.GetProperty("duplicateStructures").GetProperty("assignedElements"));
        var right = Tree(Right.GetProperty("duplicateStructures").GetProperty("exactFirstElements"));

        // [{ north, south }, { south }] against [{ south, north }, { north }]: { north, south } is on both sides, but
        // pairing it with itself (3 nodes) leaves { south } and { north }, which share no name and cannot be paired.
        // Pairing { north, south } with { north } and { south } with { south, north } matches 2 nodes each, 4 in all.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$[*].south"),
                (StructureMismatchType.ExtraProperty, "$[*].north"),
            ],
            Differences(result));
        Assert.Same(Child(left.Children[0], "south"), result.Differences[0].Left);
        Assert.Null(result.Differences[0].Right);
        Assert.Null(result.Differences[1].Left);
        Assert.Same(Child(right.Children[0], "north"), result.Differences[1].Right);

        // The root and two pairs of 2 nodes: the maximum any pairing can reach.
        Assert.Equal(5, result.MatchedCount);

        // Swapping the sides gives the same count.
        Assert.Equal(5, Match(right, left).MatchedCount);
    }

    [Fact]
    public void Empty_objects_are_one_shape_and_are_paired()
    {
        var assignmentLimit = Right.GetProperty("assignmentLimit");

        // [[], {}] against [null, {}]: the empty objects share no property name but are the same shape. The empty
        // array and the null have no partner.
        var result = Match(assignmentLimit[6], assignmentLimit[5]);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.MissingArrayElementShape, "$[*]"),
                (StructureMismatchType.ExtraArrayElementShape, "$[*]"),
            ],
            Differences(result));
        Assert.Equal(JsonValueKind.Array, result.Differences[0].Left!.ValueKind);
        Assert.Equal(JsonValueKind.Null, result.Differences[1].Right!.ValueKind);

        // The root and the empty object.
        Assert.Equal(2, result.MatchedCount);

        // Two arrays holding an empty object are a full match.
        AssertFullMatch(3, Match(assignmentLimit[2], assignmentLimit[2]));
    }

    // The speculativeLimit section holds two objects on each side, { kinds: [Number, String, Boolean] } and
    // { kinds: [Null] }, with an added property on the right so that no shape is on both sides. The crosswise pairs
    // hold an array of three shapes against one; they are compared, but the best assignment does not take them.
    [Fact]
    public void Groups_nested_in_the_pairs_of_a_group_are_assigned_on_their_own()
    {
        var left = Tree(Left.GetProperty("speculativeLimit"));
        var right = Tree(Right.GetProperty("speculativeLimit"));

        // The first with the first and the second with the second: the kinds arrays are then identical, and the
        // only differences are the two added properties.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.ExtraProperty, "$[*].added"),
                (StructureMismatchType.ExtraProperty, "$[*].added"),
            ],
            Differences(result));
        Assert.Same(Child(right.Children[0], "added"), result.Differences[0].Right);
        Assert.Same(Child(right.Children[1], "added"), result.Differences[1].Right);

        // The root, { kinds } with its array and three shapes, and { kinds } with its array and one shape.
        Assert.Equal(9, result.MatchedCount);
        Assert.Equal(9, Match(right, left).MatchedCount);
    }

    // The assignmentLimit section holds 33 distinct element shapes on each side: two objects, and one array for each
    // of the 31 non-empty sets of String, Number, Boolean, Null and the empty array. In Right.json the first 30 of
    // those arrays also hold an empty object, and the last one does not. It was written for an assignment limit of
    // 32 that no longer exists; it is now the largest group the fixtures hold.
    [Fact]
    public void Group_of_33_shapes_is_assigned_as_a_whole()
    {
        var left = Tree(Left.GetProperty("assignmentLimit"));
        var right = Tree(Right.GetProperty("assignmentLimit"));

        Assert.Equal(33, left.Children.Count);
        Assert.Equal(33, right.Children.Count);

        // The last array is on both sides and is paired with itself. The two objects are paired crosswise,
        // { north, south } with { north } and { south } with { north, south, east }, and each of the 30 other arrays
        // is paired with the right array that holds the same shapes and the empty object.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            new[]
            {
                (StructureMismatchType.MissingProperty, "$[*].south"),
                (StructureMismatchType.ExtraProperty, "$[*].north"),
                (StructureMismatchType.ExtraProperty, "$[*].east"),
            }.Concat(Enumerable.Repeat((StructureMismatchType.ExtraArrayElementShape, "$[*][*]"), 30)),
            Differences(result));

        // Every array is paired with its own counterpart: the extra shape is the empty object of right array i.
        for (var index = 0; index < 30; index++)
        {
            Assert.Same(right.Children[index + 2].Children[^1], result.Differences[index + 3].Right);
            Assert.Equal(JsonValueKind.Object, result.Differences[index + 3].Right!.ValueKind);
        }

        // The root, two pairs of 2 nodes, and each of the 31 arrays with its shapes: 5 sets of one shape, 10 of
        // two, 10 of three, 5 of four and 1 of five, which is 31 arrays and 80 shapes.
        Assert.Equal(116, result.MatchedCount);
        Assert.Equal(116, Match(right, left).MatchedCount);
    }

    [Fact]
    public void Group_of_33_shapes_against_two_is_assigned_as_a_whole()
    {
        var left = Tree(Left.GetProperty("assignmentLimit"));
        var right = Tree(Right.GetProperty("duplicateStructures").GetProperty("assignedElements"));

        // 33 shapes against [{ north, south, east }, { north }]: no shape is on both sides. { north, south } with
        // { north, south, east } is the best single pair, but it leaves { south } with { north }, which cannot be
        // paired. Crosswise both objects are paired and match 2 nodes each. The 31 arrays have no array to be paired
        // with. Under the former limit of 32 this group was paired greedily and matched 4 nodes.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal(
            new[]
            {
                (StructureMismatchType.MissingProperty, "$[*].south"),
                (StructureMismatchType.ExtraProperty, "$[*].north"),
                (StructureMismatchType.ExtraProperty, "$[*].east"),
            }.Concat(Enumerable.Repeat((StructureMismatchType.MissingArrayElementShape, "$[*]"), 31)),
            Differences(result));
        Assert.Same(Child(left.Children[0], "south"), result.Differences[0].Left);
        Assert.Same(Child(right.Children[0], "north"), result.Differences[1].Right);
        Assert.Same(Child(right.Children[0], "east"), result.Differences[2].Right);
        for (var index = 2; index < 33; index++)
        {
            Assert.Same(left.Children[index], result.Differences[index + 1].Left);
        }

        // The root and two pairs of 2 nodes.
        Assert.Equal(5, result.MatchedCount);

        // With the sides swapped the same pairs are made: { north, south, east } with { south }, then { north } with
        // { north, south }.
        var swapped = Match(right, left);

        Assert.Equal(
            new[]
            {
                (StructureMismatchType.MissingProperty, "$[*].north"),
                (StructureMismatchType.MissingProperty, "$[*].east"),
                (StructureMismatchType.ExtraProperty, "$[*].south"),
            }.Concat(Enumerable.Repeat((StructureMismatchType.ExtraArrayElementShape, "$[*]"), 31)),
            Differences(swapped));
        Assert.Equal(5, swapped.MatchedCount);
    }

    // Every section of both fixtures, and both whole documents, each against every other and against itself.
    private static List<JsonStructureNode> AllSections() =>
        Left.EnumerateObject().Select(section => Tree(section.Value))
            .Concat(Right.EnumerateObject().Select(section => Tree(section.Value)))
            .Append(Tree(Left))
            .Append(Tree(Right))
            .ToList();

    [Fact]
    public void Matched_count_is_the_same_whichever_side_is_left()
    {
        var sections = AllSections();

        // The maximum over all pairings does not depend on which side is the left one; a pairing chosen pair by pair
        // can. Full matches and mismatches must both be present for the comparison to mean anything.
        var outcomes = new HashSet<bool>();
        foreach (var left in sections)
        {
            foreach (var right in sections)
            {
                var result = Match(left, right);
                var swapped = Match(right, left);

                outcomes.Add(result.IsMatch);
                Assert.Equal(result.IsMatch, swapped.IsMatch);
                Assert.Equal(result.MatchedCount, swapped.MatchedCount);
            }
        }
        Assert.Equal([false, true], outcomes.OrderBy(outcome => outcome));
    }

    [Fact]
    public void Matched_count_is_bounded_by_the_size_of_each_side_and_reaches_it_only_for_a_full_match()
    {
        var sections = AllSections();

        // A node against itself matches every node, so that count is the size of its structure as MatchedCount
        // counts it. Against another node the count cannot exceed either size, and it reaches both exactly when
        // nothing is left unmatched on either side, which is a full match.
        var sizes = sections.Select(section => Match(section, section).MatchedCount).ToList();

        foreach (var (left, leftSize) in sections.Zip(sizes))
        {
            foreach (var (right, rightSize) in sections.Zip(sizes))
            {
                var result = Match(left, right);

                Assert.InRange(result.MatchedCount, 0, Math.Min(leftSize, rightSize));
                Assert.Equal(result.IsMatch, result.MatchedCount == leftSize && result.MatchedCount == rightSize);
            }
        }
    }

    [Fact]
    public void Equally_good_assignments_are_decided_by_document_order()
    {
        var left = Tree(Left.GetProperty("duplicateStructures").GetProperty("objects"));
        var right = Tree(Right.GetProperty("duplicateStructures").GetProperty("partialObjects"));

        // [{ north }, { north, south }] against [{ south, east, west }, { north, south, west }]: first with first
        // and second with second match 1 and 3 nodes, crosswise 2 and 2. The first left occurrence takes the
        // first right one.
        var result = Match(left, right);

        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$.entry.north"),
                (StructureMismatchType.ExtraProperty, "$.entry.south"),
                (StructureMismatchType.ExtraProperty, "$.entry.east"),
                (StructureMismatchType.ExtraProperty, "$.entry.west"),
                (StructureMismatchType.ExtraProperty, "$.entry.west"),
            ],
            Differences(result));
        Assert.Same(Child(left.Children[0], "north"), result.Differences[0].Left);
        Assert.Same(Child(right.Children[0], "west"), result.Differences[3].Right);
        Assert.Same(Child(right.Children[1], "west"), result.Differences[4].Right);
        Assert.Equal(5, result.MatchedCount);
    }

    [Fact]
    public void Duplicate_arrays_of_the_same_shapes_are_paired_whatever_their_order()
    {
        var left = Tree(Left.GetProperty("duplicateStructures").GetProperty("arrays"));
        var right = Tree(Right.GetProperty("duplicateStructures").GetProperty("arrays"));

        // { series: [Number, Number], series: [{ north }, [Boolean]] } against
        // { series: [[Boolean], { north }, [Boolean, Boolean]], series: [Number] }: within each array, element order
        // and repeated shapes play no part. The root, the array of numbers with its shape, and the other array
        // with { north } and [Boolean].
        AssertFullMatch(8, Match(left, right));
        AssertFullMatch(8, Match(right, left));

        Assert.Equal([("series", 2), ("series", 2)], left.Children.Select(child => (child.PropertyName!, child.Children.Count)));
        Assert.Equal([("series", 3), ("series", 1)], right.Children.Select(child => (child.PropertyName!, child.Children.Count)));
    }

    [Fact]
    public void Duplicate_arrays_of_different_shapes_report_the_exact_difference_and_the_surplus_occurrence()
    {
        var two = Tree(Left.GetProperty("duplicateStructures").GetProperty("arrays"));
        var three = Tree(Right.GetProperty("duplicateStructures").GetProperty("partialArrays"));

        // [Number] and [{ north }, [Boolean]] against [{ north, south }, [Boolean]], [String] and [Number]: the
        // arrays of numbers have the same shape and are paired first, although one is first and the other third.
        // The other left array is paired with the first array left over, and the array of strings is the surplus.
        var surplusOnRight = Match(two, three);

        Assert.False(surplusOnRight.IsMatch);
        Assert.Equal(
            [
                (StructureMismatchType.ExtraProperty, "$.series[*].south"),
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.series"),
            ],
            Differences(surplusOnRight));
        Assert.Same(Child(three.Children[0].Children[0], "south"), surplusOnRight.Differences[0].Right);
        Assert.Null(surplusOnRight.Differences[1].Left);
        Assert.Same(three.Children[1], surplusOnRight.Differences[1].Right);

        // The root, the arrays of numbers with their shape, and the other pair with [Boolean] and { north }.
        Assert.Equal(8, surplusOnRight.MatchedCount);

        var surplusOnLeft = Match(three, two);

        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$.series[*].south"),
                (StructureMismatchType.DuplicatePropertyCountMismatch, "$.series"),
            ],
            Differences(surplusOnLeft));
        Assert.Same(Child(three.Children[0].Children[0], "south"), surplusOnLeft.Differences[0].Left);
        Assert.Same(three.Children[1], surplusOnLeft.Differences[1].Left);
        Assert.Null(surplusOnLeft.Differences[1].Right);
        Assert.Equal(8, surplusOnLeft.MatchedCount);
    }

    [Fact]
    public void Single_quotes_in_property_names_are_preserved_and_escaped_in_paths()
    {
        var left = Tree(Left.GetProperty("quotedNames"));
        var right = Tree(Right.GetProperty("quotedNames"));

        Assert.Equal("owner's name", left.Children[0].PropertyName);
        Assert.Equal("it's valid", Child(left, "child").Children[0].PropertyName);

        // "owner's name" and the nested "it's valid" are on both sides with different values. Only the right has
        // "it\'s", whose backslash is escaped too, so its path cannot be read as a name ending before the quote.
        var result = Match(left, right);

        Assert.False(result.IsMatch);
        Assert.Equal([(StructureMismatchType.ExtraProperty, "$.child['it\\\\\\'s']")], Differences(result));
        Assert.Null(result.Differences[0].Left);
        Assert.Same(Child(Child(right, "child"), "it\\'s"), result.Differences[0].Right);

        // The root, "owner's name", child and "it's valid".
        Assert.Equal(4, result.MatchedCount);

        // { "owner's name", child } against { "it's valid" }: nothing but the roots pairs up.
        var unrelated = Match(left, Child(left, "child"));

        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$['owner\\'s name']"),
                (StructureMismatchType.MissingProperty, "$.child"),
                (StructureMismatchType.ExtraProperty, "$['it\\'s valid']"),
            ],
            Differences(unrelated));
        Assert.Same(left.Children[0], unrelated.Differences[0].Left);
        Assert.Same(Child(left, "child").Children[0], unrelated.Differences[2].Right);
        Assert.Equal(1, unrelated.MatchedCount);

        // The quoted names themselves match when nothing else differs.
        AssertFullMatch(2, Match(Child(left, "child"), Child(left, "child")));
    }

    [Fact]
    public void Path_escapes_control_characters_in_bracketed_names()
    {
        // The tab, line feed and carriage return in the names are written as their JSON escapes, so that a path is one
        // line; the emoji, visible, is written as it is. Two unrelated sections of one fixture.
        var result = Match(Left.GetProperty("controlCharacters"), Left.GetProperty("supplementaryUnicode"));

        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$['tab\\there']"),
                (StructureMismatchType.MissingProperty, "$['line\\nbreak']"),
                (StructureMismatchType.MissingProperty, "$['carriage\\rreturn']"),
                (StructureMismatchType.ExtraProperty, "$['🚀']"),
                (StructureMismatchType.ExtraProperty, "$['status🚀']"),
            ],
            Differences(result));
        Assert.Equal(1, result.MatchedCount);

        // The names themselves keep the control characters; only the path is escaped.
        Assert.Equal("tab\there", result.Differences[0].Left!.PropertyName);
        Assert.Equal("line\nbreak", result.Differences[1].Left!.PropertyName);
        Assert.All(result.Differences, difference => Assert.DoesNotContain(difference.Path, char.IsControl));
    }

    [Fact]
    public void Path_escapes_every_invisible_character_and_keeps_visible_unicode_plain()
    {
        // "pathEscapes" against the top-level "" object, which holds only "name": every name of the section is
        // missing and shows how its path is written. Backspace and form feed have short escapes; every other control
        // character (NUL, unit separator, DEL, the C1 control NEL), the format characters (zero-width space, byte
        // order mark, the supplementary tag character U+E0001 as its two surrogates), the line separator and the
        // no-break space are \u escapes, in upper-case hex. A name made of letters is plain even when a letter is
        // outside the Basic Multilingual Plane; a name starting with a digit, Arabic-Indic or not, is bracketed.
        var result = Match(Left.GetProperty("pathEscapes"), Left.GetProperty(""));

        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$['back\\bspace\\fform']"),
                (StructureMismatchType.MissingProperty, "$['nul\\u0000unit\\u001F']"),
                (StructureMismatchType.MissingProperty, "$['del\\u007F']"),
                (StructureMismatchType.MissingProperty, "$['nel\\u0085']"),
                (StructureMismatchType.MissingProperty, "$['zero\\u200Bwidth']"),
                (StructureMismatchType.MissingProperty, "$['line\\u2028sep']"),
                (StructureMismatchType.MissingProperty, "$['no\\u00A0break']"),
                (StructureMismatchType.MissingProperty, "$['\\uFEFFbom']"),
                (StructureMismatchType.MissingProperty, "$.\U00010400plain"),
                (StructureMismatchType.MissingProperty, "$['tag\\uDB40\\uDC01']"),
                (StructureMismatchType.MissingProperty, "$['\u0663rd']"),
                (StructureMismatchType.MissingProperty, "$._"),
                (StructureMismatchType.ExtraProperty, "$.name"),
            ],
            Differences(result));
        Assert.Equal(1, result.MatchedCount);

        // Every path is a single line of visible characters, and the nodes keep their names unchanged.
        Assert.All(result.Differences, difference => Assert.DoesNotContain(difference.Path, character =>
            char.IsControl(character) || character is '\u2028' or '\u2029' or '\u200b' or '\ufeff' or '\u00a0'));
        Assert.Equal("nul\0unit\u001f", result.Differences[1].Left!.PropertyName);
        Assert.Equal("\ufeffbom", result.Differences[7].Left!.PropertyName);
        Assert.Equal("tag\U000E0001", result.Differences[9].Left!.PropertyName);
    }

    [Fact]
    public void Path_escapes_names_below_escaped_and_plain_parents()
    {
        // Right adds "2nd" under the line-separator name and the ANSI escape sequence and a paragraph separator at the
        // top, and retypes the DEL name under "_". Escaping does not change how names pair up: only these four differ.
        var result = MatchSection("pathEscapes");

        Assert.Equal(
            [
                (StructureMismatchType.ExtraProperty, "$['line\\u2028sep']['2nd']"),
                (StructureMismatchType.TypeMismatch, "$._['del\\u007F']"),
                (StructureMismatchType.ExtraProperty, "$['\\u001B[0m']"),
                (StructureMismatchType.ExtraProperty, "$['para\\u2029graph']"),
            ],
            Differences(result));
        Assert.Equal("\u001b[0m", result.Differences[2].Right!.PropertyName);
        Assert.Equal("del\u007f", result.Differences[1].Left!.PropertyName);
        Assert.Equal("del\u007f", result.Differences[1].Right!.PropertyName);

        // The root, the twelve top-level names and "1st" below the line-separator name; only the retyped DEL name fails.
        Assert.Equal(14, result.MatchedCount);

        // Escaping is one way: the same section fully matches itself, and so does the whole left document.
        AssertFullMatch(15, Match(Left.GetProperty("pathEscapes"), Left.GetProperty("pathEscapes")));
    }

    [Fact]
    public void Lone_surrogate_names_are_distinct_names_and_are_escaped_in_paths()
    {
        // The root, four strings, nested, its array, the array's object and that object's string, values and its
        // one String shape.
        AssertFullMatch(11, MatchSection("loneSurrogates"));

        // Against its own "nested" object, which holds only the lone low surrogate name: the lone high surrogate
        // name (twice), the pair and the two plain names are missing, and the low one is an array against a string.
        // A lone surrogate is invisible, so it is written as its \u escape; the pair is visible and written as it is.
        var result = Match(Left.GetProperty("loneSurrogates"), Left.GetProperty("loneSurrogates").GetProperty("nested"));

        Assert.Equal(
            [
                (StructureMismatchType.MissingProperty, "$['\\uD800']"),
                (StructureMismatchType.TypeMismatch, "$['\\uDC00']"),
                (StructureMismatchType.MissingProperty, "$['\U0001F600']"),
                (StructureMismatchType.MissingProperty, "$['\\uD800']"),
                (StructureMismatchType.MissingProperty, "$.nested"),
                (StructureMismatchType.MissingProperty, "$.values"),
            ],
            Differences(result));
        Assert.Equal("\uD800", result.Differences[0].Left!.PropertyName);
        Assert.Equal("\uDC00", result.Differences[1].Left!.PropertyName);
        Assert.Equal(1, result.MatchedCount);
    }

    [Fact]
    public void Stringified_json_is_an_ordinary_string_leaf()
    {
        var left = Tree(Load("Stringified", "LeftStringify.txt"));
        var right = Tree(Load("Stringified", "RightStringify.txt"));

        Assert.Equal(JsonValueKind.String, left.ValueKind);
        Assert.Empty(left.Children);

        // The two documents inside the strings differ structurally; the strings themselves do not.
        AssertFullMatch(1, Match(left, right));

        // The same document as a string and as an object.
        var againstObject = Match(left, Tree(Left));

        Assert.Equal([(StructureMismatchType.TypeMismatch, "$")], Differences(againstObject));
        Assert.Equal(JsonValueKind.String, againstObject.Differences[0].Left!.ValueKind);
        Assert.Equal(JsonValueKind.Object, againstObject.Differences[0].Right!.ValueKind);
    }
}
