using System.Text.Json.Nodes;
using IdiotProof.Scripting;
using static IdiotProof.Strategies.Tests.StrategyJsonSchemaSupport;

namespace IdiotProof.Strategies.Tests;

/// <summary>
/// IP-T526: the published JSON Schema of the canonical strategy JSON is held to the strict reader.
/// Every key the reader accepts is in the schema and no other; a document that uses every key and
/// every condition type, built from the schema alone, is read by the reader; and whatever single
/// change the reader refuses (a key removed, a value of the wrong kind, an unknown key), the
/// schema refuses too. The reader stays the authority: the schema may be stricter, never looser.
/// </summary>
public class StrategyJsonSchemaTests
{
    private static readonly JsonObject Schema = StrategyJson.Schema();

    private static JsonObject SchemaOf(string acceptedObject) =>
        StrategyJson.SchemaDefinitionName(acceptedObject) is { } name ? Schema["$defs"]![name]!.AsObject() : Schema;

    /// <summary>Keys the schema marks with an example because their value names another part of the document (routes).</summary>
    private static readonly IReadOnlySet<string> ReferenceKeys =
        Schema["properties"]!.AsObject().Where(p => p.Value!["examples"] is not null).Select(p => p.Key).ToHashSet();

    private static bool ReaderAccepts(JsonObject document)
    {
        try
        {
            StrategyJson.Deserialize(document.ToJsonString());
            return true;
        }
        catch (Exception)
        {
            // Any refusal counts, whatever its type: the question is only whether the reader takes the document.
            return false;
        }
    }

    [Test]
    public void EveryKeyTheReaderAccepts_IsInTheSchema_AndNoOther()
    {
        var accepted = StrategyJson.AcceptedKeys();
        Assert.Multiple(() =>
        {
            foreach (var (name, keys) in accepted)
            {
                var schema = SchemaOf(name);
                Assert.That(schema["properties"]!.AsObject().Select(p => p.Key), Is.EquivalentTo(keys), name);
                Assert.That(schema["additionalProperties"]!.GetValue<bool>(), Is.False, name + " must refuse unknown keys, as the reader does");
            }
        });
    }

    [Test]
    public void EverySchemaObject_IsOneTheReaderHasAKeyTableFor()
    {
        var published = Schema["$defs"]!.AsObject().Select(d => d.Key).Where(d => d != "condition");
        var known = StrategyJson.AcceptedKeys().Keys.Select(StrategyJson.SchemaDefinitionName).Where(n => n is not null);
        Assert.That(published, Is.EquivalentTo(known));
    }

    [Test]
    public void EveryConditionType_IsAnAlternativeOfCondition()
    {
        var alternatives = Schema["$defs"]!["condition"]!["oneOf"]!.AsArray().Select(a => a!["$ref"]!.GetValue<string>().Split('/')[^1]);
        var types = StrategyJson.AcceptedKeys().Keys.Where(k => k.StartsWith("condition:", StringComparison.Ordinal)).Select(StrategyJson.SchemaDefinitionName);
        Assert.That(alternatives, Is.EquivalentTo(types));
    }

    [Test]
    public void EveryKey_IsDescribed()
    {
        Assert.Multiple(() =>
        {
            foreach (var name in StrategyJson.AcceptedKeys().Keys)
                foreach (var (key, value) in SchemaOf(name)["properties"]!.AsObject())
                {
                    var description = value!["description"]?.GetValue<string>();
                    if (key == "type" && name.StartsWith("condition:", StringComparison.Ordinal)) continue; // the discriminator is a const
                    Assert.That(description, Is.Not.Null.And.Not.Empty.And.Not.EqualTo(StrategyJson.UndescribedKey),
                        $"{name}.{key}: the reader accepts this key but StrategyJsonSchema.cs does not say what it holds");
                    Assert.That(value.AsObject().Count, Is.GreaterThan(1), $"{name}.{key} has no value schema");
                }
        });
    }

    [Test]
    public void ADocumentUsingEveryKey_BuiltFromTheSchema_IsReadByTheReader()
    {
        var document = FullDocument(Schema);

        Assert.Multiple(() =>
        {
            Assert.That(Accepts(Schema, Schema, document), Is.True, "the generated document must satisfy the schema it was generated from");
            Assert.DoesNotThrow(() => StrategyJson.Deserialize(document.ToJsonString()), document.ToJsonString());

            // The generator really did reach every key of every object.
            var text = document.ToJsonString();
            foreach (var (name, keys) in StrategyJson.AcceptedKeys())
                foreach (var key in keys)
                    Assert.That(text, Does.Contain($"\"{key}\":"), $"{name}.{key} is missing from the generated document");
            foreach (var type in StrategyJson.AcceptedKeys().Keys.Where(k => k.StartsWith("condition:", StringComparison.Ordinal)))
                Assert.That(text, Does.Contain($"\"type\":\"{type["condition:".Length..]}\""), type);
        });
    }

    [Test]
    public void WhatTheReaderWrites_SatisfiesTheSchema()
    {
        var written = StrategyJson.Serialize(StrategyJson.Deserialize(FullDocument(Schema).ToJsonString()));
        Assert.That(Accepts(Schema, Schema, JsonNode.Parse(written)), Is.True, written);
    }

    [Test]
    public void WhateverSingleChangeTheReaderRefuses_TheSchemaRefusesToo()
    {
        var document = FullDocument(Schema);
        var looser = new List<string>();
        var count = 0;
        var refused = 0;
        foreach (var (label, mutated) in Mutations(document, ReferenceKeys))
        {
            count++;
            if (ReaderAccepts(mutated)) continue;
            refused++;
            if (Accepts(Schema, Schema, mutated)) looser.Add(label);
        }

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.GreaterThan(1000), "the mutation set should cover every key of every object");
            Assert.That(refused, Is.GreaterThan(count / 2), "most single changes break a strict document");
            Assert.That(looser, Is.Empty, "the schema accepts what the reader refuses:\n" + string.Join("\n", looser.Take(40)));
        });
    }

    [TestCase("schemaVersion", "2")]
    [TestCase("symbol", "\"  \"")]
    [TestCase("session", "\"rth\"")]
    [TestCase("direction", "\"long\"")]
    [TestCase("exitTime", "\"25:00\"")]
    [TestCase("exitTime", "\"9.30\"")]
    [TestCase("velocityExhaustionWindowMinutes", "2")]
    [TestCase("thenWithinDays", "0")]
    [TestCase("quantity", "2147483648")]
    [TestCase("onStop", "null")]
    [TestCase("onExit", "\"  \"")]
    public void ValuesTheReaderRefuses_OnTheStrategyItself_AreRefusedByTheSchema(string key, string json)
    {
        var document = FullDocument(Schema);
        document[key] = JsonNode.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(ReaderAccepts(document), Is.False, "the reader should refuse this (the case is wrong if it does not)");
            Assert.That(Accepts(Schema, Schema, document), Is.False);
        });
    }

    [Test]
    public void TheSchema_NamesItsVersion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Schema["$schema"]!.GetValue<string>(), Is.EqualTo(StrategyJson.SchemaDialect));
            Assert.That(Schema["$id"]!.GetValue<string>(), Does.EndWith(":" + StrategyJson.CurrentSchemaVersion));
            Assert.That(Schema["properties"]!["schemaVersion"]!["const"]!.GetValue<int>(), Is.EqualTo(StrategyJson.CurrentSchemaVersion));
            Assert.That(Schema["required"]!.AsArray().Select(r => r!.GetValue<string>()), Is.EquivalentTo(new[] { "schemaVersion", "symbol" }));
        });
    }
}
