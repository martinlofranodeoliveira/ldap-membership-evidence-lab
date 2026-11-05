using System.Security.Cryptography;
using System.Text.Json;

namespace MembershipEvidence;

internal static class EvidenceStore
{
    internal static Publication Publish(string directory, Resolution resolution, Budget budget)
    {
        if (!resolution.Complete || !resolution.Observation.QualifiedStable)
            throw new ObservationFailure("PUBLICATION_ERROR");
        var temporary = Path.Combine(directory, "evidence", "staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            budget.Check();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(resolution, EvidenceJson.Options);
            if (bytes.Length > 4 * 1024 * 1024) throw new ObservationFailure("EVIDENCE_LIMIT");
            budget.Check();
            var guard = resolution.Observation.Before!;
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var name = $"{guard.Epoch}-{guard.Revision}-{hash}.json";
            var destination = Path.Combine(directory, "evidence", name);
            if (File.Exists(destination))
            {
                if (!File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes)) throw new ObservationFailure("PUBLICATION_ERROR");
                budget.Check();
                return new Publication(name, false);
            }
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            budget.Check();
            File.Move(temporary, destination, false);
            return new Publication(name, true);
        }
        catch (IOException) { throw new ObservationFailure("PUBLICATION_ERROR"); }
        catch (UnauthorizedAccessException) { throw new ObservationFailure("PUBLICATION_ERROR"); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
