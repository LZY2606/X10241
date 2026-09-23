using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IsoMix.App;

/// <summary>System.Text.Json 不原生支持 double[,]，这里以“行优先二维 JSON 数组”序列化，保留维度顺序。</summary>
public sealed class TwoDimDoubleConverter : JsonConverter<double[,]>
{
    public override double[,] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rows = new List<List<double>>();
        reader.Read(); // start array
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            var row = new List<double>();
            reader.Read(); // inner start
            while (reader.TokenType != JsonTokenType.EndArray)
            {
                row.Add(reader.TokenType == JsonTokenType.Null ? 0.0 : reader.GetDouble());
                reader.Read();
            }
            rows.Add(row);
            reader.Read();
        }
        int r = rows.Count, c = r == 0 ? 0 : rows[0].Count;
        var m = new double[r, c];
        for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) m[i, j] = rows[i][j];
        return m;
    }

    public override void Write(Utf8JsonWriter writer, double[,] value, JsonSerializerOptions options)
    {
        int r = value.GetLength(0), c = value.GetLength(1);
        writer.WriteStartArray();
        for (int i = 0; i < r; i++)
        {
            writer.WriteStartArray();
            for (int j = 0; j < c; j++)
                {
                    double z = value[i, j];
                    if (double.IsNaN(z) || double.IsInfinity(z)) writer.WriteNullValue();
                    else writer.WriteNumberValue(z);
                }
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }
}

public sealed class TwoDimBoolConverter : JsonConverter<bool[,]>
{
    public override bool[,] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rows = new List<List<bool>>();
        reader.Read();
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            var row = new List<bool>();
            reader.Read();
            while (reader.TokenType != JsonTokenType.EndArray) { row.Add(reader.GetBoolean()); reader.Read(); }
            rows.Add(row);
            reader.Read();
        }
        int r = rows.Count, c = r == 0 ? 0 : rows[0].Count;
        var m = new bool[r, c];
        for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) m[i, j] = rows[i][j];
        return m;
    }

    public override void Write(Utf8JsonWriter writer, bool[,] value, JsonSerializerOptions options)
    {
        int r = value.GetLength(0), c = value.GetLength(1);
        writer.WriteStartArray();
        for (int i = 0; i < r; i++)
        {
            writer.WriteStartArray();
            for (int j = 0; j < c; j++) writer.WriteBooleanValue(value[i, j]);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }
}
