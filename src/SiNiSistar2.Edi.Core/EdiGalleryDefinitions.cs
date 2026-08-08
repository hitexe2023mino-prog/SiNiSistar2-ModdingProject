using System.Text;

namespace SiNiSistar2.Edi.Core;

/// <summary>One row of the EDI gallery definition table.</summary>
public sealed record EdiGalleryDefinition(
    string Name,
    string FileName,
    long StartTime,
    long EndTime,
    string Type,
    bool Loop,
    string Description);

/// <summary>
/// Reads and rewrites <c>Edi/Gallery/Definitions.csv</c>, which tells EDI the type, loop flag, and
/// length of each gallery. The funscript holds the waveform; this table holds how EDI plays it, so
/// changing a filler's length has to update both or EDI keeps the old duration.
/// </summary>
public static class EdiGalleryDefinitions
{
    private static readonly string[] Header =
    {
        "Name", "FileName", "StartTime", "EndTime", "Type", "Loop", "Description",
    };

    public static IReadOnlyList<EdiGalleryDefinition> Read(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<EdiGalleryDefinition>();
        }

        var rows = new List<EdiGalleryDefinition>();
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            string[] fields = ParseLine(lines[index]);
            if (fields.Length < 6)
            {
                continue;
            }

            rows.Add(new EdiGalleryDefinition(
                fields[0],
                fields[1],
                ParseLong(fields[2]),
                ParseLong(fields[3]),
                fields[4],
                string.Equals(fields[5], "true", StringComparison.OrdinalIgnoreCase),
                fields.Length > 6 ? fields[6] : string.Empty));
        }

        return rows;
    }

    /// <summary>
    /// Adds or updates the row for one gallery and rewrites the file atomically. Rows other than
    /// the target are preserved exactly.
    /// <para>
    /// EDI keys its galleries by the <c>Name</c> column and only auto-generates definitions when
    /// <c>Definitions.csv</c> is absent, so a gallery with no row here simply does not exist as far
    /// as playback is concerned. <c>Name</c> is written equal to <c>FileName</c>, which is the same
    /// convention EDI's own generator uses, so the name the MOD plays is the name EDI resolves.
    /// </para>
    /// </summary>
    public static async Task<bool> UpsertAsync(
        string path,
        string fileName,
        long endTime,
        string type,
        bool loop,
        string description,
        CancellationToken cancellationToken = default)
    {
        var rows = Read(path).ToList();
        var index = rows.FindIndex(row =>
            string.Equals(row.FileName, fileName, StringComparison.Ordinal)
            || string.Equals(row.Name, fileName, StringComparison.Ordinal));

        var updated = new EdiGalleryDefinition(fileName, fileName, 0, endTime, type, loop, description);
        if (index < 0)
        {
            rows.Add(updated);
        }
        else
        {
            // Keep a description the user may have edited, and keep the existing type unless the
            // caller is authoritative about it.
            updated = updated with
            {
                Description = string.IsNullOrWhiteSpace(description) ? rows[index].Description : description,
            };
            if (rows[index] == updated)
            {
                return false;
            }

            rows[index] = updated;
        }

        await WriteAsync(path, rows, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public static async Task WriteAsync(
        string path,
        IReadOnlyList<EdiGalleryDefinition> rows,
        CancellationToken cancellationToken = default)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(",", Header)).Append('\n');
        foreach (EdiGalleryDefinition row in rows)
        {
            builder
                .Append(Escape(row.Name)).Append(',')
                .Append(Escape(row.FileName)).Append(',')
                .Append(row.StartTime).Append(',')
                .Append(row.EndTime).Append(',')
                .Append(Escape(row.Type)).Append(',')
                .Append(row.Loop ? "true" : "false").Append(',')
                .Append(Escape(row.Description)).Append('\n');
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, builder.ToString(), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
        File.Move(temp, path, true);
    }

    private static long ParseLong(string value) =>
        long.TryParse(value, out long parsed) ? parsed : 0;

    private static string Escape(string value) =>
        value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal)
            ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : value;

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (quoted)
            {
                if (character != '"')
                {
                    current.Append(character);
                }
                else if (index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
