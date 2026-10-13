# Traverse

Fixture paths and counts below are relative to the `core` document unless explicitly identified as `stress`. Both sections live in the approved JSON pair; selecting them preserves the original test scenarios.

`JsonStructureTraverser.Traverse` walks a JSON element recursively, once, and returns it as a tree of `JsonStructureNode`. Matching consumes this tree. When differences exist, the public facade separately walks source elements and this tree in lockstep to attach original locations and detached snapshots.

## Purpose

Captures the structure and the primitive values of a document in one pass, in a form that no longer depends on the `JsonDocument` it came from. It does not assign IDs, compare, score, or report differences.

## Input and output

- `JsonElement element`: the element to walk, of any value kind. `default(JsonElement)`, whose kind is `Undefined`, is accepted too.
- `string? propertyName`: the name the element is held under. Callers leave it out; the recursion supplies it for object properties.
- Optional internal `ComparisonBudget`: counts every visited node and polls cancellation. Public Match supplies it; parsing depth follows `JJMatchSettings.MaxDepth`, which has no upper bound. Existing internal callers may omit it.
- Returns the `JsonStructureNode` for the element. `JsonStructureNode.cs` lives in this folder.

| Property | Type | Content |
| --- | --- | --- |
| `PropertyName` | `string?` | The decoded property name. `null` for the root and for array elements. |
| `ValueKind` | `JsonValueKind` | The kind exactly as parsed. |
| `Value` | `string?` | The decoded text of a string, or the raw text of a number. `null` for every other kind, and for a string whose UTF-8 bytes are invalid. |
| `Children` | `IReadOnlyList<JsonStructureNode>` | Object properties or array elements, in document order. |

Nodes are immutable: every property is set by the constructor and has no setter.

## Behavior

- Every JSON value becomes exactly one node: the root, each object property value and each array element.
- An object's children are its properties and an array's children are its elements, in document order. Nothing is sorted, merged or dropped.
- Object property order is preserved as written. Ignoring it is a matching rule, not an extraction rule.
- Array element order and boundaries are preserved: a nested array is a node of its own with its own children, and an empty inner array is a node without children. An element's index is its position in its parent's `Children`.
- Duplicate keys in one object are separate children with the same `PropertyName`, each with its own value and subtree. An occurrence index is the number of earlier siblings with that name.
- `PropertyName` is `null` only for the root and for array elements. A property whose name is the empty string has `PropertyName` `""`.
- Names are the decoded names the parser returns, compared and stored without normalization: escapes, control characters and supplementary characters are decoded, and case and Unicode form are kept as written.
- Strings are stored decoded: an escaped spelling (`caf\u00e9`, `\uD83D\uDE80`, `\t`, `\"`, `\\`) and the literal one give the same `Value`.
- A string's `Value` is always decoded text, never raw JSON text. A lone surrogate escape (`"\uD800"`, `"\uDC00"`), which the parser accepts but `GetString` rejects, is decoded by `Traverse` itself from the raw text: each `\u` escape becomes the UTF-16 unit it names, whether or not it forms a pair, so the value is kept exactly and a lone surrogate inside other text stays in place. The same applies to property names, which `JsonProperty.Name` rejects in the same way: the object's UTF-8 text is read again for its names only, which are decoded by the same rule; this costs nothing for names that decode normally, and is slow for those that do not (see Known limitations in `Docs/Decisions.md`). A string whose UTF-8 bytes are invalid cannot be represented and keeps its node of kind `String` with `Value` `null`; `Traverse` does not throw for it, so the rest of the document is still walked.
- Numbers are stored as their raw text, exactly as written: `123456789012345678901234567890`, `1.5E+10` and `1.0` are kept digit for digit. Nothing is converted to `double` or `decimal`, so no precision is lost, and nothing is normalized: `1.0` and `1`, or `1.5E+10` and `15000000000`, are different `Value`s. Deciding whether they are equal belongs to the implemented value rules in `MatchValues.md`.
- `true`, `false` and `null` have no `Value`; `ValueKind` (`True`, `False`, `Null`) carries them. `True` and `False` stay distinct kinds; treating them as one Boolean type is a structural matching rule.
- `ValueKind` keeps values apart that share a `Value`: the number `3` and the string `"3"`, the string `""` and `null`, `{}` and `[]`.
- A missing property has no node. A property holding `null` has a node of kind `Null`.
- Objects and arrays have no `Value`; their content is only in `Children`. No serialized copy of a container is stored.
- Hierarchical IDs, paths, traversal order, array indices and occurrence indices are not stored. They are derived by whoever walks the tree.

