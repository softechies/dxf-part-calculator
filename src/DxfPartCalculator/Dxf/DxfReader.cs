using System.Globalization;
using System.Text;

namespace DxfPartCalculator.Dxf;

public sealed class DxfFormatException(string message) : Exception(message);

/// <summary>
/// Reads ASCII DXF files (R12 through current versions). Only the parts needed for
/// geometry are kept: header variables, block definitions and entities.
/// </summary>
public static class DxfReader
{
    public static DxfDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static DxfDocument Parse(string text) => Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));

    public static DxfDocument Read(Stream stream)
    {
        var sentinel = new byte[22];
        var read = stream.Read(sentinel, 0, sentinel.Length);
        if (read >= 18 && Encoding.ASCII.GetString(sentinel, 0, 18) == "AutoCAD Binary DXF")
        {
            throw new DxfFormatException("Binary DXF is not supported. Save the file as ASCII DXF.");
        }
        stream.Position = 0;

        // Older files are often Windows-1252; UTF-8 decoding keeps ASCII intact, which is all geometry needs.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var pairs = ReadPairs(reader).GetEnumerator();
        var doc = new DxfDocument();

        while (pairs.MoveNext())
        {
            var (code, value) = pairs.Current;
            if (code != 0 || value != "SECTION") continue;
            if (!pairs.MoveNext()) break;

            var sectionName = pairs.Current.Value;
            switch (sectionName)
            {
                case "HEADER": ReadHeader(pairs, doc); break;
                case "BLOCKS": ReadBlocks(pairs, doc); break;
                case "ENTITIES": ReadEntities(pairs, doc.Entities, "ENDSEC"); break;
                default: SkipSection(pairs); break;
            }
        }

        return doc;
    }

    private static IEnumerable<(int Code, string Value)> ReadPairs(TextReader reader)
    {
        var lineNumber = 0;
        while (true)
        {
            var codeLine = reader.ReadLine();
            if (codeLine is null) yield break;
            var valueLine = reader.ReadLine();
            lineNumber += 2;
            if (valueLine is null) yield break;

            if (!int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                throw new DxfFormatException($"Line {lineNumber - 1}: expected a group code, found '{codeLine.Trim()}'");
            }
            yield return (code, valueLine.Trim());
        }
    }

    private static void SkipSection(IEnumerator<(int Code, string Value)> pairs)
    {
        while (pairs.MoveNext())
        {
            if (pairs.Current is (0, "ENDSEC")) return;
        }
    }

    private static void ReadHeader(IEnumerator<(int Code, string Value)> pairs, DxfDocument doc)
    {
        string? variable = null;
        while (pairs.MoveNext())
        {
            var (code, value) = pairs.Current;
            if (code == 0 && value == "ENDSEC") return;
            if (code == 9)
            {
                variable = value;
            }
            else if (variable is not null && !doc.Header.ContainsKey(variable))
            {
                doc.Header[variable] = value; // first value is enough for the variables we use
            }
        }
    }

    private static void ReadBlocks(IEnumerator<(int Code, string Value)> pairs, DxfDocument doc)
    {
        // Structure: 0 BLOCK, block properties, entities..., 0 ENDBLK, ... 0 ENDSEC
        var hasCurrent = pairs.MoveNext();
        while (hasCurrent)
        {
            var (code, value) = pairs.Current;
            if (code == 0 && value == "ENDSEC") return;

            if (code == 0 && value == "BLOCK")
            {
                var header = new DxfEntity("BLOCK");
                hasCurrent = ReadProperties(pairs, header);
                var block = new DxfBlock(header.GetString(2) ?? header.GetString(3) ?? "")
                {
                    BaseX = header.GetDouble(10),
                    BaseY = header.GetDouble(20)
                };
                if (hasCurrent)
                {
                    hasCurrent = ReadEntitiesFromCurrent(pairs, block.Entities, "ENDBLK");
                }
                doc.Blocks[block.Name] = block;
                continue;
            }

            hasCurrent = pairs.MoveNext();
        }
    }

    private static void ReadEntities(IEnumerator<(int Code, string Value)> pairs, List<DxfEntity> target, string terminator)
    {
        if (pairs.MoveNext())
        {
            ReadEntitiesFromCurrent(pairs, target, terminator);
        }
    }

    /// <summary>
    /// Reads entities starting at the current pair until "0 {terminator}".
    /// Returns whether the enumerator is still positioned on a valid pair.
    /// </summary>
    private static bool ReadEntitiesFromCurrent(IEnumerator<(int Code, string Value)> pairs, List<DxfEntity> target, string terminator)
    {
        var hasCurrent = true;
        DxfEntity? openPolyline = null;

        while (hasCurrent)
        {
            var (code, value) = pairs.Current;
            if (code != 0)
            {
                hasCurrent = pairs.MoveNext();
                continue;
            }

            if (value == "ENDSEC")
            {
                return true; // leave it for the caller so the next SECTION isn't skipped
            }

            if (value == terminator)
            {
                return pairs.MoveNext();
            }

            var entity = new DxfEntity(value);
            hasCurrent = ReadProperties(pairs, entity);

            switch (value)
            {
                case "POLYLINE":
                    openPolyline = entity;
                    target.Add(entity);
                    break;
                case "VERTEX" when openPolyline is not null:
                    openPolyline.Children.Add(entity);
                    break;
                case "SEQEND":
                    openPolyline = null;
                    break;
                default:
                    target.Add(entity);
                    break;
            }
        }

        return false;
    }

    /// <summary>Collects pairs up to the next code 0. Leaves the enumerator on that code 0.</summary>
    private static bool ReadProperties(IEnumerator<(int Code, string Value)> pairs, DxfEntity entity)
    {
        while (pairs.MoveNext())
        {
            if (pairs.Current.Code == 0) return true;
            entity.Pairs.Add(pairs.Current);
        }
        return false;
    }
}
