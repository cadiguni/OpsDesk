using System.Text;

namespace OpsDesk.Application.Import;

/// <summary>
/// Leitura de CSV (RFC 4180): campos entre aspas, aspas escapadas por aspas dobradas,
/// quebra de linha dentro de campo entre aspas.
///
/// O separador é descoberto pelo cabeçalho: vírgula ou ponto e vírgula. O Excel em
/// português salva CSV com ponto e vírgula, porque a vírgula é o separador decimal — um
/// importador que só aceitasse vírgula recusaria o arquivo que a pessoa acabou de exportar.
/// </summary>
public static class CsvTable
{
    /// <summary>As linhas como dicionário pelo nome da coluna, com o número da linha no arquivo.</summary>
    public static List<(int Line, Dictionary<string, string> Fields)> Parse(string text)
    {
        // BOM do UTF-8, que o Excel põe no começo e que viraria parte do nome da primeira coluna.
        text = text.TrimStart('﻿');

        var firstLineEnd = text.IndexOfAny(['\r', '\n']);
        var header = firstLineEnd < 0 ? text : text[..firstLineEnd];
        var separator = header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';

        var records = ReadRecords(text, separator);

        if (records.Count == 0)
        {
            return [];
        }

        var columns = records[0].Fields.Select(c => c.Trim()).ToList();
        var rows = new List<(int, Dictionary<string, string>)>();

        foreach (var (line, fields) in records.Skip(1))
        {
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < columns.Count && i < fields.Count; i++)
            {
                row.TryAdd(columns[i], fields[i]);
            }

            rows.Add((line, row));
        }

        return rows;
    }

    private static List<(int Line, List<string> Fields)> ReadRecords(string text, char separator)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(field.ToString());
                field.Clear();
                records.Add((recordLine, fields));
                fields = [];
                line++;
                recordLine = line;
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add((recordLine, fields));
        }

        return records;
    }
}
