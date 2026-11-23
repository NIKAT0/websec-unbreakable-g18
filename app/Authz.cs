using OpenFga.Sdk.Client;
using OpenFga.Sdk.Client.Model;
using Unbreakable.Models;

namespace Unbreakable;

// De enige plek waar de API (de PEP) met de PDP (OpenFGA) praat.
// De API beslist zelf niets meer over wie wat mag: ze stelt de vraag en volgt het antwoord.
public static class Authz
{
    // Mag deze gebruiker deze actie (relation) op dit bericht? De PDP beslist.
    public static async Task<bool> Mag(OpenFgaClient fga, string user, string relation, int messageId)
    {
        var antwoord = await fga.Check(new ClientCheckRequest
        {
            User = $"user:{user}",
            Relation = relation,
            Object = $"message:{messageId}",
        });
        return antwoord.Allowed ?? false;
    }

    // Welke berichten mag deze gebruiker lezen? De PDP geeft de lijst (ListObjects op can_read).
    // Zo filtert de API inbox/zoeken/export niet meer zelf op ontvanger.
    public static Task<List<int>> LeesbareBerichtIds(OpenFgaClient fga, string user)
    {
        return BerichtIds(fga, user, "can_read");
    }

    // Welke berichten mag deze gebruiker verwijderen? (ListObjects op can_delete)
    public static Task<List<int>> VerwijderbareBerichtIds(OpenFgaClient fga, string user)
    {
        return BerichtIds(fga, user, "can_delete");
    }

    private static async Task<List<int>> BerichtIds(OpenFgaClient fga, string user, string relation)
    {
        var antwoord = await fga.ListObjects(new ClientListObjectsRequest
        {
            User = $"user:{user}",
            Relation = relation,
            Type = "message",
        });

        var ids = new List<int>();
        foreach (var obj in antwoord.Objects ?? new List<string>())
        {
            var deel = obj.Split(':');
            if (deel.Length == 2 && int.TryParse(deel[1], out var id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    // De relatie afzender/ontvanger ↔ bericht als tuples wegschrijven.
    public static async Task SchrijfTuples(OpenFgaClient fga, Message m)
    {
        await fga.Write(new ClientWriteRequest
        {
            Writes = new List<ClientTupleKey>
            {
                new() { User = $"user:{m.Sender}",    Relation = "sender",    Object = $"message:{m.Id}" },
                new() { User = $"user:{m.Recipient}", Relation = "recipient", Object = $"message:{m.Id}" },
            },
        });
    }

    // De tuples van een verwijderd bericht opruimen; al weg is geen probleem.
    public static async Task VerwijderTuples(OpenFgaClient fga, Message m)
    {
        try
        {
            await fga.Write(new ClientWriteRequest
            {
                Deletes = new List<ClientTupleKeyWithoutCondition>
                {
                    new() { User = $"user:{m.Sender}",    Relation = "sender",    Object = $"message:{m.Id}" },
                    new() { User = $"user:{m.Recipient}", Relation = "recipient", Object = $"message:{m.Id}" },
                },
            });
        }
        catch
        {
            // De tuples bestonden al niet meer (bv. nooit geschreven) — niets aan de hand.
        }
    }
}