## Edge cases and limitations

- A primitive passed as the root gives a single node without children. An empty object or array gives a node of that kind without children.
- `default(JsonElement)` gives a single node of kind `Undefined`, without name, value or children.
- Primitives and empty containers share one empty child list (`Array.Empty<JsonStructureNode>()`), so a leaf or an empty container costs one node and, for strings and numbers, one string.
- Strings with invalid UTF-8 bytes are indistinguishable from each other: all have `Value` `null`, so their content cannot be compared. A string of kind `String` with `Value` `null` is always such a string; an empty string has `Value` `""`. Such input can only come from parsing bytes, since a .NET string is transcoded to valid UTF-8. It does not matter where such a value sits: next to, directly under or deep below a lone surrogate name, the names are still decoded and the walk goes on.
- A property name with invalid UTF-8 bytes throws `InvalidOperationException`: a name cannot be left out, and it is not replaced by U+FFFD, which would make different names equal.
- A lone surrogate in a name or a value is a legitimate, distinct string in the tree, but it is not valid Unicode text: anything that transcodes it (writing it to UTF-8, for instance) will replace or reject it. Matching treats it as an ordinary name; a path writes it as its `\u` escape.
- The tree is a copy. It holds one object per JSON value, one string per property name occurrence and one per string or number value, so it uses several times the memory of the UTF-8 text: about 50 to 55 KB for each fixture of 5.5 to 5.9 KB (see Benchmarks). The `JsonDocument` can be disposed as soon as `Traverse` returns.
- Child lists are not trimmed and property names are not pooled. Both were measured and left out; see `Docs/Decisions.md`.
- Recursion depth follows the document's nesting depth: one stack frame per object or array level. `Traverse` has no depth limit or guard of its own. Through `JJMatcher` the limit is `JJMatchSettings.MaxDepth`, 512 by default; internal callers that parse for themselves get the parser's default of 64. See `Docs/Decisions.md` for what happens when a caller raises it.
- Duplicate keys reach `Traverse` only because `JsonDocument.Parse` allows them by default.
- Numbers are not validated or interpreted beyond what the parser accepted.

## Tests

`tests/JJMatch.Tests/JsonStructure/TraverserTests.cs` calls `Traverse` directly on sections of `Left.json` and `Right.json`:

- root object, root array and root primitives of every kind;
- an object's properties in document order with kind and value (`customer`, `propertyOrder`);
- the empty property name against unnamed nodes, and decoded escaped names (`""`, `escapedNames`);
- duplicate property names with their own values (`duplicateKeys`);
- empty string, `null`, `{}`, `[]` and missing properties (`comment`, `notes`, `preferences`, `tags`, `deletedAt`);
- array element order and nested array boundaries (`matrix`, `arrayBoundaries`, `arrayElementOrder`);
- mixed arrays (`history`, `primitives`);
- large integers, scientific notation, unnormalized numbers and decoded string values (`primitiveValues`);
- the empty child list shared by a primitive, an empty object and an empty array (`comment`, `preferences`, `tags`);
- `default(JsonElement)` as one `Undefined` node;
- lone surrogate escapes in names and values (`loneSurrogates`): a lone high surrogate name declared twice, a lone low one, a valid pair, the same names under a nested object and array, and the same escapes as values, including one inside other text.
- lone surrogate names among everything else a name can hold (`loneSurrogateNames`): a reversed pair declared twice around other names, a high surrogate before a letter, literal and escaped characters side by side, an escaped quote, backslash and solidus, an escaped backslash before a literal `uD800`, the short escapes, and a nested object declared before them, whose names must not be counted among the section's own;
- invalid UTF-8 bytes, written into the fixtures' bytes in memory since a text file cannot hold them (see `Docs/Decisions.md`): string values directly under a duplicated lone surrogate name, below an array under such a name and in a value that also holds a lone surrogate, which lose their `Value` without stopping the walk or changing any name; and a property name, which throws, with and without a lone surrogate name beside it.

## Benchmarks

`benchmarks/JJMatch.Benchmarks` measures time and allocations of `Traverse` over `Left.json` and `Right.json` with BenchmarkDotNet:

```text
dotnet run -c Release --project benchmarks/JJMatch.Benchmarks -- --filter "*Extraction*"
```

Memory is measured there and is not asserted in unit tests.
